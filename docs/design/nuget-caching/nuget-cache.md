## NuGetCache Design

![NuGetCache Structure](NuGetCachingView.svg)

### Overview

The `NuGetCache` class is a public static class providing NuGet package caching
functionality for the DemaConsulting NuGet Caching library. It is a software unit
in the sense of IEC 62304 — the smallest independently testable component responsible
for ensuring a specific NuGet package version is available in the local global packages
cache before use.

`NuGetCache` is a thin orchestrator: it validates input, checks the cache-hit fast path,
registers the NuGet credential service, and enumerates configured NuGet sources, but
delegates all source-resolution, download, authentication-failure classification, and
credential-service registration detail to four sibling internal units —
`PackageSourceResolver`, `PackageDownloader`, `AuthFailureClassifier`, and
`CredentialServiceRegistrar` — described in their own design documents. This design
document therefore focuses on the orchestration responsibilities that remain in
`NuGetCache` itself; it references, rather than duplicates, the internal design detail
of the sibling units.

The class reads NuGet configuration (package sources and the global packages folder
path) from the default machine settings, mirroring the behavior of the `dotnet` CLI
and Visual Studio package restore. It communicates with configured NuGet sources using
the NuGet client SDK, via `PackageSourceResolver` and `PackageDownloader`, to download
packages when they are not already present locally.

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

The method checks for the presence of the `.nupkg.metadata` sentinel file (using the
on-disk path computed by `PackageDownloader.GetPackagePath`) before attempting any
network communication. NuGet writes this file as the final step of package extraction,
so its presence is a reliable indicator that the package is fully installed. Checking
for this file rather than the directory avoids a race condition where a
partially-extracted package directory is mistaken for a complete installation.

#### Credential Service Registration

Before resolving any `SourceRepository`, the internal `EnsureCachedAsync` overload calls
`ICredentialServiceRegistrar.EnsureRegistered()` on an injected registrar (see the
`CredentialServiceRegistrar` design document for the full registration rationale and
memoization strategy). This registers the NuGet SDK's default credential service,
mirroring the setup performed internally by the `dotnet` CLI and MSBuild restore
pipeline, so `HttpSourceAuthenticationHandler` can retry an HTTP 401 challenge using
credentials resolved from a NuGet credential-provider plugin. Static
`packageSourceCredentials` configured in `nuget.config` are applied directly to the
underlying `HttpClientHandler` by the NuGet SDK and so are honored on the first request
regardless of credential-service registration; the credential service becomes relevant
when those static credentials are absent, incorrect, or when a credential-provider
plugin must be consulted. `NuGetCache` uses `CredentialServiceRegistrar.DefaultCredentialRegistrar`
(a single static instance shared by every real, non-test call) as the default registrar for
the public and `ISettings`-only overloads, injected through the internal
`ICredentialServiceRegistrar` seam (mirroring the existing `ISettings` injection seam) so
tests can substitute a spy double instead of depending on any shared, process-wide
test-only state.

#### Package Source Mapping Support

When `PackageSourceMapping` is enabled in the NuGet configuration, `NuGetCache`
filters the set of queried sources to only those explicitly mapped to the requested
package ID. This mirrors the security and governance behavior of the NuGet toolchain,
ensuring packages are only fetched from their authorized feeds.

#### Collaboration with Sibling Units

For each enabled, mapped package source, `NuGetCache`:

1. Constructs a `SourceRepository` for the source using the V3 provider chain.
2. Calls `PackageSourceResolver.ResolveAsync` to resolve the `FindPackageByIdResource`
   for that source, including automatic v2 OData fallback and actionable 401/403
   authentication-failure diagnosis (delegated internally to `AuthFailureClassifier`).
   See the `PackageSourceResolver` design document for the full resolution algorithm,
   including the v2-fallback candidate-construction strategy and its interaction with
   resolution-time failure masking.
3. When a resource is resolved, calls `PackageDownloader.TryDownloadAsync` to download
   the `.nupkg` bytes and install the package into the global packages folder,
   including its own actionable 401/403 authentication-failure diagnosis (again
   delegated to `AuthFailureClassifier`). See the `PackageDownloader` design document
   for the full download algorithm and the on-disk package-path convention.

`NuGetCache` accumulates any diagnostic message returned by either collaborator in a
list, so that if no source ultimately provides the package, those messages can be
included in the final exception. This separation keeps `EnsureCachedAsync` at a high
level of abstraction: it orchestrates *when* resolution and download are attempted and
*how* their results feed into the final outcome, while the sibling units own *how*
resolution and download are actually performed.

If no source has the package, an `InvalidOperationException` is thrown. When at least one
source produced a diagnostic message, those messages are appended to the exception in the
form:

```text
Package 'X' version '1.0.0' was not found in any configured NuGet source.
  - https://feed/index.json: Failed to load source index. (...)
```

This allows callers to distinguish a genuine "package absent" outcome from a feed
misconfiguration, without requiring additional logging infrastructure.

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

A third, further internal overload additionally accepts an explicit
`ICredentialServiceRegistrar`, letting tests inject a spy double to directly verify
credential-service registration invocation (see "Credential Service Registration" above)
without depending on shared, process-wide static state.

### Method Descriptions

#### `EnsureCachedAsync(string packageId, string version, CancellationToken)` (public)

Thin wrapper that delegates immediately to the internal `EnsureCachedAsync` overload,
passing `Settings.LoadDefaultSettings(null)` as the `settings` argument. This ensures
the public API continues to behave identically to the pre-testability implementation
while the full logic lives in one place. See the internal overload description below
for the complete processing steps.

Returns the absolute path to the cached package folder.

Satisfies requirements `Caching-NuGetCache-EnsureCached`, `Caching-NuGetCache-NullPackageId`,
`Caching-NuGetCache-NullVersion`, `Caching-NuGetCache-InvalidVersion`,
`Caching-NuGetCache-TransientFailure`, `Caching-NuGetCache-MultiSource`,
`Caching-NuGetCache-V2Fallback`, `Caching-NuGetCache-CacheHit`, `Caching-NuGetCache-NotFound`,
`Caching-NuGetCache-HonorCredentials`, and `Caching-NuGetCache-AuthDiagnostic`.

#### `EnsureCachedAsync(string packageId, string version, ISettings settings, CancellationToken)` (internal)

Thin wrapper that delegates immediately to a further internal `EnsureCachedAsync` overload,
passing `CredentialServiceRegistrar.DefaultCredentialRegistrar` - a single static
`CredentialServiceRegistrar` instance shared by every real (non-test) call in the process -
as the `credentialRegistrar` argument. This overload exists to support testing with an
injected `ISettings`; all non-test callers reach it only via the public wrapper. See the
next overload for the complete processing steps.

#### `EnsureCachedAsync(..., ISettings, ICredentialServiceRegistrar, CancellationToken)` (internal)

Contains all orchestration logic for the public method. The method:

1. Validates that `packageId`, `version`, `settings`, and `credentialRegistrar` are not
   null, throwing `ArgumentNullException` for any null argument.
2. Parses the `version` string using `NuGetVersion.Parse`, throwing
   `ArgumentException` when the version string is not a valid NuGet version.
3. Resolves the global packages folder from the injected `settings`.
4. Computes the expected on-disk package path (via `PackageDownloader.GetPackagePath`) and
   returns it immediately if the `.nupkg.metadata` sentinel file exists (cache-hit fast
   path) - skipping the remaining steps below, including credential-service registration,
   entirely.
5. Calls `credentialRegistrar.EnsureRegistered()` so any subsequently resolved
   `SourceRepository` can consult the NuGet credential service for authenticated
   sources.
6. Iterates over enabled, mapped package sources. For each source, calls
   `PackageSourceResolver.ResolveAsync` to resolve the resource (applying v2 fallback as
   needed), then `PackageDownloader.TryDownloadAsync` to download and install the package.
   Source-level diagnostic messages are accumulated in a list.
7. Throws `InvalidOperationException` if no source provided the package. When at
   least one source returned a diagnostic message, those messages are appended to
   the exception so the caller can identify the misconfigured source.

Returns the absolute path to the cached package folder.

Satisfies requirements `Caching-NuGetCache-EnsureCached`, `Caching-NuGetCache-NullPackageId`,
`Caching-NuGetCache-NullVersion`, `Caching-NuGetCache-InvalidVersion`,
`Caching-NuGetCache-TransientFailure`, `Caching-NuGetCache-MultiSource`,
`Caching-NuGetCache-V2Fallback`, `Caching-NuGetCache-CacheHit`, `Caching-NuGetCache-NotFound`,
`Caching-NuGetCache-HonorCredentials`, and `Caching-NuGetCache-AuthDiagnostic`.
