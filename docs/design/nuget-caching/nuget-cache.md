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
simple, flat API surface for callers.

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
- **Network errors**: `HttpRequestException` (transient network error) is always silently
  swallowed — network outages on individual feeds are non-actionable.

If no source has the package, an `InvalidOperationException` is thrown. When at least one
source produced a diagnostic message, those messages are appended to the exception in the
form:

```text
Package 'X' version '1.0.0' was not found in any configured NuGet source.
  - https://feed/index.json: Failed to load source index. (...)
```

This allows callers to distinguish a genuine "package absent" outcome from a feed
misconfiguration, without requiring additional logging infrastructure.

#### Separation of Private Helpers

Five private members encapsulate distinct sub-responsibilities:

- `TryDownloadResult` — a private record struct pairing an optional package path with
  an optional diagnostic error message, allowing helpers to communicate both success
  and actionable failure details without using out-parameters or exceptions.
- `TryDownloadPackageAsync` — thin coordinator that resolves the resource (via
  `GetFindPackageByIdResourceAsync`) and then delegates the download and install to
  `TryDownloadFromResourceAsync`.
- `GetFindPackageByIdResourceAsync` — resolves the `FindPackageByIdResource` for a
  source, applying the automatic v2 OData fallback when a `/index.json` URL fails with
  a `NuGetProtocolException`. Returns the effective repository (original or v2
  fallback), the resolved resource, and any diagnostic error message.
- `TryDownloadFromResourceAsync` — streams the `.nupkg` bytes and installs the package
  into the global packages folder using an already-resolved resource.
- `GetPackagePath` — the conventional on-disk path calculation that NuGet uses for
  installed packages (`{globalPackagesFolder}/{id.lower}/{version.lower}`).

This separation keeps `EnsureCachedAsync` at a high level of abstraction, eliminates
any recursion in the download path, and makes each sub-task individually readable.

### Method Descriptions

#### `EnsureCachedAsync(string packageId, string version, CancellationToken)`

Ensures a specific NuGet package version is available in the local global packages
cache. The method:

1. Validates that `packageId` and `version` are not null, throwing
   `ArgumentNullException` for either null argument.
2. Parses the `version` string using `NuGetVersion.Parse`, throwing
   `ArgumentException` when the version string is not a valid NuGet version.
3. Loads the default NuGet settings and resolves the global packages folder.
4. Computes the expected on-disk package path and returns it immediately if the
   `.nupkg.metadata` sentinel file exists (cache-hit fast path).
5. Iterates over enabled, mapped package sources and delegates to
   `TryDownloadPackageAsync` for each one until a download succeeds. Source-level
   diagnostic messages from `TryDownloadPackageAsync` are accumulated in a list.
6. Throws `InvalidOperationException` if no source provided the package. When at
   least one source returned a diagnostic message, those messages are appended to
   the exception so the caller can identify the misconfigured source.

Returns the absolute path to the cached package folder.

Satisfies requirements `Caching-NuGetCache-EnsureCached`, `Caching-NuGetCache-NullValidation`, and `Caching-NuGetCache-NotFound`.

#### `TryDownloadPackageAsync` (private)

Thin coordinator. Calls `GetFindPackageByIdResourceAsync` to resolve the
`FindPackageByIdResource` for the source (applying the v2 fallback as needed), then
calls `TryDownloadFromResourceAsync` to stream and install the package. Returns the
`TryDownloadResult` from whichever step determines the outcome.

#### `GetFindPackageByIdResourceAsync` (private)

Resolves the `FindPackageByIdResource` for a source repository, with automatic v2 OData
fallback when a v3 `/index.json` URL fails with a `NuGetProtocolException`. The method:

1. Calls `GetResourceAsync<FindPackageByIdResource>` on the source repository.
2. On success, returns a tuple of `(sourceRepository, resource, null)`.
3. On `NuGetProtocolException`:
   - If the source URL ends in `/index.json`, constructs a fallback repository with
     the base URL (stripping `/index.json`), preserving the source name and credentials,
     and calls `GetResourceAsync` again.
   - If the fallback succeeds, returns `(fallbackRepository, resource, null)`.
   - If the fallback also throws `NuGetProtocolException`, falls through to return an
     error result referencing the original configured URL.
   - If the fallback throws `HttpRequestException`, returns a silent skip result.
   - If the source URL does not end in `/index.json`, returns an error result.
4. On `HttpRequestException` (either the original or the fallback), returns a silent
   skip result — transient network errors are not actionable.

#### `TryDownloadFromResourceAsync` (private)

Downloads and installs a package using an already-resolved `FindPackageByIdResource`.
The method:

1. Streams the `.nupkg` bytes into a `MemoryStream` using `CopyNupkgToStreamAsync`.
   Returns an empty result if the package is absent from this source, an error result
   on `NuGetProtocolException`, or an empty result on `HttpRequestException`.
2. Installs the package into the global packages folder using
   `GlobalPackagesFolderUtility.AddPackageAsync`.
3. Returns a success result containing the conventional package path.

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
