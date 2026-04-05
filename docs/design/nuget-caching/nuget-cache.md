# NuGetCache Design

## Overview

The `NuGetCache` class is a public static class providing NuGet package caching
functionality for the DemaConsulting NuGet Caching library. It is a software unit
in the sense of IEC 62304 — the smallest independently testable component responsible
for ensuring a specific NuGet package version is available in the local global packages
cache before use.

The class reads NuGet configuration (package sources and the global packages folder
path) from the default machine settings, mirroring the behavior of the `dotnet` CLI
and Visual Studio package restore. It communicates with configured NuGet sources using
the NuGet client SDK to download packages when they are not already present locally.

## Design Decisions

### Static Class

`NuGetCache` is designed as a static class because it provides a service — ensuring
a package is cached — that does not require instance state. All configuration is
read from the machine-level NuGet settings on each call, making the class naturally
stateless. A static class avoids unnecessary object instantiation and provides a
simple, flat API surface for callers.

### Async Approach

The primary method `EnsureCachedAsync` is asynchronous because NuGet source
communication involves network I/O. Using `async`/`await` throughout allows the
calling thread to be returned to the thread pool during network waits, keeping the
library cooperative in concurrent or UI-driven environments.

### NuGet Settings Integration

Rather than accepting source URIs directly, `NuGetCache` reads from the default
NuGet settings on the local machine via `Settings.LoadDefaultSettings(null)`. This
ensures the library respects the same `nuget.config` hierarchy (machine-wide, user,
and project-level) as the `dotnet` CLI and Visual Studio, including authenticated
feeds, proxy settings, and package source mapping.

### Early-Exit on Cache Hit

The method checks for the presence of the `.nupkg.metadata` sentinel file before
attempting any network communication. NuGet writes this file as the final step of
package extraction, so its presence is a reliable indicator that the package is
fully installed. Checking for this file rather than the directory avoids a race
condition where a partially-extracted package directory is mistaken for a complete
installation.

### Package Source Mapping Support

When `PackageSourceMapping` is enabled in the NuGet configuration, `NuGetCache`
filters the set of queried sources to only those explicitly mapped to the requested
package ID. This mirrors the security and governance behavior of the NuGet toolchain,
ensuring packages are only fetched from their authorized feeds.

### Resilient Source Enumeration

Sources are queried sequentially. If a source is unreachable (`HttpRequestException`)
or does not support the required NuGet protocol (`NuGetProtocolException`), the error
is silently swallowed and the next source is tried. This design tolerates transient
network failures and misconfigured feeds without propagating exceptions to the caller,
as long as at least one source carries the requested package. If no source has the
package, an `InvalidOperationException` is thrown with a descriptive message.

Note: The current implementation uses `NullLogger.Instance` throughout, which means
per-source failures leave no diagnostic trace. Callers that need visibility into
which sources were tried — for troubleshooting feed configuration or intermittent
network issues — can wrap `EnsureCachedAsync` and correlate the
`InvalidOperationException` message against their `nuget.config` source list.

### Separation of Private Helpers

Two private methods encapsulate distinct sub-responsibilities:

- `TryDownloadPackageAsync` — all logic for querying and downloading from a single
  NuGet source repository.
- `GetPackagePath` — the conventional on-disk path calculation that NuGet uses for
  installed packages (`{globalPackagesFolder}/{id.lower}/{version.lower}`).

This separation keeps `EnsureCachedAsync` at a high level of abstraction and makes
each sub-task individually readable.

## Method Descriptions

### `EnsureCachedAsync(string packageId, string version, CancellationToken)`

Ensures a specific NuGet package version is available in the local global packages
cache. The method:

1. Validates that `packageId` and `version` are not null, throwing
   `ArgumentNullException` for either null argument.
2. Loads the default NuGet settings and resolves the global packages folder.
3. Computes the expected on-disk package path and returns it immediately if the
   `.nupkg.metadata` sentinel file exists (cache-hit fast path).
4. Iterates over enabled, mapped package sources and delegates to
   `TryDownloadPackageAsync` for each one until a download succeeds.
5. Throws `InvalidOperationException` if no source provided the package.

Returns the absolute path to the cached package folder.

Satisfies requirements `Caching-Lib-EnsureCached`, `Caching-Lib-NullValidation`, and `Caching-Lib-NotFound`.

### `TryDownloadPackageAsync` (private)

Attempts to download a NuGet package from a single `SourceRepository`. The method:

1. Obtains a `FindPackageByIdResource` from the source repository, returning `null`
   if the source does not support the protocol or cannot be reached.
2. Streams the `.nupkg` bytes into a `MemoryStream` using `CopyNupkgToStreamAsync`,
   returning `null` if the package is absent from this source or a protocol/network
   error occurs.
3. Installs the package into the global packages folder using
   `GlobalPackagesFolderUtility.AddPackageAsync`.
4. Returns the conventional package path on success.

### `GetPackagePath` (private)

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
