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
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Local-integration tests for the <see cref="PackageDownloader"/> class, using a local
///     <see cref="NuGetTestServer"/> (WireMock) to simulate NuGet v3 feed download scenarios without
///     making real network calls.
/// </summary>
/// <remarks>
///     These tests resolve a real <c>FindPackageByIdResource</c> against a WireMock v3 feed, then
///     call <see cref="PackageDownloader.TryDownloadAsync"/> directly, focusing on behaviors owned
///     specifically by this unit (download outcome classification and the on-disk package-path
///     convention) rather than the full source-enumeration flow, which is covered by
///     <c>NuGetCacheServerTests</c>.
/// </remarks>
public class PackageDownloaderTests
{
    /// <summary>
    ///     Tests that <c>TryDownloadAsync</c> downloads and installs a package that is available from
    ///     the resolved resource, returning the conventional on-disk package path.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageDownloader_TryDownloadAsync_PackageAvailable_ReturnsInstalledPackagePath()
    {
        // Arrange
        var globalPackagesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(globalPackagesFolder);
            const string packageId = "TestPackage.Downloader.Available";
            const string version = "1.0.0";

            await using var server = new NuGetTestServer();
            var nupkgBytes = NuGetPackageBuilder.CreateMinimalPackage(packageId, version);
            server.RegisterV3Package(packageId, version, nupkgBytes);
            var settings = server.CreateSettings(globalPackagesFolder, server.IndexUrl);

            var (sourceRepository, resource) = await ResolveResourceAsync(server.IndexUrl, "test-source");
            var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, NullLogger.Instance);
            using var cacheContext = new SourceCacheContext();

            // Act
            var result = await PackageDownloader.TryDownloadAsync(
                sourceRepository,
                resource,
                packageId,
                NuGetVersion.Parse(version),
                globalPackagesFolder,
                clientPolicyContext,
                cacheContext,
                CancellationToken.None);

            // Assert - the returned path matches the GetPackagePath convention and points to a real,
            // fully installed package directory
            var expectedPath = PackageDownloader.GetPackagePath(globalPackagesFolder, packageId, version);
            Assert.Equal(expectedPath, result.PackagePath);
            Assert.Null(result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(result.PackagePath!, ".nupkg.metadata")));
        }
        finally
        {
            if (Directory.Exists(globalPackagesFolder))
            {
                Directory.Delete(globalPackagesFolder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that <c>TryDownloadAsync</c> returns an empty result (both <c>PackagePath</c> and
    ///     <c>ErrorMessage</c> <see langword="null"/>) when the source confirms the requested package
    ///     version is absent, without treating absence as an error.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageDownloader_TryDownloadAsync_PackageAbsent_ReturnsEmptyResult()
    {
        // Arrange - the feed carries a different package than the one requested
        var globalPackagesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(globalPackagesFolder);
            const string registeredPackageId = "TestPackage.Downloader.Registered";
            const string requestedPackageId = "TestPackage.Downloader.Absent";
            const string version = "1.0.0";

            await using var server = new NuGetTestServer();
            var nupkgBytes = NuGetPackageBuilder.CreateMinimalPackage(registeredPackageId, version);
            server.RegisterV3Package(registeredPackageId, version, nupkgBytes);
            var settings = server.CreateSettings(globalPackagesFolder, server.IndexUrl);

            var (sourceRepository, resource) = await ResolveResourceAsync(server.IndexUrl, "test-source");
            var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, NullLogger.Instance);
            using var cacheContext = new SourceCacheContext();

            // Act - request a package identity the feed does not carry
            var result = await PackageDownloader.TryDownloadAsync(
                sourceRepository,
                resource,
                requestedPackageId,
                NuGetVersion.Parse(version),
                globalPackagesFolder,
                clientPolicyContext,
                cacheContext,
                CancellationToken.None);

            // Assert - an empty result, not an error, since the source was reachable but simply did
            // not carry this package identity
            Assert.Null(result.PackagePath);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(globalPackagesFolder))
            {
                Directory.Delete(globalPackagesFolder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that <c>TryDownloadAsync</c> returns an empty result (no diagnostic error message)
    ///     when the download endpoint fails with a network-level error (HTTP 500 or a dropped
    ///     connection), treating it as a transient, non-actionable failure.
    /// </summary>
    /// <remarks>
    ///     Both an HTTP 500 response and a dropped connection during
    ///     <c>CopyNupkgToStreamAsync</c> surface identically as
    ///     <see cref="System.Net.Http.HttpRequestException"/> (confirmed by the corresponding
    ///     <c>NuGetCacheServerTests.NuGetCache_EnsureCachedAsync_DownloadProtocolError_ThrowsInvalidOperationException</c>
    ///     end-to-end test), so <c>TryDownloadAsync</c> silently swallows both rather than surfacing a
    ///     per-source diagnostic message.
    /// </remarks>
    [Theory]
    [Trait("Category", "LocalIntegration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageDownloader_TryDownloadAsync_NetworkOrProtocolFailureDuringDownload_ReturnsEmptyResult(
        bool simulateProtocolError)
    {
        // Arrange
        var globalPackagesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(globalPackagesFolder);
            var packageId = simulateProtocolError
                ? "TestPackage.Downloader.ProtocolError"
                : "TestPackage.Downloader.NetworkFailure";
            const string version = "1.0.0";

            await using var server = new NuGetTestServer();
            if (simulateProtocolError)
            {
                server.RegisterV3PackageWithDownloadProtocolError(packageId, version);
            }
            else
            {
                server.RegisterV3PackageWithDownloadNetworkFailure(packageId, version);
            }

            var settings = server.CreateSettings(globalPackagesFolder, server.IndexUrl);

            var (sourceRepository, resource) = await ResolveResourceAsync(server.IndexUrl, "test-source");
            var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, NullLogger.Instance);
            using var cacheContext = new SourceCacheContext();

            // Act
            var result = await PackageDownloader.TryDownloadAsync(
                sourceRepository,
                resource,
                packageId,
                NuGetVersion.Parse(version),
                globalPackagesFolder,
                clientPolicyContext,
                cacheContext,
                CancellationToken.None);

            // Assert - transient failures are silently swallowed with no diagnostic message
            Assert.Null(result.PackagePath);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(globalPackagesFolder))
            {
                Directory.Delete(globalPackagesFolder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that <c>TryDownloadAsync</c> returns an actionable diagnostic error message
    ///     identifying the source and the HTTP 401 status code when the download endpoint rejects
    ///     the request for lack of authentication, using a resource resolved through the same
    ///     production resolution path (including v2 fallback) as <see cref="NuGetCache"/>.
    /// </summary>
    /// <remarks>
    ///     For a v3 <c>/index.json</c> source, an authentication failure at the index-fetch stage is
    ///     masked by <see cref="PackageSourceResolver"/>'s <c>ResolveAsync</c> v2 fallback candidate, which
    ///     resolves without performing any HTTP request (see
    ///     <c>PackageSourceResolverTests.PackageSourceResolver_ResolveAsync_NetworkFailureOnIndex_DoesNotThrowAndReturnsNoErrorMessage</c>).
    ///     The failure is therefore deferred and reliably surfaces once <c>TryDownloadAsync</c>
    ///     performs the real HTTP request via <c>CopyNupkgToStreamAsync</c> against the masked v2
    ///     resource.
    /// </remarks>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageDownloader_TryDownloadAsync_AuthenticationFailureDuringDownload_ReturnsActionableErrorMessage()
    {
        // Arrange - the feed requires Basic Auth on every endpoint; no credentials are supplied
        var globalPackagesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(globalPackagesFolder);
            const string packageId = "TestPackage.Downloader.AuthFailure";
            const string version = "1.0.0";

            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(packageId, version, [], "test-user", "test-password");
            var settings = server.CreateSettings(globalPackagesFolder, server.IndexUrl);

            var (sourceRepository, resource) = await ResolveResourceAsync(server.IndexUrl, "test-source");
            var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, NullLogger.Instance);
            using var cacheContext = new SourceCacheContext();

            // Act
            var result = await PackageDownloader.TryDownloadAsync(
                sourceRepository,
                resource,
                packageId,
                NuGetVersion.Parse(version),
                globalPackagesFolder,
                clientPolicyContext,
                cacheContext,
                CancellationToken.None);

            // Assert - the diagnostic identifies the source name and the 401 status code
            Assert.Null(result.PackagePath);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("test-source", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains("401", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(globalPackagesFolder))
            {
                Directory.Delete(globalPackagesFolder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that <c>GetPackagePath</c> follows the NuGet global packages folder convention of
    ///     lower-casing both the package identifier and version when building the on-disk path.
    /// </summary>
    [Fact]
    public void PackageDownloader_GetPackagePath_MixedCaseIdAndVersion_ReturnsLowerCasedPath()
    {
        // Arrange
        var globalPackagesFolder = Path.Combine(Path.GetTempPath(), "packages");

        // Act
        const string packageId = "MixedCase.PackageId";
        const string version = "1.0.0-BETA";
        var path = PackageDownloader.GetPackagePath(globalPackagesFolder, packageId, version);

        // Assert - both the package ID and version segments are lower-cased
        var expected = Path.Combine(globalPackagesFolder, packageId.ToLowerInvariant(), version.ToLowerInvariant());
        Assert.Equal(expected, path);
    }

    /// <summary>
    ///     Resolves a real <see cref="FindPackageByIdResource"/> against the given v3 service index
    ///     URL, using <see cref="PackageSourceResolver"/>'s <c>ResolveAsync</c> - the same production
    ///     resolution logic (including v2 fallback candidate construction) used by
    ///     <see cref="NuGetCache"/> - rather than a single direct
    ///     <c>SourceRepository.GetResourceAsync{T}</c> call.
    /// </summary>
    private static async Task<(SourceRepository SourceRepository, FindPackageByIdResource Resource)> ResolveResourceAsync(
        string indexUrl,
        string sourceName)
    {
        var packageSource = new PackageSource(indexUrl, sourceName);
        var providers = Repository.Provider.GetCoreV3();
        var sourceRepository = new SourceRepository(packageSource, providers);
        var resolution = await PackageSourceResolver.ResolveAsync(sourceRepository, providers, CancellationToken.None);
        Assert.NotNull(resolution.Resource);
        return (resolution.Repository, resolution.Resource);
    }
}
