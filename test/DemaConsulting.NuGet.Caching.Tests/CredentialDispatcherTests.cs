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
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Local-integration tests for the per-call credential handling of <c>NuGetCache</c>: the
///     credential callback, the authentication timeout, the credential-request loop guard, concurrent
///     calls with differing options, the logger, and registration of the dispatching credential service.
/// </summary>
/// <remarks>
///     These tests replace the process-wide <c>HttpHandlerResourceV3.CredentialService</c> and restore it
///     afterward; xUnit parallelization is disabled for this assembly (see <c>AssemblyInfo.cs</c>).
/// </remarks>
public class CredentialDispatcherTests
{
    private const string UserName = "test-user";
    private const string Password = "test-password";

    /// <summary>
    ///     Tests that a callback supplying credentials lets <c>EnsureCachedAsync</c> pass an HTTP
    ///     401 challenge from a local server that then accepts the Basic credentials.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_CallbackSuppliesCredentials_DownloadsFrom401Source()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - a feed that answers 401 until it sees the right Basic credentials
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Callback.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Callback.Package", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var challengedUris = new ConcurrentBag<Uri>();
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (uri, _) =>
                {
                    challengedUris.Add(uri);
                    return Task.FromResult<NuGetCredential?>(new NuGetCredential(UserName, Password));
                },
            };

            // Act
            var path = await NuGetCache.EnsureCachedAsync(
                "Callback.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None);

            // Assert - the package was installed and the callback was asked about the server
            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
            Assert.NotEmpty(challengedUris);
            Assert.All(challengedUris, uri => Assert.StartsWith(server.BaseUrl, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    ///     Tests that a callback supplying credentials lets <c>GetVersionsAsync</c> list versions from
    ///     a source that answers HTTP 401 until it sees the credentials.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_CallbackSuppliesCredentials_ReturnsVersions()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Callback.Versions", "2.0.0", NuGetPackageBuilder.CreateMinimalPackage("Callback.Versions", "2.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) => Task.FromResult<NuGetCredential?>(new NuGetCredential(UserName, Password)),
            };

            // Act
            var versions = await NuGetCache.GetVersionsAsync(
                "Callback.Versions", false, settings, options, new CredentialServiceRegistrar(), CancellationToken.None);

            // Assert
            Assert.Equal(["2.0.0"], versions);
        });
    }

    /// <summary>
    ///     Tests that a callback returning <see langword="null"/> falls through to the credential
    ///     providers, so an unauthenticated source still ends in a <see cref="NuGetAuthenticationException"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_CallbackReturnsNull_FallsThroughAndFailsWithAuthenticationException()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Null.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Null.Package", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var calls = 0;
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) =>
                {
                    Interlocked.Increment(ref calls);
                    return Task.FromResult<NuGetCredential?>(null);
                },
            };

            // Act
            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Null.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert - the callback was consulted and the failure identifies the source
            Assert.True(calls > 0);
            Assert.Equal("test-source", exception.Source);
        });
    }

    /// <summary>
    ///     Tests the loop guard: a callback whose credentials are rejected is not asked again for a
    ///     request it already answered, so the call fails instead of looping.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_CallbackCredentialsRejected_AsksOncePerRequestAndFails()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - the callback supplies a wrong password every time it is asked
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Rejected.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Rejected.Package", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var challengedUris = new ConcurrentBag<string>();
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (uri, _) =>
                {
                    challengedUris.Add(uri.AbsoluteUri);
                    return Task.FromResult<NuGetCredential?>(new NuGetCredential(UserName, "wrong-password"));
                },
            };

            // Act
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Rejected.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert - no URI was ever asked about twice
            Assert.NotEmpty(challengedUris);
            Assert.Equal(challengedUris.Count, challengedUris.Distinct().Count());
        });
    }

    /// <summary>
    ///     Tests that a callback that never completes is abandoned after
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> and the call is canceled with
    ///     <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - a callback that ignores its cancellation token and never finishes
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Timeout.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Timeout.Package", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var neverCompletes = new TaskCompletionSource<NuGetCredential?>();
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) => neverCompletes.Task,
                AuthenticationTimeout = TimeSpan.FromMilliseconds(500),
            };

            // Act
            var started = DateTime.UtcNow;
            var exception = await Record.ExceptionAsync(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Timeout.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert - it returned promptly, and the timeout (not an authentication failure) was reported
            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.IsNotType<NuGetAuthenticationException>(exception);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
        });
    }

    /// <summary>
    ///     Tests that a callback that never completes is abandoned after
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> and <c>GetVersionsAsync</c> is canceled
    ///     with <see cref="OperationCanceledException"/> rather than an authentication failure.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_CallbackNeverCompletes_TimesOutWithOperationCanceled()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Timeout.Versions", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Timeout.Versions", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var neverCompletes = new TaskCompletionSource<NuGetCredential?>();
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) => neverCompletes.Task,
                AuthenticationTimeout = TimeSpan.FromMilliseconds(500),
            };

            // Act
            var started = DateTime.UtcNow;
            var exception = await Record.ExceptionAsync(async () =>
                await NuGetCache.GetVersionsAsync(
                    "Timeout.Versions", false, settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert
            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.IsNotType<NuGetAuthenticationException>(exception);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
        });
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: after an authentication timeout the dispatcher records it on
    ///     the call's scope, refuses further exchanges without invoking the callback, and the scope
    ///     reports the timeout as <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_TimedOut_SubsequentRequestsCancelWithoutCallback()
    {
        // Arrange
        var calls = 0;
        var neverCompletes = new TaskCompletionSource<NuGetCredential?>();
        var callbackStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                callbackStarted.TrySetResult(true);
                return neverCompletes.Task;
            },
            AuthenticationTimeout = TimeSpan.FromMilliseconds(100),
        };
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act / Assert - nothing is recorded before the timeout
        scope.ThrowIfTimedOut();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));

        // Under CPU contention the timer can fire before the abandoned callback work item is scheduled,
        // so wait until the first request's callback has started before checking the repeat request
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // A repeat request is canceled immediately and does not invoke the callback again
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        Assert.Equal(1, Volatile.Read(ref calls));

        // The scope reports the timeout so the owning call can surface it
        Assert.ThrowsAny<OperationCanceledException>(scope.ThrowIfTimedOut);
    }

    /// <summary>
    ///     Tests that concurrent calls with differing options do not influence each other: a call
    ///     with a callback succeeds against its own server while a concurrent call without options
    ///     (no credentials) still fails with an authentication error against another.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_ConcurrentCalls_DifferentOptions_AreIsolated()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - two independent authenticated feeds
            using var folderA = new TempFolder();
            using var folderB = new TempFolder();
            await using var serverA = new NuGetTestServer();
            await using var serverB = new NuGetTestServer();
            serverA.RegisterV3PackageWithBasicAuth(
                "Concurrent.A", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Concurrent.A", "1.0.0"), UserName, Password);
            serverB.RegisterV3PackageWithBasicAuth(
                "Concurrent.B", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Concurrent.B", "1.0.0"), UserName, Password);
            var settingsA = serverA.CreateSettings(folderA.Path, serverA.IndexUrl);
            var settingsB = serverB.CreateSettings(folderB.Path, serverB.IndexUrl);
            var registrar = new CredentialServiceRegistrar();
            var optionsA = new NuGetCacheOptions
            {
                Interactive = true,
                CredentialCallback = async (_, _) =>
                {
                    await Task.Delay(100);
                    return new NuGetCredential(UserName, Password);
                },
            };

            // Act - run both at once; B uses default options (no callback, non-interactive)
            var callA = NuGetCache.EnsureCachedAsync(
                "Concurrent.A", "1.0.0", settingsA, optionsA, registrar, CancellationToken.None);
            var callB = NuGetCache.EnsureCachedAsync(
                "Concurrent.B", "1.0.0", settingsB, new NuGetCacheOptions(), registrar, CancellationToken.None);

            // Assert
            var pathA = await callA;
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () => await callB);
            Assert.True(File.Exists(Path.Combine(pathA, ".nupkg.metadata")));
        });
    }

    /// <summary>
    ///     Tests that the supplied logger receives NuGet messages and that no message contains the
    ///     credential secret.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_Logger_ReceivesMessagesWithoutSecret()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Logged.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Logged.Package", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var logger = new CapturingLogger();
            var options = new NuGetCacheOptions
            {
                Logger = logger,
                CredentialCallback = (_, _) => Task.FromResult<NuGetCredential?>(new NuGetCredential(UserName, Password)),
            };

            // Act
            await NuGetCache.EnsureCachedAsync(
                "Logged.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None);

            // Assert
            Assert.NotEmpty(logger.Messages);
            Assert.DoesNotContain(logger.Messages, m => m.Contains(Password, StringComparison.Ordinal));
        });
    }

    /// <summary>
    ///     Tests that the real registrar registers the dispatching credential service when none is
    ///     registered, and that a service registered earlier by a host is left in place.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public void CredentialServiceRegistrar_EnsureRegistered_RegistersDispatcherOnlyWhenNoServiceExists()
    {
        var original = HttpHandlerResourceV3.CredentialService;
        try
        {
            // Arrange / Act - nothing registered: the dispatcher is installed
            HttpHandlerResourceV3.CredentialService = null;
            new CredentialServiceRegistrar().EnsureRegistered();
            Assert.True(CredentialServiceRegistrar.IsDispatcherActive);

            // Arrange / Act - a host service is already present: it must not be replaced
            var hostOwned = new Lazy<global::NuGet.Configuration.ICredentialService>(() => throw new InvalidOperationException("host"));
            HttpHandlerResourceV3.CredentialService = hostOwned;
            new CredentialServiceRegistrar().EnsureRegistered();

            // Assert
            Assert.Same(hostOwned, HttpHandlerResourceV3.CredentialService);
            Assert.False(CredentialServiceRegistrar.IsDispatcherActive);
        }
        finally
        {
            HttpHandlerResourceV3.CredentialService = original;
        }
    }

    /// <summary>
    ///     Tests that a callback that blocks the calling thread synchronously (never returning its
    ///     task) is still bounded by <see cref="NuGetCacheOptions.AuthenticationTimeout"/>.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallbackBlocksSynchronously_TimesOutPromptly()
    {
        // Arrange
        using var release = new ManualResetEventSlim(false);
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return Task.FromResult<NuGetCredential?>(null);
            },
            AuthenticationTimeout = TimeSpan.FromMilliseconds(200),
        };
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        using var scope = CredentialDispatcher.BeginScope(options, null);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Act
            var call = dispatcher.GetCredentialsAsync(
                new Uri("http://localhost/feed"), null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);

            // Assert - canceled within a bounded time, long before the blocked callback would return
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
            Assert.ThrowsAny<OperationCanceledException>(scope.ThrowIfTimedOut);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    ///     Tests that a configured callback makes the dispatcher bypass the last-known-good credential
    ///     cache for non-proxy requests, that proxy requests and callback-less calls still use it, and
    ///     that a call with a logger is routed to a logger-aware service rather than the default one.
    /// </summary>
    [Fact]
    public void CredentialDispatcher_TryGetLastKnownGoodCredentialsFromCache_HonorsCallbackAndLoggerRouting()
    {
        var uri = new Uri("https://example.test/index.json");
        var dispatcher = new CredentialDispatcher(new CachedHitService());

        // No ambient scope: the default service answers
        Assert.True(dispatcher.TryGetLastKnownGoodCredentialsFromCache(uri, false, out _));

        // Options without a callback or logger: the default service answers
        using (CredentialDispatcher.BeginScope(new NuGetCacheOptions(), null))
        {
            Assert.True(dispatcher.TryGetLastKnownGoodCredentialsFromCache(uri, false, out _));
        }

        // A callback bypasses the cache for non-proxy requests only
        var withCallback = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) => Task.FromResult<NuGetCredential?>(null),
        };
        using (CredentialDispatcher.BeginScope(withCallback, null))
        {
            Assert.False(dispatcher.TryGetLastKnownGoodCredentialsFromCache(uri, false, out var credentials));
            Assert.Null(credentials);
            Assert.True(dispatcher.TryGetLastKnownGoodCredentialsFromCache(uri, true, out _));
        }

        // A logger selects the logger-aware service, which holds no cached credentials
        using (CredentialDispatcher.BeginScope(
            new NuGetCacheOptions { Logger = new CapturingLogger() },
            NuGetLoggerAdapter.Create(new CapturingLogger())))
        {
            Assert.False(dispatcher.TryGetLastKnownGoodCredentialsFromCache(uri, false, out _));
        }
    }

    /// <summary>
    ///     Tests that a call supplying only an authentication timeout (plus a logger) logs the
    ///     host-owned-credential-service warning, mentioning the timeout.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_HostOwnedServiceWithTimeoutOnly_LogsWarningMentioningTimeout()
    {
        var original = HttpHandlerResourceV3.CredentialService;
        try
        {
            // Arrange - a host application already registered its own credential service
            HttpHandlerResourceV3.CredentialService = new Lazy<global::NuGet.Configuration.ICredentialService>(() => new CachedHitService());
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersions("Host.Owned", "1.0.0");
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var logger = new CapturingLogger();
            var options = new NuGetCacheOptions { Logger = logger, AuthenticationTimeout = TimeSpan.FromSeconds(30) };

            // Act
            await NuGetCache.GetVersionsAsync(
                "Host.Owned", false, settings, options, new CredentialServiceRegistrar(() => { }), CancellationToken.None);

            // Assert
            Assert.Contains(logger.Messages, m => m.Contains("AuthenticationTimeout", StringComparison.Ordinal));
        }
        finally
        {
            HttpHandlerResourceV3.CredentialService = original;
        }
    }

    /// <summary>
    ///     Tests that a callback returning <see langword="null"/> is asked at most once per URL and
    ///     request type in a call, while the credential providers are still reached.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_CallbackReturnsNull_AsksOncePerUriAndType()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - every request is rejected and the callback never supplies anything
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var challenged = new ConcurrentBag<string>();
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (uri, _) =>
                {
                    challenged.Add(uri.AbsoluteUri);
                    return Task.FromResult<NuGetCredential?>(null);
                },
            };

            // Act
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Null.Once", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert - no URL was asked about twice
            Assert.NotEmpty(challenged);
            Assert.Equal(challenged.Count, challenged.Distinct().Count());
        });
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: a callback that returned <see langword="null"/> is not asked
    ///     again for the same URL and request type, but each request still reaches the credential
    ///     providers.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallbackReturnsNull_AsksOnceButStillReachesProviders()
    {
        // Arrange
        var calls = 0;
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<NuGetCredential?>(null);
            },
        };
        var provider = new FakeProvider(failOnRetry: false);
        using var providers = UseDefaultProvider(provider);
        var dispatcher = new CredentialDispatcher(new SuppliedCredentialService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act
        var first = await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);
        var second = await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);

        // Assert - the callback ran once; both requests reached the credential provider
        Assert.Equal(1, calls);
        Assert.Equal(2, provider.Calls);
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    /// <summary>
    ///     Deterministic, HTTP-free regression test: with a callback, the credential providers' retry
    ///     state is scoped to one call. A repeat request within a call is a retry (the provider, like
    ///     the integrated-authentication provider, then declines, so no loop), while a request in a later
    ///     call for the same URL is a fresh request and reaches the provider again.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallbackSet_RetryStateIsScopedToOneCall()
    {
        // Arrange
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) => Task.FromResult<NuGetCredential?>(null),
        };
        var provider = new FakeProvider(failOnRetry: true);
        using var providers = UseDefaultProvider(provider);
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        var type = global::NuGet.Configuration.CredentialRequestType.Unauthorized;

        // Act / Assert - call 1: the first request succeeds, the repeat request is a retry and is not satisfied
        using (CredentialDispatcher.BeginScope(options, null))
        {
            Assert.NotNull(await dispatcher.GetCredentialsAsync(uri, null, type, "m", CancellationToken.None));
            await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
                await dispatcher.GetCredentialsAsync(uri, null, type, "m", CancellationToken.None));
        }

        // Act / Assert - call 2: the same URL is a fresh request again (this failed with process-wide state)
        using (CredentialDispatcher.BeginScope(options, null))
        {
            Assert.NotNull(await dispatcher.GetCredentialsAsync(uri, null, type, "m", CancellationToken.None));
        }

        Assert.Equal(3, provider.Calls);
    }

    /// <summary>
    ///     Regression test for Windows integrated authentication: two sequential calls for the same
    ///     source, each with a callback that declines, must both be satisfied by a credential provider that
    ///     (like <c>DefaultNetworkCredentialsCredentialProvider</c>) answers a first request but declines a
    ///     retry. Before the fix the second call received a spurious 401 and failed authentication.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_CallbackDeclinesTwice_ProviderServesEveryCall()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - a feed that answers 401 until it sees the provider's Basic credentials
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Repeat.Versions", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Repeat.Versions", "1.0.0"), UserName, Password);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) => Task.FromResult<NuGetCredential?>(null),
            };
            using var providers = UseDefaultProvider(new FakeProvider(failOnRetry: true));

            // Act
            var first = await NuGetCache.GetVersionsAsync(
                "Repeat.Versions", false, settings, options, new CredentialServiceRegistrar(), CancellationToken.None);
            var second = await NuGetCache.GetVersionsAsync(
                "Repeat.Versions", false, settings, options, new CredentialServiceRegistrar(), CancellationToken.None);

            // Assert
            Assert.Equal(["1.0.0"], first);
            Assert.Equal(["1.0.0"], second);
        });
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: when neither the callback nor the credential providers supply a
    ///     credential for an HTTP challenge, the dispatcher fails the request with the fail-fast exception
    ///     (an <see cref="OperationCanceledException"/>), while a proxy challenge is not failed.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_NoCredentialAvailable_ThrowsUnsatisfiedChallenge()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json?sig=TopSecretToken");
        using var scope = CredentialDispatcher.BeginScope(null, null);

        // Act / Assert
        var exception = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(uri, exception.ChallengeUri);
        Assert.DoesNotContain("TopSecretToken", exception.Message, StringComparison.Ordinal);
        Assert.True(AuthFailureClassifier.IsUnsatisfiedChallenge(exception, uri.AbsoluteUri, CancellationToken.None));
        Assert.True(AuthFailureClassifier.TryDescribeAuthFailure(exception, "src", uri.AbsoluteUri, out var described));
        Assert.DoesNotContain("TopSecretToken", described, StringComparison.Ordinal);
        Assert.Null(await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Proxy, "m", CancellationToken.None));
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: a plain cancellation observed after the call recorded an unsatisfied
    ///     challenge is reported as an authentication failure only while the caller's token is not canceled
    ///     and only for a source on the challenged host, while the dispatcher's own exception is always reported.
    /// </summary>
    [Fact]
    public async Task AuthFailureClassifier_IsUnsatisfiedChallenge_CallerCancellation_IsNotAuthFailure()
    {
        // Arrange - record an unsatisfied challenge for the current call
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        var unsatisfied = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        var canceledToken = new CancellationToken(canceled: true);
        var plainCancellation = new TaskCanceledException();
        var source = uri.AbsoluteUri;

        // Act / Assert
        Assert.True(AuthFailureClassifier.IsUnsatisfiedChallenge(plainCancellation, source, CancellationToken.None));
        Assert.False(AuthFailureClassifier.IsUnsatisfiedChallenge(plainCancellation, source, canceledToken));
        Assert.True(AuthFailureClassifier.IsUnsatisfiedChallenge(unsatisfied, source, canceledToken));

        // A cancellation for a source on another host is not attributed to the recorded challenge
        Assert.False(AuthFailureClassifier.IsUnsatisfiedChallenge(
            plainCancellation, "http://other.example/feed/index.json", CancellationToken.None));
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: when the caller's token is already canceled, the dispatcher throws a
    ///     normal cancellation (not the fail-fast exception) and records no unsatisfied challenge.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallerCanceled_ThrowsPlainCancellation()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", cts.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.IsNotType<CredentialDispatcher.AuthChallengeUnsatisfiedException>(exception);
        Assert.Null(CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri));
    }

    /// <summary>
    ///     Tests that a source that rejects every request fails promptly with
    ///     <see cref="NuGetAuthenticationException"/> when no credentials are available, without NuGet's
    ///     retry delays and without the v2 fallback request.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_NoCredentials_FailsFastWithoutRetries()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "FastFail.Package", "1.0.0", settings, null, new CredentialServiceRegistrar(), CancellationToken.None));
            stopwatch.Stop();

            // Assert - without the fix this takes about 8 seconds and makes 16 requests
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), $"Took {stopwatch.Elapsed}.");
            Assert.True(server.RequestPaths.Count <= 6, $"Made {server.RequestPaths.Count} requests.");
            Assert.DoesNotContain(server.RequestPaths, p => p.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    ///     Tests that a 403 from a source - a signed-in user who lacks the permission, as Azure DevOps
    ///     answers - is reported as a <see cref="NuGetAuthenticationException"/> with status 403 and the
    ///     server's reason (from <c>X-TFS-ServiceError</c>, URL-decoded), promptly, even though a callback
    ///     and the providers had nothing to offer.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_Forbidden_ReportsStatusAndServerReasonWithoutRetries()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - the header carries the reason, URL-encoded
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterForbiddenForAllRequests(
                "VS403403%3A%20User%20%27alice%27%20lacks%20permission%20to%20complete%20this%20action.%20You%20need%20to%20have%20%27ReadPackages%27.",
                "{\"$id\":\"1\",\"message\":\"Body message that is not preferred.\"}");
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var options = new NuGetCacheOptions { CredentialCallback = (_, _) => Task.FromResult<NuGetCredential?>(null) };

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Forbidden.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));
            stopwatch.Stop();

            // Assert - the 403 and the reason survive; nothing was retried
            Assert.Equal(403, exception.StatusCode);
            Assert.Equal(
                "VS403403: User 'alice' lacks permission to complete this action. You need to have 'ReadPackages'.",
                exception.ServerReason);
            Assert.Contains("HTTP 403 (Forbidden)", exception.Message, StringComparison.Ordinal);
            Assert.Contains("ReadPackages", exception.Message, StringComparison.Ordinal);
            Assert.Equal("test-source", exception.Source);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), $"Took {stopwatch.Elapsed}.");
            Assert.True(server.RequestPaths.Count <= 6, $"Made {server.RequestPaths.Count} requests.");
        });
    }

    /// <summary>
    ///     Tests that version listing reports a 403 with the <c>message</c> of the JSON body when the server
    ///     sends no <c>X-TFS-ServiceError</c> header.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_ForbiddenWithJsonBody_ReportsMessageAsServerReason()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterForbiddenForAllRequests(null, "{\"$id\":\"1\",\"message\":\"You need \\\"ReadPackages\\\" on the feed.\",\"typeKey\":\"X\"}");
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);

            // Act
            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.GetVersionsAsync(
                    "Forbidden.Versions", false, settings, null, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert - quotes are replaced so the reason stays quotable
            Assert.Equal(403, exception.StatusCode);
            Assert.Equal("You need 'ReadPackages' on the feed.", exception.ServerReason);
        });
    }

    /// <summary>
    ///     Tests that a 403 without any explanation is still reported as a 403, with no reason.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_ForbiddenWithoutReason_ReportsStatusOnly()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterForbiddenForAllRequests(null, null);
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);

            // Act
            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.GetVersionsAsync(
                    "Forbidden.NoReason", false, settings, null, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert
            Assert.Equal(403, exception.StatusCode);
            Assert.Null(exception.ServerReason);
            Assert.Contains("HTTP 403 (Forbidden)", exception.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Tests that a 401 is unchanged: status 401, no server reason, and the unauthorized wording.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_Unauthorized_ReportsStatus401WithoutReason()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);

            // Act
            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Unauthorized.Package", "1.0.0", settings, null, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert
            Assert.Equal(401, exception.StatusCode);
            Assert.Null(exception.ServerReason);
            Assert.Contains("HTTP 401 (Unauthorized)", exception.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: a forbidden request that no credential can satisfy fails with the
    ///     403 status and the reason the capture handler recorded for the marked source, while an unsatisfied
    ///     401 keeps status 401 and carries no reason.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_ForbiddenNoCredential_FailsWithStatus403AndRecordedReason()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        using var source = CredentialDispatcher.BeginSource(uri.AbsoluteUri);
        CredentialDispatcher.RecordForbiddenReason(uri, "You need 'ReadPackages'.");

        // Act
        var forbidden = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));

        // Assert
        Assert.Equal(403, forbidden.Challenge.StatusCode);
        Assert.Equal("You need 'ReadPackages'.", forbidden.Challenge.Reason);
        Assert.True(AuthFailureClassifier.TryDescribeAuthFailure(forbidden, "src", uri.AbsoluteUri, out var message, out var failure));
        Assert.NotNull(failure);
        Assert.Equal(403, failure.StatusCode);
        Assert.Contains("Server said: \"You need 'ReadPackages'.\"", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that, within one call, a 403 with a reason followed by a bare 403 for the same source fails
    ///     without the stale reason, both for a marked source and for an unmarked (authority-keyed) request.
    /// </summary>
    /// <param name="marked">Whether the package source is marked for the call.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CredentialDispatcher_GetCredentialsAsync_ReasonedThenBareForbidden_DoesNotReportStaleReason(bool marked)
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        using var source = marked ? CredentialDispatcher.BeginSource(uri.AbsoluteUri) : null;
        CredentialDispatcher.RecordForbiddenReason(uri, "You need 'ReadPackages'.");
        CredentialDispatcher.RecordForbiddenReason(uri, null);

        // Act
        var forbidden = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));

        // Assert
        Assert.Equal(403, forbidden.Challenge.StatusCode);
        Assert.Null(forbidden.Challenge.Reason);
        Assert.Null(CredentialDispatcher.FindAmbientForbiddenReason(uri.AbsoluteUri));
        Assert.True(AuthFailureClassifier.TryDescribeAuthFailure(forbidden, "src", uri.AbsoluteUri, out var message, out var failure));
        Assert.NotNull(failure);
        Assert.Null(failure.Reason);
        Assert.DoesNotContain("ReadPackages", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Server said", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that, within one call, a 403 with a reason that failed (and was recorded as the unsatisfied
    ///     challenge) followed by a bare 403 for the same source replaces the recorded challenge, so the call's
    ///     final failure and its classification carry no stale reason.
    /// </summary>
    /// <param name="marked">Whether the package source is marked for the call.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CredentialDispatcher_GetCredentialsAsync_ReasonedForbiddenThenBareForbiddenChallenge_ReplacesRecordedChallenge(bool marked)
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        using var source = marked ? CredentialDispatcher.BeginSource(uri.AbsoluteUri) : null;

        // Act - a reasoned 403 fails and is recorded
        CredentialDispatcher.RecordForbiddenReason(uri, "You need 'ReadPackages'.");
        var first = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));
        Assert.Equal("You need 'ReadPackages'.", first.Challenge.Reason);
        Assert.Equal("You need 'ReadPackages'.", CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri)?.Reason);

        // Act - a later bare 403 for the same source fails and replaces it
        CredentialDispatcher.RecordForbiddenReason(uri, null);
        var second = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));

        // Assert
        Assert.Null(second.Challenge.Reason);
        var recorded = CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri);
        Assert.NotNull(recorded);
        Assert.Equal(403, recorded.StatusCode);
        Assert.Null(recorded.Reason);
        Assert.True(AuthFailureClassifier.TryDescribeRecordedChallenge("src", uri.AbsoluteUri, out var message, out var failure));
        Assert.Equal(403, failure.StatusCode);
        Assert.Null(failure.Reason);
        Assert.DoesNotContain("ReadPackages", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Server said", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that a later 401 for the same source does not replace a recorded 403 (the more specific
    ///     outcome), while a later 403 does replace a recorded 401.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_ChallengeStatusOrdering_KeepsForbiddenOverUnauthorized()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        using var source = CredentialDispatcher.BeginSource(uri.AbsoluteUri);

        // Act / Assert - a 401 followed by a 403: the 403 is recorded
        await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        Assert.Equal(401, CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri)?.StatusCode);
        CredentialDispatcher.RecordForbiddenReason(uri, "You need 'ReadPackages'.");
        await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));
        Assert.Equal(403, CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri)?.StatusCode);

        // Act / Assert - a later 401 does not displace the 403
        await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        var recorded = CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri);
        Assert.Equal(403, recorded?.StatusCode);
        Assert.Equal("You need 'ReadPackages'.", recorded?.Reason);
    }

    /// <summary>
    ///     Tests that <c>Install</c> constructs no provider itself: the stock HTTP handler provider is replaced
    ///     only when the installed provider is read, and every other provider is not created until read.
    /// </summary>
    [Fact]
    public void ForbiddenReasonHandlerProvider_Install_DoesNotConstructProvidersUntilRead()
    {
        // Arrange
        var httpCreated = 0;
        var otherCreated = 0;
        var other = new ServiceIndexResourceV3Provider();
        var providers = new List<Lazy<INuGetResourceProvider>>
        {
            new(() =>
            {
                httpCreated++;
                return new HttpHandlerResourceV3Provider();
            }),
            new(() =>
            {
                otherCreated++;
                return other;
            }),
        };

        // Act
        var installed = CredentialDispatcher.ForbiddenReasonHandlerProvider.Install(providers);

        // Assert - nothing was constructed by Install
        Assert.Equal(2, installed.Count());
        Assert.Equal(0, httpCreated);
        Assert.Equal(0, otherCreated);

        // Act / Assert - reading the HTTP handler entry replaces it with the reason-capturing provider
        Assert.IsType<CredentialDispatcher.ForbiddenReasonHandlerProvider>(installed.ElementAt(0).Value);
        Assert.Equal(1, httpCreated);
        Assert.Equal(0, otherCreated);

        // Act / Assert - reading another entry yields the original provider, constructed once
        Assert.Same(other, installed.ElementAt(1).Value);
        Assert.Same(other, installed.ElementAt(1).Value);
        Assert.Equal(1, otherCreated);
    }

    /// <summary>
    ///     Tests that a forbidden (403) challenge fails fast with the recorded reason and never invokes the
    ///     host credential callback, and that a 401 followed by a 403 for the same request invokes the
    ///     callback exactly once (for the 401).
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_ForbiddenWithCallback_DoesNotInvokeCallback()
    {
        // Arrange
        var calls = 0;
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<NuGetCredential?>(new NuGetCredential("user", "secret"));
            },
        };
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);
        using var source = CredentialDispatcher.BeginSource(uri.AbsoluteUri);
        CredentialDispatcher.RecordForbiddenReason(uri, "You need 'ReadPackages'.");

        // Act - a 403 alone
        var forbidden = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));

        // Assert
        Assert.Equal(0, calls);
        Assert.Equal(403, forbidden.Challenge.StatusCode);
        Assert.Equal("You need 'ReadPackages'.", forbidden.Challenge.Reason);

        // Act - a 401 answered by the callback, then a 403 for the same request
        var credentials = await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);
        await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await dispatcher.GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));

        // Assert - the callback ran once, for the 401
        Assert.NotNull(credentials);
        Assert.Equal(1, calls);
    }

    /// <summary>
    ///     Tests that a body larger than the read limit does not lose the reason: the handler falls back to
    ///     the reason phrase.
    /// </summary>
    [Fact]
    public async Task ForbiddenReasonHandler_OversizedBody_FallsBackToReasonPhrase()
    {
        // Arrange
        const string phrase = "Forbidden - You need 'ReadPackages'.";

        // Act
        var reason = await CredentialDispatcherReasonAsync(() => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
        {
            ReasonPhrase = phrase,
            Content = new StringContent("{\"message\":\"" + new string('x', 64 * 1024) + "\"}"),
        });

        // Assert
        Assert.Equal(phrase, reason);
    }

    /// <summary>
    ///     Deterministic, HTTP-free test of <c>ForbiddenReasonHandler</c>: the reason comes from the
    ///     <c>X-TFS-ServiceError</c> header first, then a JSON body, then a descriptive reason phrase; it is
    ///     reduced to one short line without URL secrets, and the response passes through unchanged.
    /// </summary>
    [Fact]
    public async Task ForbiddenReasonHandler_Forbidden_RecordsSanitizedReasonInPriorityOrder()
    {
        // Arrange - a reason phrase as an on-premises Azure DevOps Server sends it
        const string phrase = "Forbidden - User 'alice' lacks permission to complete this action. You need to have 'ReadPackages'.";

        // Act / Assert - the reason phrase alone (each case uses a fresh call scope)
        Assert.Equal(
            phrase,
            await CredentialDispatcherReasonAsync(() => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { ReasonPhrase = phrase }));

        // The standard phrase says nothing, so no reason is recorded
        Assert.Null(await CredentialDispatcherReasonAsync(() => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)));

        // A long, multi-line, URL-bearing header is reduced to one short line without the URL's secrets
        var header = Uri.EscapeDataString("Line one\r\nsee http://user:pw@host/p?sig=Secret123 " + new string('x', 400));
        var reason = await CredentialDispatcherReasonAsync(() =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { ReasonPhrase = "Forbidden" };
            response.Headers.TryAddWithoutValidation("X-TFS-ServiceError", header);
            return response;
        });
        Assert.NotNull(reason);
        Assert.DoesNotContain("Secret123", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pw", reason, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', reason);
        Assert.StartsWith("Line one see http://host/p", reason, StringComparison.Ordinal);
        Assert.True(reason.Length <= CredentialDispatcher.ForbiddenReasonHandler.MaxReasonLength);
    }

    private static async Task<string?> CredentialDispatcherReasonAsync(Func<HttpResponseMessage> respond)
    {
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);
        using var source = CredentialDispatcher.BeginSource(uri.AbsoluteUri);
        using var invoker = new HttpMessageInvoker(new CredentialDispatcher.ForbiddenReasonHandler(new StubHandler(respond)));
        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), CancellationToken.None);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        var failure = await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
            await new CredentialDispatcher(new CachedHitService()).GetCredentialsAsync(
                uri, null, global::NuGet.Configuration.CredentialRequestType.Forbidden, "m", CancellationToken.None));
        return failure.Challenge.Reason;
    }

    /// <summary>
    ///     A handler that answers every request with a response built by a factory.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond());
    }

    /// <summary>
    ///     Tests that a 401 from an explicit source whose URL carries a query-string secret never
    ///     puts that secret in the exception message or source name (NuGet defaults the source name
    ///     to the URL).
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_ExplicitSourceWithQueryString_AuthFailureRedactsSecret()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var options = new NuGetCacheOptions { Sources = [server.IndexUrl + "?sig=TopSecretToken"] };

            // Act - net481's HTTP stack does not surface the 401 text for this v2-style URL, so there the
            // failure is a plain InvalidOperationException; the secret must be absent either way
            var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Secret.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert
#if !NETFRAMEWORK
            Assert.IsType<NuGetAuthenticationException>(exception);
#endif
            Assert.DoesNotContain("TopSecretToken", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("TopSecretToken", exception.Source ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("TopSecretToken", exception.ToString(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Tests that a classifier-built message redacts a URL-valued source name.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_UrlSourceNameWithQuery_RedactsName()
    {
        // Arrange
        var url = "https://host/index.json?sig=TopSecretToken";
        var exception = new global::NuGet.Protocol.Core.Types.FatalProtocolException("Response status code does not indicate success: 401 (Unauthorized).");

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(exception, url, url, out var message);

        // Assert
        Assert.True(result);
        Assert.DoesNotContain("TopSecretToken", message, StringComparison.Ordinal);
        Assert.DoesNotContain("TopSecretToken", new AuthFailureInfo(url, url).SourceName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: concurrent challenges for the same URL and request type
    ///     await the single in-flight callback and share its credential instead of treating it as declined.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_ConcurrentSameKey_AwaitsSingleCallback()
    {
        // Arrange
        var calls = 0;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<NuGetCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult(true);
                return release.Task;
            },
        };
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act - the first request is in flight when the second arrives
        var first = dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);
        await started.Task;
        var second = dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted, "The concurrent request must wait for the running callback.");
        release.SetResult(new NuGetCredential("u", "p"));

        // Assert
        var firstCredentials = await first;
        var secondCredentials = await second;
        Assert.Equal(1, calls);
        Assert.NotNull(firstCredentials);
        Assert.NotNull(secondCredentials);
    }

    /// <summary>
    ///     Tests that <c>GetVersionsAsync</c> and <c>ListVersionsAsync</c> also fail promptly with
    ///     <see cref="NuGetAuthenticationException"/> when no credentials are available.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_VersionListing_NoCredentials_FailsFastWithoutRetries()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.GetVersionsAsync(
                    "FastFail.Versions", false, settings, null, new CredentialServiceRegistrar(), CancellationToken.None));
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.ListVersionsAsync(
                    "FastFail.Versions", false, settings, null, new CredentialServiceRegistrar(), CancellationToken.None));
            stopwatch.Stop();

            // Assert - each call took about 6 seconds before; both together now finish well under that
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Took {stopwatch.Elapsed}.");
            Assert.DoesNotContain(server.RequestPaths, p => p.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    ///     Tests that <see cref="NuGetCacheOptions.AuthenticationTimeout"/> surfaces promptly: a
    ///     callback that never completes costs about the timeout, is asked once, and the call fails with
    ///     <see cref="OperationCanceledException"/> rather than an authentication failure.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_AuthenticationTimeout_CallbackNeverCompletes_FailsPromptlyAfterTimeout()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);
            var calls = 0;
            var neverCompletes = new TaskCompletionSource<NuGetCredential?>();
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) =>
                {
                    Interlocked.Increment(ref calls);
                    return neverCompletes.Task;
                },
                AuthenticationTimeout = TimeSpan.FromMilliseconds(300),
            };

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var ensure = await Record.ExceptionAsync(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Prompt.Timeout", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));
            var ensureElapsed = stopwatch.Elapsed;
            var ensureCalls = Volatile.Read(ref calls);
            stopwatch.Restart();
            var list = await Record.ExceptionAsync(async () =>
                await NuGetCache.ListVersionsAsync(
                    "Prompt.Timeout", false, settings, options, new CredentialServiceRegistrar(), CancellationToken.None));
            var listElapsed = stopwatch.Elapsed;

            // Assert
            Assert.IsAssignableFrom<OperationCanceledException>(ensure);
            Assert.IsNotType<NuGetAuthenticationException>(ensure);
            Assert.IsAssignableFrom<OperationCanceledException>(list);
            Assert.IsNotType<NuGetAuthenticationException>(list);
            Assert.True(ensureElapsed < TimeSpan.FromSeconds(3), $"EnsureCached took {ensureElapsed}.");
            Assert.True(listElapsed < TimeSpan.FromSeconds(3), $"ListVersions took {listElapsed}.");
            Assert.Equal(1, ensureCalls);
        });
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: when the callback threw on the first challenge, a repeat challenge for the
    ///     same URL and request type rethrows that original exception instance (not wrapped, not an
    ///     authentication failure) without invoking the callback again.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallbackThrew_RepeatChallengeRethrowsOriginal()
    {
        // Arrange
        var calls = 0;
        var original = new InvalidOperationException("boom");
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                throw original;
            },
        };
        var dispatcher = new CredentialDispatcher(new SuppliedCredentialService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act
        var first = await Record.ExceptionAsync(async () => await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        var second = await Record.ExceptionAsync(async () => await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));

        // Assert - the very same exception both times, the callback ran once, nothing was recorded as unsatisfied
        Assert.Same(original, first);
        Assert.Same(original, second);
        Assert.Equal(1, calls);
        Assert.Null(CredentialDispatcher.FindAmbientUnsatisfiedChallenge(uri.AbsoluteUri));
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: a callback that threw <see cref="OperationCanceledException"/> makes a
    ///     repeat challenge fail with a plain cancellation, never with an authentication failure.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallbackCanceled_RepeatChallengeStaysPlainCancellation()
    {
        // Arrange
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) => throw new OperationCanceledException("callback canceled"),
        };
        var dispatcher = new CredentialDispatcher(new SuppliedCredentialService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act
        var first = await Record.ExceptionAsync(async () => await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        var second = await Record.ExceptionAsync(async () => await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));

        // Assert
        Assert.IsType<OperationCanceledException>(first);
        Assert.IsType<OperationCanceledException>(second);
        Assert.Equal("callback canceled", second.Message);
        Assert.False(AuthFailureClassifier.IsUnsatisfiedChallenge(second, uri.AbsoluteUri, CancellationToken.None));
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: a concurrent waiter that shares an in-flight callback which then faults
    ///     receives the original exception.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_ConcurrentWaiterCallbackFaults_WaiterGetsOriginalException()
    {
        // Arrange
        var original = new InvalidOperationException("boom");
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<NuGetCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) =>
            {
                started.TrySetResult(true);
                return release.Task;
            },
        };
        var dispatcher = new CredentialDispatcher(new SuppliedCredentialService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act
        var first = dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);
        await started.Task;
        var second = dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        release.SetException(original);

        // Assert
        Assert.Same(original, await Record.ExceptionAsync(async () => await first));
        Assert.Same(original, await Record.ExceptionAsync(async () => await second));
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: with an authentication timeout configured, a callback that throws is
    ///     reported once and the repeat challenge rethrows the same exception (the timeout was not hit).
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_CallbackThrewWithTimeout_RepeatChallengeRethrowsOriginal()
    {
        // Arrange
        var original = new InvalidOperationException("boom");
        var options = new NuGetCacheOptions
        {
            CredentialCallback = (_, _) => throw original,
            AuthenticationTimeout = TimeSpan.FromSeconds(30),
        };
        var dispatcher = new CredentialDispatcher(new SuppliedCredentialService());
        var uri = new Uri("http://localhost/feed/index.json");
        using var scope = CredentialDispatcher.BeginScope(options, null);

        // Act
        var first = await Record.ExceptionAsync(async () => await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        var second = await Record.ExceptionAsync(async () => await dispatcher.GetCredentialsAsync(
            uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));

        // Assert
        Assert.Same(original, first);
        Assert.Same(original, second);
        scope.ThrowIfTimedOut();
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: an unsatisfied challenge is attributed only to the source marked with
    ///     <c>BeginSource</c>, even when the challenged resource lives at another path of the same host, and a
    ///     different source on that host does not see it.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_ChallengeInMarkedSource_AttributedToThatSourceOnly()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        const string authed = "http://localhost:5000/authed/index.json";
        const string open = "http://localhost:5000/open/index.json";
        var challenged = new Uri("http://localhost:5000/flat/pkg/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);

        // Act
        using (CredentialDispatcher.BeginSource(authed))
        {
            await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
                await dispatcher.GetCredentialsAsync(
                    challenged, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        }

        // Assert
        Assert.Equal(challenged, CredentialDispatcher.FindAmbientUnsatisfiedChallenge(authed)?.Uri);
        Assert.Null(CredentialDispatcher.FindAmbientUnsatisfiedChallenge(open));
        Assert.False(AuthFailureClassifier.IsUnsatisfiedChallenge(new TaskCanceledException(), open, CancellationToken.None));
        Assert.True(AuthFailureClassifier.IsUnsatisfiedChallenge(new TaskCanceledException(), authed, CancellationToken.None));
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: two sources that differ only by query string (for example a SAS token)
    ///     keep distinct correlation keys, so a challenge raised for one is never attributed to the other.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_SourcesDifferingOnlyByQuery_AttributedSeparately()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        const string authed = "http://localhost:5000/feed/index.json?sig=SecretOne";
        const string open = "http://localhost:5000/feed/index.json?sig=SecretTwo";
        var challenged = new Uri("http://localhost:5000/flat/pkg/index.json");
        using var scope = CredentialDispatcher.BeginScope(null, null);

        // Act
        using (CredentialDispatcher.BeginSource(authed))
        {
            await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
                await dispatcher.GetCredentialsAsync(
                    challenged, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
        }

        // Assert
        Assert.Equal(challenged, CredentialDispatcher.FindAmbientUnsatisfiedChallenge(authed)?.Uri);
        Assert.Null(CredentialDispatcher.FindAmbientUnsatisfiedChallenge(open));
        Assert.NotEqual(CredentialDispatcher.SourceKey(authed), CredentialDispatcher.SourceKey(open));
        Assert.DoesNotContain("Secret", CredentialDispatcher.SourceKey(authed), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Deterministic, HTTP-free test: an unmarked challenge is still attributed by host and port,
    ///     and the marked-source tag reaches the dispatcher through the authentication-timeout task hop.
    /// </summary>
    [Fact]
    public async Task CredentialDispatcher_GetCredentialsAsync_UnmarkedAndTimeoutHop_AttributionBehaves()
    {
        // Arrange
        var dispatcher = new CredentialDispatcher(new CachedHitService());
        var uri = new Uri("http://localhost:5000/feed/index.json");

        // Act / Assert - no marked source: attributed by authority
        using (var scope = CredentialDispatcher.BeginScope(null, null))
        {
            await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
                await dispatcher.GetCredentialsAsync(
                    uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
            Assert.NotNull(CredentialDispatcher.FindAmbientUnsatisfiedChallenge("http://localhost:5000/other/index.json"));
        }

        // Act / Assert - marked source with a timeout configured (the work runs through Task.Run)
        using (var scope = CredentialDispatcher.BeginScope(
            new NuGetCacheOptions { AuthenticationTimeout = TimeSpan.FromSeconds(30) }, null))
        using (CredentialDispatcher.BeginSource("http://localhost:5000/authed/index.json"))
        {
            await Assert.ThrowsAsync<CredentialDispatcher.AuthChallengeUnsatisfiedException>(async () =>
                await dispatcher.GetCredentialsAsync(
                    uri, null, global::NuGet.Configuration.CredentialRequestType.Unauthorized, "m", CancellationToken.None));
            Assert.NotNull(CredentialDispatcher.FindAmbientUnsatisfiedChallenge("http://localhost:5000/authed/index.json"));
            Assert.Null(CredentialDispatcher.FindAmbientUnsatisfiedChallenge("http://localhost:5000/open/index.json"));
        }
    }

    /// <summary>
    ///     Tests that with two sources on the same host where only one needs authentication, a package missing
    ///     from the public source is reported as an authentication failure of the protected source only, in
    ///     either source order, and a package present in the public source is still downloaded.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_TwoSourcesSameHostOneAuthed_FailureNamesOnlyAuthedSource()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange - the public source does not carry the requested package
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterAuthedAndOpenSourcesOnSameHost("Other.Package", "1.0.0", []);

            // Act / Assert - authed source first, then public; and the reverse order
            foreach (var (first, second) in new[]
                     {
                         (server.AuthedIndexUrl, server.OpenIndexUrl),
                         (server.OpenIndexUrl, server.AuthedIndexUrl),
                     })
            {
                var settings = server.CreateSettings(folder.Path, first, second);
                var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                    await NuGetCache.EnsureCachedAsync(
                        "Missing.Package", "1.0.0", settings, null, new CredentialServiceRegistrar(), CancellationToken.None));

                var authedName = first == server.AuthedIndexUrl ? "test-source-primary" : "test-source-secondary";
                var openName = first == server.AuthedIndexUrl ? "test-source-secondary" : "test-source-primary";
                Assert.Equal(authedName, exception.Source);
                Assert.Equal(server.AuthedIndexUrl, exception.Uri?.AbsoluteUri);
                Assert.DoesNotContain(openName, exception.Message, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>
    ///     Tests that two sources on the same host where only one needs authentication still yield a package
    ///     held by the public source without any credentials.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_TwoSourcesSameHostOneAuthed_DownloadsFromOpenSource()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterAuthedAndOpenSourcesOnSameHost(
                "Open.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Open.Package", "1.0.0"));
            var settings = server.CreateSettings(folder.Path, server.AuthedIndexUrl, server.OpenIndexUrl);

            // Act
            var path = await NuGetCache.EnsureCachedAsync(
                "Open.Package", "1.0.0", settings, null, new CredentialServiceRegistrar(), CancellationToken.None);

            // Assert
            Assert.True(Directory.Exists(path));
        });
    }

    /// <summary>
    ///     Tests that version discovery over two sources on the same host, one needing authentication and
    ///     the public one lacking the package, reports the authentication failure for the protected source only.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_TwoSourcesSameHostOneAuthed_FailureNamesOnlyAuthedSource()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterAuthedAndOpenSourcesOnSameHost("Other.Package", "1.0.0", []);
            var settings = server.CreateSettings(folder.Path, server.AuthedIndexUrl, server.OpenIndexUrl);

            // Act
            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.GetVersionsAsync(
                    "Missing.Package", false, settings, null, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert
            Assert.Equal("test-source-primary", exception.Source);
            Assert.DoesNotContain("test-source-secondary", exception.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Tests that <see cref="NuGetAuthenticationException.Uri"/> never carries user info, query or fragment
    ///     of an explicit source, for both package retrieval and version discovery.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_AuthFailure_ExplicitSourceWithSecrets_ExceptionUriIsRedacted()
    {
        await WithDispatcherAsync(async () =>
        {
            // Arrange
            using var folder = new TempFolder();
            await using var server = new NuGetTestServer();
            server.RegisterUnauthorizedForAllRequests();
            var settings = server.CreateSettings(folder.Path, server.IndexUrl);

            // Act / Assert
            foreach (var secretSource in new[]
                     {
                         server.IndexUrl + "?sig=TopSecretToken#frag",
                         server.IndexUrl.Replace("http://", "http://user:TopSecretToken@"),
                     })
            {
                var options = new NuGetCacheOptions { Sources = [secretSource] };
                var ensure = await Record.ExceptionAsync(async () =>
                    await NuGetCache.EnsureCachedAsync(
                        "Secret.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));
                var versions = await Record.ExceptionAsync(async () =>
                    await NuGetCache.GetVersionsAsync(
                        "Secret.Package", false, settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

                foreach (var exception in new[] { ensure, versions })
                {
                    Assert.NotNull(exception);
                    Assert.DoesNotContain("TopSecretToken", exception.ToString(), StringComparison.Ordinal);
                    if (exception is NuGetAuthenticationException auth && auth.Uri != null)
                    {
                        Assert.Equal(string.Empty, auth.Uri.Query);
                        Assert.Equal(string.Empty, auth.Uri.UserInfo);
                        Assert.Equal(string.Empty, auth.Uri.Fragment);
                    }
                }

#if !NETFRAMEWORK
                Assert.IsType<NuGetAuthenticationException>(ensure);
                Assert.IsType<NuGetAuthenticationException>(versions);
#endif
            }
        });
    }

    /// <summary>
    ///     Substitutes the default-network-credentials provider appended to every provider-based
    ///     credential service, restoring the real one when the returned scope is disposed.
    /// </summary>
    /// <param name="provider">The provider to use.</param>
    /// <returns>The scope that restores the original factory.</returns>
    private static RestoreDefaultProvider UseDefaultProvider(global::NuGet.Credentials.ICredentialProvider provider)
    {
        var restore = new RestoreDefaultProvider(CredentialDispatcher.DefaultProviderFactory);
        CredentialDispatcher.DefaultProviderFactory = () => provider;
        return restore;
    }

    /// <summary>
    ///     Restores the original default-provider factory on dispose.
    /// </summary>
    private sealed class RestoreDefaultProvider : IDisposable
    {
        private readonly Func<global::NuGet.Credentials.ICredentialProvider> _original;

        public RestoreDefaultProvider(Func<global::NuGet.Credentials.ICredentialProvider> original) => _original = original;

        public void Dispose() => CredentialDispatcher.DefaultProviderFactory = _original;
    }

    /// <summary>
    ///     A credential provider that supplies the test credentials and counts its requests. When
    ///     <c>failOnRetry</c> is set it behaves like <c>DefaultNetworkCredentialsCredentialProvider</c>,
    ///     answering <c>ProviderNotApplicable</c> whenever NuGet reports the request as a retry.
    /// </summary>
    private sealed class FakeProvider : global::NuGet.Credentials.ICredentialProvider
    {
        private readonly bool _failOnRetry;
        private int _calls;

        public FakeProvider(bool failOnRetry) => _failOnRetry = failOnRetry;

        /// <summary>Gets the number of credential requests received.</summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <inheritdoc />
        public string Id => "FakeProvider";

        /// <inheritdoc />
        public Task<global::NuGet.Credentials.CredentialResponse> GetAsync(
            Uri uri,
            System.Net.IWebProxy? proxy,
            global::NuGet.Configuration.CredentialRequestType type,
            string message,
            bool isRetry,
            bool nonInteractive,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_failOnRetry && isRetry)
            {
                return Task.FromResult(new global::NuGet.Credentials.CredentialResponse(
                    global::NuGet.Credentials.CredentialStatus.ProviderNotApplicable));
            }

            return Task.FromResult(new global::NuGet.Credentials.CredentialResponse(
                new System.Net.NetworkCredential(UserName, Password)));
        }
    }

    /// <summary>
    ///     A credential service that always supplies a credential.
    /// </summary>
    private sealed class SuppliedCredentialService : global::NuGet.Configuration.ICredentialService
    {
        /// <inheritdoc />
        public bool HandlesDefaultCredentials => true;

        /// <inheritdoc />
        public Task<System.Net.ICredentials?> GetCredentialsAsync(
            Uri uri,
            System.Net.IWebProxy? proxy,
            global::NuGet.Configuration.CredentialRequestType type,
            string message,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<System.Net.ICredentials?>(new System.Net.NetworkCredential("u", "p"));
        }

        /// <inheritdoc />
        public bool TryGetLastKnownGoodCredentialsFromCache(Uri uri, bool isProxy, out System.Net.ICredentials? credentials)
        {
            credentials = null;
            return false;
        }
    }

    /// <summary>
    ///     A credential service whose last-known-good cache always reports a hit.
    /// </summary>
    private sealed class CachedHitService : global::NuGet.Configuration.ICredentialService
    {
        /// <inheritdoc />
        public bool HandlesDefaultCredentials => true;

        /// <inheritdoc />
        public Task<System.Net.ICredentials?> GetCredentialsAsync(
            Uri uri,
            System.Net.IWebProxy? proxy,
            global::NuGet.Configuration.CredentialRequestType type,
            string message,
            CancellationToken cancellationToken) => Task.FromResult<System.Net.ICredentials?>(null);

        /// <inheritdoc />
        public bool TryGetLastKnownGoodCredentialsFromCache(Uri uri, bool isProxy, out System.Net.ICredentials? credentials)
        {
            credentials = new System.Net.NetworkCredential("cached", "cached");
            return true;
        }
    }

    /// <summary>
    ///     Runs <paramref name="test"/> with a freshly registered dispatching credential service,
    ///     restoring the process-wide credential service afterward.
    /// </summary>
    /// <param name="test">The test body.</param>
    private static async Task WithDispatcherAsync(Func<Task> test)
    {
        var original = HttpHandlerResourceV3.CredentialService;
        try
        {
            HttpHandlerResourceV3.CredentialService = null;
            new CredentialServiceRegistrar().EnsureRegistered();
            await test();
        }
        finally
        {
            HttpHandlerResourceV3.CredentialService = original;
        }
    }

    /// <summary>
    ///     A temporary directory used as a global packages folder, deleted on dispose.
    /// </summary>
    private sealed class TempFolder : IDisposable
    {
        /// <summary>
        ///     Initializes a new instance and creates the directory.
        /// </summary>
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>
        ///     Gets the directory path.
        /// </summary>
        public string Path { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    /// <summary>
    ///     A logger that records every formatted message.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        /// <summary>
        ///     Gets the messages recorded so far.
        /// </summary>
        public IReadOnlyCollection<string> Messages => _messages;

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => _messages.Enqueue(formatter(state, exception));
    }
}
