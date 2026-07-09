## NuGetCache Unit Verification

This document provides the verification design for the `NuGetCache` unit. Requirements for this
unit are defined in the NuGetCache Unit Requirements document.

### Required Functionality

The `NuGetCache` unit shall ensure a specified NuGet package version is available in the local
cache, reject null arguments, and report an error when the requested package cannot be found in
any configured NuGet source.

### Verification Approach

Unit requirements are verified by two complementary sets of tests, both exercising
`NuGetCache.EnsureCachedAsync`:

- **Live integration tests** — test against real NuGet infrastructure; prove the end-to-end
  download path works against a production feed.
- **Controlled integration tests** — test against a local WireMock server; prove every internal
  branch (v2 fallback, protocol errors, network failures, multi-source enumeration, cache-hit
  fast path) without requiring internet access.

Each test scenario names a specific test method that provides evidence for one or more unit
requirements.

### Test Scenarios

#### NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder

**Scenario**: `EnsureCachedAsync` is called with a valid package ID and version. The package may
already be cached (cache hit) or may require downloading (cache miss).

**Expected**: Returns a non-null absolute path to the cached package folder on disk.

**Requirement coverage**: `Caching-NuGetCache-EnsureCached`.

#### NuGetCache_EnsureCachedAsync_CalledTwiceWithSamePackage_ReturnsSamePath

**Scenario**: `EnsureCachedAsync` is called twice with the same package ID and version. The second
call exercises the early-exit cache-hit path via the `.nupkg.metadata` sentinel check.

**Expected**: Both calls return the same non-null path, proving the early-exit path returns
consistent results.

**Requirement coverage**: `Caching-NuGetCache-EnsureCached`.

#### NuGetCache_EnsureCachedAsync_NullPackageId_ThrowsArgumentNullException

**Scenario**: `EnsureCachedAsync` is called with a null `packageId`.

**Expected**: Throws `ArgumentNullException` with parameter name `packageId`.

**Requirement coverage**: `Caching-NuGetCache-NullValidation`.

#### NuGetCache_EnsureCachedAsync_NullVersion_ThrowsArgumentNullException

**Scenario**: `EnsureCachedAsync` is called with a null `version`.

**Expected**: Throws `ArgumentNullException` with parameter name `version`.

**Requirement coverage**: `Caching-NuGetCache-NullValidation`.

#### NuGetCache_EnsureCachedAsync_InvalidVersion_ThrowsArgumentException

**Scenario**: `EnsureCachedAsync` is called with a `version` string that is not a valid NuGet
version (e.g. `"not-a-version"`).

**Expected**: Throws `ArgumentException` (propagated from `NuGetVersion.Parse`).

**Requirement coverage**: `Caching-NuGetCache-InvalidVersion`.

#### NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ThrowsInvalidOperationException

**Scenario**: `EnsureCachedAsync` is called with a package ID and version that is not present in
any configured NuGet source.

**Expected**: Throws `InvalidOperationException` with a message that identifies the package.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ExceptionMessageContainsPackageIdAndVersion

**Scenario**: `EnsureCachedAsync` is called with a package ID and version that is not present in
any configured NuGet source.

**Expected**: Throws `InvalidOperationException` whose message contains both the package ID and
the version string passed to the method.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath

**Scenario**: `EnsureCachedAsync` is called with a valid package ID and version served by a
local WireMock NuGet v3 flat-container feed. The feed provides a valid service index and the
package `.nupkg` bytes are available.

**Expected**: Returns a non-null absolute path to a real directory on disk that contains the
`.nupkg.metadata` sentinel file written by the NuGet SDK after a successful extraction.

**Requirement coverage**: `Caching-NuGetCache-EnsureCached`.

#### NuGetCache_EnsureCachedAsync_V3PackageAbsent_ThrowsInvalidOperationException

**Scenario**: `EnsureCachedAsync` is called with a package ID that is absent from a WireMock v3
feed. A different package is registered so the service index is valid, but the target package's
version list returns HTTP 404.

**Expected**: Throws `InvalidOperationException`.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_V3IndexFailsV2PackageRegistered_ReturnsExistingPackagePath

**Scenario**: The v3 `/index.json` endpoint returns HTTP 500 (simulating a NuGetProtocolException),
while the base URL serves a valid v2 OData feed containing the requested package. This exercises
the automatic v2 fallback path used for JFrog Artifactory-style feeds.

**Expected**: Either returns a non-null path to the installed package (v2 download succeeded), or
throws `InvalidOperationException` while the WireMock server log shows a v2-specific request
(`/$metadata`, `/FindPackagesById()`, `/Packages(...)`, or `/`) — confirming
`BuildCandidateRepositories` attempted the v2 fallback.

**Requirement coverage**: `Caching-NuGetCache-V2Fallback`.

#### NuGetCache_EnsureCachedAsync_V3AndV2BothFail_ThrowsInvalidOperationException

**Scenario**: Both the v3 service index and the v2 fallback base URL return HTTP 500. The NuGet
SDK wraps these as `HttpRequestException`, which `NuGetCache` treats as transient and silently
skips.

**Expected**: Throws `InvalidOperationException` with a message containing the package ID.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_NetworkFailureOnIndex_ThrowsInvalidOperationException

**Scenario**: The `/index.json` endpoint drops the connection (simulating a network-level
failure). The NuGet SDK raises `HttpRequestException`, which the implementation silently swallows
and continues to the next source.

**Expected**: Throws `InvalidOperationException` when no source provides the package.

**Requirement coverage**: `Caching-NuGetCache-TransientFailure`.

#### NuGetCache_EnsureCachedAsync_V3IndexFailsNetworkFailureOnFallback_ThrowsInvalidOperationException

**Scenario**: The v3 service index returns HTTP 500 (NuGetProtocolException) and the v2 fallback
base URL drops the connection (HttpRequestException). Both candidates fail through different error
paths.

**Expected**: Throws `InvalidOperationException`.

**Requirement coverage**: `Caching-NuGetCache-TransientFailure`.

#### NuGetCache_EnsureCachedAsync_DifferentPackageRegistered_ThrowsInvalidOperationException

**Scenario**: The WireMock v3 feed serves a different package ("Other.Package") but the call
requests a package that is absent ("Wanted.Package"). The version list returns HTTP 404 for the
requested identity.

**Expected**: Throws `InvalidOperationException`.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_DownloadProtocolError_ThrowsInvalidOperationException

**Scenario**: The v3 service index and version list succeed, but the `.nupkg` download endpoint
returns HTTP 500. The NuGet SDK wraps this as `HttpRequestException`, which the implementation
treats as a transient failure.

**Expected**: Throws `InvalidOperationException` with a message containing the package ID.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_DownloadNetworkFailure_ThrowsInvalidOperationException

**Scenario**: The v3 service index and version list succeed, but the `.nupkg` download endpoint
drops the connection. `CopyNupkgToStreamAsync` raises `HttpRequestException`, which is silently
swallowed by `TryDownloadFromResourceAsync`.

**Expected**: Throws `InvalidOperationException`.

**Requirement coverage**: `Caching-NuGetCache-NotFound`.

#### NuGetCache_EnsureCachedAsync_PackageInSecondSourceOnly_ReturnsExistingPackagePath

**Scenario**: Two independent WireMock v3 servers are configured. The first (primary) server
carries a different package and returns HTTP 404 for the requested identity. The second
(secondary) server carries the requested package. This exercises the multi-source enumeration
loop.

**Expected**: Returns a non-null path to a real directory containing the `.nupkg.metadata`
sentinel file, proving the loop continued past the first source.

**Requirement coverage**: `Caching-NuGetCache-MultiSource`.

#### NuGetCache_EnsureCachedAsync_PackageAlreadyCached_ReturnsCachedPathWithoutHttpCalls

**Scenario**: The global packages folder is pre-populated with the `.nupkg.metadata` sentinel
file at the expected path for the requested package identity. A WireMock server is configured as
the source.

**Expected**: Returns the pre-populated path immediately without making any HTTP calls (confirmed
by asserting the WireMock server's request log is empty).

**Requirement coverage**: `Caching-NuGetCache-CacheHit`.

#### NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithCredentials_ReturnsExistingPackagePath

**Scenario**: A WireMock v3 feed requires HTTP Basic Auth on every endpoint, including the
service index, the flat-container version list, and the `.nupkg` download. `CreateSettings` (with
credentials) writes a `nuget.config` containing a `packageSourceCredentials` block with a valid
username and password for the source, matching the real-world JFrog Artifactory shape.
`EnsureCachedAsync` is called against a fresh, empty global packages folder (a cold cache).

**Expected**: Returns a non-null absolute path to the installed package, proving that a cold
cache with valid statically-configured credentials succeeds against a fully authenticated feed.

**Requirement coverage**: `Caching-NuGetCache-AuthenticatedSource`.

#### NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithoutCredentials_ThrowsWithActionableDiagnostic

**Scenario**: The same fully authenticated WireMock v3 feed as above, but `EnsureCachedAsync` is
called with no `packageSourceCredentials` configured for the source, so every request receives
HTTP 401.

**Expected**: Throws `InvalidOperationException` whose message contains both the configured
source name (`test-source`) and the detected HTTP status code (`401`), proving the failure is
reported as an actionable authentication diagnostic rather than a generic, indistinguishable
"not found" error.

**Requirement coverage**: `Caching-NuGetCache-AuthenticatedSource`.

#### NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar

**Scenario**: An ordinary, unauthenticated WireMock v3 feed. The internal `EnsureCachedAsync`
overload is called with an injected `SpyCredentialServiceRegistrar` test double (implementing
`ICredentialServiceRegistrar`) against a fresh, empty global packages folder.

**Expected**: After the call completes, the spy's invocation counter is exactly `1`, directly
proving that `EnsureCachedAsync` invokes credential-service registration. This is a white-box
regression test for the registration step itself, using an injected test double rather than any
shared, process-wide static state - the two authenticated-source tests above exercise only
static `packageSourceCredentials`, which succeed with or without credential-service registration,
so neither would catch a regression that removed or skipped the registration call.

**Requirement coverage**: `Caching-NuGetCache-AuthenticatedSource`.

#### NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService

**Scenario**: An ordinary, unauthenticated WireMock v3 feed. The public `ISettings`-only
`EnsureCachedAsync` overload (which delegates to the real, default `CredentialServiceRegistrar`)
is called against a fresh, empty global packages folder.

**Expected**: After the call completes, `HttpHandlerResourceV3.CredentialService` is non-null,
proving the default registrar is correctly wired to the real NuGet SDK
`DefaultCredentialServiceUtility.SetupDefaultCredentialService` call, complementing the
spy-based test above (which proves invocation but never touches the real NuGet SDK).

**Requirement coverage**: `Caching-NuGetCache-AuthenticatedSource`.

### Requirements Coverage

- **`Caching-NuGetCache-EnsureCached`**:
  NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder,
  NuGetCache_EnsureCachedAsync_CalledTwiceWithSamePackage_ReturnsSamePath,
  NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath
- **`Caching-NuGetCache-NullValidation`**:
  NuGetCache_EnsureCachedAsync_NullPackageId_ThrowsArgumentNullException,
  NuGetCache_EnsureCachedAsync_NullVersion_ThrowsArgumentNullException
- **`Caching-NuGetCache-InvalidVersion`**:
  NuGetCache_EnsureCachedAsync_InvalidVersion_ThrowsArgumentException
- **`Caching-NuGetCache-TransientFailure`**:
  NuGetCache_EnsureCachedAsync_NetworkFailureOnIndex_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_V3IndexFailsNetworkFailureOnFallback_ThrowsInvalidOperationException
- **`Caching-NuGetCache-MultiSource`**:
  NuGetCache_EnsureCachedAsync_PackageInSecondSourceOnly_ReturnsExistingPackagePath
- **`Caching-NuGetCache-V2Fallback`**:
  NuGetCache_EnsureCachedAsync_V3IndexFailsV2PackageRegistered_ReturnsExistingPackagePath
- **`Caching-NuGetCache-CacheHit`**:
  NuGetCache_EnsureCachedAsync_PackageAlreadyCached_ReturnsCachedPathWithoutHttpCalls
- **`Caching-NuGetCache-AuthenticatedSource`**:
  NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithCredentials_ReturnsExistingPackagePath,
  NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithoutCredentials_ThrowsWithActionableDiagnostic,
  NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar,
  NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
- **`Caching-NuGetCache-NotFound`**:
  NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ExceptionMessageContainsPackageIdAndVersion,
  NuGetCache_EnsureCachedAsync_V3PackageAbsent_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_V3AndV2BothFail_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_DifferentPackageRegistered_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_DownloadProtocolError_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_DownloadNetworkFailure_ThrowsInvalidOperationException
