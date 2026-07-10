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
///     Unit tests for the <see cref="NuGetCache"/> class.
/// </summary>
public class NuGetCacheTests
{
    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> returns the path to an existing
    ///     package folder after downloading a known small package from nuget.org.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-NuGetCache-EnsureCached: the library can ensure a NuGet package is cached locally.
    /// </remarks>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder()
    {
        // Arrange - use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act - ensure the package is present in the local NuGet global packages cache
        var packageFolder = await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None);

        // Assert - the returned path must point to a real directory on disk
        Assert.NotNull(packageFolder);
        Assert.True(
            Directory.Exists(packageFolder),
            $"Expected package folder to exist at: {packageFolder}");

        // Assert - the directory must contain at least one .nupkg or .nuspec file,
        // proving the package was properly extracted into the global packages cache
        var hasPackageContent =
            Directory.EnumerateFiles(packageFolder, "*.nupkg", SearchOption.AllDirectories).Any() ||
            Directory.EnumerateFiles(packageFolder, "*.nuspec", SearchOption.AllDirectories).Any();

        Assert.True(
            hasPackageContent,
            $"Expected package folder to contain .nupkg or .nuspec files at: {packageFolder}");
    }

    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> throws
    ///     <see cref="InvalidOperationException"/> when the package cannot be found in any configured NuGet source.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-NuGetCache-NotFound: the library reports when a package cannot be found.
    /// </remarks>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ThrowsInvalidOperationException()
    {
        // Arrange - use a GUID-based package ID that cannot exist on any NuGet feed
        var packageId = $"DemaConsulting.NonExistent.{Guid.NewGuid():N}";
        const string version = "1.0.0";

        // Act & Assert - calling with a non-existent package must throw InvalidOperationException
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None));
    }

    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> throws
    ///     <see cref="ArgumentNullException"/> when <c>packageId</c> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-NuGetCache-NullPackageId: the library validates the
    ///     <c>packageId</c> parameter and throws <see cref="ArgumentNullException"/> when it is null.
    /// </remarks>
    [Fact]
    public async Task NuGetCache_EnsureCachedAsync_NullPackageId_ThrowsArgumentNullException()
    {
        // Arrange - null packageId is an invalid argument
        const string version = "1.5.0";

        // Act & Assert - calling with null packageId must throw ArgumentNullException
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(null!, version, cancellationToken: CancellationToken.None));
    }

    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> throws
    ///     <see cref="ArgumentNullException"/> when <c>version</c> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-NuGetCache-NullVersion: the library validates the
    ///     <c>version</c> parameter and throws <see cref="ArgumentNullException"/> when it is null.
    /// </remarks>
    [Fact]
    public async Task NuGetCache_EnsureCachedAsync_NullVersion_ThrowsArgumentNullException()
    {
        // Arrange - null version is an invalid argument
        const string packageId = "DemaConsulting.TestResults";

        // Act & Assert - calling with null version must throw ArgumentNullException
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, null!, cancellationToken: CancellationToken.None));
    }

    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> throws
    ///     <see cref="ArgumentException"/> when <c>version</c> is not a valid NuGet version string.
    /// </summary>
    [Fact]
    public async Task NuGetCache_EnsureCachedAsync_InvalidVersion_ThrowsArgumentException()
    {
        // Arrange: a string that is not a valid NuGet version
        const string packageId = "DemaConsulting.TestResults";
        const string version = "not-a-version";

        // Act & Assert: calling with an invalid version must throw ArgumentException
        _ = await Assert.ThrowsAsync<ArgumentException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None));
    }

    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> throws
    ///     <see cref="InvalidOperationException"/> whose message identifies the package ID and version
    ///     when the package cannot be found in any configured NuGet source.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-NuGetCache-NotFound: the exception message includes both the
    ///     package ID and the version string so callers can correlate the error to their input.
    /// </remarks>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NuGetCache_EnsureCachedAsync_PackageAbsentFromAllSources_ExceptionMessageContainsPackageIdAndVersion()
    {
        // Arrange - use a GUID-based package ID that cannot exist on any NuGet feed.
        // The N format specifier produces a 32-character hex string without hyphens,
        // which is valid in a NuGet package ID.
        var packageId = $"DemaConsulting.NonExistent.{Guid.NewGuid():N}";
        const string version = "1.0.0";

        // Act - calling with a non-existent package must throw InvalidOperationException
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None));

        // Assert - the exception message must identify both the package ID and the version
        // so that callers have enough context to diagnose the problem
        Assert.Contains(packageId, exception.Message, StringComparison.Ordinal);
        Assert.Contains(version, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>NuGetCache.EnsureCachedAsync</c> is idempotent: calling it twice
    ///     with the same package returns the same path both times.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-NuGetCache-EnsureCached: the library can ensure a NuGet package is cached locally,
    ///     and the operation is idempotent — calling it a second time returns the same path from the cache.
    /// </remarks>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NuGetCache_EnsureCachedAsync_CalledTwiceWithSamePackage_ReturnsSamePath()
    {
        // Arrange - use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act - call EnsureCachedAsync twice with the same package identity
        var firstPath = await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None);
        var secondPath = await NuGetCache.EnsureCachedAsync(packageId, version, cancellationToken: CancellationToken.None);

        // Assert - both calls must return identical paths, proving the method is idempotent
        // and does not change the cache location on subsequent calls
        Assert.Equal(firstPath, secondPath);
    }
}
