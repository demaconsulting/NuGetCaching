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

using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Local-integration tests for the <see cref="PackageSourceResolver"/> class, using a local
///     <see cref="NuGetTestServer"/> (WireMock) to simulate NuGet v3 and v2 feed scenarios without
///     making real network calls.
/// </summary>
/// <remarks>
///     These tests call <see cref="PackageSourceResolver.ResolveAsync"/> directly against a manually
///     constructed <see cref="SourceRepository"/>, focusing on resolution behaviors owned specifically
///     by this unit (candidate-repository construction and resource-resolution outcomes) rather than
///     the full download-and-install flow, which is covered by <c>NuGetCacheServerTests</c> and
///     <c>PackageDownloaderTests</c>.
/// </remarks>
public class PackageSourceResolverTests
{
    /// <summary>
    ///     Tests that <c>ResolveAsync</c> resolves the <c>FindPackageByIdResource</c> directly from a
    ///     healthy v3 service index without needing any fallback.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageSourceResolver_ResolveAsync_HealthyV3Index_ReturnsResourceForOriginalRepository()
    {
        // Arrange
        await using var server = new NuGetTestServer();
        server.RegisterV3Package("TestPackage.Resolver.V3", "1.0.0", []);
        var packageSource = new PackageSource(server.IndexUrl, "test-source");
        var providers = Repository.Provider.GetCoreV3();
        var sourceRepository = new SourceRepository(packageSource, providers);

        // Act
        var result = await PackageSourceResolver.ResolveAsync(sourceRepository, providers, CancellationToken.None);

        // Assert - resolved successfully against the originally configured repository, no fallback needed
        Assert.NotNull(result.Resource);
        Assert.Null(result.ErrorMessage);
        Assert.Same(sourceRepository, result.Repository);
    }

    /// <summary>
    ///     Tests that <c>ResolveAsync</c> falls back to a v2 OData repository when the configured v3
    ///     <c>/index.json</c> URL fails with a protocol error, and that the returned effective
    ///     repository is the v2 fallback rather than the originally configured one.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageSourceResolver_ResolveAsync_V3IndexProtocolError_FallsBackToV2Repository()
    {
        // Arrange - the v3 index fails, but the base URL serves a valid v2 OData feed
        await using var server = new NuGetTestServer();
        server.SimulateV3IndexProtocolError();
        server.SimulateV2Package("TestPackage.Resolver.V2Fallback", "1.0.0", []);
        var packageSource = new PackageSource(server.IndexUrl, "test-source");
        var providers = Repository.Provider.GetCoreV3();
        var sourceRepository = new SourceRepository(packageSource, providers);

        // Act
        var result = await PackageSourceResolver.ResolveAsync(sourceRepository, providers, CancellationToken.None);

        // Assert - the v2 fallback candidate resolved successfully, and the effective repository
        // returned is the fallback (base URL), not the originally configured v3 index repository
        Assert.NotNull(result.Resource);
        Assert.NotSame(sourceRepository, result.Repository);
        Assert.Equal(server.BaseUrl, result.Repository.PackageSource.Source);
    }

    /// <summary>
    ///     Tests that <c>ResolveAsync</c> does not attempt any v2 fallback when the configured source
    ///     URL does not end in <c>/index.json</c>, confirming <c>BuildCandidateRepositories</c> only
    ///     produces the original repository as a single candidate.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageSourceResolver_ResolveAsync_NonIndexJsonSourceUrl_ResolvesDirectlyAsV2()
    {
        // Arrange - configure the source at the bare base URL (no '/index.json' suffix) serving v2
        await using var server = new NuGetTestServer();
        server.SimulateV2Package("TestPackage.Resolver.NativeV2", "1.0.0", []);
        var packageSource = new PackageSource(server.BaseUrl, "test-source");
        var providers = Repository.Provider.GetCoreV3();
        var sourceRepository = new SourceRepository(packageSource, providers);

        // Act
        var result = await PackageSourceResolver.ResolveAsync(sourceRepository, providers, CancellationToken.None);

        // Assert - resolves directly against the single configured candidate; no fallback repository
        // is created, so the effective repository is the same instance as the original
        Assert.NotNull(result.Resource);
        Assert.Same(sourceRepository, result.Repository);
    }

    /// <summary>
    ///     Tests that <c>ResolveAsync</c> does not surface a diagnostic error message when the v3
    ///     index request fails with a transient network-level error at resolution time, confirming
    ///     that resolving a resource does not by itself require a successful v3 service-index fetch:
    ///     the NuGet SDK's provider chain (<c>Repository.Provider.GetCoreV3()</c>) falls back
    ///     internally to a V2-typed resource object without making any further HTTP request,
    ///     deferring actual protocol validation to first use (search/download) rather than
    ///     resolution.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PackageSourceResolver_ResolveAsync_NetworkFailureOnIndex_DoesNotThrowAndReturnsNoErrorMessage()
    {
        // Arrange
        await using var server = new NuGetTestServer();
        server.SimulateNetworkFailureOnIndex();
        var packageSource = new PackageSource(server.IndexUrl, "test-source");
        var providers = Repository.Provider.GetCoreV3();
        var sourceRepository = new SourceRepository(packageSource, providers);

        // Act
        var result = await PackageSourceResolver.ResolveAsync(sourceRepository, providers, CancellationToken.None);

        // Assert - resolution itself does not surface the transient failure as a diagnostic error.
        // Any actual auth, protocol, or network failure only appears once the resolved resource is
        // used to search for or download the package, exercised separately by PackageDownloaderTests
        Assert.Null(result.ErrorMessage);
    }
}

