## CredentialServiceRegistrar Design

### Overview

`CredentialServiceRegistrar` (together with the `ICredentialServiceRegistrar` interface it
implements) registers the NuGet SDK's default credential service once per process, in the
DemaConsulting NuGet Caching library. Registration mirrors the setup performed internally by the
`dotnet` CLI and MSBuild restore pipeline, and is a prerequisite for `HttpSourceAuthenticationHandler`
to retry an HTTP 401 challenge using credentials resolved from a NuGet credential-provider plugin
(e.g. for JFrog Artifactory or Azure Artifacts).

The interface is marked `internal` because it exists purely as a testability seam; the concrete
`CredentialServiceRegistrar` class is likewise `internal` because it is an implementation detail
of the library and is not part of the public API surface.

### Class Structure

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

    public void EnsureRegistered();
}
```

Default `ICredentialServiceRegistrar` implementation that registers the NuGet SDK's default
credential service via `DefaultCredentialServiceUtility.SetupDefaultCredentialService`, memoized
per instance so repeated calls on the same instance are cheap. `DefaultCredentialRegistrar` is
the single, process-wide instance shared by every real (non-test) `EnsureCachedAsync` call.

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

### Method Descriptions

#### `EnsureRegistered()`

Ensures the NuGet SDK's default credential service is registered. On the first call on a given
instance, invokes `DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance,
nonInteractive: true)`, mirroring the setup performed internally by the `dotnet` CLI and MSBuild
restore pipeline. Subsequent calls on the same instance are a cheap, thread-safe no-op due to the
`Lazy<bool>` memoization.

Satisfies requirements `Caching-CredentialServiceRegistrar-RegisterOnce` and
`Caching-CredentialServiceRegistrar-DefaultWiredToRealFlow`.
