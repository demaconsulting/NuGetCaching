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
///     System-level integration tests for the DemaConsulting NuGet Caching library.
/// </summary>
public class NuGetCachingTests
{
    /// <summary>
    ///     Tests that the library provides programmatic NuGet package caching by ensuring a known
    ///     package is available in the local global packages cache after calling the library.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Sys-PackageCaching: the system provides a .NET library for
    ///     programmatic NuGet package caching.
    /// </remarks>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NuGetCaching_EnsureCachedAsync_WhenKnownPackageAndVersionProvided_ReturnsPackageFolder()
    {
        // Arrange: use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act: invoke the library's package caching capability
        var packageFolder = await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None);

        // Assert: the library returned a valid path to the cached package on disk
        Assert.NotNull(packageFolder);
        Assert.True(
            Directory.Exists(packageFolder),
            $"Expected package folder to exist at: {packageFolder}");

        // Assert: a fully installed package should include the NuGet installation sentinel file
        var metadataFilePath = Path.Combine(packageFolder, ".nupkg.metadata");

        Assert.True(
            File.Exists(metadataFilePath),
            $"Expected package folder to contain .nupkg.metadata at: {metadataFilePath}");

        // Assert the path ends with the normalized package ID and version (NuGet convention)
        var expectedSuffix = Path.Combine(packageId.ToLowerInvariant(), version.ToLowerInvariant());
        Assert.EndsWith(expectedSuffix, packageFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Tests that the library throws <see cref="InvalidOperationException"/> when the requested
    ///     package cannot be found in any configured NuGet source.
    /// </summary>
    /// <remarks>
    ///     This test proves the error-path behavior of Caching-Sys-PackageCaching: the system
    ///     correctly signals when a package version is unavailable across all configured sources.
    /// </remarks>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NuGetCaching_EnsureCachedAsync_WhenPackageNotFound_ThrowsInvalidOperationException()
    {
        // Arrange: use a package ID and version combination that does not exist on any source
        const string packageId = "DemaConsulting.NonExistentPackage.DoesNotExist";
        const string version = "99.99.99";

        // Act & Assert: the library should throw when the package cannot be found
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None));

        // Assert: the exception message must identify the package ID and version
        Assert.Contains(packageId, ex.Message);
        Assert.Contains(version, ex.Message);
    }

    /// <summary>
    ///     Tests that the library throws <see cref="ArgumentNullException"/> when
    ///     <c>packageId</c> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Sys-NullValidation: the system rejects null arguments
    ///     with a clear, actionable error.
    /// </remarks>
    [Fact]
    public async Task NuGetCaching_EnsureCachedAsync_WhenPackageIdIsNull_ThrowsArgumentNullException()
    {
        // Arrange: null packageId is an invalid argument
        const string version = "1.5.0";

        // Act & Assert: the library should throw ArgumentNullException for a null package ID
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(null!, version, cancellationToken: CancellationToken.None));
    }

    /// <summary>
    ///     Tests that the library throws <see cref="ArgumentNullException"/> when
    ///     <c>version</c> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Sys-NullValidation: the system rejects null arguments
    ///     with a clear, actionable error.
    /// </remarks>
    [Fact]
    public async Task NuGetCaching_EnsureCachedAsync_WhenVersionIsNull_ThrowsArgumentNullException()
    {
        // Arrange: null version is an invalid argument
        const string packageId = "DemaConsulting.TestResults";

        // Act & Assert: the library should throw ArgumentNullException for a null version
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, null!, cancellationToken: CancellationToken.None));
    }

    /// <summary>
    ///     Tests that the library discovers available package versions through its public API,
    ///     using a repo-local <c>nuget.config</c> discovered from the supplied root.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Sys-VersionDiscovery: the system lists the available versions
    ///     of a package as normalized, ascending strings.
    /// </remarks>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCaching_GetVersionsAsync_WhenPackageHasVersions_ReturnsAscendingVersionStrings()
    {
        // Arrange: a local HTTP test feed advertising unsorted versions, and a repo-local nuget.config using only it
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersions("TestPackage.System", "2.0.0", "1.0.0", "1.5.0-beta");
            await File.WriteAllTextAsync(Path.Combine(root, "nuget.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{server.IndexUrl}" />
                  </packageSources>
                </configuration>
                """, TestContext.Current.CancellationToken);

            // Act: invoke the public API
            var versions = await NuGetCache.GetVersionsAsync(
                "TestPackage.System", includePrerelease: true, root: root, cancellationToken: CancellationToken.None);

            // Assert: normalized strings in ascending order
            Assert.Equal(new[] { "1.0.0", "1.5.0-beta", "2.0.0" }, versions);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that the library caches a package from a source that challenges with HTTP 401 when
    ///     the caller supplies per-call options with a credential callback, through the public
    ///     options overload of <c>EnsureCachedAsync</c>.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Sys-CacheOptions: callers can supply per-call options (here a
    ///     credential callback and a logger) and the call succeeds against a private feed.
    /// </remarks>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCaching_EnsureCachedAsync_WhenOptionsSupplyCredentialCallback_CachesFromAuthenticatedSource()
    {
        // Arrange: a feed requiring Basic credentials and a repo-local nuget.config using only it
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var packagesFolder = Path.Combine(root, "packages");
        try
        {
            Directory.CreateDirectory(root);
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "TestPackage.SystemOptions",
                "1.0.0",
                NuGetPackageBuilder.CreateMinimalPackage("TestPackage.SystemOptions", "1.0.0"),
                "test-user",
                "test-password");
            await File.WriteAllTextAsync(Path.Combine(root, "nuget.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{packagesFolder}" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="local" value="{server.IndexUrl}" />
                  </packageSources>
                </configuration>
                """, TestContext.Current.CancellationToken);
            var callbackInvoked = false;
            var options = new NuGetCacheOptions
            {
                CredentialCallback = (_, _) =>
                {
                    callbackInvoked = true;
                    return Task.FromResult<NuGetCredential?>(new NuGetCredential("test-user", "test-password"));
                },
            };

            // Act: invoke the public options overload
            var path = await NuGetCache.EnsureCachedAsync(
                "TestPackage.SystemOptions", "1.0.0", root, options, CancellationToken.None);

            // Assert: the package was cached and the callback supplied the credentials
            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
            Assert.True(callbackInvoked);
        }
        finally
        {
            // The public overload registers the dispatching credential service once per process
            // (memoized), exactly as in production; it is intentionally left in place because
            // restoring a null service would make the memoized registration unrecoverable for
            // later tests that rely on the default registrar.
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
