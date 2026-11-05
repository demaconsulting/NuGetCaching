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

This unit's tests live in `NuGetCacheServerTests` and `CredentialDispatcherTests`. A pure unit
test constructs a `CredentialServiceRegistrar` with the internal action-accepting constructor and
counts how many times registration runs across repeated and concurrent `EnsureRegistered` calls.
Integration tests inject an `ICredentialServiceRegistrar` test double to confirm
`EnsureRegistered` is invoked on every call regardless of authentication outcome, construct a
fresh, real `CredentialServiceRegistrar` instance - passed explicitly via the internal
registrar-accepting internal `EnsureCachedAsync` core - to confirm it registers a real
`ICredentialService` via a genuine null-to-non-null transition, and call the `ISettings`-only internal
seam (which uses the shared `DefaultCredentialRegistrar`) to confirm a
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
core. A fresh instance starts with its own memoization not yet triggered, so the test resets
`HttpHandlerResourceV3.CredentialService` to `null` beforehand (restoring the original value
afterward) to guarantee this specific call performs the registration.

**Expected**: After the call, `HttpHandlerResourceV3.CredentialService` is non-null, proving a
genuine null-to-non-null transition and that the real registrar is wired to
`DefaultCredentialServiceUtility.SetupDefaultCredentialService`.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-RegisterOnce`.

#### NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService

**Scenario**: The `ISettings`-only internal `EnsureCachedAsync` seam that supplies no registrar (and so uses
the shared, process-wide `CredentialServiceRegistrar.DefaultCredentialRegistrar`) is called
against a healthy unauthenticated source.

**Expected**: After the call, `HttpHandlerResourceV3.CredentialService` is non-null. Because the
default instance memoizes registration for the life of the process and has no reset hook, the
test verifies the observable end state rather than a null-to-non-null transition; the
fresh-instance test above covers the transition.

**Requirement coverage**: `Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`.

### Dispatcher Test Scenarios

The dispatcher tests live in `CredentialDispatcherTests`. Each runs with the process-wide credential
service nulled, a fresh `CredentialServiceRegistrar` registered, and the original value restored afterward.

- **CredentialServiceRegistrar_EnsureRegistered_RegistersDispatcherOnlyWhenNoServiceExists**: the
  dispatcher is installed when nothing is registered and a pre-registered host service is left in place.
- **CredentialServiceRegistrar_Register_HostServiceAppearsBeforeAssignment_IsNotOverwritten**: via the
  `Register` seam, a host service appearing before the final assignment is not overwritten, and the
  wrapper is assigned otherwise.
- **CredentialDispatcher_GetCredentialsAsync_CallbackBlocksSynchronously_TimesOutPromptly**: a callback
  that blocks its thread synchronously is still canceled with `OperationCanceledException` within a
  bounded time.
- **NuGetCache_EnsureCachedAsync_WithOptions_InvokesCredentialServiceRegistrar**: the options-bearing core
  calls the injected registrar test double.
- **CredentialServiceRegistrar_Register_HostServiceAlreadyPresent_DoesNotCreateDefault**: when a service
  is already present, no default is created and nothing is assigned.
- **CredentialServiceRegistrar_Register_ConcurrentCalls_AssignExactlyOnce**: concurrent `Register` calls
  assign exactly once.
- **CredentialServiceRegistrar_Register_CreateDefaultThrows_AssignsNothingAndRecovers**: a throwing
  `createDefault` assigns nothing and a later call succeeds.
- **NuGetCache_EnsureCachedAsync_CallbackSuppliesCredentials_DownloadsFrom401Source**: the callback is
  consulted before providers and its credential unlocks the 401 source.
- **NuGetCache_EnsureCachedAsync_CallbackReturnsNull_FallsThroughAndFailsWithAuthenticationException**:
  null falls through and the call fails with `NuGetAuthenticationException`.
- **NuGetCache_EnsureCachedAsync_CallbackCredentialsRejected_AsksOncePerRequestAndFails**: a rejected
  credential is not requested again for the same URI and type.
- **NuGetCache_EnsureCachedAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled**: the timeout
  surfaces as `OperationCanceledException`, not `NuGetAuthenticationException`, even when the
  callback ignores its token.
- **NuGetCache_GetVersionsAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled**: the same
  guarantee for `GetVersionsAsync`.
- **CredentialDispatcher_GetCredentialsAsync_TimedOut_SubsequentRequestsCancelWithoutCallback**:
  deterministic, HTTP-free: after a timeout a repeat request is canceled without invoking the
  callback again and the scope's `ThrowIfTimedOut()` throws `OperationCanceledException`.
- **NuGetCache_ConcurrentCalls_DifferentOptions_AreIsolated**: concurrent calls with and without a
  callback do not see each other's options.
- **CredentialDispatcher_TryGetLastKnownGoodCredentialsFromCache_HonorsCallbackAndLoggerRouting**: a
  configured callback bypasses the last-known-good cache for non-proxy requests, and a call with a
  logger is routed to the logger-aware non-interactive service.
- **NuGetCache_GetVersionsAsync_HostOwnedServiceWithTimeoutOnly_LogsWarningMentioningTimeout**: with a
  host-owned credential service, a timeout-only call logs the warning naming `AuthenticationTimeout`.

### Requirements Coverage

- **`Caching-CredentialServiceRegistrar-DispatchPerCall`**:
  CredentialServiceRegistrar_EnsureRegistered_RegistersDispatcherOnlyWhenNoServiceExists,
  NuGetCache_ConcurrentCalls_DifferentOptions_AreIsolated,
  NuGetCache_EnsureCachedAsync_WithOptions_InvokesCredentialServiceRegistrar
- **`Caching-CredentialServiceRegistrar-CallbackFirst`**:
  NuGetCache_EnsureCachedAsync_CallbackSuppliesCredentials_DownloadsFrom401Source,
  NuGetCache_EnsureCachedAsync_CallbackReturnsNull_FallsThroughAndFailsWithAuthenticationException
- **`Caching-CredentialServiceRegistrar-LoopGuard`**:
  NuGetCache_EnsureCachedAsync_CallbackCredentialsRejected_AsksOncePerRequestAndFails,
  NuGetCache_EnsureCachedAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled,
  CredentialDispatcher_GetCredentialsAsync_CallbackBlocksSynchronously_TimesOutPromptly
- **`Caching-CredentialServiceRegistrar-RegisterOnce`**:
  CredentialServiceRegistrar_EnsureRegistered_RepeatedCalls_RegistersExactlyOnce,
  NuGetCache_EnsureCachedAsync_AnySource_InvokesCredentialServiceRegistrar,
  NuGetCache_EnsureCachedAsync_FreshRealRegistrar_RegistersRealCredentialService,
  CredentialServiceRegistrar_Register_HostServiceAppearsBeforeAssignment_IsNotOverwritten,
  CredentialServiceRegistrar_Register_HostServiceAlreadyPresent_DoesNotCreateDefault,
  CredentialServiceRegistrar_Register_ConcurrentCalls_AssignExactlyOnce,
  CredentialServiceRegistrar_Register_CreateDefaultThrows_AssignsNothingAndRecovers
- **`Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`**:
  NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
