## CredentialServiceRegistrar Design

![CredentialServiceRegistrar Structure](NuGetCachingView.svg)

### Purpose

`CredentialServiceRegistrar` (together with the `ICredentialServiceRegistrar` interface it
implements) registers the NuGet SDK's default credential service once per process, in the
DemaConsulting NuGet Caching library. Registration mirrors the setup performed internally by the
`dotnet` CLI and MSBuild restore pipeline, and is a prerequisite for `HttpSourceAuthenticationHandler`
to retry an HTTP 401 challenge using credentials resolved from a NuGet credential-provider plugin
(e.g. for JFrog Artifactory or Azure Artifacts).

The interface is marked `internal` because it exists purely as a testability seam; the concrete
`CredentialServiceRegistrar` class is likewise `internal` because it is an implementation detail
of the library and is not part of the public API surface.

### Data Model

The state of each `CredentialServiceRegistrar` instance is a single memoized registration
(`Lazy<bool>` created with `LazyThreadSafetyMode.ExecutionAndPublication`); the invariant is
that the registration action runs at most once per instance. `DefaultCredentialRegistrar` is
the one shared, process-wide instance.

#### ICredentialServiceRegistrar Interface

```csharp
internal interface ICredentialServiceRegistrar
{
    void EnsureRegistered();
}
```

Abstracts NuGet SDK credential-service registration so it can be substituted with a test double,
letting a test assert that `EnsureCachedAsync` invokes registration without observing or
resetting any shared, static process-wide state.

#### CredentialServiceRegistrar Class

```csharp
internal sealed class CredentialServiceRegistrar : ICredentialServiceRegistrar
{
    internal static readonly ICredentialServiceRegistrar DefaultCredentialRegistrar = new CredentialServiceRegistrar();

    internal CredentialServiceRegistrar();
    internal CredentialServiceRegistrar(Action registration);
    internal static void Register(
        Func<Lazy<ICredentialService>?> get,
        Action<Lazy<ICredentialService>?> set,
        Func<ICredentialService> createDefault);

    public void EnsureRegistered();
}
```

Default `ICredentialServiceRegistrar` implementation that registers the NuGet SDK's default
credential service via `DefaultCredentialServiceUtility.SetupDefaultCredentialService`, memoized
per instance so repeated calls on the same instance are cheap. `DefaultCredentialRegistrar` is
the single, process-wide instance shared by every real (non-test) `EnsureCachedAsync` call. The
no-argument constructor registers the real credential service; the action-accepting
constructor memoizes a caller-supplied registration action instead, so tests can count
registrations (proving exactly-once behavior) without touching the NuGet SDK's static state.

### Design Decisions

#### Interface Seam for Testability

`ICredentialServiceRegistrar` mirrors the existing `ISettings` injection seam already used by
`NuGetCache` for testability. Injecting a test double lets a test assert that `EnsureCachedAsync`
invokes credential-service registration exactly once, without observing or resetting the real,
shared, process-wide NuGet SDK static state (`HttpHandlerResourceV3.CredentialService`), and
without needing test isolation/ordering guarantees around that shared state.

#### `DefaultCredentialRegistrar` as a Static Member of the Class

The single, process-wide default instance is exposed as `CredentialServiceRegistrar.DefaultCredentialRegistrar`
— a static member of the concrete class itself, rather than a free-standing static field
elsewhere. This keeps the registrar's default instance colocated with its implementation,
following normal C# conventions for exposing a well-known default instance of a class (comparable
to `System.Text.Encoding.UTF8` or `System.Array.Empty<T>()`), and lets `NuGetCache` reference it
simply as `CredentialServiceRegistrar.DefaultCredentialRegistrar`.

#### Memoization via `Lazy<bool>`

`DefaultCredentialServiceUtility.SetupDefaultCredentialService` is itself idempotent (it only
assigns `HttpHandlerResourceV3.CredentialService` when still `null`), but it always re-creates a
delegating logger. Memoizing the call per instance via `Lazy<bool>` with
`LazyThreadSafetyMode.ExecutionAndPublication` avoids that redundant work on every
`EnsureCachedAsync` call while remaining thread-safe. Because `DefaultCredentialRegistrar` is a
single shared instance, the registration work happens only once per process even though
`EnsureCachedAsync` may be called many times; a freshly constructed instance (as used by tests)
naturally starts unregistered, giving each test independent control.

#### `nonInteractive: true`

Registration always passes `nonInteractive: true` to `SetupDefaultCredentialService`, since this
is a library used in build tooling, not an interactive CLI: credential providers must not attempt
to show a UI prompt.

#### Extraction from NuGetCache

This class was extracted verbatim from the original `NuGetCache.ICredentialServiceRegistrar`
nested interface, the private `NuGetCache.CredentialServiceRegistrar` nested class, and the
`NuGetCache.DefaultCredentialRegistrar` static field, as a pure structural refactor: the
registration algorithm, memoization strategy, and logger configuration are unchanged. The types
were promoted from nested members of `NuGetCache` to top-level types in this sibling file
(retaining `internal` visibility), and `DefaultCredentialRegistrar` was moved from a
free-standing static field into a static member of the `CredentialServiceRegistrar` class itself,
so `NuGetCache` now references `CredentialServiceRegistrar.DefaultCredentialRegistrar`.

### Key Methods

#### `EnsureRegistered()`

Ensures the NuGet SDK's default credential service is registered. On the first call on a given
instance, invokes `DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance,
nonInteractive: true)`, mirroring the setup performed internally by the `dotnet` CLI and MSBuild
restore pipeline. Subsequent calls on the same instance are a cheap, thread-safe no-op due to the
`Lazy<bool>` memoization.

Satisfies requirements `Caching-CredentialServiceRegistrar-RegisterOnce` and
`Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`.

#### Dispatching Credential Service

After the default service is created, the registrar wraps it in an internal `CredentialDispatcher`
and assigns that as the process-wide `HttpHandlerResourceV3.CredentialService`, exactly once. If a
host application has already registered a credential service, the registrar leaves it in place
(first writer wins) and per-call options cannot apply (`NuGetCache` logs a warning). `Register(get, set, createDefault)`
(testable through its delegates) serializes registrations with a static lock, returns without creating
anything when a service is already present, and otherwise creates the default service and re-reads the
property immediately before the final assignment so a host service that appeared meanwhile is not
overwritten; if `createDefault` throws, nothing is assigned and a later call may retry.
The SDK property has no atomic compare-and-set, so a host write landing between that last read and the
assignment remains possible: the residual window is tiny but nonzero. A host service written during
`SetupDefaultCredentialService` itself cannot be distinguished from the default it installs; hosts should
register their service before the first call.

The dispatcher reads an `AsyncLocal` call context set by `CredentialDispatcher.BeginScope` from the
public methods of `NuGetCache`:

- **No scope** (for example, work that suppresses execution-context flow): delegates to the original
  non-interactive default service.
- **Scope with a callback**: for non-proxy requests the last-known-good credential cache is bypassed
  (so credentials cached by another call cannot take precedence over the callback) and the callback is
  consulted first. A non-null
  `NuGetCredential` becomes the response. A per-call map records, for each (URI, request type) pair
  asked, the in-flight (then completed) callback task: the callback runs at most once per pair per
  call. A concurrent challenge for a pair whose callback is still running awaits that same task and
  shares its credential (or its decline) rather than seeing a premature decline. A null answer is
  remembered and the
  providers are still consulted; a second challenge for a pair whose supplied credential was
  rejected throws the unsatisfied-challenge exception.
- **Fail fast**: when no credential can be supplied for a non-proxy challenge (callback declined and
  providers returned nothing, or a supplied credential was rejected), the dispatcher throws
  `AuthChallengeUnsatisfiedException`, an `OperationCanceledException`, instead of returning null.
  Returning null made NuGet retry with ~1.1 s back-off (about 8 s and 16 requests for a 401
  everywhere). `AuthFailureClassifier`, `PackageSourceResolver`, `PackageDownloader` and `NuGetCache`
  translate it into `NuGetAuthenticationException`. NuGet's flat-container download loop swallows
  the exception, retries (~5 s) and reports "not found", so the dispatcher also records the
  challenge URI in an ambient field consulted by the downloader and version listing. This
  download-stage retry delay is inherent to NuGet and is not eliminated.
- **Scope with a logger, non-interactive**: a lazily created non-interactive `CredentialService` whose
  plugin logger forwards to the ambient call's logger, so provider diagnostics reach the caller. Without
  a logger the original default service is used unchanged.
- **Interactive** (or any scope that needs providers): a lazily created `CredentialService` built from
  the secure plugin credential providers (dialogs allowed) and the default network credentials provider.
- **Timeout**: acquisition runs under a linked cancellation token with `CancelAfter(AuthenticationTimeout)`
  and is raced against the timeout, so a callback that ignores its token is abandoned and the call fails
  with `OperationCanceledException`. The work is started via `Task.Run`, so a callback that blocks
  synchronously before returning its task cannot defeat the timeout; abandoned work cannot be interrupted
  and may keep running (holding a thread) after the timeout. Plugin processes are owned by NuGet's
  shared `PluginManager`; verified by decompiling NuGet.Protocol 7.3.1 and re-checked against the 7.9.0
  source, a plugin is started with
  `CancellationToken.None`, so cancellation or timeout does not terminate it. It exits on NuGet's idle
  timeout (30 minutes by default, `NUGET_PLUGIN_IDLE_TIMEOUT_IN_SECONDS`), on parent-exit detection, or at
  process end. The manager is a static `Lazy` with no reset hook and is shared with other NuGet users in
  the process, so the library does not dispose it.
- **Timed-out flag**: when the timeout elapses the dispatcher records it on the call's context (shared by
  reference across the call's async flow) and refuses any later credential exchange for that call with
  `OperationCanceledException`, without invoking the callback again. NuGet's HTTP pipeline may retry or
  wrap the timeout failure (and a retried challenge would otherwise look like a rejected credential), so
  `BeginScope` returns a `Scope` whose `ThrowIfTimedOut()` lets `NuGetCache` raise
  `OperationCanceledException` after each resolve, download, or version query and before building any
  failure. The timeout therefore always takes priority over swallowed or classified failures. The flag is
  set only when the caller's own token was not canceled.
`NuGetLoggerAdapter` adapts a `Microsoft.Extensions.Logging.ILogger` to the NuGet logger so SDK
messages reach the caller's logger.

Limitations (stated honestly rather than hidden):

- The scope is an `AsyncLocal`, so it follows the call's async flow only. Work that suppresses
  execution-context flow sees no scope and gets the default non-interactive behavior. The scope is restored when the
  disposable returned by `BeginScope` is disposed, so concurrent calls with different options stay isolated.
- The credential-request loop guard is per call and keyed by (URI, request type); it prevents
  repeated prompts but cannot tell a rejected credential from a legitimate second request.
- After a timeout the abandoned callback or plugin may keep running; the library does not
  terminate plugin processes, and NuGet's `PluginManager` stops them only on idle timeout, parent exit, or
  process end (see above). Callers should treat a timeout as a failed call, not as proof that cleanup completed.
- When a host already registered its own credential service, per-call options are not applied.
- A cancellation requested by the caller's own `CancellationToken` is never reported as an authentication
  failure, even if a challenge was recorded earlier in the call; it propagates as a cancellation.

Satisfies requirements `Caching-CredentialServiceRegistrar-DispatchPerCall`,
`Caching-CredentialServiceRegistrar-CallbackFirst`, `Caching-CredentialServiceRegistrar-LoopGuard`, and
`Caching-CredentialServiceRegistrar-FailFastOnUnsatisfiedChallenge`.

### Error Handling

The class performs no local error handling. Because registration is memoized with
`Lazy<bool>` (`ExecutionAndPublication`), an exception thrown by the registration action
propagates to the caller of `EnsureRegistered` and is cached for that instance, so later calls
on the same instance rethrow it rather than retrying.

### Dependencies

- **NuGet** (OTS): `DefaultCredentialServiceUtility.SetupDefaultCredentialService`, `CredentialService`,
  plugin credential providers, and `NullLogger` from the NuGet client SDK.
- `Microsoft.Extensions.Logging.Abstractions` (logger adapter), treated as part of the NuGet OTS item.
- BCL: `System.Lazy<T>`, `System.Threading.LazyThreadSafetyMode`.
- No other units or subsystems.

### Callers

- `NuGetCache`, from both `EnsureCachedAsync` and `GetVersionsAsync`, via the injected
  `ICredentialServiceRegistrar` (the shared `DefaultCredentialRegistrar` in production, a spy
  double in tests).
