## PackageDownloader Design

![PackageDownloader Structure](NuGetCachingView.svg)

### Purpose

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

### Data Model

The class holds no instance state. Its only data type is the immutable `TryDownloadResult`
record struct below; the invariant is that `PackagePath` is non-null only when the package was
installed successfully.

#### TryDownloadResult Record Struct

```csharp
internal readonly record struct TryDownloadResult(string? PackagePath, string? ErrorMessage)
{
    internal AuthFailureInfo? AuthFailure { get; init; }
}
```

Represents the result of a single package download attempt from one NuGet source:

- `PackagePath` — the absolute path to the installed package folder, or `null` if the package
  was not available or could not be downloaded from this source.
- `ErrorMessage` — a diagnostic message describing a source-level failure (e.g. protocol
  mismatch), or `null` when the source was reachable but simply did not carry the requested
  package, or when the failure is transient and non-actionable.
- `AuthFailure` — an `AuthFailureInfo` (source name and URL) set only when the failure was
  classified as HTTP 401/403; `null` otherwise. It is an init-only property rather than a
  positional parameter so existing deconstruction `var (path, error) = ...` keeps working.

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
    CancellationToken cancellationToken,
    ILogger? logger = null)
```

Downloads and installs a NuGet package using an already-resolved `FindPackageByIdResource`. The
optional `logger` receives NuGet SDK messages from the download and install calls; when `null`,
`NullLogger.Instance` is used so messages are discarded and behavior matches earlier versions.

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

### Key Methods

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
    CancellationToken cancellationToken,
    ILogger? logger = null)
```

Downloads and installs a package using an already-resolved `FindPackageByIdResource`. The
`logger` is passed to the NuGet download and install calls. The
method:

1. Streams the `.nupkg` bytes into a `MemoryStream` using `CopyNupkgToStreamAsync`. Returns an
   empty result if the package is absent from this source; an actionable diagnostic result
   (with `AuthFailure` set to an `AuthFailureInfo` of the source name and URL) when
   `AuthFailureClassifier.TryDescribeAuthFailure` detects an HTTP 401/403 in either a
   `NuGetProtocolException` or an `HttpRequestException`; a generic protocol-error result on any
   other `NuGetProtocolException`; or an empty result on any other `HttpRequestException`
   (transient network failure).
2. When the client policy is `require`, explicitly verifies the downloaded bytes with `VerifySignedAsync`
   (see below) before anything is installed, so unsigned or invalid packages fail closed on every platform
   (NuGet's own check is skipped on Linux/macOS unless `DOTNET_NUGET_SIGNATURE_VERIFICATION=true`).
3. Installs the package into the global packages folder using
   `GlobalPackagesFolderUtility.AddPackageAsync`.
4. Returns a success result containing the conventional package path (computed via
   `GetPackagePath`).

Satisfies requirements `Caching-PackageDownloader-DownloadOutcome`,
`Caching-PackageDownloader-AuthDiagnostic`, and `Caching-PackageDownloader-AuthFailureInfo`.

Satisfies requirement `Caching-PackageDownloader-SignatureRequirement`.

#### `CreateClientPolicy(settings, requireSigned, logger)`

Returns `ClientPolicyContext.GetClientPolicy(settings, logger)`; when `requireSigned` is true the settings
are first wrapped in `RequireSignedSettings`, an `ISettings` decorator whose `config` section carries
`signatureValidationMode=require` (loaded once from a short-lived temporary `nuget.config` because NuGet's
in-memory section types are internal) while every other section, including `trustedSigners`, passes through
to the inner settings. Mutating members throw `NotSupportedException`.

#### `VerifyCachedPackageSignedAsync(...)`

Used on the cache-hit path when the effective policy requires signatures (the option, or
`signatureValidationMode=require` in `nuget.config`). Opens the installed `.nupkg` and applies
`VerifySignedAsync`; throws `InvalidOperationException` when the file is missing, unsigned or invalid.

#### `VerifySignedAsync(...)`

Platform-independent explicit check shared by the download and cache-hit paths. Requires
`PackageArchiveReader.IsSignedAsync` to be true, then runs a `PackageSignatureVerifier` with integrity and
trust/validity providers, plus the client `trustedSigners` allow-list provider only when signers are configured
(no signers means any validly signed, trusted package is accepted), mirroring
NuGet's own extraction-time providers. Throws `InvalidOperationException` when unsigned or invalid.

Trust roots and revocation: the trust/validity provider builds certificate chains for the signing and
timestamp certificates against the operating system trust store, so a signature is accepted only where the
signer's root (for nuget.org packages, a DigiCert root) is trusted by the OS, and it checks revocation online
by default. Setting the `NUGET_CERT_REVOCATION_MODE` environment variable to `offline` makes NuGet use only
cached revocation data; an unknown revocation status is then reported as a warning, not an error. The
library does not change these settings, so callers in restricted-network environments can choose the mode.

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

### Error Handling

- **Handled locally**: `NuGetProtocolException` and `HttpRequestException` raised while
  copying the `.nupkg` are converted to a `TryDownloadResult` (actionable 401/403 diagnostic,
  generic protocol-error message, or an empty result for transient network failures) so the
  caller can continue to the next source.
- **Propagated**: `OperationCanceledException` and any other exception, including those from
  `GlobalPackagesFolderUtility.AddPackageAsync`, propagate to the caller. A NuGet `SignatureException`
  is rethrown as `InvalidOperationException` naming the package, version and source; it is not treated as
  "not in this source", so the next source is not tried.
- **`GetPackagePath`**: propagates `ArgumentException` from `PathHelpers.SafePathCombine` when
  an identifier or version would escape the global packages folder.

### Dependencies

- `AuthFailureClassifier` (authentication-failure diagnosis).
- `PathHelpers` (safe path combination).
- **NuGet** (OTS): `FindPackageByIdResource`, `GlobalPackagesFolderUtility`, `SourceRepository`,
  `SourceCacheContext`, and `ClientPolicyContext`.

### Callers

- `NuGetCache`: `TryDownloadAsync` during source enumeration and `GetPackagePath` for the
  cache-hit fast-path check.
