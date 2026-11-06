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
///     (no options supplied) every request is delegated unchanged to the default
///     non-interactive service. Inside a scope: a <see cref="NuGetCacheOptions.CredentialCallback"/>
///     is consulted first for 401/403 challenges; when it yields nothing the request goes to the
///     interactive or non-interactive provider-based service according to
///     <see cref="NuGetCacheOptions.Interactive"/>. The optional
///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> bounds each exchange.
/// </remarks>
internal sealed class CredentialDispatcher : ICredentialService
{
    private static readonly AsyncLocal<CallContext?> Ambient = new();

    // The package source (as a redacted key) the current code path is operating on, if marked
    private static readonly AsyncLocal<string?> CurrentSource = new();

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
    /// <param name="defaultService">The non-interactive credential service used outside any option scope.</param>
    internal CredentialDispatcher(ICredentialService defaultService)
    {
        ArgumentNullException.ThrowIfNull(defaultService);
        _defaultService = defaultService;
    }

    /// <summary>
    ///     Gets the logger of the current ambient call, or <see langword="null"/> when there is none.
    /// </summary>
    internal static NuGetLoggerAdapter? AmbientLogger => Ambient.Value?.Logger;

    /// <summary>
    ///     Gets the URL of the first authentication challenge that could not be satisfied during the
    ///     current ambient call while operating on the same package source as
    ///     <paramref name="source"/>, or <see langword="null"/> when there was none.
    /// </summary>
    /// <param name="source">The package source URL the caller is operating on.</param>
    /// <returns>The recorded challenge URL for the source, or <see langword="null"/>.</returns>
    internal static Uri? FindAmbientUnsatisfiedChallenge(string source)
    {
        var context = Ambient.Value;
        if (context == null || !Uri.TryCreate(source, UriKind.Absolute, out var sourceUri))
        {
            return null;
        }

        return context.FindUnsatisfied(SourceKey(source), sourceUri);
    }

    /// <summary>
    ///     Marks the package source the current code path is operating on, so an unsatisfied
    ///     authentication challenge raised while the returned scope is open is attributed to that
    ///     source only (and not to another source on the same host).
    /// </summary>
    /// <remarks>
    ///     The caller must invoke this inline in the async method that awaits the NuGet calls and dispose
    ///     the result before moving on to the next source; it flows to the credential service through
    ///     the execution context like the call scope does.
    /// </remarks>
    /// <param name="sourceUrl">The URL of the package source.</param>
    /// <returns>A disposable that restores the previously marked source.</returns>
    internal static IDisposable BeginSource(string sourceUrl)
    {
        var previous = CurrentSource.Value;
        CurrentSource.Value = SourceKey(sourceUrl);
        return new SourceScope(previous);
    }

    private static string SourceKey(string sourceUrl) => AuthFailureClassifier.RedactUrl(sourceUrl);

    /// <inheritdoc />
    public bool HandlesDefaultCredentials => _defaultService.HandlesDefaultCredentials;

    /// <summary>
    ///     Opens an ambient scope that makes <paramref name="options"/> visible to credential
    ///     requests made by the current call and the tasks it awaits.
    /// </summary>
    /// <param name="options">The per-call options, or <see langword="null"/> when none were supplied.</param>
    /// <param name="logger">The adapted logger for the call, or <see langword="null"/>.</param>
    /// <returns>
    ///     A disposable scope that restores the previous ambient scope and can report whether
    ///     authentication timed out during the call.
    /// </returns>
    internal static Scope BeginScope(NuGetCacheOptions? options, NuGetLoggerAdapter? logger)
    {
        var previous = Ambient.Value;
        // Option-less calls use default options, which behave exactly like delegating to the default
        // service but still get the fail-fast handling of unsatisfied authentication challenges
        var current = new CallContext(options ?? new NuGetCacheOptions(), logger);
        Ambient.Value = current;
        return new Scope(previous, current);
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

        // Once authentication has timed out for this call, never start another exchange: NuGet may
        // retry the failed request, and a repeat challenge must not be turned into a 401 failure
        context.ThrowIfTimedOut();

        return await WithTimeoutAsync(
            context.Options.AuthenticationTimeout,
            token => AcquireAsync(context, uri, proxy, type, message, token),
            context.MarkTimedOut,
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

        // Only a call with a logger needs the logger-aware service; otherwise the default
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
            // The callback is consulted at most once per (uri, type) per call. A repeat challenge after it
            // supplied a credential means that credential was rejected, so the call fails instead of
            // looping. A repeat challenge after it declined (null) skips the callback but still falls
            // through to the credential providers.
            var key = (uri.AbsoluteUri, type);
            // The in-flight task is stored before the callback runs, so a concurrent challenge for the
            // same key awaits that single callback result instead of mistaking it for a decline.
            var inFlight = new TaskCompletionSource<NuGetCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var existing = context.Asked.GetOrAdd(key, inFlight);
            if (!ReferenceEquals(existing, inFlight))
            {
                if (!existing.Task.IsCompleted)
                {
                    // Concurrent challenge: share the outcome of the callback that is still running
                    context.Logger?.LogDebug("Credential callback is already answering a challenge for this request; awaiting its result.");
                    var shared = await AwaitSharedAsync(existing.Task, cancellationToken);
                    if (shared != null)
                    {
                        return new NetworkCredential(shared.UserName, shared.Secret);
                    }
                }
                else if (existing.Task.Status != TaskStatus.RanToCompletion)
                {
                    // The callback threw (or was canceled) on an earlier challenge: surface that original
                    // exception again instead of treating it as a decline
                    context.Logger?.LogDebug("Credential callback previously failed for this request; throwing its exception again.");
                    await existing.Task;
                }
                else if (existing.Task.Result != null)
                {
                    context.Logger?.LogDebug("Credential callback already answered a challenge for this request; not asking again.");
                    throw FailChallenge(context, uri, cancellationToken);
                }
                else
                {
                    context.Logger?.LogDebug("Credential callback already declined a challenge for this request; not asking again.");
                }
            }
            else
            {
                NuGetCredential? credential;
                try
                {
                    credential = await callback(uri, cancellationToken);
                }
                catch (Exception ex)
                {
                    inFlight.TrySetException(ex);
                    _ = inFlight.Task.Exception; // observed here; concurrent waiters rethrow it
                    throw;
                }

                inFlight.TrySetResult(credential);
                if (credential != null)
                {
                    return new NetworkCredential(credential.UserName, credential.Secret);
                }
            }
        }

        var service = SelectService(context);
        var credentials = await service.GetCredentialsAsync(uri, proxy, type, message, cancellationToken);

        // No credential is available for an HTTP challenge: stop NuGet's retry loop (which would sleep
        // about a second per retry) by canceling the request; callers report it as an authentication failure
        if (credentials == null && type != CredentialRequestType.Proxy)
        {
            context.Logger?.LogDebug("No credentials are available for the authentication challenge; failing the request.");
            throw FailChallenge(context, uri, cancellationToken);
        }

        return credentials;
    }

    private static async Task<NuGetCredential?> AwaitSharedAsync(Task<NuGetCredential?> shared, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            // A waiter must remain cancelable even if the shared callback ignores its token
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
            if (await Task.WhenAny(shared, cancelled.Task) != shared)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return await shared;
    }

    private static Exception FailChallenge(CallContext context, Uri uri, CancellationToken cancellationToken)
    {
        // Caller cancellation must propagate as a normal cancellation, never become an authentication failure
        cancellationToken.ThrowIfCancellationRequested();
        context.MarkUnsatisfied(uri, CurrentSource.Value);
        return new AuthChallengeUnsatisfiedException(uri);
    }

    private static async Task<T> WithTimeoutAsync<T>(
        TimeSpan? timeout,
        Func<CancellationToken, Task<T>> work,
        Action<TimeSpan> onTimeout,
        CancellationToken cancellationToken)
    {
        if (timeout == null || timeout.Value == Timeout.InfiniteTimeSpan)
        {
            return await work(cancellationToken);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout.Value);

        // Race the work against cancellation so a callback that ignores its token is still abandoned
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = linked.Token.Register(() => cancelled.TrySetResult(true));

        // Start the work on the thread pool so a callback or provider that blocks synchronously
        // before returning its Task cannot prevent the timeout race below from being reached. The
        // abandoned work cannot be interrupted if it ignores the token and may keep running (and
        // hold its thread) after the timeout; only the caller is released.
        var task = Task.Run(() => work(linked.Token), CancellationToken.None);

        if (await Task.WhenAny((Task)task, cancelled.Task) == task)
        {
            try
            {
                return await task;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // The work honored the timeout token; record the timeout before propagating
                onTimeout(timeout.Value);
                throw;
            }
        }

        // Observe any later failure of the abandoned work so it is not reported as unobserved
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        cancellationToken.ThrowIfCancellationRequested();

        // Record the timeout so the owning call can surface it even if NuGet wraps or retries the failure
        onTimeout(timeout.Value);
        throw new OperationCanceledException(
            $"Authentication did not complete within {timeout.Value.TotalSeconds:0.###} seconds.",
            linked.Token);
    }

    private static ICredentialService CreateInteractiveService() => CreateProviderService(interactive: true);

    /// <summary>
    ///     Creates the default non-interactive credential service that the dispatcher wraps and that
    ///     serves requests made outside any option scope.
    /// </summary>
    /// <returns>The default credential service.</returns>
    internal static ICredentialService CreateDefaultService() => CreateProviderService(interactive: false);

    private static ICredentialService CreateProviderService(bool interactive)
    {
        // Equivalent to DefaultCredentialServiceUtility.SetupDefaultCredentialService, but it does not
        // write NuGet's static credential service; the logger forwards
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

    internal sealed record CallContext(NuGetCacheOptions Options, NuGetLoggerAdapter? Logger)
    {
        // Set when authentication exceeded AuthenticationTimeout; shared by reference with every
        // task of the call, so it is visible however NuGet surfaces or wraps the failure
        private volatile string? _timeoutMessage;

        // Set when a challenge could not be satisfied; NuGet's own retry loops can swallow the fail-fast
        // exception (reporting "not found" instead), so the owning call checks this to still report the
        // authentication failure
        // Keyed by the package source the challenge was raised for (see BeginSource), so two sources on
        // the same host are never conflated; a challenge raised outside any marked source is keyed by
        // its authority (host and port) instead
        private readonly ConcurrentDictionary<string, Uri> _unsatisfiedChallenges =
            new(StringComparer.OrdinalIgnoreCase);

        internal Uri? FindUnsatisfied(string sourceKey, Uri source)
        {
            if (_unsatisfiedChallenges.TryGetValue(sourceKey, out var challenge))
            {
                return challenge;
            }

            // Authority keys exist only for challenges that were raised outside any marked source
            return _unsatisfiedChallenges.TryGetValue(source.Authority, out challenge) ? challenge : null;
        }

        internal void MarkUnsatisfied(Uri uri, string? sourceKey) =>
            _unsatisfiedChallenges.TryAdd(sourceKey ?? uri.Authority, uri);

        // Challenges the callback has been asked about; the value is the callback's (possibly still
        // running) answer: a credential when it supplied one, null when it declined, or a faulted or
        // canceled task when it threw
        internal ConcurrentDictionary<(string Uri, CredentialRequestType Type), TaskCompletionSource<NuGetCredential?>> Asked { get; } = new();

        internal void MarkTimedOut(TimeSpan timeout) =>
            _timeoutMessage ??= $"Authentication did not complete within {timeout.TotalSeconds:0.###} seconds.";

        internal void ThrowIfTimedOut()
        {
            var message = _timeoutMessage;
            if (message != null)
            {
                throw new OperationCanceledException(message);
            }
        }
    }

    /// <summary>
    ///     The scope of one <c>NuGetCache</c> call; disposing restores the previous ambient scope.
    /// </summary>
    internal sealed class Scope : IDisposable
    {
        private readonly CallContext? _previous;
        private readonly CallContext? _current;

        internal Scope(CallContext? previous, CallContext? current)
        {
            _previous = previous;
            _current = current;
        }

        /// <summary>
        ///     Throws <see cref="OperationCanceledException"/> when authentication timed out during
        ///     this call, giving the timeout priority over any failure NuGet reported instead.
        /// </summary>
        internal void ThrowIfTimedOut() => _current?.ThrowIfTimedOut();

        /// <inheritdoc />
        public void Dispose() => Ambient.Value = _previous;
    }

    private sealed class SourceScope : IDisposable
    {
        private readonly string? _previous;

        internal SourceScope(string? previous) => _previous = previous;

        public void Dispose() => CurrentSource.Value = _previous;
    }

    /// <summary>
    ///     Thrown by the dispatcher when an HTTP authentication challenge cannot be satisfied.
    /// </summary>
    /// <remarks>
    ///     It derives from <see cref="OperationCanceledException"/> because NuGet's retry loops rethrow
    ///     cancellation immediately instead of retrying (with a delay) a request that cannot succeed.
    ///     The owning <c>NuGetCache</c> call translates it into an authentication failure.
    /// </remarks>
    internal sealed class AuthChallengeUnsatisfiedException : OperationCanceledException
    {
        /// <summary>
        ///     Initializes a new instance for the challenged <paramref name="uri"/>.
        /// </summary>
        /// <param name="uri">The URL that issued the authentication challenge.</param>
        internal AuthChallengeUnsatisfiedException(Uri uri)
            : base("Response status code does not indicate success: 401 (Unauthorized). No credentials are available for the challenged request.")
        {
            ChallengeUri = uri;
        }

        /// <summary>
        ///     Gets the URL that issued the authentication challenge. It is deliberately not part of
        ///     <see cref="Exception.Message"/> because it may carry secrets in its query string.
        /// </summary>
        internal Uri ChallengeUri { get; }
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
