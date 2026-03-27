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
[TestClass]
public class NuGetCacheTests
{
    /// <summary>
    ///     Gets or sets the test context provided by the MSTest framework before each test runs.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> returns the path to an existing
    ///     package folder after downloading a known small package from nuget.org.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Lib-EnsureCached: the library can ensure a NuGet package is cached locally.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task NuGetCache_EnsureCachedAsync_ReturnsPackageFolder()
    {
        // Arrange - use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act - ensure the package is present in the local NuGet global packages cache
        var packageFolder = await NuGetCache.EnsureCachedAsync(packageId, version, TestContext.CancellationToken);

        // Assert - the returned path must point to a real directory on disk
        Assert.IsNotNull(packageFolder, "EnsureCachedAsync should not return null");
        Assert.IsTrue(
            Directory.Exists(packageFolder),
            $"Expected package folder to exist at: {packageFolder}");

        // Assert - the directory must contain at least one .nupkg or .nuspec file,
        // proving the package was properly extracted into the global packages cache
        var hasPackageContent =
            Directory.EnumerateFiles(packageFolder, "*.nupkg", SearchOption.AllDirectories).Any() ||
            Directory.EnumerateFiles(packageFolder, "*.nuspec", SearchOption.AllDirectories).Any();

        Assert.IsTrue(
            hasPackageContent,
            $"Expected package folder to contain .nupkg or .nuspec files at: {packageFolder}");
    }

    /// <summary>
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> throws
    ///     <see cref="InvalidOperationException"/> when the package cannot be found in any configured NuGet source.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Lib-NotFound: the library reports when a package cannot be found.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task NuGetCache_EnsureCachedAsync_ThrowsWhenPackageNotFound()
    {
        // Arrange - use a GUID-based package ID that cannot exist on any NuGet feed
        var packageId = $"DemaConsulting.NonExistent.{Guid.NewGuid():N}";
        const string version = "1.0.0";

        // Act & Assert - calling with a non-existent package must throw InvalidOperationException
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, version, TestContext.CancellationToken));
    }

    /// <summary>
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> throws
    ///     <see cref="ArgumentNullException"/> when <c>packageId</c> is <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public async Task NuGetCache_EnsureCachedAsync_ThrowsForNullPackageId()
    {
        // Arrange - null packageId is an invalid argument
        const string version = "1.5.0";

        // Act & Assert - calling with null packageId must throw ArgumentNullException
        _ = await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(null!, version, TestContext.CancellationToken));
    }

    /// <summary>
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> throws
    ///     <see cref="ArgumentNullException"/> when <c>version</c> is <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public async Task NuGetCache_EnsureCachedAsync_ThrowsForNullVersion()
    {
        // Arrange - null version is an invalid argument
        const string packageId = "DemaConsulting.TestResults";

        // Act & Assert - calling with null version must throw ArgumentNullException
        _ = await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(packageId, null!, TestContext.CancellationToken));
    }

    /// <summary>
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> is idempotent: calling it twice
    ///     with the same package returns the same path both times.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task NuGetCache_EnsureCachedAsync_ReturnsSamePathWhenCalledTwice()
    {
        // Arrange - use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act - call EnsureCachedAsync twice with the same package identity
        var firstPath = await NuGetCache.EnsureCachedAsync(packageId, version, TestContext.CancellationToken);
        var secondPath = await NuGetCache.EnsureCachedAsync(packageId, version, TestContext.CancellationToken);

        // Assert - both calls must return identical paths, proving the method is idempotent
        // and does not change the cache location on subsequent calls
        Assert.AreEqual(firstPath, secondPath, "EnsureCachedAsync must return the same path on repeated calls");
    }
}
