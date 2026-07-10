## AuthFailureClassifier Design

![AuthFailureClassifier Structure](NuGetCachingView.svg)

### Overview

`AuthFailureClassifier` is an internal static class that classifies an exception raised while
communicating with a NuGet source as an actionable HTTP 401 (Unauthorized) or 403 (Forbidden)
authentication failure, or as a non-actionable transient error, in the DemaConsulting NuGet
Caching library. It is pure logic with no I/O: it only inspects exception messages and (where
available) strongly typed status-code properties.

The class is marked `internal` because it is an implementation detail of the library and is not
part of the public API surface. It is used by both `PackageSourceResolver` (while resolving a
`FindPackageByIdResource`) and `PackageDownloader` (while downloading the `.nupkg` bytes), so the
detection logic is centralized in a single unit rather than duplicated across both call sites.

### Class Structure

#### HttpStatusCodePattern Field

```csharp
private static readonly Regex HttpStatusCodePattern =
    new(@"\b(401)\s*\(Unauthorized\)|\b(403)\s*\(Forbidden\)", RegexOptions.Compiled);
```

A compiled regular expression matching an HTTP status code embedded in a NuGet SDK exception
message, e.g. `"Response status code does not indicate success: 401 (Unauthorized)."`.

#### TryDescribeAuthFailure Method

```csharp
internal static bool TryDescribeAuthFailure(
    Exception exception,
    string sourceName,
    string source,
    out string? message)
```

Determines whether the exception (or any exception in its `InnerException` chain) represents an
HTTP 401 or 403 response, and if so, builds an actionable diagnostic message identifying the
source and the authentication failure.

#### TryGetHttpStatusCode Method

```csharp
private static bool TryGetHttpStatusCode(Exception? exception, out int statusCode)
```

Walks the exception and its `InnerException` chain looking for an HTTP 401 or 403 status code.

### Design Decisions

#### Static Class, Pure Logic

`AuthFailureClassifier` performs no I/O and holds no state beyond the compiled regular
expression; it is a pure function of its exception-chain input. A static class is the natural
fit, mirroring `PathHelpers`.

#### Regex Requires the Reason Phrase, Not a Bare Number

The pattern requires the status code to be immediately followed by its standard HTTP reason
phrase in parentheses (e.g. `401 (Unauthorized)` / `403 (Forbidden)`) — the exact text format
emitted for a failed `HttpRequestException` and surfaced through NuGet's wrapped protocol
exceptions — rather than a bare `\b(401|403)\b` match, which could misclassify unrelated
standalone numbers elsewhere in a message (e.g. a port number) as an authentication failure.

#### Exception-Chain Walking

The NuGet SDK does not consistently expose the failing HTTP status code as a strongly typed
property: `HttpRequestException.StatusCode` is only available on net5.0+ (not
`netstandard2.0`, one of this library's target frameworks), and protocol-level failures are
frequently wrapped in a `NuGetProtocolException` (e.g. `FatalProtocolException`) whose message —
or whose `InnerException`'s message — simply embeds the reason-phrase text.
`TryGetHttpStatusCode` checks the strongly typed `HttpRequestException.StatusCode` property where
available (`#if !NETSTANDARD2_0`) and falls back to matching that text across the whole exception
chain (`InnerException` by `InnerException`) so detection is consistent on every target framework
(`netstandard2.0`, `net8.0`, `net9.0`, `net10.0`) regardless of which exception shape the SDK
happens to surface at a given call site.

#### Actionable Diagnostic Message Format

When a 401/403 is detected, `TryDescribeAuthFailure` builds a diagnostic message naming the
source (its configured name and URL), the detected status code, and a hint to check
`packageSourceCredentials` in `nuget.config` or a configured credential provider, while
preserving the original exception's message text verbatim within the built message. This gives
callers a concrete, actionable signal rather than an indistinguishable "not found" outcome,
without discarding the underlying diagnostic detail.

#### Extraction from NuGetCache

This class was extracted verbatim from the original `NuGetCache.HttpStatusCodePattern` field and
`NuGetCache.TryDescribeAuthFailure` / `NuGetCache.TryGetHttpStatusCode` private methods as a pure
structural refactor: the detection algorithm and message format are unchanged.
`TryDescribeAuthFailure` was changed from `private` to `internal` (the minimum visibility change
required) so it can be called from the sibling `PackageSourceResolver` and `PackageDownloader`
units; `TryGetHttpStatusCode` remains `private`, as it is only ever called by
`TryDescribeAuthFailure` within this class.

### Method Descriptions

#### `TryDescribeAuthFailure(Exception exception, string sourceName, string source, out string? message)`

Determines whether `exception` (or any exception in its `InnerException` chain) represents an
HTTP 401 (Unauthorized) or 403 (Forbidden) response, and if so, builds an actionable diagnostic
message identifying the source and the authentication failure. The method:

1. Calls `TryGetHttpStatusCode` to search the exception chain for a 401/403 status code. Returns
   `false` with a `null` message when no such status code is found — the failure should be
   treated as a non-actionable transient error, preserving prior behavior.
2. When a status code is found, builds a message naming `sourceName` and `source`, the detected
   status text (`401 (Unauthorized)` or `403 (Forbidden)`), a hint to check
   `packageSourceCredentials` or a configured credential provider, and the original exception's
   message text.
3. Returns `true` with the built message.

Satisfies requirement `Caching-AuthFailureClassifier-DetectUnauthorized`.

#### `TryGetHttpStatusCode(Exception? exception, out int statusCode)` (private)

Walks `exception` and its `InnerException` chain looking for an HTTP 401 or 403 status code. For
each exception in the chain:

1. On target frameworks other than `netstandard2.0`, checks whether the exception is an
   `HttpRequestException` with a non-null `StatusCode` property equal to 401 or 403.
2. Falls back to matching `HttpStatusCodePattern` against the exception's `Message`.
3. Returns `true` and the detected status code as soon as either check matches on any exception
   in the chain; returns `false` with a status code of `0` if the chain is exhausted with no
   match.

Satisfies requirement `Caching-AuthFailureClassifier-DetectUnauthorized`.
