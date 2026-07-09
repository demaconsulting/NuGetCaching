## PackageSourceResolver Unit Verification

This document provides the verification design for the `PackageSourceResolver` unit.
Requirements for this unit are defined in the PackageSourceResolver Unit Requirements document.

### Required Functionality

The `PackageSourceResolver` unit shall resolve the `FindPackageByIdResource` for a configured
NuGet package source, shall fall back to a v2 OData endpoint when a configured v3 index URL
fails with a protocol error, shall not construct a fallback candidate for a source URL that
does not already end in `/index.json`, and shall not surface a diagnostic error message for a
purely transient, non-actionable network-level failure at resolution time.

### Verification Approach

Unit requirements are verified by local-integration tests that call
`PackageSourceResolver.ResolveAsync` directly against a manually constructed `SourceRepository`,
using a local `NuGetTestServer` (WireMock) to simulate NuGet v3 and v2 feed scenarios without
making real network calls. Each test scenario names a specific test method that provides evidence
for one or more unit requirements.

### Test Scenarios

#### PackageSourceResolver_ResolveAsync_HealthyV3Index_ReturnsResourceForOriginalRepository

**Scenario**: `ResolveAsync` is called against a source repository whose v3 `/index.json` URL
serves a healthy service index and flat-container feed.

**Expected**: Returns a non-null `Resource`, a `null` `ErrorMessage`, and the effective
`Repository` is the same instance as the originally configured repository (no fallback needed).

**Requirement coverage**: `Caching-PackageSourceResolver-Resolve`.

#### PackageSourceResolver_ResolveAsync_V3IndexProtocolError_FallsBackToV2Repository

**Scenario**: The v3 `/index.json` endpoint returns HTTP 500 (simulating a
`NuGetProtocolException`), while the base URL serves a valid v2 OData feed.

**Expected**: Returns a non-null `Resource`, and the effective `Repository` is a different
instance than the originally configured repository, with its `PackageSource.Source` equal to
the base URL (with `/index.json` stripped) - confirming `BuildCandidateRepositories` constructed
and successfully resolved the v2 fallback candidate.

**Requirement coverage**: `Caching-PackageSourceResolver-FallbackOnProtocolError`.

#### PackageSourceResolver_ResolveAsync_NonIndexJsonSourceUrl_ResolvesDirectlyAsV2

**Scenario**: `ResolveAsync` is called against a source configured at the bare base URL (no
`/index.json` suffix) serving a v2 OData feed directly.

**Expected**: Returns a non-null `Resource`, and the effective `Repository` is the same instance
as the originally configured repository - confirming `BuildCandidateRepositories` produced only
a single candidate (no fallback repository constructed) for a non-`/index.json` URL.

**Requirement coverage**: `Caching-PackageSourceResolver-Resolve`,
`Caching-PackageSourceResolver-NoFallbackForNonIndexUrl`.

#### PackageSourceResolver_ResolveAsync_NetworkFailureOnIndex_DoesNotThrowAndReturnsNoErrorMessage

**Scenario**: The `/index.json` endpoint drops the connection (simulating a network-level
failure) at resolution time.

**Expected**: `ResolveAsync` does not throw, and returns a `null` `ErrorMessage`, confirming that
resolving a resource does not by itself require a successful v3 service-index fetch: the
underlying NuGet SDK provider chain defers actual protocol validation to first use (search or
download) rather than resolution, so a resolution-time network failure on the v3 candidate is
masked by the v2 fallback candidate rather than surfaced as a diagnostic.

**Requirement coverage**: `Caching-PackageSourceResolver-TransientFailure`.

### Requirements Coverage

- **`Caching-PackageSourceResolver-Resolve`**:
  PackageSourceResolver_ResolveAsync_HealthyV3Index_ReturnsResourceForOriginalRepository,
  PackageSourceResolver_ResolveAsync_NonIndexJsonSourceUrl_ResolvesDirectlyAsV2
- **`Caching-PackageSourceResolver-FallbackOnProtocolError`**:
  PackageSourceResolver_ResolveAsync_V3IndexProtocolError_FallsBackToV2Repository
- **`Caching-PackageSourceResolver-NoFallbackForNonIndexUrl`**:
  PackageSourceResolver_ResolveAsync_NonIndexJsonSourceUrl_ResolvesDirectlyAsV2
- **`Caching-PackageSourceResolver-TransientFailure`**:
  PackageSourceResolver_ResolveAsync_NetworkFailureOnIndex_DoesNotThrowAndReturnsNoErrorMessage
