## PackageDownloader Unit Verification

This document provides the verification design for the `PackageDownloader` unit.
Requirements for this unit are defined in the PackageDownloader Unit Requirements document.

### Required Functionality

The `PackageDownloader` unit shall download and install a package using an already-resolved
`FindPackageByIdResource`, returning the on-disk package path on success, an empty result when
the package is absent or the failure is transient, or an actionable diagnostic message when the
source rejects the request with HTTP 401 or 403 (as separately verified unit requirements); and
shall compute the conventional on-disk package path.

### Verification Approach

Unit requirements are verified by local-integration tests that resolve a real
`FindPackageByIdResource` against a local `NuGetTestServer` (WireMock) - using the same
production resolution path (`PackageSourceResolver.ResolveAsync`, including v2 fallback
candidate construction) as `NuGetCache` - then call `PackageDownloader.TryDownloadAsync`
directly, focusing on behaviors owned specifically by this unit rather than the full
source-enumeration flow, which is covered by `NuGetCacheServerTests`. A pure unit test (no
WireMock) additionally verifies the `GetPackagePath` path convention directly. Each test
scenario names a specific test method that provides evidence for one or more unit requirements.

### Test Scenarios

#### PackageDownloader_TryDownloadAsync_PackageAvailable_ReturnsInstalledPackagePath

**Scenario**: `TryDownloadAsync` is called with a resolved resource against a WireMock v3 feed
that serves the requested package's `.nupkg` bytes.

**Expected**: Returns a `PackagePath` equal to the `GetPackagePath` convention, a `null`
`ErrorMessage`, and a real, fully installed package directory containing the
`.nupkg.metadata` sentinel file.

**Requirement coverage**: `Caching-PackageDownloader-DownloadOutcome`.

#### PackageDownloader_TryDownloadAsync_PackageAbsent_ReturnsEmptyResult

**Scenario**: `TryDownloadAsync` is called requesting a package identity the feed does not
carry (a different package is registered, so the feed is reachable but does not serve this
identity).

**Expected**: Returns an empty result (both `PackagePath` and `ErrorMessage` `null`), confirming
absence is not treated as an error.

**Requirement coverage**: `Caching-PackageDownloader-DownloadOutcome`.

#### PackageDownloader_TryDownloadAsync_NetworkOrProtocolFailureDuringDownload_ReturnsEmptyResult

**Scenario** (parameterized over two cases): the `.nupkg` download endpoint either returns HTTP
500 or drops the connection, after the service index and version list succeed normally. Both
scenarios surface identically as `HttpRequestException` from `CopyNupkgToStreamAsync`.

**Expected**: In both cases, returns an empty result (both `PackagePath` and `ErrorMessage`
`null`), confirming transient network-level failures during download are silently swallowed
rather than surfaced as a diagnostic.

**Requirement coverage**: `Caching-PackageDownloader-DownloadOutcome`.

#### PackageDownloader_TryDownloadAsync_AuthenticationFailureDuringDownload_ReturnsActionableErrorMessage

**Scenario**: A WireMock feed requires HTTP Basic Auth on every endpoint (including the
service index); no credentials are supplied. The resource is resolved through
`PackageSourceResolver.ResolveAsync`'s production v2-fallback logic, which - per
`PackageSourceResolverTests.PackageSourceResolver_ResolveAsync_NetworkFailureOnIndex_DoesNotThrowAndReturnsNoErrorMessage`

- masks the index-fetch authentication failure by resolving the v2 fallback candidate without
performing any HTTP request. `TryDownloadAsync` is then called with that masked resource,
performing the first real HTTP request against the feed.

**Expected**: Returns a `null` `PackagePath` and a non-null `ErrorMessage` containing both the
configured source name (`test-source`) and the detected HTTP status code (`401`), confirming the
authentication failure - deferred past resolution - reliably surfaces once a real HTTP request is
made during download.

**Requirement coverage**: `Caching-PackageDownloader-AuthDiagnostic`.

#### PackageDownloader_GetPackagePath_MixedCaseIdAndVersion_ReturnsLowerCasedPath

**Scenario**: `GetPackagePath` is called with a mixed-case package identifier and version
string.

**Expected**: Returns `{globalPackagesFolder}/{packageId.lower}/{version.lower}`, confirming
both segments are lower-cased following the NuGet global packages folder convention.

**Requirement coverage**: `Caching-PackageDownloader-PackagePathConvention`.

### Requirements Coverage

- **`Caching-PackageDownloader-DownloadOutcome`**:
  PackageDownloader_TryDownloadAsync_PackageAvailable_ReturnsInstalledPackagePath,
  PackageDownloader_TryDownloadAsync_PackageAbsent_ReturnsEmptyResult,
  PackageDownloader_TryDownloadAsync_NetworkOrProtocolFailureDuringDownload_ReturnsEmptyResult
- **`Caching-PackageDownloader-AuthDiagnostic`**:
  PackageDownloader_TryDownloadAsync_AuthenticationFailureDuringDownload_ReturnsActionableErrorMessage
- **`Caching-PackageDownloader-PackagePathConvention`**:
  PackageDownloader_GetPackagePath_MixedCaseIdAndVersion_ReturnsLowerCasedPath
