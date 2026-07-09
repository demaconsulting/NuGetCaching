## PackageSourceResolver Design

### Overview

`PackageSourceResolver` is an internal static class that resolves the `FindPackageByIdResource`
for a configured NuGet package source, in the DemaConsulting NuGet Caching library. It owns the
ordered candidate-repository construction needed to support automatic v2 OData fallback for
feeds configured with a v3 `/index.json` URL, and the resource-resolution loop that tries each
candidate in turn.

The class is marked `internal` because it is an implementation detail of the library and is not
part of the public API surface. It does not download package content; once a resource has been
resolved, `PackageDownloader` uses it to download and install the package. Authentication-failure
diagnosis is delegated to `AuthFailureClassifier` so that logic is not duplicated between
resolution and download.

### Class Structure

#### PackageSourceResolution Record Struct

```csharp
internal readonly record struct PackageSourceResolution(
    SourceRepository Repository,
    FindPackageByIdResource? Resource,
    string? ErrorMessage);
```

Represents the result of resolving a `FindPackageByIdResource` for a configured package source:

- `Repository` — the effective `SourceRepository` to use for subsequent operations (may be a v2
  fallback repository rather than the originally configured one).
- `Resource` — the resolved `FindPackageByIdResource`, or `null` when resolution failed.
- `ErrorMessage` — a diagnostic message describing a source-level failure (e.g. protocol
  mismatch or an actionable authentication failure), or `null` when the source was reachable
  but simply did not resolve a resource, or when the failure is transient and non-actionable.

#### ResolveAsync Method

```csharp
internal static async Task<PackageSourceResolution> ResolveAsync(
    SourceRepository sourceRepository,
    IEnumerable<Lazy<INuGetResourceProvider>> providers,
    CancellationToken cancellationToken)
```

Resolves the `FindPackageByIdResource` for a source repository, with automatic v2 OData fallback
when a v3 `/index.json` URL fails with a protocol error.

#### BuildCandidateRepositories Method

```csharp
private static IReadOnlyList<SourceRepository> BuildCandidateRepositories(
    SourceRepository sourceRepository,
    IEnumerable<Lazy<INuGetResourceProvider>> providers)
```

Builds the ordered list of candidate repositories to try when resolving a
`FindPackageByIdResource` for a package source.

### Design Decisions

#### Static Class

`PackageSourceResolver` provides a stateless service — resolving a resource for a given source —
that requires no instance state. A static class avoids unnecessary object instantiation and
keeps the API surface flat, mirroring the design of `PathHelpers`.

#### Record Struct Result Type

`PackageSourceResolution` replaces the tuple previously returned inline in `NuGetCache` with a
named record struct. This gives the three result fields self-documenting names at every call
site (`result.Resource`, `result.ErrorMessage`) rather than positional tuple elements, while a
record struct still supports the same `var (repository, resource, error) = ...` deconstruction
syntax the orchestrator previously relied on, so `NuGetCache` did not need to change its
consumption pattern.

#### Resilient Source Enumeration with Automatic v2 Fallback

Sources are queried sequentially. If a source fails with a `NuGetProtocolException` while
loading the service index (e.g. a v2-only feed configured with a v3 `.json` URL), the library
checks whether the source URL ends in `/index.json`. If it does, it automatically retries using
the base URL (with `/index.json` stripped) as a v2 OData endpoint. This transparently handles
the JFrog Artifactory pattern where administrators copy a v3-style URL that is actually a
v2-only feed.

- **Automatic v2 fallback**: If the fallback succeeds (or confirms the package is absent), the
  result is returned with no diagnostic overhead — the URL mismatch is resolved transparently.
- **Both attempts fail**: If both the original URL and the v2 fallback fail with protocol
  errors, the error from the original configured URL is captured and accumulated for the final
  exception.
- **Non-`/index.json` protocol errors**: If the URL does not end in `/index.json` and a
  `NuGetProtocolException` occurs, the error is captured and accumulated.
- **Authentication failures (401/403)**: Whether surfaced as an `HttpRequestException` or
  wrapped in a `NuGetProtocolException`, a response indicating HTTP 401 or 403 is detected by
  `AuthFailureClassifier.TryDescribeAuthFailure` and treated as actionable rather than
  transient. An actionable diagnostic naming the source and the detected status code is
  captured and accumulated for the final exception, instead of being silently swallowed.
- **Other network errors**: `HttpRequestException` that does not represent a 401/403 response
  (transient network error, connection refused, DNS failure, timeout) is still silently
  swallowed — network outages on individual feeds remain non-actionable.

#### Resolution-Time Failures Are Not Always Observable

Resolving a `FindPackageByIdResource` does not by itself require a successful v3 service-index
fetch: for a source URL that does not end in `/index.json` (including the v2 fallback candidate
constructed by `BuildCandidateRepositories`), the underlying NuGet SDK provider chain
(`Repository.Provider.GetCoreV3()`) constructs a V2-typed resource object without making any
HTTP request at all, deferring actual protocol and authentication validation to first use
(search or download) rather than resolution. Consequently, an index-fetch failure of any kind on
the first (v3) candidate for a `/index.json` source — including an otherwise-actionable 401/403
— is masked once the loop moves on to the v2 fallback candidate, which resolves successfully
without ever performing the HTTP request that would have surfaced the failure. The actionable
diagnostic only reliably surfaces once `PackageDownloader` performs its own, unavoidable HTTP
request against the resolved (possibly masked) resource. This is a property of the underlying
NuGet SDK provider chain, not a defect in this class: the `HttpRequestException`- and
`NuGetProtocolException`-handling branches in `ResolveAsync` remain correct and are exercised
for scenarios where a `/index.json` source has no fallback candidate to mask the failure, or
where the SDK does raise the exception before this class's own candidate loop can proceed.

#### Extraction from NuGetCache

This class was extracted verbatim from the original `NuGetCache.GetFindPackageByIdResourceAsync`
and `NuGetCache.BuildCandidateRepositories` private methods as a pure structural refactor: the
resolution algorithm, exception handling, and candidate-construction logic are unchanged. The
method was renamed from `GetFindPackageByIdResourceAsync` to `ResolveAsync` to read naturally as
`PackageSourceResolver.ResolveAsync(...)` at the call site in `NuGetCache`.

### Method Descriptions

#### `ResolveAsync(...)`

Full signature:

```csharp
internal static async Task<PackageSourceResolution> ResolveAsync(
    SourceRepository sourceRepository,
    IEnumerable<Lazy<INuGetResourceProvider>> providers,
    CancellationToken cancellationToken)
```

Resolves the `FindPackageByIdResource` for a source repository, with automatic v2 OData
fallback when a v3 `/index.json` URL fails with a protocol error. The method:

1. Calls `BuildCandidateRepositories` to get the ordered list of repositories to try.
2. Iterates over the candidates, calling `GetResourceAsync<FindPackageByIdResource>` on each
   one.
3. Returns the first successful result where the resource is non-null. A null resource is
   treated as "not supported by this candidate" and the loop continues to the next candidate.
4. On an `HttpRequestException` where `AuthFailureClassifier.TryDescribeAuthFailure` detects an
   HTTP 401/403, returns the actionable diagnostic message immediately rather than continuing
   to the next candidate — an authenticated HTTP-level rejection is definitive for this
   candidate URL.
5. On any other `HttpRequestException` from any candidate, returns a result carrying any
   actionable diagnostic already captured from an earlier candidate (or `null` if none) — this
   candidate's transient network error is not itself actionable, but must not discard a genuine
   authentication failure detected on a prior candidate.
6. On a `NuGetProtocolException` where `AuthFailureClassifier.TryDescribeAuthFailure` detects an
   HTTP 401/403, captures the actionable diagnostic message via `??=` and tries the next
   candidate (e.g. the v2 OData fallback), the same as any other `NuGetProtocolException` — the
   protocol layer already implies the request reached the source and got a structured response,
   so a fallback candidate is still worth trying before giving up.
7. On any other `NuGetProtocolException`, captures the first (configured URL's) error message
   via `??=` and tries the next candidate. After all candidates are exhausted, returns the
   captured error message (auth-diagnostic or generic) referencing the original configured URL.

Satisfies requirements `Caching-PackageSourceResolver-Resolve` and
`Caching-PackageSourceResolver-FallbackOnProtocolError`.

#### `BuildCandidateRepositories(...)` (private)

Full signature:

```csharp
private static IReadOnlyList<SourceRepository> BuildCandidateRepositories(
    SourceRepository sourceRepository,
    IEnumerable<Lazy<INuGetResourceProvider>> providers)
```

Builds the ordered list of candidate repositories to try for a source. Returns a single-element
list `[sourceRepository]` when the source URL does not end in `/index.json`. When the URL ends
in `/index.json`, returns a two-element list of the original repository followed by a v2 OData
fallback repository constructed from the base URL (with `/index.json` stripped), preserving the
source name and credentials. This transparently handles v2-only feeds (e.g. JFrog Artifactory)
configured with a v3-style URL.

Satisfies requirements `Caching-PackageSourceResolver-FallbackOnProtocolError` and
`Caching-PackageSourceResolver-NoFallbackForNonIndexUrl`.
