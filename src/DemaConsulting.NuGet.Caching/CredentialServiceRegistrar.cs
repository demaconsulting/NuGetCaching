// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Abstracts NuGet SDK credential-service registration so it can be substituted with a test
///     double, letting a test assert that <c>EnsureCachedAsync</c> invokes registration without
///     observing or resetting any shared, static process-wide state.
/// </summary>
internal interface ICredentialServiceRegistrar
{
    /// <summary>
    ///     Ensures the NuGet SDK's default credential service is registered.
    /// </summary>
    void EnsureRegistered();
}

/// <summary>
///     Default <see cref="ICredentialServiceRegistrar"/> implementation that registers the
///     NuGet SDK's default credential service, mirroring the setup performed internally by the
///     <c>dotnet</c> CLI and MSBuild restore pipeline. Static <c>packageSourceCredentials</c>
///     configured in <c>nuget.config</c> are applied directly to the underlying
///     <c>HttpClientHandler</c> and are honored on a source's HTTP 401 challenge regardless of
///     whether a credential service is registered. Registration instead matters when a NuGet
///     credential-provider plugin must be consulted, or an <see cref="ICredentialService"/>-
///     mediated retry is required (e.g. for JFrog Artifactory or Azure Artifacts), which static
///     credentials alone do not exercise.
/// </summary>
/// <remarks>
///     Registration work is memoized per instance via <see cref="Lazy{T}"/> with
///     <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>, so
///     <see cref="EnsureRegistered"/> is cheap and thread-safe to call repeatedly on the same
///     instance. Registration is idempotent (it only assigns
///     <c>HttpHandlerResourceV3.CredentialService</c> when it is still <see langword="null"/>), but
///     memoizing avoids re-creating the default service on every call. A single static instance
///     (<see cref="DefaultCredentialRegistrar"/>) is shared by every real (non-test)
///     <c>EnsureCachedAsync</c> call in the process, giving the required once-per-process
///     registration semantics; a freshly constructed instance (as used by tests) naturally
///     starts unregistered. The registered service is a <c>CredentialDispatcher</c> wrapping the
///     default service: it applies per-call <c>NuGetCacheOptions</c> when a call scope is open and
///     otherwise behaves as the plain non-interactive default. If a host application registered its
///     own credential service first, that service is left in place.
/// </remarks>
internal sealed class CredentialServiceRegistrar : ICredentialServiceRegistrar
{
    /// <summary>
    ///     The single, process-wide <see cref="ICredentialServiceRegistrar"/> instance used by every
    ///     real (non-test) <c>EnsureCachedAsync</c> call, giving once-per-process registration
    ///     semantics for the real NuGet SDK credential service.
    /// </summary>
    internal static readonly ICredentialServiceRegistrar DefaultCredentialRegistrar = new CredentialServiceRegistrar();

    private readonly Lazy<bool> _registered;

    /// <summary>
    ///     Initializes a new instance that registers the NuGet SDK's real default credential service.
    /// </summary>
    internal CredentialServiceRegistrar()
        : this(RegisterDefaultCredentialService)
    {
    }

    /// <summary>
    ///     Initializes a new instance that performs the supplied registration action at most once.
    /// </summary>
    /// <remarks>
    ///     This overload exists so tests can count how many times registration actually runs
    ///     without observing or resetting the NuGet SDK's process-wide static state.
    /// </remarks>
    /// <param name="registration">The registration action to memoize.</param>
    internal CredentialServiceRegistrar(Action registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registered = new Lazy<bool>(
            () =>
            {
                registration();
                return true;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public void EnsureRegistered() => _ = _registered.Value;

    /// <summary>
    ///     Gets a value indicating whether the NuGet SDK's process-wide credential service is this
    ///     library's <see cref="CredentialDispatcher"/>, so per-call options can be applied.
    /// </summary>
    internal static bool IsDispatcherActive =>
        HttpHandlerResourceV3.CredentialService is { } current && ReferenceEquals(current, _installed);

    private static readonly object RegistrationLock = new();

    private static Lazy<ICredentialService>? _installed;

    private static void RegisterDefaultCredentialService() =>
        Register(
            () => HttpHandlerResourceV3.CredentialService,
            value => HttpHandlerResourceV3.CredentialService = value,
            // Non-interactive by default; interactive providers are selected per call by the dispatcher
            CredentialDispatcher.CreateDefaultService);

    /// <summary>
    ///     Registers the dispatcher around a freshly created default credential service unless a host
    ///     application's own service is (or becomes) present.
    /// </summary>
    /// <remarks>
    ///     The default service is created by this library (never through
    ///     <c>DefaultCredentialServiceUtility.SetupDefaultCredentialService</c>, which writes the static
    ///     property itself), so the only write to <c>HttpHandlerResourceV3.CredentialService</c> is the
    ///     final assignment below. Our own registrations are serialized with a lock, and the property is
    ///     read immediately before that assignment so a host service that appeared while the default service
    ///     was being created is not overwritten. The NuGet SDK exposes the property as a plain static with no
    ///     atomic compare-and-set, so a host write landing between that last read and the assignment (two
    ///     adjacent statements) is still possible: the residual window is tiny but nonzero and cannot be
    ///     closed without reflection on NuGet internals, which is rejected as fragile. Hosts that need their
    ///     own credential service should register it before the first call.
    /// </remarks>
    /// <param name="get">Reads the process-wide credential service.</param>
    /// <param name="set">Assigns the process-wide credential service.</param>
    /// <param name="createDefault">Creates the default (non-interactive) credential service.</param>
    internal static void Register(
        Func<Lazy<ICredentialService>?> get,
        Action<Lazy<ICredentialService>?> set,
        Func<ICredentialService> createDefault)
    {
        lock (RegistrationLock)
        {
            // Only register when nothing else has: a host application's own credential service
            // (first writer wins) is never replaced
            if (get() != null)
            {
                return;
            }

            // Wrap the default service so per-call options can be applied; with no ambient options
            // the dispatcher delegates every request unchanged
            var defaultService = createDefault();
            var wrapper = new Lazy<ICredentialService>(() => new CredentialDispatcher(defaultService));

            // Re-check immediately before assigning: leave a service that appeared in the meantime alone
            if (get() != null)
            {
                return;
            }

            _installed = wrapper;
            set(wrapper);
        }
    }
}
