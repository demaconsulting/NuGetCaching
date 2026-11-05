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

These tests exercise `NuGetCache` end-to-end through its collaboration with the
`PackageSourceResolver`, `PackageDownloader`, `AuthFailureClassifier`, and
`CredentialServiceRegistrar` sibling units; unit-level verification of the internal branches
owned specifically by those sibling units (e.g. v2 fallback candidate construction, resolution
diagnostics, download error handling, status-code detection) is documented in their own
verification documents. Each test scenario below names a specific test method that provides
evidence for one or more `NuGetCache`-level requirements.

### Test Environment

- **Live integration tests**: internet access to the public NuGet feed and the default NuGet
  configuration of the host.
- **Controlled integration tests**: a local `NuGetTestServer` (WireMock) on a random loopback
  port, an injected `ISettings` pointing at it, and a disposable temporary global packages folder
  per test; for `GetVersionsAsync`, a `root` directory containing a `nuget.config` that points at
  the local feed. No internet access is required.

### Acceptance Criteria

All test scenarios below pass on every supported target framework: `EnsureCachedAsync` returns the
cached package folder or fails with the documented exception, and `GetVersionsAsync` returns
normalized, ascending, de-duplicated version strings (empty when not found), honors the prerelease
flag, skips failing sources, and propagates cancellation.

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

**Requirement coverage**: `Caching-NuGetCache-NullPackageId`.

#### NuGetCache_EnsureCachedAsync_NullVersion_ThrowsArgumentNullException

**Scenario**: `EnsureCachedAsync` is called with a null `version`.

**Expected**: Throws `ArgumentNullException` with parameter name `version`.

**Requirement coverage**: `Caching-NuGetCache-NullVersion`.

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
`PackageSourceResolver.BuildCandidateRepositories` attempted the v2 fallback.

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
swallowed by `PackageDownloader.TryDownloadAsync`.

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

**Requirement coverage**: `Caching-NuGetCache-HonorCredentials`.

#### NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithoutCredentials_ThrowsWithActionableDiagnostic

**Scenario**: The same fully authenticated WireMock v3 feed as above, but `EnsureCachedAsync` is
called with no `packageSourceCredentials` configured for the source, so every request receives
HTTP 401.

**Expected**: Throws `InvalidOperationException` whose message contains both the configured
source name (`test-source`) and the detected HTTP status code (`401`), proving the failure is
reported as an actionable authentication diagnostic rather than a generic, indistinguishable
"not found" error.

**Requirement coverage**: `Caching-NuGetCache-AuthDiagnostic`.

#### NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar

**Scenario**: An ordinary, unauthenticated WireMock v3 feed. The internal `EnsureCachedAsync`
The internal core is called with an injected `SpyCredentialServiceRegistrar` test double (implementing
`ICredentialServiceRegistrar`) against a fresh, empty global packages folder.

**Expected**: After the call completes, the spy's invocation counter is exactly `1`, directly
proving that `EnsureCachedAsync` invokes credential-service registration. This is a white-box
regression test for the registration step itself, using an injected test double rather than any
shared, process-wide static state - the two authenticated-source tests above exercise only
static `packageSourceCredentials`, which succeed with or without credential-service registration,
so neither would catch a regression that removed or skipped the registration call.

**Requirement coverage**: `Caching-NuGetCache-HonorCredentials`.

#### NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService

**Scenario**: An ordinary, unauthenticated WireMock v3 feed. The `ISettings`-only internal
`EnsureCachedAsync` seam (which delegates to the real, default `CredentialServiceRegistrar`)
is called against a fresh, empty global packages folder.

**Expected**: After the call completes, `HttpHandlerResourceV3.CredentialService` is non-null,
proving the default registrar is correctly wired to the real NuGet SDK
`DefaultCredentialServiceUtility.SetupDefaultCredentialService` call, complementing the
spy-based test above (which proves invocation but never touches the real NuGet SDK).

**Requirement coverage**: `Caching-NuGetCache-HonorCredentials`.

#### NuGetCache_GetVersionsAsync_MultipleVersions_ReturnsAscendingNormalizedStrings

**Scenario**: A v3 feed advertises `1.10.0`, `1.2.0`, and `1.0` (unsorted, one non-normalized).

**Expected**: Returns `1.0.0`, `1.2.0`, `1.10.0` - semantic (not lexical) order, normalized.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsOrdering`.

#### NuGetCache_GetVersionsAsync_NonNormalizedVersions_ReturnsNormalizedStrings

**Scenario**: A v3 feed advertises the non-normalized forms `1.0` and `2.0.0.0`.

**Expected**: Returns `1.0.0` and `2.0.0`.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsNormalization`.

#### NuGetCache_GetVersionsAsync_UnsortedVersions_ReturnsSemanticAscendingOrder

**Scenario**: A v3 feed advertises `2.0.0`, `1.10.0`, `1.2.0`, and `1.9.0` (unsorted).

**Expected**: Returns `1.2.0`, `1.9.0`, `1.10.0`, `2.0.0` - semantic (not lexical) order.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsOrdering`.

#### NuGetCache_GetVersionsAsync_PrereleaseExcludedByDefault

**Scenario**: The feed advertises `1.0.0-beta` and `1.0.0`; `includePrerelease` is false.

**Expected**: Returns only `1.0.0`.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsPrerelease`.

#### NuGetCache_GetVersionsAsync_IncludePrerelease_ReturnsPrereleaseVersions

**Scenario**: The same versions with `includePrerelease` true.

**Expected**: Returns `1.0.0-beta` then `1.0.0`.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsPrerelease`.

#### NuGetCache_GetVersionsAsync_PackageAbsent_ReturnsEmptyList

**Scenario**: The feed does not carry the requested package (HTTP 404 on its version list).

**Expected**: Returns an empty list without throwing.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsNotFound`.

#### NuGetCache_GetVersionsAsync_VersionsInMultipleSources_MergedDeduplicated

**Scenario**: Two feeds advertise overlapping version sets.

**Expected**: Returns the union, de-duplicated, in ascending order.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsDeduplication`.

#### NuGetCache_GetVersionsAsync_SourceUnreachable_ContinuesToNextSource

**Scenario**: The first feed drops the connection on the version list request; the second is healthy.

**Expected**: Returns the versions from the second feed.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsSourceFailure`.

#### NuGetCache_GetVersionsAsync_AllSourcesFail_ThrowsInvalidOperationException

**Scenario**: The only configured feed drops the connection on the version list request.

**Expected**: Throws `InvalidOperationException` whose message names the package and the failing
source.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsAllSourcesFail`.

#### NuGetCache_GetVersionsAsync_PackageSourceMapping_HonorsMapping

**Scenario**: Both feeds carry the package but package source mapping maps it only to the second.

**Expected**: Returns only the second feed's versions and the first feed receives no requests.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsSources`.

#### NuGetCache_GetVersionsAsync_LocalFolderSource_IsIgnored

**Scenario**: The settings list a remote feed advertising `1.0.0` and a local folder feed
containing a valid `.nupkg` for `9.9.9`.

**Expected**: Returns only `1.0.0`; the local folder source is not queried.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsSources`.

#### NuGetCache_GetVersionsAsync_NonHttpNonLocalSource_IsIgnored

**Scenario**: The settings list a remote HTTP feed advertising `1.0.0` and a source that is neither
local nor HTTP/HTTPS (a custom-scheme URI).

**Expected**: Returns only `1.0.0`; the non-HTTP source is ignored rather than queried.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsSources`.

#### NuGetCache_GetVersionsAsync_CancelledToken_ThrowsOperationCanceledException

**Scenario**: `GetVersionsAsync` is called with an already-cancelled token.

**Expected**: Throws `OperationCanceledException` (or a derived type); cancellation is not
swallowed as a skipped source.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsCancellation`.

#### NuGetCache_GetVersionsAsync_CancelledTokenNoHttpSources_ThrowsBeforeRegistration

**Scenario**: `GetVersionsAsync` is called with an already-cancelled token, settings that contain
no sources, and a spy `ICredentialServiceRegistrar`.

**Expected**: Throws `OperationCanceledException` (or a derived type) and the registrar is never
invoked, proving cancellation is observed before registration and source enumeration.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsCancellation`.

#### NuGetCache_GetVersionsAsync_AuthenticatedSourceWithCredentials_ReturnsVersions

**Scenario**: A Basic Auth-protected feed with matching `packageSourceCredentials`.

**Expected**: Returns the advertised versions.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsAuth`.

#### NuGetCache_GetVersionsAsync_AnySource_InvokesCredentialServiceRegistrar

**Scenario**: The internal core is called with a spy `ICredentialServiceRegistrar`.

**Expected**: The spy is invoked exactly once.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsAuth`.

#### NuGetCache_GetVersionsAsync_ReturnedVersionUsableByEnsureCachedAsync

**Scenario**: A version returned by `GetVersionsAsync` is passed to `EnsureCachedAsync`.

**Expected**: The package is installed (the `.nupkg.metadata` sentinel exists).

**Requirement coverage**: `Caching-NuGetCache-GetVersions`.

#### NuGetCache_GetVersionsAsync_PublicSignature_ExposesOnlyStrings

**Scenario**: Reflection over the public `GetVersionsAsync` signature.

**Expected**: Returns `Task<IReadOnlyList<string>>` and no parameter type is from a NuGet namespace.

**Requirement coverage**: `Caching-NuGetCache-GetVersions`.

#### NuGetCache_GetVersionsAsync_NullPackageId_ThrowsArgumentNullException

**Scenario**: The public `GetVersionsAsync` is called with a null `packageId`.

**Expected**: Throws `ArgumentNullException`.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsNullArguments`.

#### NuGetCache_GetVersionsAsync_NullInternalArguments_ThrowsArgumentNullException

**Scenario**: The internal core is called with a null `packageId`, `settings`, or registrar.

**Expected**: Throws `ArgumentNullException` in each case.

**Requirement coverage**: `Caching-NuGetCache-GetVersionsNullArguments`.

#### Options scenarios

The following tests live in `NuGetCacheOptionsTests` and `CredentialDispatcherTests`. The
callback tests run against a local `NuGetTestServer` returning 401 until it receives HTTP basic
authentication, and call the internal cores with a fresh registrar so the
process-wide credential service can be isolated and restored.

- **NuGetCacheOptions_Default_HasAllOptionsOff**: a new `NuGetCacheOptions` has
  non-interactive mode, no logger, no callback, no timeout, no explicit sources, local sources off, and
  signed packages not required. Covers `SingleApiShape`.
- **NuGetCache_PublicMethods_InvalidArguments_Throw**: the public methods throw
  `ArgumentNullException`/`ArgumentException` for a null or blank `packageId` (and a null `version`).
  Covers `SingleApiShape`.
- **NuGetCache_PublicMethods_Signature_HasOptionalRootOptionsThenToken**: reflection check that the public
  methods are `(..., string? root = null, NuGetCacheOptions? options = null, CancellationToken = default)`.
  Covers `SingleApiShape`.
- **NuGetCache_EnsureCachedAsync_CallbackSuppliesCredentials_DownloadsFrom401Source** and
  **NuGetCache_GetVersionsAsync_CallbackSuppliesCredentials_ReturnsVersions**: the callback
  returns basic credentials after the 401 and the operation succeeds. Covers `CredentialCallback`.
- **NuGetCache_EnsureCachedAsync_CallbackReturnsNull_FallsThroughAndFailsWithAuthenticationException**:
  a null answer falls through to the providers; with none available the call throws
  `NuGetAuthenticationException` naming `test-source`. Covers `CredentialCallback`.
- **NuGetCache_EnsureCachedAsync_CallbackCredentialsRejected_AsksOncePerRequestAndFails**: wrong
  credentials are asked for once per URI and request type, not in a loop. Covers `CredentialCallback`.
- **NuGetCache_UnauthorizedWithoutCredentials_ThrowsNuGetAuthenticationException**: 401 with no
  credentials throws `NuGetAuthenticationException`, with or without options. Covers
  `AuthenticationException`; **NuGetCache_ListVersionsAsync_Unauthorized_ThrowsNuGetAuthenticationException**
  covers the same for `ListVersionsAsync`.
- **NuGetAuthenticationException_Properties_AreExposed**: `Source`, `Uri`, and message are
  preserved. Covers `AuthenticationException`.
- **NuGetCache_EnsureCachedAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled**: a
  callback that never completes, with a 500 ms timeout, ends in `OperationCanceledException` (not
  `NuGetAuthenticationException`); the `GetVersionsAsync` variant and a deterministic dispatcher-level
  test (`CredentialDispatcher_GetCredentialsAsync_TimedOut_SubsequentRequestsCancelWithoutCallback`)
  cover the same guarantee.
  Covers `AuthenticationTimeout`.
- **NuGetCache_InvalidAuthenticationTimeout_ThrowsArgumentOutOfRangeException** (zero, negative,
  and over-`int.MaxValue`-ms timeouts, all three async methods) and
  **NuGetCacheOptions_Validate_ValidTimeouts_DoNotThrow** (null, infinite, 1 tick, max). Cover
  `AuthenticationTimeout` validation.
- **NuGetCache_InvalidAuthenticationTimeout_ValidatedBeforeSettingsLoad**: with a root holding a
  malformed `nuget.config` (settings loading fails), an invalid timeout still throws
  `ArgumentOutOfRangeException` from the public methods, proving validation precedes settings
  loading. Covers `AuthenticationTimeout` validation.

#### Sources, listing, and signature scenarios

These tests live in `NuGetCacheSourcesTests`, `NuGetCacheListingTests`, and `NuGetCacheSignatureTests`
and use `NuGetTestServer` (WireMock) or local folder sources.

- **NuGetCacheOptions_Validate_InvalidSourceEntry_ThrowsArgumentException** and
  **NuGetCacheOptions_Validate_ValidSources_DoNotThrow**: `Sources` entries that are null, blank, or
  malformed are rejected; names, URLs, and rooted paths are accepted.
- **NuGetCache_Sources_UnknownRelativeEntry_ThrowsArgumentException**: an entry that is neither a configured
  name, URL, nor rooted path throws.
- **NuGetCache_Sources_ExplicitUrl_ReplacesConfiguredSources**,
  **NuGetCache_Sources_ConfiguredName_ResolvesToConfiguredSource**,
  **NuGetCache_Sources_ExplicitSource_BypassesSourceMapping**, and
  **NuGetCache_Sources_DuplicateEntries_QueryTheSourceOnce**: explicit sources replace configured ones, a
  configured name reuses the configured source, package source mapping is bypassed, and duplicates are
  queried once. Cover `Sources`.
- **NuGetCache_GetVersionsAsync_LocalSource_RequiresIncludeLocalSourcesOrExplicitSource** and
  **NuGetCache_EnsureCachedAsync_LocalSource_IsAlwaysConsulted**: local sources are skipped by version
  queries unless `IncludeLocalSources` or an explicit source applies, and are always used for download.
  Cover `IncludeLocalSources`.
- **NuGetCache_GetCachedVersions_InstalledVersions_ReturnsCompleteOnesSorted**,
  **NuGetCache_GetCachedVersions_NotInstalled_ReturnsEmptyWithoutNetworkAccess**, and
  **NuGetCache_GetCachedVersions_RootWithConfig_ReadsConfiguredGlobalPackagesFolder**: installed versions
  are returned sorted, only complete installs count, nothing is installed yields an empty list with no
  HTTP traffic, and the `nuget.config` global packages folder is honored. Cover `GetCachedVersions`.
- **NuGetCache_ListVersionsAsync_RegistrationMetadata_ReturnsListedStateAndPublishedDate**,
  **NuGetCache_ListVersionsAsync_NoRegistrationMetadata_FallsBackToFlatList**,
  **NuGetCache_ListVersionsAsync_VersionInTwoSources_FirstSourceWins**, and
  **NuGetPackageVersion_Record_HasValueSemantics**: metadata supplies listed state and date, absent
  metadata falls back to the flat list, the first source wins, and the record has value equality. Cover
  `ListVersions`.
- **NuGetCache_RequireSignedPackages_UnsignedPackageDownload_ThrowsInvalidOperationException**,
  **NuGetCache_RequireSignedPackages_Off_AcceptsUnsignedPackage**, and
  **NuGetCache_RequireSignedPackages_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit**: an unsigned
  package is rejected on download and on cache hit when required, and accepted otherwise. Cover
  `RequireSignedPackages`.
- **NuGetCache_ConcurrentCalls_DifferentOptions_AreIsolated**: one call with a callback succeeds
  while a concurrent call with default options fails authentication. Covers `OptionsIsolation`.
- **NuGetCache_EnsureCachedAsync_Logger_ReceivesMessagesWithoutSecret**,
  **NuGetCredential_ToString_RedactsSecret**, **NuGetCredential_Formatted_DoesNotExposeSecret**,
  **NuGetCredential_ToString_IncludesUserNameAndRedactsSecret**, and
  **NuGetCredential_Interpolation_DoesNotExposeSecret** (the last two in `NuGetCredentialTests`):
  the logger receives messages and neither logs nor string forms contain the secret. Covers `Logging`.

- **`Caching-NuGetCache-EnsureCached`**:
  NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder,
  NuGetCache_EnsureCachedAsync_CalledTwiceWithSamePackage_ReturnsSamePath,
  NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath
- **`Caching-NuGetCache-NullPackageId`**:
  NuGetCache_EnsureCachedAsync_NullPackageId_ThrowsArgumentNullException
- **`Caching-NuGetCache-NullVersion`**:
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
- **`Caching-NuGetCache-HonorCredentials`**:
  NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithCredentials_ReturnsExistingPackagePath,
  NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar,
  NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
- **`Caching-NuGetCache-AuthDiagnostic`**:
  NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithoutCredentials_ThrowsWithActionableDiagnostic
- **`Caching-NuGetCache-SingleApiShape`**:
  NuGetCacheOptions_Default_HasAllOptionsOff,
  NuGetCache_PublicMethods_Signature_HasOptionalRootOptionsThenToken,
  NuGetCache_PublicMethods_InvalidArguments_Throw,
  NuGetCache_GetVersionsAsync_PublicSignature_ExposesOnlyStrings
- **`Caching-NuGetCache-CredentialCallback`**:
  NuGetCache_EnsureCachedAsync_CallbackSuppliesCredentials_DownloadsFrom401Source,
  NuGetCache_GetVersionsAsync_CallbackSuppliesCredentials_ReturnsVersions,
  NuGetCache_EnsureCachedAsync_CallbackReturnsNull_FallsThroughAndFailsWithAuthenticationException,
  NuGetCache_EnsureCachedAsync_CallbackCredentialsRejected_AsksOncePerRequestAndFails
- **`Caching-NuGetCache-AuthenticationException`**:
  NuGetAuthenticationException_Properties_AreExposed,
  NuGetCache_UnauthorizedWithoutCredentials_ThrowsNuGetAuthenticationException,
  NuGetCache_EnsureCachedAsync_AuthenticatedSourceWithoutCredentials_ThrowsWithActionableDiagnostic,
  NuGetCache_ListVersionsAsync_Unauthorized_ThrowsNuGetAuthenticationException
- **`Caching-NuGetCache-AuthenticationTimeout`**:
  NuGetCache_EnsureCachedAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled,
  NuGetCache_GetVersionsAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled,
  NuGetCacheOptions_Validate_ValidTimeouts_DoNotThrow,
  NuGetCache_InvalidAuthenticationTimeout_ThrowsArgumentOutOfRangeException,
  NuGetCache_InvalidAuthenticationTimeout_ValidatedBeforeSettingsLoad
- **`Caching-NuGetCache-OptionsIsolation`**:
  NuGetCache_ConcurrentCalls_DifferentOptions_AreIsolated
- **`Caching-NuGetCache-Logging`**:
  NuGetCache_EnsureCachedAsync_Logger_ReceivesMessagesWithoutSecret,
  NuGetCredential_ToString_RedactsSecret,
  NuGetCredential_ToString_IncludesUserNameAndRedactsSecret,
  NuGetCredential_Interpolation_DoesNotExposeSecret,
  NuGetCredential_Formatted_DoesNotExposeSecret
- **`Caching-NuGetCache-GetVersions`**:
  NuGetCache_GetVersionsAsync_ReturnedVersionUsableByEnsureCachedAsync,
  NuGetCache_GetVersionsAsync_PublicSignature_ExposesOnlyStrings
- **`Caching-NuGetCache-GetVersionsNormalization`**:
  NuGetCache_GetVersionsAsync_NonNormalizedVersions_ReturnsNormalizedStrings
- **`Caching-NuGetCache-GetVersionsOrdering`**:
  NuGetCache_GetVersionsAsync_MultipleVersions_ReturnsAscendingNormalizedStrings,
  NuGetCache_GetVersionsAsync_UnsortedVersions_ReturnsSemanticAscendingOrder
- **`Caching-NuGetCache-GetVersionsDeduplication`**:
  NuGetCache_GetVersionsAsync_VersionsInMultipleSources_MergedDeduplicated
- **`Caching-NuGetCache-GetVersionsSources`**:
  NuGetCache_GetVersionsAsync_PackageSourceMapping_HonorsMapping,
  NuGetCache_GetVersionsAsync_LocalFolderSource_IsIgnored,
  NuGetCache_GetVersionsAsync_NonHttpNonLocalSource_IsIgnored
- **`Caching-NuGetCache-GetVersionsAuth`**:
  NuGetCache_GetVersionsAsync_AuthenticatedSourceWithCredentials_ReturnsVersions,
  NuGetCache_GetVersionsAsync_AnySource_InvokesCredentialServiceRegistrar
- **`Caching-NuGetCache-GetVersionsSourceFailure`**:
  NuGetCache_GetVersionsAsync_SourceUnreachable_ContinuesToNextSource
- **`Caching-NuGetCache-GetVersionsAllSourcesFail`**:
  NuGetCache_GetVersionsAsync_AllSourcesFail_ThrowsInvalidOperationException
- **`Caching-NuGetCache-GetVersionsCancellation`**:
  NuGetCache_GetVersionsAsync_CancelledToken_ThrowsOperationCanceledException,
  NuGetCache_GetVersionsAsync_CancelledTokenNoHttpSources_ThrowsBeforeRegistration
- **`Caching-NuGetCache-GetVersionsPrerelease`**:
  NuGetCache_GetVersionsAsync_PrereleaseExcludedByDefault,
  NuGetCache_GetVersionsAsync_IncludePrerelease_ReturnsPrereleaseVersions
- **`Caching-NuGetCache-GetVersionsNotFound`**:
  NuGetCache_GetVersionsAsync_PackageAbsent_ReturnsEmptyList
- **`Caching-NuGetCache-GetVersionsNullArguments`**:
  NuGetCache_GetVersionsAsync_NullPackageId_ThrowsArgumentNullException,
  NuGetCache_GetVersionsAsync_NullInternalArguments_ThrowsArgumentNullException
- **`Caching-NuGetCache-Sources`**:
  NuGetCacheOptions_Validate_InvalidSourceEntry_ThrowsArgumentException,
  NuGetCacheOptions_Validate_ValidSources_DoNotThrow,
  NuGetCache_Sources_UnknownRelativeEntry_ThrowsArgumentException,
  NuGetCache_Sources_ExplicitUrl_ReplacesConfiguredSources,
  NuGetCache_Sources_ConfiguredName_ResolvesToConfiguredSource,
  NuGetCache_Sources_ExplicitSource_BypassesSourceMapping,
  NuGetCache_Sources_DuplicateEntries_QueryTheSourceOnce
- **`Caching-NuGetCache-IncludeLocalSources`**:
  NuGetCache_GetVersionsAsync_LocalSource_RequiresIncludeLocalSourcesOrExplicitSource,
  NuGetCache_EnsureCachedAsync_LocalSource_IsAlwaysConsulted
- **`Caching-NuGetCache-GetCachedVersions`**:
  NuGetCache_GetCachedVersions_InstalledVersions_ReturnsCompleteOnesSorted,
  NuGetCache_GetCachedVersions_NotInstalled_ReturnsEmptyWithoutNetworkAccess,
  NuGetCache_GetCachedVersions_RootWithConfig_ReadsConfiguredGlobalPackagesFolder
- **`Caching-NuGetCache-ListVersions`**:
  NuGetCache_ListVersionsAsync_RegistrationMetadata_ReturnsListedStateAndPublishedDate,
  NuGetCache_ListVersionsAsync_NoRegistrationMetadata_FallsBackToFlatList,
  NuGetCache_ListVersionsAsync_VersionInTwoSources_FirstSourceWins,
  NuGetPackageVersion_Record_HasValueSemantics
- **`Caching-NuGetCache-RequireSignedPackages`**:
  NuGetCache_RequireSignedPackages_UnsignedPackageDownload_ThrowsInvalidOperationException,
  NuGetCache_RequireSignedPackages_Off_AcceptsUnsignedPackage,
  NuGetCache_RequireSignedPackages_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit
- **`Caching-NuGetCache-NotFound`**:
  NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ExceptionMessageContainsPackageIdAndVersion,
  NuGetCache_EnsureCachedAsync_V3PackageAbsent_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_V3AndV2BothFail_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_DifferentPackageRegistered_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_DownloadProtocolError_ThrowsInvalidOperationException,
  NuGetCache_EnsureCachedAsync_DownloadNetworkFailure_ThrowsInvalidOperationException
