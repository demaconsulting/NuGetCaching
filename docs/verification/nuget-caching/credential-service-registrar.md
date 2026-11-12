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
- **CredentialDispatcher_GetCredentialsAsync_CallbackSet_RetryStateIsScopedToOneCall**: deterministic,
  HTTP-free: with a callback and a substituted provider that declines retries (like the integrated
  authentication provider), a repeat request within one call is not satisfied, while the same URL in a
  second call is answered again.
- **NuGetCache_GetVersionsAsync_CallbackDeclinesTwice_ProviderServesEveryCall**: end-to-end regression
  test: two sequential `GetVersionsAsync` calls for one 401-challenging source, each with a callback that
  returns `null` and a retry-declining substituted provider, both succeed. Before the per-call service
  the second call failed with an authentication error.
- **NuGetCache_EnsureCachedAsync_CallbackReturnsNull_AsksOncePerUriAndType** and
  **CredentialDispatcher_GetCredentialsAsync_CallbackReturnsNull_AsksOnceButStillReachesProviders**:
  a null answer is remembered per (URI, type) so the callback
  runs once, while the providers are still consulted.
- **CredentialDispatcher_GetCredentialsAsync_ConcurrentSameKey_AwaitsSingleCallback**: a concurrent
  challenge for the same (URI, type) awaits the single in-flight callback and shares its credential
  instead of treating it as a decline.
- **CredentialDispatcher_GetCredentialsAsync_NoCredentialAvailable_ThrowsUnsatisfiedChallenge**: with no
  credential available the dispatcher throws an `OperationCanceledException`-derived exception whose
  message carries no URL (the challenged URL, which may hold secrets, is only in `ChallengeUri`).
- **CredentialDispatcher_GetCredentialsAsync_ForbiddenNoCredential_FailsWithStatus403AndRecordedReason**: a
  forbidden (403) request that no credential satisfies fails with status 403 and the reason recorded for the
  marked source, and the classifier's message quotes it.
- **CredentialDispatcher_GetCredentialsAsync_ReasonedThenBareForbidden_DoesNotReportStaleReason**: within one call
  (with a marked source and without), a 403 with a reason followed by a 403 with none clears the recorded reason,
  so the failure carries no `Reason` and its message does not quote the earlier one.
- **CredentialDispatcher_GetCredentialsAsync_ReasonedForbiddenThenBareForbiddenChallenge_ReplacesRecordedChallenge**:
  within one call (with a marked source and without), a reasoned 403 that fails and is recorded, followed by a bare
  403 that fails, replaces the recorded challenge, so the final recorded failure and its classifier message carry no
  stale reason.
- **CredentialDispatcher_GetCredentialsAsync_ChallengeStatusOrdering_KeepsForbiddenOverUnauthorized**: a 403 replaces
  a recorded 401 for the same source, but a later 401 does not displace the recorded 403 (and its reason).
- **ForbiddenReasonHandlerProvider_Install_DoesNotConstructProvidersUntilRead**: `Install` constructs no provider;
  the HTTP handler entry becomes the reason-capturing provider only when read, and other providers stay lazy and are
  constructed once when read.
- **ForbiddenReasonHandler_Forbidden_RecordsSanitizedReasonInPriorityOrder**: the handler reads the reason from
  the reason phrase, records nothing for the bare "Forbidden" phrase, and reduces a long, multi-line,
  URL-bearing `X-TFS-ServiceError` header to one short line without URL secrets (the header wins over the body
  in the end-to-end `NuGetCache_EnsureCachedAsync_Forbidden_ReportsStatusAndServerReasonWithoutRetries`, which
  also asserts a prompt failure, and the JSON `message` is used in
  `NuGetCache_GetVersionsAsync_ForbiddenWithJsonBody_ReportsMessageAsServerReason`).
- **CredentialDispatcher_GetCredentialsAsync_ForbiddenWithCallback_DoesNotInvokeCallback**: with a host
  credential callback configured, a forbidden (403) request fails with status 403 and the recorded reason without
  invoking the callback; a 401 answered by the callback followed by a 403 for the same request leaves the callback
  invoked exactly once (for the 401).
- **ForbiddenReasonHandler_OversizedBody_FallsBackToReasonPhrase**: a 403 body larger than the read limit does not
  lose the reason; the handler falls back to the descriptive reason phrase.
- **CredentialDispatcher_GetCredentialsAsync_CallerCanceled_ThrowsPlainCancellation**: when the caller's
  token is canceled the dispatcher throws a plain cancellation and records no challenge.
- **AuthFailureClassifier_IsUnsatisfiedChallenge_CallerCancellation_IsNotAuthFailure**: a plain
  cancellation after a recorded challenge is an authentication failure only while the caller's token is
  not canceled and only for a source whose challenge was recorded (same marked source, or same host and port
  when no source was marked); the dispatcher's own exception is always one.
- **NuGetCache_EnsureCachedAsync_NoCredentials_FailsFastWithoutRetries**,
  **NuGetCache_VersionListing_NoCredentials_FailsFastWithoutRetries** and
  **NuGetCache_AuthenticationTimeout_CallbackNeverCompletes_FailsPromptlyAfterTimeout**: end-to-end
  fail-fast and prompt-timeout behavior.
- **CredentialDispatcher_GetCredentialsAsync_CallbackThrew_RepeatChallengeRethrowsOriginal**,
  **CredentialDispatcher_GetCredentialsAsync_CallbackCanceled_RepeatChallengeStaysPlainCancellation**,
  **CredentialDispatcher_GetCredentialsAsync_ConcurrentWaiterCallbackFaults_WaiterGetsOriginalException** and
  **CredentialDispatcher_GetCredentialsAsync_CallbackThrewWithTimeout_RepeatChallengeRethrowsOriginal**:
  a callback that threw (or was canceled) has its original exception instance rethrown on a repeat
  challenge and to a concurrent waiter (with and without a timeout), the callback runs once, and the
  failure is never reported as an authentication failure.
- **CredentialDispatcher_GetCredentialsAsync_ChallengeInMarkedSource_AttributedToThatSourceOnly**: a
  challenge raised for a resource outside the source's path is attributed to the marked source only; a
  second source on the same host does not see it.
- **CredentialDispatcher_GetCredentialsAsync_SourcesDifferingOnlyByQuery_AttributedSeparately**: two
  sources that differ only by query string get distinct non-secret keys; a challenge marked for one is
  not reported for the other.
- **CredentialDispatcher_GetCredentialsAsync_UnmarkedAndTimeoutHop_AttributionBehaves**: with no marked
  source the host-and-port fallback applies, and the source tag survives the timeout's `Task.Run` hop.
- **NuGetCache_EnsureCachedAsync_TwoSourcesSameHostOneAuthed_FailureNamesOnlyAuthedSource**,
  **NuGetCache_EnsureCachedAsync_TwoSourcesSameHostOneAuthed_DownloadsFromOpenSource** and
  **NuGetCache_GetVersionsAsync_TwoSourcesSameHostOneAuthed_FailureNamesOnlyAuthedSource**: with two
  sources on one host and port (one always 401, one public), in either order, a package missing from the
  public source yields an authentication failure naming only the protected source, and a package held by
  the public source is still downloaded.
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
  CredentialDispatcher_GetCredentialsAsync_CallbackBlocksSynchronously_TimesOutPromptly,
  NuGetCache_EnsureCachedAsync_CallbackReturnsNull_AsksOncePerUriAndType,
  CredentialDispatcher_GetCredentialsAsync_ConcurrentSameKey_AwaitsSingleCallback,
  CredentialDispatcher_GetCredentialsAsync_CallbackReturnsNull_AsksOnceButStillReachesProviders,
  NuGetCache_AuthenticationTimeout_CallbackNeverCompletes_FailsPromptlyAfterTimeout
- **`Caching-CredentialServiceRegistrar-PerCallProviderState`**:
  CredentialDispatcher_GetCredentialsAsync_CallbackSet_RetryStateIsScopedToOneCall,
  NuGetCache_GetVersionsAsync_CallbackDeclinesTwice_ProviderServesEveryCall
- **`Caching-CredentialServiceRegistrar-FailFastOnUnsatisfiedChallenge`**:
  CredentialDispatcher_GetCredentialsAsync_NoCredentialAvailable_ThrowsUnsatisfiedChallenge,
  CredentialDispatcher_GetCredentialsAsync_CallerCanceled_ThrowsPlainCancellation,
  AuthFailureClassifier_IsUnsatisfiedChallenge_CallerCancellation_IsNotAuthFailure,
  NuGetCache_VersionListing_NoCredentials_FailsFastWithoutRetries
- **`Caching-CredentialServiceRegistrar-ForbiddenReason`**:
  CredentialDispatcher_GetCredentialsAsync_ForbiddenNoCredential_FailsWithStatus403AndRecordedReason,
  CredentialDispatcher_GetCredentialsAsync_ReasonedThenBareForbidden_DoesNotReportStaleReason,
  CredentialDispatcher_GetCredentialsAsync_ReasonedForbiddenThenBareForbiddenChallenge_ReplacesRecordedChallenge,
  CredentialDispatcher_GetCredentialsAsync_ChallengeStatusOrdering_KeepsForbiddenOverUnauthorized,
  ForbiddenReasonHandlerProvider_Install_DoesNotConstructProvidersUntilRead,
  CredentialDispatcher_GetCredentialsAsync_ForbiddenWithCallback_DoesNotInvokeCallback,
  ForbiddenReasonHandler_Forbidden_RecordsSanitizedReasonInPriorityOrder,
  ForbiddenReasonHandler_OversizedBody_FallsBackToReasonPhrase,
  NuGetCache_EnsureCachedAsync_Forbidden_ReportsStatusAndServerReasonWithoutRetries,
  NuGetCache_GetVersionsAsync_ForbiddenWithJsonBody_ReportsMessageAsServerReason
- **`Caching-CredentialServiceRegistrar-StoredFaultRethrown`**:
  CredentialDispatcher_GetCredentialsAsync_CallbackThrew_RepeatChallengeRethrowsOriginal,
  CredentialDispatcher_GetCredentialsAsync_CallbackCanceled_RepeatChallengeStaysPlainCancellation,
  CredentialDispatcher_GetCredentialsAsync_ConcurrentWaiterCallbackFaults_WaiterGetsOriginalException,
  CredentialDispatcher_GetCredentialsAsync_CallbackThrewWithTimeout_RepeatChallengeRethrowsOriginal
- **`Caching-CredentialServiceRegistrar-PerSourceChallenge`**:
  CredentialDispatcher_GetCredentialsAsync_ChallengeInMarkedSource_AttributedToThatSourceOnly,
  CredentialDispatcher_GetCredentialsAsync_SourcesDifferingOnlyByQuery_AttributedSeparately,
  CredentialDispatcher_GetCredentialsAsync_UnmarkedAndTimeoutHop_AttributionBehaves,
  NuGetCache_EnsureCachedAsync_TwoSourcesSameHostOneAuthed_FailureNamesOnlyAuthedSource,
  NuGetCache_GetVersionsAsync_TwoSourcesSameHostOneAuthed_FailureNamesOnlyAuthedSource
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
