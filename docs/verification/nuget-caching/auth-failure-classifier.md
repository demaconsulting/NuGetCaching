## AuthFailureClassifier Unit Verification

This document provides the verification design for the `AuthFailureClassifier` unit.
Requirements for this unit are defined in the AuthFailureClassifier Unit Requirements document.

### Required Functionality

The `AuthFailureClassifier` unit shall detect an HTTP 401 or 403 status code anywhere in an
exception's `InnerException` chain - whether exposed via a strongly typed status-code property
or embedded in an exception message - and shall build an actionable diagnostic message
identifying the source and the detected status, without reporting a false positive for an
unrelated exception or an unrelated standalone number that resembles a status code.

### Verification Approach

Unit requirements are verified by pure unit tests (no WireMock server or network I/O) that
construct exceptions directly and call `AuthFailureClassifier.TryDescribeAuthFailure`, exercising
every detection branch: message-text matching, strongly typed status-code property detection,
inner-exception chain walking, and non-matching inputs. Each test scenario names a specific test
method that provides evidence for the unit requirement.

### Test Scenarios

#### AuthFailureClassifier_TryDescribeAuthFailure_401InMessage_ReturnsTrueWithActionableMessage

**Scenario**: `TryDescribeAuthFailure` is called with an exception whose message embeds
`"401 (Unauthorized)"` text typical of a NuGet SDK wrapped protocol exception.

**Expected**: Returns `true` and a non-null message containing the source name, source URL, and
`401`/`Unauthorized`.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_403InMessage_ReturnsTrueWithActionableMessage

**Scenario**: `TryDescribeAuthFailure` is called with an exception whose message embeds
`"403 (Forbidden)"` text.

**Expected**: Returns `true` and a non-null message containing `403`/`Forbidden`.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_NoMatchInMessage_ReturnsFalse

**Scenario**: `TryDescribeAuthFailure` is called with a generic transient-failure exception
message ("Connection refused.") containing no status-code text.

**Expected**: Returns `false` with a `null` message.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_UnrelatedNumberInMessage_ReturnsFalse

**Scenario**: `TryDescribeAuthFailure` is called with an exception message containing a
standalone number resembling 401 (a port number, `localhost:40100`) that is not immediately
followed by the expected HTTP reason phrase.

**Expected**: Returns `false` with a `null` message, confirming no false positive.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_StatusCodeInInnerException_ReturnsTrue

**Scenario**: The outer exception's own message does not contain status-code text, but its
`InnerException` does.

**Expected**: Returns `true`, confirming the exception chain is walked past the outer
exception.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_StatusCodeInDeeplyNestedInnerException_ReturnsTrue

**Scenario**: Three levels of nested exceptions, with the status-code text present only in the
innermost exception.

**Expected**: Returns `true`, confirming the chain is walked through multiple levels of
nesting.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_NoMatchInFullChain_ReturnsFalse

**Scenario**: A chain of nested exceptions, none of which mention a 401/403 status code.

**Expected**: Returns `false` with a `null` message after walking the entire chain.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithStatusCodeProperty_ReturnsTrue

**Scenario**: An `HttpRequestException` whose message text carries no recognizable status-code
text, but whose strongly typed `StatusCode` property is `HttpStatusCode.Unauthorized`. Compiled
only for target frameworks where the strongly typed detection branch is compiled in and the
3-argument `HttpRequestException` constructor is available (introduced in .NET 5).

**Expected**: Returns `true` with a message containing `401`, confirming detection via the
strongly typed property independent of message text.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithForbiddenStatusCodeProperty_ReturnsTrue

**Scenario**: An `HttpRequestException` with `StatusCode` equal to `HttpStatusCode.Forbidden`.

**Expected**: Returns `true` with a message containing `403`.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithUnrelatedStatusCodeProperty_ReturnsFalse

**Scenario**: An `HttpRequestException` with an unrelated `StatusCode` (`InternalServerError`,
i.e. HTTP 500), neither 401 nor 403.

**Expected**: Returns `false` with a `null` message, confirming the strongly typed property
check only reports a match for 401/403.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### AuthFailureClassifier_TryDescribeAuthFailure_MatchFound_MessageIncludesOriginalExceptionText

**Scenario**: `TryDescribeAuthFailure` is called with an exception carrying recognizable
401 status-code text.

**Expected**: The returned message contains the original exception's message text verbatim, in
addition to the summarized actionable framing.

**Requirement coverage**: `Caching-AuthFailureClassifier-DetectUnauthorized`.

### Requirements Coverage

- **`Caching-AuthFailureClassifier-DetectUnauthorized`**:
  AuthFailureClassifier_TryDescribeAuthFailure_401InMessage_ReturnsTrueWithActionableMessage,
  AuthFailureClassifier_TryDescribeAuthFailure_403InMessage_ReturnsTrueWithActionableMessage,
  AuthFailureClassifier_TryDescribeAuthFailure_NoMatchInMessage_ReturnsFalse,
  AuthFailureClassifier_TryDescribeAuthFailure_UnrelatedNumberInMessage_ReturnsFalse,
  AuthFailureClassifier_TryDescribeAuthFailure_StatusCodeInInnerException_ReturnsTrue,
  AuthFailureClassifier_TryDescribeAuthFailure_StatusCodeInDeeplyNestedInnerException_ReturnsTrue,
  AuthFailureClassifier_TryDescribeAuthFailure_NoMatchInFullChain_ReturnsFalse,
  AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithStatusCodeProperty_ReturnsTrue,
  AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithForbiddenStatusCodeProperty_ReturnsTrue,
  AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithUnrelatedStatusCodeProperty_ReturnsFalse,
  AuthFailureClassifier_TryDescribeAuthFailure_MatchFound_MessageIncludesOriginalExceptionText
