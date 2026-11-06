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

### Test Environment

A local `NuGetTestServer` (WireMock) listening on a random loopback port, and a disposable
temporary global packages folder per test; no internet access is required. The `GetPackagePath`
test is a pure unit test with no environment needs.

### Acceptance Criteria

All test scenarios below pass on every supported target framework: a package that is available is
installed and its conventional path returned, absent and failing sources produce the documented
empty or diagnostic results, and the package-path convention matches the NuGet layout.

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

**Scenario**: A WireMock feed serves a public service index and version list, but its `.nupkg`
endpoint answers HTTP 401. A dispatching credential scope with no credentials is active, so
the credential service cancels the rejected request with its unsatisfied-challenge exception. NuGet's
flat-container download loop swallows that exception and reports "not found"; `TryDownloadAsync`
therefore consults the challenge recorded for the call and reports the authentication failure.

**Expected**: Returns a `null` `PackagePath` and a non-null `ErrorMessage` containing both the
configured source name (`test-source`) and the detected HTTP status code (`401`).

**Note**: NuGet's own download retries (about 5 s) still run
before the failure is reported. The test runs on every target framework, including .NET Framework.

**Requirement coverage**: `Caching-PackageDownloader-AuthDiagnostic`.

#### PackageDownloader_TryDownloadAsync_AuthenticationFailureDuringDownload_ReturnsAuthFailureInfo

**Scenario**: Same Basic-Auth-protected WireMock feed and masked-resolution setup as the previous
scenario, with an explicit `NullLogger` passed through the optional `logger` parameter.

**Expected**: Returns a `null` `PackagePath` and a non-null `AuthFailure` whose `SourceName` is
`test-source` and whose `SourceUrl` is non-empty. Passing a logger does not change the outcome.

**Requirement coverage**: `Caching-PackageDownloader-AuthFailureInfo`.

**Deviation note**: Forwarding of NuGet messages through the `logger` parameter to the caller's
logger is verified end to end at the `NuGetCache` level (`Caching-NuGetCache-Logging`,
`NuGetCache_EnsureCachedAsync_Logger_ReceivesMessagesWithoutSecret`) rather than by a separate
unit scenario, because the parameter is a pass-through to NuGet SDK calls.

#### PackageDownloader_GetPackagePath_MixedCaseIdAndVersion_ReturnsLowerCasedPath

**Scenario**: `GetPackagePath` is called with a mixed-case package identifier and version
string.

**Expected**: Returns `{globalPackagesFolder}/{packageId.lower}/{version.lower}`, confirming
both segments are lower-cased following the NuGet global packages folder convention.

**Requirement coverage**: `Caching-PackageDownloader-PackagePathConvention`.

#### Signature requirement scenarios

The signature tests live in `NuGetCacheSignatureTests` and exercise this unit through `NuGetCache`:

- **NuGetCache_RequireSignedPackages_UnsignedPackageDownload_ThrowsInvalidOperationException**: an unsigned
  package from a WireMock feed is rejected with `InvalidOperationException` and not installed. This holds on
  every OS because the explicit check does not depend on NuGet's platform-gated verification.
- **NuGetCache_NuGetConfigRequireMode_UnsignedPackageDownload_ThrowsInvalidOperationException**: the same
  rejection when `signatureValidationMode=require` comes from `nuget.config` and the option is not set.
- **NuGetCache_RequireSignedPackages_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit**: an unsigned
  package already installed is rejected on the cache-hit path.
- **NuGetCache_NuGetConfigRequireMode_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit**: the cache-hit
  check also applies when only `nuget.config` requires signatures.
- **NuGetCache_RequireSignedPackages_InvalidlySignedPackageDownload_ThrowsInvalidOperationException** and
  **NuGetCache_RequireSignedPackages_InvalidlySignedPackageAlreadyInstalled_RejectedOnCacheHit**: a package
  with a corrupt signature entry is rejected on download (not installed) and on cache hit.
- **NuGetCache_RequireSignedPackages_SignedPackageDownload_Succeeds**: a real, unmodified package from
  nuget.org (Newtonsoft.Json 13.0.1, signed by its author and countersigned by the nuget.org repository;
  downloaded from nuget.org once per test run, with no binary checked in) is accepted with no trusted
  signers configured and fully installed.
- **NuGetCache_RequireSignedPackages_TrustedAuthorSignerMatches_Succeeds** and
  **NuGetCache_RequireSignedPackages_TrustedRepositorySignerMatches_Succeeds**: the same package is accepted
  when `nuget.config` lists its author certificate, or its repository certificate, as a trusted signer.
- **NuGetCache_RequireSignedPackages_TrustedSignerDoesNotMatch_ThrowsInvalidOperationException**: the
  package is rejected and not installed when trusted signers are configured and none match.
- **NuGetCache_RequireSignedPackages_SignedPackageAlreadyInstalled_AcceptedOnCacheHit** and
  **NuGetCache_RequireSignedPackages_SignedPackageAlreadyInstalled_SignerMismatchRejectedOnCacheHit**: the
  installed signed package passes the cache-hit check, and is rejected when the trusted signers do not match.

The signed-package tests are network-dependent: the package is downloaded once per test run from
`https://api.nuget.org/v3-flatcontainer/` into a temporary folder (removed when the run ends) and verified
against a SHA-256 pinned in the test; a hash mismatch fails the tests because it would indicate tampering,
while a download failure (offline, network error, timeout) skips them with a clear reason. They also set
`NUGET_CERT_REVOCATION_MODE=offline` (in a non-parallel test collection) so they
never need network access for revocation checking; the package's certificates are expired, so acceptance
rests on the signature timestamps. Before each test, a one-time preflight checks that the operating system
trusts the DigiCert roots the package chains to (revocation and validity dates ignored). If it does not
(for example a Linux distribution that has removed the legacy DigiCert Assured ID and High Assurance EV
roots), the test is skipped with that reason rather than failing; this is an environment gap, not a
product defect. There is no OS-based skip.

**Requirement coverage**: `Caching-PackageDownloader-SignatureRequirement`.

### Requirements Coverage

- **`Caching-PackageDownloader-DownloadOutcome`**:
  PackageDownloader_TryDownloadAsync_PackageAvailable_ReturnsInstalledPackagePath,
  PackageDownloader_TryDownloadAsync_PackageAbsent_ReturnsEmptyResult,
  PackageDownloader_TryDownloadAsync_NetworkOrProtocolFailureDuringDownload_ReturnsEmptyResult
- **`Caching-PackageDownloader-AuthDiagnostic`**:
  PackageDownloader_TryDownloadAsync_AuthenticationFailureDuringDownload_ReturnsActionableErrorMessage
- **`Caching-PackageDownloader-AuthFailureInfo`**:
  PackageDownloader_TryDownloadAsync_AuthenticationFailureDuringDownload_ReturnsAuthFailureInfo
- **`Caching-PackageDownloader-PackagePathConvention`**:
  PackageDownloader_GetPackagePath_MixedCaseIdAndVersion_ReturnsLowerCasedPath
- **`Caching-PackageDownloader-SignatureRequirement`**:
  NuGetCache_RequireSignedPackages_UnsignedPackageDownload_ThrowsInvalidOperationException,
  NuGetCache_NuGetConfigRequireMode_UnsignedPackageDownload_ThrowsInvalidOperationException,
  NuGetCache_RequireSignedPackages_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit,
  NuGetCache_NuGetConfigRequireMode_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit,
  NuGetCache_RequireSignedPackages_InvalidlySignedPackageDownload_ThrowsInvalidOperationException,
  NuGetCache_RequireSignedPackages_InvalidlySignedPackageAlreadyInstalled_RejectedOnCacheHit,
  NuGetCache_RequireSignedPackages_SignedPackageDownload_Succeeds,
  NuGetCache_RequireSignedPackages_TrustedAuthorSignerMatches_Succeeds,
  NuGetCache_RequireSignedPackages_TrustedRepositorySignerMatches_Succeeds,
  NuGetCache_RequireSignedPackages_TrustedSignerDoesNotMatch_ThrowsInvalidOperationException,
  NuGetCache_RequireSignedPackages_SignedPackageAlreadyInstalled_AcceptedOnCacheHit,
  NuGetCache_RequireSignedPackages_SignedPackageAlreadyInstalled_SignerMismatchRejectedOnCacheHit
