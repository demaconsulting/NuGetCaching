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

#### Resilient Source Enumeration with Diagnostic Reporting

Sources are queried sequentially. If a source fails with a `NuGetProtocolException` (e.g.
a v2-only feed configured with a v3 `.json` URL), the error message is captured and
accumulated so it can be appended to the final `InvalidOperationException`. If a source
fails with an `HttpRequestException` (transient network error), the failure is silently
swallowed and the next source is tried — network outages on individual feeds should not
surface actionable errors to the caller.

If no source has the package, an `InvalidOperationException` is thrown. When at least one
source produced a diagnostic message, those messages are appended to the exception in the
form:

```text
Package 'X' version '1.0.0' was not found in any configured NuGet source.
  - https://feed/index.json: Failed to load source index. The feed may be v2-only; ...
```

This allows callers to distinguish a genuine "package absent" outcome from a feed
misconfiguration (such as a JFrog Artifactory v2-only endpoint addressed with a v3 URL)
without requiring additional logging infrastructure.

#### Separation of Private Helpers

Three private members encapsulate distinct sub-responsibilities:

- `TryDownloadResult` — a private record struct pairing an optional package path with
  an optional diagnostic error message, so `TryDownloadPackageAsync` can communicate
  both success and actionable failure details to its caller without using out-parameters
  or exceptions.
- `TryDownloadPackageAsync` — all logic for querying and downloading from a single
  NuGet source repository.
- `GetPackagePath` — the conventional on-disk path calculation that NuGet uses for
  installed packages (`{globalPackagesFolder}/{id.lower}/{version.lower}`).

This separation keeps `EnsureCachedAsync` at a high level of abstraction and makes
each sub-task individually readable.

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

Attempts to download a NuGet package from a single `SourceRepository`. Returns a
`TryDownloadResult` value whose fields carry either the installed package path (on
success) or a diagnostic error message (on an actionable failure). The method:

1. Obtains a `FindPackageByIdResource` from the source repository. On
   `NuGetProtocolException` (e.g. v2-only feed at a v3 URL), returns an error result
   with a diagnostic message. On `HttpRequestException` (transient network error) or a
   null resource, returns an empty result so the caller silently tries the next source.
2. Streams the `.nupkg` bytes into a `MemoryStream` using `CopyNupkgToStreamAsync`,
   returning an empty result if the package is absent from this source, an error result
   on `NuGetProtocolException`, or an empty result on `HttpRequestException`.
3. Installs the package into the global packages folder using
   `GlobalPackagesFolderUtility.AddPackageAsync`.
4. Returns a success result containing the conventional package path.

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
