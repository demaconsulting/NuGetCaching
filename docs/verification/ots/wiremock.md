## WireMock.Net Verification

This document provides the verification evidence for the WireMock.Net OTS software item.
Requirements for this OTS item are defined in the WireMock.Net OTS Software Requirements document (`docs/reqstream/ots/wiremock.yaml`).

### Required Functionality

WireMock.Net is the HTTP stub server used exclusively by the controlled
integration test suite. It is a **test-only dependency** — it is not shipped with or required by
the production library. It binds to a random localhost port, serves pre-configured responses for
NuGet v3 and v2 feed endpoints, and records all received requests for post-test inspection. Its
role is to replace real NuGet feeds so the tests can exercise every internal branch of
`NuGetCache` without network access or external infrastructure.

### Verification Approach

WireMock.Net is verified by self-validation evidence from the controlled integration tests. Tests
that successfully download a package from a WireMock stub confirm the stub-serving capability.
Tests that assert the request log is empty (cache-hit path) or non-empty (v2 fallback path)
confirm the request-inspection capability. A passing run of all controlled integration tests
constitutes evidence that both requirements are satisfied.

### Test Scenarios

#### NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath

**Scenario**: WireMock.Net serves a complete v3 flat-container feed including a service index,
version list, and `.nupkg` download endpoint. The NuGet client SDK requests each in turn and
downloads the package.

**Expected**: WireMock.Net delivers all configured responses correctly and the package is
installed in the temp global packages folder.

**Requirement coverage**: `Caching-OTS-WireMock-Stub`.

#### NuGetCache_EnsureCachedAsync_PackageAlreadyCached_ReturnsCachedPathWithoutHttpCalls

**Scenario**: `EnsureCachedAsync` detects the `.nupkg.metadata` sentinel and returns immediately
without making any HTTP calls. After the act phase, the test asserts that the WireMock request
log is empty.

**Expected**: WireMock.Net records zero requests, confirming the request log correctly reflects
the absence of HTTP activity.

**Requirement coverage**: `Caching-OTS-WireMock-Inspect`.

#### NuGetCache_EnsureCachedAsync_V3IndexFailsV2PackageRegistered_ReturnsExistingPackagePath

**Scenario**: The v3 `/index.json` stub returns HTTP 500 and the v2 base URL stub serves a valid
OData response. After the act phase, the test asserts that at least one v2-specific request
(`/$metadata`, `/FindPackagesById()`, `/Packages(...)`, or `/`) reached the WireMock server,
confirming the v2 fallback URL was attempted.

**Expected**: WireMock.Net records at least one v2-specific request, confirming the request log
correctly reflects HTTP activity from the fallback attempt.

**Requirement coverage**: `Caching-OTS-WireMock-Inspect`.

### Requirements Coverage

- **`Caching-OTS-WireMock-Stub`**:
  NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath
- **`Caching-OTS-WireMock-Inspect`**:
  NuGetCache_EnsureCachedAsync_PackageAlreadyCached_ReturnsCachedPathWithoutHttpCalls,
  NuGetCache_EnsureCachedAsync_V3IndexFailsV2PackageRegistered_ReturnsExistingPackagePath
