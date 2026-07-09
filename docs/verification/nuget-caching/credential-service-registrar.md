## CredentialServiceRegistrar Unit Verification

This document provides the verification design for the `CredentialServiceRegistrar` unit.
Requirements for this unit are defined in the CredentialServiceRegistrar Unit Requirements
document.

### Required Functionality

The `CredentialServiceRegistrar` unit shall register the NuGet SDK's default credential service
with `HttpHandlerResourceV3.CredentialService` when one is not already registered, so that
credential-provider plugins or `ICredentialService`-mediated retries are available for sources
that require them.

### Verification Approach

This unit has no dedicated test file, since its only externally observable behavior is exercised
end-to-end through the public `NuGetCache.EnsureCachedAsync` API. Unit requirements are verified
by the existing `NuGetCacheServerTests` integration tests, which inject an
`ICredentialServiceRegistrar` test double to confirm `EnsureRegistered` is invoked on every call
regardless of authentication outcome, and separately exercise the real,
production `CredentialServiceRegistrar.DefaultCredentialRegistrar` instance (resetting its
internal memoization via the test-only `ResetForTesting()` seam) to confirm it registers a real
`ICredentialService` via a genuine null-to-non-null transition. Each test scenario names a
specific test method that provides evidence for the unit requirement.

### Test Scenarios

#### NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar

**Scenario**: `EnsureCachedAsync` is called with a test-double `ICredentialServiceRegistrar`
substituted for the default, against a healthy unauthenticated source.

**Expected**: The test double's `EnsureRegistered` method is invoked, confirming
`NuGetCache` calls the registrar unconditionally as part of orchestration, ahead of source
enumeration.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-RegisterOnce`.

#### NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService

**Scenario**: `EnsureCachedAsync` is called using the real, production
`CredentialServiceRegistrar.DefaultCredentialRegistrar` instance (the default used when no
registrar is explicitly supplied). Because that instance is shared, process-wide, and memoizes
registration exactly once for its lifetime, the test first resets
`HttpHandlerResourceV3.CredentialService` to `null` and calls the internal `ResetForTesting()`
seam to clear the memoization, guaranteeing this specific call - not a memoized no-op left over
from an earlier test - performs the registration.

**Expected**: After the call, `HttpHandlerResourceV3.CredentialService` is non-null, proving a
genuine null-to-non-null transition caused by this call, confirming the default registrar is
correctly wired to the real NuGet SDK `DefaultCredentialServiceUtility.SetupDefaultCredentialService`
call in an idempotent manner (safe to call on every request without overwriting an existing
registration or throwing on repeated invocation), complementing the spy-based test above (which
proves invocation but never touches the real NuGet SDK).

**Requirement coverage**: `Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`.

### Requirements Coverage

- **`Caching-CredentialServiceRegistrar-RegisterOnce`**:
  NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar
- **`Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`**:
  NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
