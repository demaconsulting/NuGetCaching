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

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Plugins;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     The single process-wide NuGet <see cref="ICredentialService"/> that applies the
///     <see cref="NuGetCacheOptions"/> of whichever <c>NuGetCache</c> call is currently executing.
/// </summary>
/// <remarks>
///     NuGet exposes one static credential service, so per-call behavior is selected from an
///     <see cref="AsyncLocal{T}"/> scope opened by <see cref="BeginScope"/>. With no ambient scope
///     (the 1.5.0 overloads) every request is delegated unchanged to the default
///     non-interactive service. Inside a scope: a <see cref="NuGetCacheOptions.CredentialCallback"/>
///     is consulted first for 401/403 challenges; when it yields nothing the request goes to the
///     interactive or non-interactive provider-based service according to
///     <see cref="NuGetCacheOptions.Interactive"/>. The optional
///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> bounds each exchange.
/// </remarks>
internal sealed class CredentialDispatcher : ICredentialService
{
    private static readonly AsyncLocal<CallContext?> Ambient = new();

    private static readonly Lazy<ICredentialService> InteractiveService = new(
        CreateInteractiveService,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<ICredentialService> NonInteractiveLoggedService = new(
        () => CreateProviderService(interactive: false),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ICredentialService _defaultService;

    /// <summary>
    ///     Initializes a new instance that delegates non-interactive and option-less requests to
    ///     <paramref name="defaultService"/>.
    /// </summary>
    /// <param name="defaultService">The 1.5.0-equivalent, non-interactive credential service.</param>
    internal CredentialDispatcher(ICredentialService defaultService)
    {
        ArgumentNullException.ThrowIfNull(defaultService);
        _defaultService = defaultService;
    }

    /// <summary>
    ///     Gets the logger of the current ambient call, or <see langword="null"/> when there is none.
    /// </summary>
    internal static NuGetLoggerAdapter? AmbientLogger => Ambient.Value?.Logger;

    /// <inheritdoc />
    public bool HandlesDefaultCredentials => _defaultService.HandlesDefaultCredentials;

    /// <summary>
    ///     Opens an ambient scope that makes <paramref name="options"/> visible to credential
    ///     requests made by the current call and the tasks it awaits.
    /// </summary>
    /// <param name="options">The per-call options, or <see langword="null"/> for 1.5.0 behavior.</param>
    /// <param name="logger">The adapted logger for the call, or <see langword="null"/>.</param>
    /// <returns>A disposable that restores the previous ambient scope.</returns>
    internal static IDisposable BeginScope(NuGetCacheOptions? options, NuGetLoggerAdapter? logger)
    {
        var previous = Ambient.Value;
        Ambient.Value = options == null ? null : new CallContext(options, logger);
        return new Scope(previous);
    }

    /// <inheritdoc />
    public async Task<ICredentials?> GetCredentialsAsync(
        Uri uri,
        IWebProxy? proxy,
        CredentialRequestType type,
        string message,
        CancellationToken cancellationToken)
    {
        var context = Ambient.Value;
        if (context == null)
        {
            return await _defaultService.GetCredentialsAsync(uri, proxy, type, message, cancellationToken);
        }

        return await WithTimeoutAsync(
            context.Options.AuthenticationTimeout,
            token => AcquireAsync(context, uri, proxy, type, message, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public bool TryGetLastKnownGoodCredentialsFromCache(
        Uri uri,
        bool isProxy,
        out ICredentials? credentials)
    {
        var context = Ambient.Value;
        if (context == null)
        {
            return _defaultService.TryGetLastKnownGoodCredentialsFromCache(uri, isProxy, out credentials);
        }

        // A configured callback must be consulted before any cached credential (which may have been
        // populated by a different call), so the cache is bypassed for non-proxy requests
        if (!isProxy && context.Options.CredentialCallback != null)
        {
            credentials = null;
            return false;
        }

        return SelectService(context).TryGetLastKnownGoodCredentialsFromCache(uri, isProxy, out credentials);
    }

    private ICredentialService SelectService(CallContext context)
    {
        if (context.Options.Interactive)
        {
            return InteractiveService.Value;
        }

        // Only a call with a logger needs the logger-aware service; otherwise the 1.5.0 default
        // service (with a null logger) is used unchanged
        return context.Logger != null ? NonInteractiveLoggedService.Value : _defaultService;
    }

    private async Task<ICredentials?> AcquireAsync(
        CallContext context,
        Uri uri,
        IWebProxy? proxy,
        CredentialRequestType type,
        string message,
        CancellationToken cancellationToken)
    {
        var callback = context.Options.CredentialCallback;
        if (callback != null && type != CredentialRequestType.Proxy)
        {
            // A repeat challenge for a (uri, type) the callback already answered means the supplied
            // credential was rejected; supply nothing so the call fails instead of looping
            if (!context.Answered.TryAdd((uri.AbsoluteUri, type), 0))
            {
                context.Logger?.LogDebug("Credential callback already answered a challenge for this request; not asking again.");
                return null;
            }

            var credential = await callback(uri, cancellationToken);
            if (credential != null)
            {
                return new NetworkCredential(credential.UserName, credential.Secret);
            }

            // Nothing supplied: fall through to the credential providers (and allow a later,
            // provider-driven retry for this URI)
            context.Answered.TryRemove((uri.AbsoluteUri, type), out _);
        }

        var service = SelectService(context);
        return await service.GetCredentialsAsync(uri, proxy, type, message, cancellationToken);
    }

    private static async Task<T> WithTimeoutAsync<T>(
        TimeSpan? timeout,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        if (timeout == null)
        {
            return await work(cancellationToken);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout.Value);

        // Race the work against cancellation so a callback that ignores its token is still abandoned
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = linked.Token.Register(() => cancelled.TrySetResult(true));
        var task = work(linked.Token);

        if (await Task.WhenAny((Task)task, cancelled.Task) == task)
        {
            return await task;
        }

        // Observe any later failure of the abandoned work so it is not reported as unobserved
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(
            $"Authentication did not complete within {timeout.Value.TotalSeconds:0.###} seconds.",
            linked.Token);
    }

    private static ICredentialService CreateInteractiveService() => CreateProviderService(interactive: true);

    private static ICredentialService CreateProviderService(bool interactive)
    {
        // Mirrors DefaultCredentialServiceUtility.SetupDefaultCredentialService; the logger forwards
        // to whichever call is currently ambient
        var providers = new AsyncLazy<IEnumerable<ICredentialProvider>>(async () =>
        {
            var list = new List<ICredentialProvider>();
            var builder = new SecurePluginCredentialProviderBuilder(
                PluginManager.Instance, canShowDialog: interactive, new AmbientNuGetLogger());
            list.AddRange(await builder.BuildAllAsync());
            list.Add(new DefaultNetworkCredentialsCredentialProvider());
            return list;
        });
        return new CredentialService(providers, nonInteractive: !interactive, handlesDefaultCredentials: true);
    }

    private sealed record CallContext(NuGetCacheOptions Options, NuGetLoggerAdapter? Logger)
    {
        internal ConcurrentDictionary<(string Uri, CredentialRequestType Type), byte> Answered { get; } = new();
    }

    private sealed class Scope(CallContext? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }

    private sealed class AmbientNuGetLogger : LoggerBase
    {
        public override void Log(ILogMessage message) => AmbientLogger?.Log(message);

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
