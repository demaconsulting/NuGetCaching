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
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
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
///     is consulted first for 401 challenges (a 403 fails before any callback or provider runs, since
///     no credential can grant a missing permission); when it yields nothing the request goes to the
///     interactive or non-interactive provider-based service according to
///     <see cref="NuGetCacheOptions.Interactive"/>; for a call with a callback that service (and with
///     it NuGet's retry cache) is created per call so one call's success never turns a later call's
///     fresh request for the same URL into a retry. The optional
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
    internal static UnsatisfiedChallenge? FindAmbientUnsatisfiedChallenge(string source)
    {
        var context = Ambient.Value;
        if (context == null || !Uri.TryCreate(source, UriKind.Absolute, out var sourceUri))
        {
            return null;
        }

        return context.FindUnsatisfied(SourceKey(source), sourceUri);
    }

    /// <summary>
    ///     Gets the reason the server gave for an HTTP 403 during the current ambient call while operating on
    ///     the same package source as <paramref name="source"/>, or <see langword="null"/> when none was seen.
    /// </summary>
    /// <param name="source">The package source URL the caller is operating on.</param>
    /// <returns>The recorded sanitized reason for the source, or <see langword="null"/>.</returns>
    internal static string? FindAmbientForbiddenReason(string source)
    {
        var context = Ambient.Value;
        if (context == null || !Uri.TryCreate(source, UriKind.Absolute, out var sourceUri))
        {
            return null;
        }

        return context.FindForbiddenReason(SourceKey(source), sourceUri);
    }

    /// <summary>
    ///     Remembers the reason a server gave for an HTTP 403 response, for the current ambient call, so
    ///     the unsatisfied challenge that follows can carry it.
    /// </summary>
    /// <remarks>
    ///     The credential service is only told that a request was forbidden, not why; the response (with its
    ///     headers and body) is seen only by <see cref="ForbiddenReasonHandler"/>, which calls this. The reason
    ///     is keyed like an unsatisfied challenge: by the marked package source, otherwise by the authority.
    /// </remarks>
    /// <param name="requestUri">The URL that answered 403.</param>
    /// <param name="reason">The sanitized reason, or <see langword="null"/> when the server gave none.</param>
    internal static void RecordForbiddenReason(Uri requestUri, string? reason)
    {
        if (reason != null)
        {
            Ambient.Value?.SetForbiddenReason(CurrentSource.Value ?? requestUri.Authority, reason);
        }
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

    /// <summary>
    ///     Builds a non-secret correlation key for a package source from a hash of its full URL text
    ///     (including any query), so sources differing only by query string or SAS token stay distinct
    ///     while no secret is ever stored in a key.
    /// </summary>
    internal static string SourceKey(string sourceUrl)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sourceUrl));
        // "src:" can never equal a host[:port] authority key; 16 hex characters (64 bits) suffice
        return "src:" + BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
    }

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

        return SelectService(context, isProxy).TryGetLastKnownGoodCredentialsFromCache(uri, isProxy, out credentials);
    }

    private ICredentialService SelectService(CallContext context, bool isProxy)
    {
        // A call with a callback bypasses the last-known-good cache for non-proxy requests (see
        // TryGetLastKnownGoodCredentialsFromCache), so the only thing a process-wide service would
        // retain for them is NuGet's retry cache (provider + request type + URL). After one successful
        // call that cache makes every later fresh request for the same URL a "retry", and
        // DefaultNetworkCredentialsCredentialProvider (Windows integrated authentication) returns
        // ProviderNotApplicable on a retry - a spurious 401 for a feed integrated authentication can
        // actually satisfy. Scoping the service, and with it that retry cache, to one call keeps a
        // credential rejected within the call a retry (no loops) while a later call starts clean.
        // Creating the service is cheap: the plugin providers are built lazily on the first credential
        // request that reaches them, and the plugin processes belong to the shared PluginManager.
        // Proxy requests and calls without a callback keep the shared services, whose last-known-good
        // cache works as NuGet intends.
        if (!isProxy && context.Options.CredentialCallback != null)
        {
            return context.GetOrCreatePerCallService(
                () => CreateProviderService(context.Options.Interactive));
        }

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
        // A forbidden response (HTTP 403) means the server knows who the caller is and still refuses access,
        // so no credential the host callback or a provider could supply helps; asking would only prompt
        // for nothing or make NuGet retry. Fail fast with the reason the server gave.
        if (type == CredentialRequestType.Forbidden)
        {
            context.Logger?.LogDebug("The server refused access (HTTP 403); not asking for credentials and failing the request.");
            throw FailChallenge(context, uri, type, cancellationToken);
        }

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
                    throw FailChallenge(context, uri, type, cancellationToken);
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

        var service = SelectService(context, type == CredentialRequestType.Proxy);
        var credentials = await service.GetCredentialsAsync(uri, proxy, type, message, cancellationToken);

        // No credential is available for an HTTP challenge: stop NuGet's retry loop (which would sleep
        // about a second per retry) by canceling the request; callers report it as an authentication failure
        if (credentials == null && type != CredentialRequestType.Proxy)
        {
            context.Logger?.LogDebug("No credentials are available for the authentication challenge; failing the request.");
            throw FailChallenge(context, uri, type, cancellationToken);
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

    private static Exception FailChallenge(
        CallContext context, Uri uri, CredentialRequestType type, CancellationToken cancellationToken)
    {
        // Caller cancellation must propagate as a normal cancellation, never become an authentication failure
        cancellationToken.ThrowIfCancellationRequested();

        // A forbidden response means the server accepted who the caller is but refuses access, so the
        // failure keeps its status code and the reason the server gave (when it was seen)
        var challenge = type == CredentialRequestType.Forbidden
            ? new UnsatisfiedChallenge(uri, 403, context.FindForbiddenReason(CurrentSource.Value ?? uri.Authority))
            : new UnsatisfiedChallenge(uri, 401, null);
        context.MarkUnsatisfied(challenge, CurrentSource.Value);
        return new AuthChallengeUnsatisfiedException(challenge);
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
            list.Add(DefaultProviderFactory());
            return list;
        });
        return new CredentialService(providers, nonInteractive: !interactive, handlesDefaultCredentials: true);
    }

    /// <summary>
    ///     Gets or sets the factory for the default-network-credentials provider appended after the
    ///     plugin providers; a test seam that lets tests substitute a provider with the retry behavior
    ///     of <see cref="DefaultNetworkCredentialsCredentialProvider"/> without needing a domain.
    /// </summary>
    internal static Func<ICredentialProvider> DefaultProviderFactory { get; set; } =
        () => new DefaultNetworkCredentialsCredentialProvider();

    internal sealed record CallContext(NuGetCacheOptions Options, NuGetLoggerAdapter? Logger)
    {
        // The call's own credential service (see SelectService); created on first use
        private ICredentialService? _perCallService;

        internal ICredentialService GetOrCreatePerCallService(Func<ICredentialService> create)
        {
            // Concurrent challenges of one call must share a single service (and retry cache). Creation
            // is cheap, so a racing loser is simply discarded.
            var existing = Volatile.Read(ref _perCallService);
            if (existing != null)
            {
                return existing;
            }

            var created = create();
            return Interlocked.CompareExchange(ref _perCallService, created, null) ?? created;
        }

        // Set when authentication exceeded AuthenticationTimeout; shared by reference with every
        // task of the call, so it is visible however NuGet surfaces or wraps the failure
        private volatile string? _timeoutMessage;

        // Set when a challenge could not be satisfied; NuGet's own retry loops can swallow the fail-fast
        // exception (reporting "not found" instead), so the owning call checks this to still report the
        // authentication failure
        // Keyed by the package source the challenge was raised for (see BeginSource), so two sources on
        // the same host are never conflated; a challenge raised outside any marked source is keyed by
        // its authority (host and port) instead
        private readonly ConcurrentDictionary<string, UnsatisfiedChallenge> _unsatisfiedChallenges =
            new(StringComparer.OrdinalIgnoreCase);

        // The reason a server gave for an HTTP 403, per source key (or authority), as seen by the
        // ForbiddenReasonHandler; the latest answer wins
        private readonly ConcurrentDictionary<string, string> _forbiddenReasons =
            new(StringComparer.OrdinalIgnoreCase);

        internal UnsatisfiedChallenge? FindUnsatisfied(string sourceKey, Uri source)
        {
            if (_unsatisfiedChallenges.TryGetValue(sourceKey, out var challenge))
            {
                return challenge;
            }

            // Authority keys exist only for challenges that were raised outside any marked source
            return _unsatisfiedChallenges.TryGetValue(source.Authority, out challenge) ? challenge : null;
        }

        internal void MarkUnsatisfied(UnsatisfiedChallenge challenge, string? sourceKey) =>
            _unsatisfiedChallenges.TryAdd(sourceKey ?? challenge.Uri.Authority, challenge);

        internal void SetForbiddenReason(string key, string reason) => _forbiddenReasons[key] = reason;

        internal string? FindForbiddenReason(string key) =>
            _forbiddenReasons.TryGetValue(key, out var reason) ? reason : null;

        internal string? FindForbiddenReason(string sourceKey, Uri source) =>
            FindForbiddenReason(sourceKey) ?? FindForbiddenReason(source.Authority);

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
        ///     Initializes a new instance for the challenged <paramref name="uri"/> (an HTTP 401).
        /// </summary>
        /// <param name="uri">The URL that issued the authentication challenge.</param>
        internal AuthChallengeUnsatisfiedException(Uri uri)
            : this(new UnsatisfiedChallenge(uri, 401, null))
        {
        }

        /// <summary>
        ///     Initializes a new instance for an unsatisfied <paramref name="challenge"/>.
        /// </summary>
        /// <param name="challenge">The challenge: its URL, status code (401 or 403) and, for a 403, the
        ///     server's reason.</param>
        internal AuthChallengeUnsatisfiedException(UnsatisfiedChallenge challenge)
            : base(challenge.StatusCode == 403
                ? "Response status code does not indicate success: 403 (Forbidden). No credential can satisfy the forbidden request."
                : "Response status code does not indicate success: 401 (Unauthorized). No credentials are available for the challenged request.")
        {
            Challenge = challenge;
        }

        /// <summary>
        ///     Gets the challenge that could not be satisfied.
        /// </summary>
        internal UnsatisfiedChallenge Challenge { get; }

        /// <summary>
        ///     Gets the URL that issued the authentication challenge. It is deliberately not part of
        ///     <see cref="Exception.Message"/> because it may carry secrets in its query string.
        /// </summary>
        internal Uri ChallengeUri => Challenge.Uri;
    }

    /// <summary>
    ///     An authentication challenge (or forbidden response) that no credential could satisfy.
    /// </summary>
    /// <param name="Uri">The URL the challenge was raised for; it may carry secrets, so it is never put in a message.</param>
    /// <param name="StatusCode">The HTTP status code: 401 (unauthorized) or 403 (forbidden).</param>
    /// <param name="Reason">For a 403, the sanitized reason the server gave, or <see langword="null"/> when it gave none.</param>
    internal sealed record UnsatisfiedChallenge(Uri Uri, int StatusCode, string? Reason);

    /// <summary>
    ///     The HTTP handler provider that makes a server's reason for an HTTP 403 visible to the
    ///     dispatcher: it is <see cref="HttpHandlerResourceV3Provider"/> with a
    ///     <see cref="ForbiddenReasonHandler"/> placed directly above the network handler.
    /// </summary>
    internal sealed class ForbiddenReasonHandlerProvider : HttpHandlerResourceV3Provider
    {
        /// <inheritdoc />
        public override async Task<Tuple<bool, INuGetResource?>> TryCreate(
            SourceRepository source, CancellationToken token)
        {
            var created = await base.TryCreate(source, token);
            if (created.Item1 && created.Item2 is HttpHandlerResourceV3 resource)
            {
                // The authentication handler is the handler whose inner handler is the network handler; the
                // capture goes between them so it sees the response before the credential service is asked
                for (var handler = resource.MessageHandler as DelegatingHandler;
                     handler != null;
                     handler = handler.InnerHandler as DelegatingHandler)
                {
                    if (ReferenceEquals(handler.InnerHandler, resource.ClientHandler))
                    {
                        handler.InnerHandler = new ForbiddenReasonHandler(resource.ClientHandler);
                        break;
                    }
                }
            }

            return created;
        }

        /// <summary>
        ///     Replaces the NuGet HTTP handler provider in <paramref name="providers"/> with this one.
        /// </summary>
        /// <param name="providers">The core NuGet resource providers.</param>
        /// <returns>The providers, with the HTTP handler provider replaced.</returns>
        internal static IEnumerable<Lazy<INuGetResourceProvider>> Install(IEnumerable<Lazy<INuGetResourceProvider>> providers) =>
            providers.Select(p => p.Value.GetType() == typeof(HttpHandlerResourceV3Provider)
                ? new Lazy<INuGetResourceProvider>(() => new ForbiddenReasonHandlerProvider())
                : p).ToList();
    }

    /// <summary>
    ///     A handler that reads the reason a server gives for an HTTP 403 response and records it for the
    ///     current call, then passes the response on unchanged.
    /// </summary>
    /// <remarks>
    ///     The NuGet authentication handler asks the credential service for credentials when a response is
    ///     401 or 403 but does not pass the response on. Azure DevOps (and Azure Artifacts) explain a 403
    ///     in the <c>X-TFS-ServiceError</c> header (URL-encoded), a JSON body with a <c>message</c> member,
    ///     or the response's reason phrase.
    /// </remarks>
    internal sealed class ForbiddenReasonHandler : DelegatingHandler
    {
        // A body larger than this is not read: it is not a short explanation
        private const long MaxBodyBytes = 16 * 1024;

        // The longest reason that is kept
        internal const int MaxReasonLength = 300;

        private static readonly Regex JsonMessagePattern = new(
            "\"message\"\\s*:\\s*\"(?<message>(?:[^\"\\\\]|\\\\.)*)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        /// <summary>
        ///     Initializes a new instance that sends requests to <paramref name="innerHandler"/>.
        /// </summary>
        /// <param name="innerHandler">The handler that performs the request.</param>
        internal ForbiddenReasonHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden && request.RequestUri != null)
            {
                string? reason = null;
                try
                {
                    reason = await ReadReasonAsync(response, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The reason is a courtesy; failing to read it must not change the outcome
                }

                RecordForbiddenReason(request.RequestUri, reason);
            }

            return response;
        }

        /// <summary>
        ///     Reads the server's reason for a 403 response: the <c>X-TFS-ServiceError</c> header, else the
        ///     <c>message</c> of a JSON body, else a reason phrase that says more than "Forbidden".
        /// </summary>
        /// <param name="response">The 403 response.</param>
        /// <param name="cancellationToken">Cancels reading the body.</param>
        /// <returns>The sanitized reason, or <see langword="null"/> when the server gave none.</returns>
        internal static async Task<string?> ReadReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Headers.TryGetValues("X-TFS-ServiceError", out var serviceErrors))
            {
                var fromHeader = Sanitize(Decode(serviceErrors.FirstOrDefault()));
                if (fromHeader != null)
                {
                    return fromHeader;
                }
            }

            if (response.Content != null)
            {
                var fromBody = await TryReadBodyReasonAsync(response.Content, cancellationToken);
                if (fromBody != null)
                {
                    return fromBody;
                }
            }

            var phrase = Sanitize(response.ReasonPhrase);
            return phrase != null && !string.Equals(phrase, "Forbidden", StringComparison.OrdinalIgnoreCase)
                ? phrase
                : null;
        }

        /// <summary>
        ///     Reads the <c>message</c> of a JSON body, tolerating a body that is too large or cannot be read so
        ///     that the caller can still fall back to the reason phrase.
        /// </summary>
        /// <remarks>
        ///     The cancellation-token overloads used here are provided on <c>netstandard2.0</c> by the Polyfill
        ///     package, so no conditional compilation is needed.
        /// </remarks>
        /// <param name="content">The response content.</param>
        /// <param name="cancellationToken">Cancels reading the body.</param>
        /// <returns>The sanitized message, or <see langword="null"/> when there is none or it could not be read.</returns>
        private static async Task<string?> TryReadBodyReasonAsync(HttpContent content, CancellationToken cancellationToken)
        {
            try
            {
                await content.LoadIntoBufferAsync(MaxBodyBytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var body = await content.ReadAsStringAsync(cancellationToken);
                return Sanitize(ExtractJsonMessage(body));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An oversized or unreadable body is not a short explanation; the caller falls back to the
                // reason phrase
                return null;
            }
        }

        /// <summary>
        ///     Reduces a server-supplied text to a single, short line with no URL secrets.
        /// </summary>
        /// <param name="text">The raw text, or <see langword="null"/>.</param>
        /// <returns>The sanitized text, or <see langword="null"/> when nothing remains.</returns>
        internal static string? Sanitize(string? text)
        {
            if (text == null || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            // Whitespace and control characters (line breaks included) become single spaces; a double quote
            // would end the quoted reason in the failure message, so it becomes a single quote
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                builder.Append(ToLineCharacter(c));
            }

            var line = Regex.Replace(builder.ToString(), " {2,}", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
            line = AuthFailureClassifier.RedactEmbeddedUrls(line);
            if (line.Length > MaxReasonLength)
            {
                line = line[..(MaxReasonLength - 1)].TrimEnd() + "…";
            }

            return line.Length == 0 ? null : line;
        }

        private static char ToLineCharacter(char c)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                return ' ';
            }

            return c == '"' ? '\'' : c;
        }

        private static string? Decode(string? value)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                return Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return value;
            }
        }

        private static string? ExtractJsonMessage(string body)
        {
            try
            {
                var match = JsonMessagePattern.Match(body);
                return match.Success ? Regex.Unescape(match.Groups["message"].Value) : null;
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                return null;
            }
        }
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
