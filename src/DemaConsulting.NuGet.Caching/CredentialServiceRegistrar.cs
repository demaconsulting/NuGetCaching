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
///     instance. <see cref="DefaultCredentialServiceUtility.SetupDefaultCredentialService"/> is
///     itself idempotent (it only assigns <c>HttpHandlerResourceV3.CredentialService</c> when it
///     is still <see langword="null"/>), but it always re-creates the delegating logger, so
///     memoizing avoids that redundant work on every call. A single static instance
///     (<see cref="DefaultCredentialRegistrar"/>) is shared by every real (non-test)
///     <c>EnsureCachedAsync</c> call in the process, giving the required once-per-process
///     registration semantics; a freshly constructed instance (as used by tests) naturally
///     starts unregistered.
/// </remarks>
internal sealed class CredentialServiceRegistrar : ICredentialServiceRegistrar
{
    /// <summary>
    ///     The single, process-wide <see cref="ICredentialServiceRegistrar"/> instance used by every
    ///     real (non-test) <c>EnsureCachedAsync</c> call, giving once-per-process registration
    ///     semantics for the real NuGet SDK credential service.
    /// </summary>
    internal static readonly ICredentialServiceRegistrar DefaultCredentialRegistrar = new CredentialServiceRegistrar();

    private Lazy<bool> _registered = CreateRegistrationLazy();

    private static Lazy<bool> CreateRegistrationLazy() =>
        new(
            () =>
            {
                // nonInteractive: true - this is a library used in build tooling, not an
                // interactive CLI, so credential providers must not attempt to show a UI prompt
                DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
                return true;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public void EnsureRegistered() => _ = _registered.Value;

    /// <summary>
    ///     Test-only seam that resets this instance's one-time registration memoization,
    ///     forcing the next call to <see cref="EnsureRegistered"/> to genuinely re-invoke
    ///     <see cref="DefaultCredentialServiceUtility.SetupDefaultCredentialService"/>. This lets
    ///     a test prove a real null-to-non-null <c>HttpHandlerResourceV3.CredentialService</c>
    ///     transition caused by a specific call to the shared, process-wide
    ///     <see cref="DefaultCredentialRegistrar"/>, rather than observing a stale non-null value
    ///     left behind by an earlier call elsewhere in the same test run.
    /// </summary>
    internal void ResetForTesting() => _registered = CreateRegistrationLazy();
}
