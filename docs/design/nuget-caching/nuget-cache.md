## NuGetCache Design

### Overview

The `NuGetCache` class is a public static class providing NuGet package caching
functionality for the DemaConsulting NuGet Caching library. It is a software unit
in the sense of IEC 62304 — the smallest independently testable component responsible
for ensuring a specific NuGet package version is available in the local global packages
cache before use.

The class reads NuGet configuration (package sources and the global packages folder
path) from the default machine settings, mirroring the behavior of the `dotnet` CLI
and Visual Studio package restore. It communicates with configured NuGet sources using
the NuGet client SDK to download packages when they are not already present locally.

### Design Decisions

#### Static Class

`NuGetCache` is designed as a static class because it provides a service — ensuring
a package is cached — that does not require instance state. All configuration is
read from the machine-level NuGet settings on each call, making the class naturally
stateless. A static class avoids unnecessary object instantiation and provides a
simple, flat API surface for callers. Because all state is local to each call,
`NuGetCache` is safe for concurrent use.

#### Async Approach

The primary method `EnsureCachedAsync` is asynchronous because NuGet source
communication involves network I/O. Using `async`/`await` throughout allows the
calling thread to be returned to the thread pool during network waits, keeping the
library cooperative in concurrent or UI-driven environments.

#### NuGet Settings Integration

Rather than accepting source URIs directly, `NuGetCache` reads from the default
NuGet settings on the local machine via `Settings.LoadDefaultSettings(null)`. This
ensures the library respects the same `nuget.config` hierarchy (machine-wide, user,
and project-level) as the `dotnet` CLI and Visual Studio, including authenticated
feeds, proxy settings, and package source mapping.

#### Early-Exit on Cache Hit

The method checks for the presence of the `.nupkg.metadata` sentinel file before
attempting any network communication. NuGet writes this file as the final step of
package extraction, so its presence is a reliable indicator that the package is
fully installed. Checking for this file rather than the directory avoids a race
condition where a partially-extracted package directory is mistaken for a complete
installation.

#### Credential Service Registration

Before resolving any `SourceRepository`, the internal `EnsureCachedAsync` overload calls
`ICredentialServiceRegistrar.EnsureRegistered()` on an injected registrar, which registers the
NuGet SDK's default credential service via
`DefaultCredentialServiceUtility.SetupDefaultCredentialService(logger, nonInteractive: true)`,
mirroring the setup performed internally by the `dotnet` CLI and MSBuild restore pipeline.
Without this registration, `HttpHandlerResourceV3.CredentialService` remains `null`, so
`HttpSourceAuthenticationHandler` returns a source's first HTTP 401 response as-is instead of
retrying with credentials resolved from a NuGet credential-provider plugin (e.g. for JFrog
Artifactory or Azure Artifacts). Static `packageSourceCredentials` configured in `nuget.config`
are applied directly to the underlying `HttpClientHandler` by the NuGet SDK and so are honored
on the first request regardless of credential-service registration; the credential service
becomes relevant when those static credentials are absent, incorrect, or when a
credential-provider plugin must be consulted.

`SetupDefaultCredentialService` is itself idempotent (it only assigns
`HttpHandlerResourceV3.CredentialService` when still `null`), but it always re-creates a
delegating logger. The default `CredentialServiceRegistrar` implementation memoizes the call
per instance via `Lazy<bool>`, and a single static instance is shared by every real (non-test)
`EnsureCachedAsync` call, so the registration work happens only once per process, before the
first use, even if `EnsureCachedAsync` is called many times. This registrar is injected through
an internal `ICredentialServiceRegistrar` seam (mirroring the existing `ISettings` injection
seam) so tests can substitute a spy double instead of depending on any shared, process-wide
test-only state.

#### Package Source Mapping Support

When `PackageSourceMapping` is enabled in the NuGet configuration, `NuGetCache`
filters the set of queried sources to only those explicitly mapped to the requested
package ID. This mirrors the security and governance behavior of the NuGet toolchain,
ensuring packages are only fetched from their authorized feeds.

#### Resilient Source Enumeration with Automatic v2 Fallback

Sources are queried sequentially. If a source fails with a `NuGetProtocolException` while
loading the service index (e.g. a v2-only feed configured with a v3 `.json` URL), the
library checks whether the source URL ends in `/index.json`. If it does, it automatically
retries using the base URL (with `/index.json` stripped) as a v2 OData endpoint. This
transparently handles the JFrog Artifactory pattern where administrators copy a v3-style
URL that is actually a v2-only feed.

- **Automatic v2 fallback**: If the fallback succeeds (or confirms the package is absent),
  the result is returned with no diagnostic overhead — the URL mismatch is resolved
  transparently.
- **Both attempts fail**: If both the original URL and the v2 fallback fail with protocol
  errors, the error from the original configured URL is captured and accumulated for the
  final exception.
- **Non-`/index.json` protocol errors**: If the URL does not end in `/index.json` and
  a `NuGetProtocolException` occurs, the error is captured and accumulated.
- **Authentication failures (401/403)**: Whether surfaced as an `HttpRequestException` or
  wrapped in a `NuGetProtocolException`, a response indicating HTTP 401 or 403 is detected
  by `TryDescribeAuthFailure`/`TryGetHttpStatusCode` and treated as actionable rather than
  transient. An actionable diagnostic naming the source and the detected status code is
  captured and accumulated for the final exception, instead of being silently swallowed.
- **Other network errors**: `HttpRequestException` that does not represent a 401/403 response
  (transient network error, connection refused, DNS failure, timeout) is still silently
  swallowed — network outages on individual feeds remain non-actionable.

If no source has the package, an `InvalidOperationException` is thrown. When at least one
source produced a diagnostic message, those messages are appended to the exception in the
form:

```text
Package 'X' version '1.0.0' was not found in any configured NuGet source.
  - https://feed/index.json: Failed to load source index. (...)
```

This allows callers to distinguish a genuine "package absent" outcome from a feed
misconfiguration, without requiring additional logging infrastructure.

#### Actionable 401/403 Diagnostics

The NuGet SDK does not consistently expose the failing HTTP status code as a strongly typed
property: `HttpRequestException.StatusCode` only exists on net5.0+ (not `netstandard2.0`,
one of this library's target frameworks), and authentication failures are frequently wrapped
in a `NuGetProtocolException` (e.g. `FatalProtocolException`) whose message - or whose
`InnerException`'s message - simply embeds text such as
`"Response status code does not indicate success: 401 (Unauthorized)."`.

`TryGetHttpStatusCode` walks the full exception chain (`InnerException` by `InnerException`),
checking the strongly typed `HttpRequestException.StatusCode` property where available
(`#if !NETSTANDARD2_0`) and falling back to a compiled regular expression that matches the
standard HTTP reason-phrase text NuGet emits - `401 (Unauthorized)` or `403 (Forbidden)` -
rather than a bare standalone number, avoiding false positives on unrelated numbers elsewhere
in an exception message (e.g. a port number). This keeps detection consistent
across every target framework (`netstandard2.0`, `net8.0`, `net9.0`, `net10.0`) regardless of
which exception shape the SDK happens to surface at a given call site.

When a 401/403 is detected, `TryDescribeAuthFailure` builds a diagnostic message naming the
source (its configured name and URL), the detected status code, and a hint to check
`packageSourceCredentials` in `nuget.config` or a configured credential provider. This message
is captured in the same accumulation used for other protocol failures, so it is included
verbatim in the final `InvalidOperationException` when no source has the package - giving
callers a concrete signal that a source rejected the request for lack of (or incorrect)
credentials, rather than an indistinguishable "not found" outcome. This detection is applied
uniformly at both call sites that consume network resources: resolving the
`FindPackageByIdResource` (`GetFindPackageByIdResourceAsync`) and downloading the `.nupkg`
bytes (`TryDownloadFromResourceAsync`), since either step may be the one an authenticated
feed rejects.

#### Testability via Injected Settings

The public `EnsureCachedAsync` method is a thin wrapper that calls the internal overload
with `Settings.LoadDefaultSettings(null)`. The internal overload accepts an `ISettings`
parameter that replaces the call to `LoadDefaultSettings`. This design gives tests full
control over the NuGet source and global packages folder without touching the developer's
real machine configuration:

- A test can point the settings at a local WireMock server running on a random port.
- A test can specify a disposable temp directory as the global packages folder, preventing
  cross-test pollution and avoiding accidental use of the developer's real package cache.
- All caching logic lives in one place (the internal overload), so there is a single
  implementation path shared by both the production (default-settings) call and all tests.

The internal overload is only accessible to the `DemaConsulting.NuGet.Caching.Tests`
assembly, enforced via `InternalsVisibleTo` in the library project file. This keeps the
public API surface minimal while enabling complete test coverage of the download path.

Seven private members encapsulate distinct sub-responsibilities:

- `TryDownloadResult` — a private record struct pairing an optional package path with
  an optional diagnostic error message, allowing helpers to communicate both success
  and actionable failure details without using out-parameters or exceptions.
- `GetFindPackageByIdResourceAsync` — iterates over the candidate repositories returned
  by `BuildCandidateRepositories`, returning the first successfully resolved
  `FindPackageByIdResource` and its effective repository. Silently skips on a non-auth
  `HttpRequestException`; returns an actionable diagnostic immediately on an HTTP-level
  401/403; for a `NuGetProtocolException` (auth-related or not) accumulates the first
  diagnostic message and tries the next candidate before surfacing it as the final error.
- `BuildCandidateRepositories` — builds the ordered list of repositories to try for a
  source. Returns a single-element list for non-`/index.json` URLs, or a two-element
  list (original + v2 OData fallback at the base URL) for `/index.json` URLs.
- `TryDownloadFromResourceAsync` — streams the `.nupkg` bytes and installs the package
  into the global packages folder using an already-resolved resource, applying the same
  401/403 detection as `GetFindPackageByIdResourceAsync`.
- `ICredentialServiceRegistrar` / `CredentialServiceRegistrar` — an internal seam that
  registers the NuGet SDK's default credential service once per process before any
  `SourceRepository` is resolved, injectable by tests as an alternative to relying on
  shared, process-wide static state.
- `TryDescribeAuthFailure` / `TryGetHttpStatusCode` — detect an HTTP 401/403 status
  anywhere in an exception chain and build the actionable diagnostic message describing
  it, used by both `GetFindPackageByIdResourceAsync` and `TryDownloadFromResourceAsync`.
- `GetPackagePath` — the conventional on-disk path calculation that NuGet uses for
  installed packages (`{globalPackagesFolder}/{id.lower}/{version.lower}`).

This separation keeps `EnsureCachedAsync` at a high level of abstraction, eliminates
any recursion in the download path, and makes each sub-task individually readable.

### Method Descriptions

#### `EnsureCachedAsync(string packageId, string version, CancellationToken)` (public)

Thin wrapper that delegates immediately to the internal `EnsureCachedAsync` overload,
passing `Settings.LoadDefaultSettings(null)` as the `settings` argument. This ensures
the public API continues to behave identically to the pre-testability implementation
while the full logic lives in one place. See the internal overload description below
for the complete processing steps.

Returns the absolute path to the cached package folder.

Satisfies requirements `Caching-NuGetCache-EnsureCached`, `Caching-NuGetCache-NullValidation`,
`Caching-NuGetCache-InvalidVersion`, `Caching-NuGetCache-TransientFailure`,
`Caching-NuGetCache-MultiSource`, `Caching-NuGetCache-V2Fallback`,
`Caching-NuGetCache-CacheHit`, `Caching-NuGetCache-NotFound`, and
`Caching-NuGetCache-AuthenticatedSource`.

#### `EnsureCachedAsync(string packageId, string version, ISettings settings, CancellationToken)` (internal)

Thin wrapper that delegates immediately to a further internal `EnsureCachedAsync` overload,
passing `DefaultCredentialRegistrar` - a single static `CredentialServiceRegistrar` instance
shared by every real (non-test) call in the process - as the `credentialRegistrar` argument.
This overload exists to support testing with an injected `ISettings`; all non-test callers
reach it only via the public wrapper. See the next overload for the complete processing steps.

#### `EnsureCachedAsync(..., ISettings settings, ICredentialServiceRegistrar credentialRegistrar, CancellationToken)` (internal)

Contains all caching logic for the public method. The method:

1. Validates that `packageId`, `version`, `settings`, and `credentialRegistrar` are not
   null, throwing `ArgumentNullException` for any null argument.
2. Parses the `version` string using `NuGetVersion.Parse`, throwing
   `ArgumentException` when the version string is not a valid NuGet version.
3. Resolves the global packages folder from the injected `settings`.
4. Computes the expected on-disk package path and returns it immediately if the
   `.nupkg.metadata` sentinel file exists (cache-hit fast path) - skipping the
   remaining steps below, including credential-service registration, entirely.
5. Calls `credentialRegistrar.EnsureRegistered()` so any subsequently resolved
   `SourceRepository` can consult the NuGet credential service for authenticated
   sources.
6. Iterates over enabled, mapped package sources. For each source, calls
   `GetFindPackageByIdResourceAsync` to resolve the resource (applying v2 fallback as
   needed), then `TryDownloadFromResourceAsync` to download and install the package.
   Source-level diagnostic messages are accumulated in a list.
7. Throws `InvalidOperationException` if no source provided the package. When at
   least one source returned a diagnostic message, those messages are appended to
   the exception so the caller can identify the misconfigured source.

Returns the absolute path to the cached package folder.

#### `GetFindPackageByIdResourceAsync` (private)

Resolves the `FindPackageByIdResource` for a source repository, with automatic v2 OData
fallback when a v3 `/index.json` URL fails with a protocol error. The method:

1. Calls `BuildCandidateRepositories` to get the ordered list of repositories to try.
2. Iterates over the candidates, calling `GetResourceAsync<FindPackageByIdResource>` on
   each one.
3. Returns the first successful `(repository, resource, null)` result where the resource
   is non-null. A null resource is treated as "not supported by this candidate" and the
   loop continues to the next candidate.
4. On an `HttpRequestException` where `TryDescribeAuthFailure` detects an HTTP 401/403,
   returns the actionable diagnostic message immediately rather than continuing to the
   next candidate — an authenticated HTTP-level rejection is definitive for this
   candidate URL.
5. On any other `HttpRequestException` from any candidate, returns a result carrying any
   actionable diagnostic already captured from an earlier candidate (or `null` if none) —
   this candidate's transient network error is not itself actionable, but must not discard
   a genuine authentication failure detected on a prior candidate.
6. On a `NuGetProtocolException` where `TryDescribeAuthFailure` detects an HTTP 401/403,
   captures the actionable diagnostic message via `??=` and tries the next candidate
   (e.g. the v2 OData fallback), the same as any other `NuGetProtocolException` — the
   protocol layer already implies the request reached the source and got a structured
   response, so a fallback candidate is still worth trying before giving up.
7. On any other `NuGetProtocolException`, captures the first (configured URL's) error
   message via `??=` and tries the next candidate. After all candidates are exhausted,
   returns the captured error message (auth-diagnostic or generic) referencing the
   original configured URL.

#### `BuildCandidateRepositories` (private)

Builds the ordered list of candidate repositories to try for a source. Returns a
single-element list `[sourceRepository]` when the source URL does not end in
`/index.json`. When the URL ends in `/index.json`, returns a two-element list of the
original repository followed by a v2 OData fallback repository constructed from the
base URL (with `/index.json` stripped), preserving the source name and credentials.
This transparently handles v2-only feeds (e.g. JFrog Artifactory) configured with a
v3-style URL.

#### `TryDownloadFromResourceAsync` (private)

Downloads and installs a package using an already-resolved `FindPackageByIdResource`.
The method:

1. Streams the `.nupkg` bytes into a `MemoryStream` using `CopyNupkgToStreamAsync`.
   Returns an empty result if the package is absent from this source; an actionable
   diagnostic result when `TryDescribeAuthFailure` detects an HTTP 401/403 in either
   a `NuGetProtocolException` or an `HttpRequestException`; a generic protocol-error
   result on any other `NuGetProtocolException`; or an empty result on any other
   `HttpRequestException` (transient network failure).
2. Installs the package into the global packages folder using
   `GlobalPackagesFolderUtility.AddPackageAsync`.
3. Returns a success result containing the conventional package path.

#### `ICredentialServiceRegistrar` / `CredentialServiceRegistrar` (private/internal)

`ICredentialServiceRegistrar` is a single-method internal interface (`EnsureRegistered()`)
that abstracts NuGet SDK credential-service registration, mirroring the existing `ISettings`
injection seam used for testability. The private `CredentialServiceRegistrar` implementation
registers the NuGet SDK's default credential service via
`DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance,
nonInteractive: true)`, memoized per instance using `Lazy<bool>` with
`LazyThreadSafetyMode.ExecutionAndPublication` so repeated calls on the same instance are
cheap and thread-safe. A single static `DefaultCredentialRegistrar` instance is shared by every
real (non-test) `EnsureCachedAsync` call, giving once-per-process registration semantics;
tests inject their own `ICredentialServiceRegistrar` test double (e.g. a call-counting spy)
via the internal overload, avoiding any dependency on shared, process-wide static test-only
state.

#### `TryDescribeAuthFailure` / `TryGetHttpStatusCode` (private)

`TryGetHttpStatusCode` walks an exception and its `InnerException` chain looking for an
HTTP 401 or 403 status, checking the strongly typed `HttpRequestException.StatusCode`
property where available (`#if !NETSTANDARD2_0`) and falling back to a compiled regular
expression matching the reason-phrase text `401 (Unauthorized)` / `403 (Forbidden)` in each
exception's message (to handle `netstandard2.0`, and cases where the SDK wraps the failure
in a `NuGetProtocolException` whose own message or inner exception carries the status text). `TryDescribeAuthFailure`
calls `TryGetHttpStatusCode` and, when a status is found, builds an actionable diagnostic
message naming the source (name and URL), the detected status, and a hint to check
`packageSourceCredentials` or a configured credential provider; both methods are used as
exception filters (`when` clauses) so 401/403 failures take a distinct, actionable path
ahead of the pre-existing generic/transient-failure catch clauses.

#### `GetPackagePath` (private)

Computes the conventional on-disk path that NuGet uses for an installed package:

```text
{globalPackagesFolder}/{packageId.lower}/{version.lower}
```

Both `packageId` and `version` are lowercased internally by this method before being
appended to `globalPackagesFolder`. Callers pass the identifiers as received and do
not need to pre-lowercase them.

Uses `PathHelpers.SafePathCombine` for both path-combination steps to guard against
any unexpected traversal sequences in package identifiers or version strings sourced
from external NuGet feeds.
