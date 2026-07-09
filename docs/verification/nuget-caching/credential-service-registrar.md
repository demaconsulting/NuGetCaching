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
regardless of authentication outcome, and separately construct a fresh, real
`CredentialServiceRegistrar` instance - passed explicitly via the internal registrar-accepting
`EnsureCachedAsync` overload - to confirm it registers a real `ICredentialService` via a genuine
null-to-non-null transition. Each test scenario names a specific test method that provides
evidence for the unit requirement.

### Test Scenarios

#### NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar

**Scenario**: `EnsureCachedAsync` is called with a test-double `ICredentialServiceRegistrar`
substituted for the default, against a healthy unauthenticated source.

**Expected**: The test double's `EnsureRegistered` method is invoked, confirming
`NuGetCache` calls the registrar unconditionally as part of orchestration, ahead of source
enumeration.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-RegisterOnce`.

#### NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService

**Scenario**: `EnsureCachedAsync` is called with a freshly-constructed, real
`CredentialServiceRegistrar` instance (the same concrete implementation used by the shared,
process-wide `CredentialServiceRegistrar.DefaultCredentialRegistrar` default) passed explicitly
via the internal registrar-accepting overload. A fresh instance starts with its own memoization
not yet triggered, so the test only needs to reset `HttpHandlerResourceV3.CredentialService` to `null`
beforehand to guarantee this specific call - not leftover state from an earlier test - performs
the registration.

**Expected**: After the call, `HttpHandlerResourceV3.CredentialService` is non-null, proving a
genuine null-to-non-null transition caused by this call, confirming the real registrar
implementation is correctly wired to the real NuGet SDK
`DefaultCredentialServiceUtility.SetupDefaultCredentialService` call in an idempotent manner (safe
to call on every request without overwriting an existing registration or throwing on repeated
invocation), complementing the spy-based test above (which proves invocation but never touches the
real NuGet SDK).

**Requirement coverage**: `Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`.

### Requirements Coverage

- **`Caching-CredentialServiceRegistrar-RegisterOnce`**:
  NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar
- **`Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`**:
  NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
