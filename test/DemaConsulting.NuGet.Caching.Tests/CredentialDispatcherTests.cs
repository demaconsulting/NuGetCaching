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
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol;

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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Timeout.Package", "1.0.0", settings, options, new CredentialServiceRegistrar(), CancellationToken.None));

            // Assert - it returned promptly rather than hanging on the callback
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
        });
    }

    /// <summary>
    ///     Tests that concurrent calls with differing options do not influence each other: a call
    ///     with a callback succeeds against its own server while a concurrent call without options
    ///     (the 1.5.0 behavior) still fails with an authentication error against another.
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
    ///     Tests that a configured callback makes the dispatcher bypass the last-known-good credential
    ///     cache for non-proxy requests, that proxy requests and callback-less calls still use it, and
    ///     that a call with a logger is routed to a logger-aware service rather than the default one.
    /// </summary>
    [Fact]
    public void CredentialDispatcher_TryGetLastKnownGoodCredentialsFromCache_HonorsCallbackAndLoggerRouting()
    {
        var uri = new Uri("https://example.test/index.json");
        var dispatcher = new CredentialDispatcher(new CachedHitService());

        // No ambient scope: the default service answers (1.5.0 behavior)
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
