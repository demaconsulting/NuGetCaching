## PackageDownloader Design

![PackageDownloader Structure](NuGetCachingView.svg)

### Overview

`PackageDownloader` is an internal static class that downloads a NuGet package using an
already-resolved `FindPackageByIdResource` and installs it into the global packages folder, in
the DemaConsulting NuGet Caching library. It also owns the on-disk package-path convention used
to locate installed packages, exposed so both `NuGetCache` (for its cache-hit fast-path check)
and this class itself (after a successful install) compute the identical path.

The class is marked `internal` because it is an implementation detail of the library and is not
part of the public API surface. It owns download and installation concerns only; resolving the
`FindPackageByIdResource` to download from is the responsibility of `PackageSourceResolver`.
Authentication-failure diagnosis during download is delegated to `AuthFailureClassifier` so that
logic is not duplicated between resolution and download.

### Class Structure

#### TryDownloadResult Record Struct

```csharp
internal readonly record struct TryDownloadResult(string? PackagePath, string? ErrorMessage);
```

Represents the result of a single package download attempt from one NuGet source:

- `PackagePath` — the absolute path to the installed package folder, or `null` if the package
  was not available or could not be downloaded from this source.
- `ErrorMessage` — a diagnostic message describing a source-level failure (e.g. protocol
  mismatch), or `null` when the source was reachable but simply did not carry the requested
  package, or when the failure is transient and non-actionable.

#### TryDownloadAsync Method

```csharp
internal static async Task<TryDownloadResult> TryDownloadAsync(
    SourceRepository sourceRepository,
    FindPackageByIdResource resource,
    string packageId,
    NuGetVersion version,
    string globalPackagesFolder,
    ClientPolicyContext clientPolicyContext,
    SourceCacheContext cacheContext,
    CancellationToken cancellationToken)
```

Downloads and installs a NuGet package using an already-resolved `FindPackageByIdResource`.

#### GetPackagePath Method

```csharp
internal static string GetPackagePath(string globalPackagesFolder, string packageId, string version)
```

Gets the conventional on-disk path for a cached NuGet package.

### Design Decisions

#### Static Class

`PackageDownloader` provides a stateless service — downloading and installing a package given an
already-resolved resource — that requires no instance state. A static class avoids unnecessary
object instantiation and keeps the API surface flat, mirroring the design of `PathHelpers` and
`PackageSourceResolver`.

#### Record Struct Result Type

`TryDownloadResult` replaces the tuple previously returned inline in `NuGetCache` with a named
record struct, giving the two result fields self-documenting names at every call site while
still supporting `var (path, error) = ...` deconstruction, so `NuGetCache` did not need to
change its consumption pattern.

#### Actionable 401/403 Diagnostics During Download

The NuGet SDK does not consistently expose the failing HTTP status code as a strongly typed
property, and authentication failures are frequently wrapped in a `NuGetProtocolException`
whose message — or whose `InnerException`'s message — simply embeds text such as `"Response
status code does not indicate success: 401 (Unauthorized)."`. `AuthFailureClassifier` is
consulted uniformly at this call site (as it is in `PackageSourceResolver`) since either the
resolution or the download step may be the one an authenticated feed rejects: for a v3
`/index.json` source, resolution-time authentication failures are frequently masked by
`PackageSourceResolver`'s v2 fallback candidate (see the "Resolution-Time Failures Are Not
Always Observable" design note in the `PackageSourceResolver` design document), so download is
often where the actionable diagnostic is actually surfaced.

#### `GetPackagePath` Exposed as Internal

`GetPackagePath` is declared `internal` (rather than `private`) specifically so `NuGetCache` can
call `PackageDownloader.GetPackagePath(...)` directly for its cache-hit fast-path check, instead
of duplicating the `{globalPackagesFolder}/{packageId.lower}/{normalizedVersion.lower}` path
convention in two places. This keeps the on-disk path convention owned by a single unit.

#### Extraction from NuGetCache

This class was extracted verbatim from the original `NuGetCache.TryDownloadFromResourceAsync`
and `NuGetCache.GetPackagePath` private methods, and the `NuGetCache.TryDownloadResult` private
record struct, as a pure structural refactor: the download algorithm, exception handling, and
path-computation logic are unchanged. The method was renamed from
`TryDownloadFromResourceAsync` to `TryDownloadAsync` to read naturally as
`PackageDownloader.TryDownloadAsync(...)` at the call site in `NuGetCache`.

### Method Descriptions

#### `TryDownloadAsync(...)`

Full signature:

```csharp
internal static async Task<TryDownloadResult> TryDownloadAsync(
    SourceRepository sourceRepository,
    FindPackageByIdResource resource,
    string packageId,
    NuGetVersion version,
    string globalPackagesFolder,
    ClientPolicyContext clientPolicyContext,
    SourceCacheContext cacheContext,
    CancellationToken cancellationToken)
```

Downloads and installs a package using an already-resolved `FindPackageByIdResource`. The
method:

1. Streams the `.nupkg` bytes into a `MemoryStream` using `CopyNupkgToStreamAsync`. Returns an
   empty result if the package is absent from this source; an actionable diagnostic result when
   `AuthFailureClassifier.TryDescribeAuthFailure` detects an HTTP 401/403 in either a
   `NuGetProtocolException` or an `HttpRequestException`; a generic protocol-error result on any
   other `NuGetProtocolException`; or an empty result on any other `HttpRequestException`
   (transient network failure).
2. Installs the package into the global packages folder using
   `GlobalPackagesFolderUtility.AddPackageAsync`.
3. Returns a success result containing the conventional package path (computed via
   `GetPackagePath`).

Satisfies requirements `Caching-PackageDownloader-DownloadOutcome` and
`Caching-PackageDownloader-AuthDiagnostic`.

#### `GetPackagePath(string globalPackagesFolder, string packageId, string version)`

Computes the conventional on-disk path that NuGet uses for an installed package:

```text
{globalPackagesFolder}/{packageId.lower}/{version.lower}
```

Both `packageId` and `version` are lowercased internally by this method before being appended to
`globalPackagesFolder`. Callers pass the identifiers as received and do not need to
pre-lowercase them.

Uses `PathHelpers.SafePathCombine` for both path-combination steps to guard against any
unexpected traversal sequences in package identifiers or version strings sourced from external
NuGet feeds.

Satisfies requirement `Caching-PackageDownloader-PackagePathConvention`.
