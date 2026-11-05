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

This unit has no dedicated test file; its tests live in `NuGetCacheServerTests`. A pure unit
test constructs a `CredentialServiceRegistrar` with the internal action-accepting constructor and
counts how many times registration runs across repeated and concurrent `EnsureRegistered` calls.
Integration tests inject an `ICredentialServiceRegistrar` test double to confirm
`EnsureRegistered` is invoked on every call regardless of authentication outcome, construct a
fresh, real `CredentialServiceRegistrar` instance - passed explicitly via the internal
registrar-accepting `EnsureCachedAsync` overload - to confirm it registers a real
`ICredentialService` via a genuine null-to-non-null transition, and call the production
`EnsureCachedAsync` overload (which uses the shared `DefaultCredentialRegistrar`) to confirm a
credential service is registered. Each test scenario names a specific test method that provides
evidence for the unit requirement.

### Test Environment

The standard test runner plus a local `NuGetTestServer` (WireMock) and a disposable temporary
global packages folder for the integration tests; no internet access is required. Tests that
touch the real NuGet SDK credential service run in-process and observe its process-wide static
state.

### Acceptance Criteria

All test scenarios below pass on every supported target framework: registration runs exactly once
per instance, including under concurrent calls; the injected double is invoked on each call; and
the production path leaves a credential service registered.

### Test Scenarios

#### CredentialServiceRegistrar_EnsureRegistered_RepeatedCalls_RegistersExactlyOnce

**Scenario**: A `CredentialServiceRegistrar` is constructed with a registration action that
increments a counter. `EnsureRegistered` is called three times sequentially and then 16 times
concurrently on the same instance.

**Expected**: The counter is exactly `1`.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-RegisterOnce`.

#### NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar

**Scenario**: `EnsureCachedAsync` is called with a test-double `ICredentialServiceRegistrar`
substituted for the default, against a healthy unauthenticated source.

**Expected**: The test double's `EnsureRegistered` method is invoked, confirming
`NuGetCache` calls the registrar unconditionally as part of orchestration, ahead of source
enumeration.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-RegisterOnce`.

#### NuGetCache_EnsureCachedAsync_FreshRealRegistrar_RegistersRealCredentialService

**Scenario**: `EnsureCachedAsync` is called with a freshly-constructed, real
`CredentialServiceRegistrar` instance passed explicitly via the internal registrar-accepting
overload. A fresh instance starts with its own memoization not yet triggered, so the test resets
`HttpHandlerResourceV3.CredentialService` to `null` beforehand (restoring the original value
afterward) to guarantee this specific call performs the registration.

**Expected**: After the call, `HttpHandlerResourceV3.CredentialService` is non-null, proving a
genuine null-to-non-null transition and that the real registrar is wired to
`DefaultCredentialServiceUtility.SetupDefaultCredentialService`.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-RegisterOnce`.

#### NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService

**Scenario**: The production `EnsureCachedAsync` overload that supplies no registrar (and so uses
the shared, process-wide `CredentialServiceRegistrar.DefaultCredentialRegistrar`) is called
against a healthy unauthenticated source.

**Expected**: After the call, `HttpHandlerResourceV3.CredentialService` is non-null. Because the
default instance memoizes registration for the life of the process and has no reset hook, the
test verifies the observable end state rather than a null-to-non-null transition; the
fresh-instance test above covers the transition.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`.

### Requirements Coverage

- **`Caching-CredentialServiceRegistrar-RegisterOnce`**:
  CredentialServiceRegistrar_EnsureRegistered_RepeatedCalls_RegistersExactlyOnce,
  NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar,
  NuGetCache_EnsureCachedAsync_FreshRealRegistrar_RegistersRealCredentialService
- **`Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`**:
  NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
