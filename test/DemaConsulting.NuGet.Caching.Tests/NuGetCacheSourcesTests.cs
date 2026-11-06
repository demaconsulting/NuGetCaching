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

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Tests for <see cref="NuGetCacheOptions.Sources"/>: validation, resolution of names, URLs and
///     paths, and bypassing configured sources and source mapping.
/// </summary>
public class NuGetCacheSourcesTests
{
    /// <summary>
    ///     Tests that null, empty, white-space and malformed entries are rejected by options validation.
    /// </summary>
    /// <param name="entry">The invalid entry.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.test/feed")]
    [InlineData("http://")]
    public void NuGetCacheOptions_Validate_InvalidSourceEntry_ThrowsArgumentException(string? entry)
    {
        var options = new NuGetCacheOptions { Sources = [entry!] };

        var exception = Assert.Throws<ArgumentException>(options.Validate);

        Assert.Equal(nameof(NuGetCacheOptions.Sources), exception.ParamName);
    }

    /// <summary>
    ///     Tests that names, URLs, URIs and rooted paths are accepted by options validation.
    /// </summary>
    [Fact]
    public void NuGetCacheOptions_Validate_ValidSources_DoNotThrow()
    {
        var rooted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "feed"));

        new NuGetCacheOptions { Sources = ["my-source", "https://example.test/index.json", rooted, new Uri(rooted).AbsoluteUri] }.Validate();
        new NuGetCacheOptions { Sources = [] }.Validate();
    }

    /// <summary>
    ///     Tests that an entry that is not a configured name, URL or rooted path is rejected by a call
    ///     before any network access.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_Sources_UnknownRelativeEntry_ThrowsArgumentException()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersions("Some.Package", "1.0.0");
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var options = new NuGetCacheOptions { Sources = ["not-a-configured-name"] };

            var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await NuGetCache.GetVersionsAsync(
                    "Some.Package", false, settings, options, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Equal(nameof(NuGetCacheOptions.Sources), exception.ParamName);
            Assert.Empty(server.LogEntries);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that explicit source URLs replace the configured sources for both operations.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_Sources_ExplicitUrl_ReplacesConfiguredSources()
    {
        var folder = CreateTempFolder();
        try
        {
            // The configured source knows nothing; only the explicit source has the package
            await using var configured = new NuGetTestServer();
            configured.RegisterV3PackageVersions("Explicit.Package");
            await using var explicitServer = new NuGetTestServer();
            explicitServer.RegisterV3Package("Explicit.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Explicit.Package", "1.0.0"));
            var settings = configured.CreateSettings(folder, configured.IndexUrl);
            var options = new NuGetCacheOptions { Sources = [explicitServer.IndexUrl] };
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var versions = await NuGetCache.GetVersionsAsync("Explicit.Package", false, settings, options, registrar, CancellationToken.None);
            var path = await NuGetCache.EnsureCachedAsync("Explicit.Package", "1.0.0", settings, options, registrar, CancellationToken.None);

            Assert.Equal(["1.0.0"], versions);
            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));

            // The configured source was never contacted
            Assert.Empty(configured.LogEntries);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a configured source name resolves to that source.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_Sources_ConfiguredName_ResolvesToConfiguredSource()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var primary = new NuGetTestServer();
            primary.RegisterV3PackageVersions("Named.Package", "1.0.0");
            await using var secondary = new NuGetTestServer();
            secondary.RegisterV3PackageVersions("Named.Package", "2.0.0");
            var settings = primary.CreateSettings(folder, primary.IndexUrl, secondary.IndexUrl);
            var options = new NuGetCacheOptions { Sources = ["test-source-secondary"] };

            var versions = await NuGetCache.GetVersionsAsync(
                "Named.Package", false, settings, options, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.Equal(["2.0.0"], versions);
            Assert.Empty(primary.LogEntries);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that explicit sources bypass package source mapping, which would otherwise restrict the
    ///     package to a different source.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_Sources_ExplicitSource_BypassesSourceMapping()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var primary = new NuGetTestServer();
            primary.RegisterV3PackageVersions("Mapped.Package", "1.0.0");
            await using var secondary = new NuGetTestServer();
            secondary.RegisterV3PackageVersions("Mapped.Package", "2.0.0");

            // Mapping allows the package only from the secondary source
            var settings = primary.CreateSettingsWithSourceMapping(
                folder, primary.IndexUrl, secondary.IndexUrl, "Mapped.Package");
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var mapped = await NuGetCache.GetVersionsAsync("Mapped.Package", false, settings, null, registrar, CancellationToken.None);
            var explicitVersions = await NuGetCache.GetVersionsAsync(
                "Mapped.Package", false, settings, new NuGetCacheOptions { Sources = [primary.IndexUrl] }, registrar, CancellationToken.None);

            Assert.Equal(["2.0.0"], mapped);
            Assert.Equal(["1.0.0"], explicitVersions);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that duplicate entries are collapsed, so a source is queried once.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_Sources_DuplicateEntries_QueryTheSourceOnce()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersions("Dup.Package", "1.0.0");
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var duplicated = new NuGetCacheOptions { Sources = [server.IndexUrl, "test-source", server.IndexUrl] };

            await NuGetCache.GetVersionsAsync(
                "Dup.Package", false, settings, duplicated, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            // The version list endpoint was requested once, not once per duplicate entry
            var versionListRequests = server.LogEntries
                .Cast<global::WireMock.Logging.ILogEntry>()
                .Count(e => e.RequestMessage!.Path.Contains("dup.package", StringComparison.Ordinal));
            Assert.Equal(1, versionListRequests);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that an HTTP(S) source URL ending in <c>.json</c> with a query string logs a warning,
    ///     because NuGet treats it as a V2 feed, while a plain <c>index.json</c> URL does not.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_Sources_QueryStringJsonUrl_LogsV2Warning()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersions("Query.Package", "1.0.0");
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            // A query string makes NuGet treat the source as V2; the outcome of the call is irrelevant
            var withQuery = new MessageLogger();
            await Record.ExceptionAsync(async () => await NuGetCache.GetVersionsAsync(
                "Query.Package",
                false,
                settings,
                new NuGetCacheOptions { Sources = [server.IndexUrl + "?sig=TopSecretToken"], Logger = withQuery },
                registrar,
                CancellationToken.None));
            Assert.Contains(withQuery.Warnings, m => m.Contains("query string", StringComparison.Ordinal));

            // The query string (which may carry a secret) must never be logged
            Assert.DoesNotContain(withQuery.Warnings, m => m.Contains("TopSecretToken", StringComparison.Ordinal));

            // A plain service index URL is not warned about
            var plain = new MessageLogger();
            await NuGetCache.GetVersionsAsync(
                "Query.Package",
                false,
                settings,
                new NuGetCacheOptions { Sources = [server.IndexUrl], Logger = plain },
                registrar,
                CancellationToken.None);
            Assert.Empty(plain.Warnings);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Creates an empty unique temporary folder.
    /// </summary>
    /// <returns>The folder path.</returns>
    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    ///     A logger that records the formatted messages written at warning level or above.
    /// </summary>
    private sealed class MessageLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _warnings = new();

        /// <summary>
        ///     Gets the warning messages recorded so far.
        /// </summary>
        public IReadOnlyCollection<string> Warnings => _warnings;

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                _warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
