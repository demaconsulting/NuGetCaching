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
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> returns the path to an existing
    ///     package folder after downloading a known small package from nuget.org.
    /// </summary>
    /// <remarks>
    ///     This test proves CACH-REQ-007: the library can ensure a NuGet package is cached locally.
    /// </remarks>
    [TestMethod]
    public async Task NuGetCache_EnsureCachedAsync_ReturnsPackageFolder()
    {
        // Arrange - use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act - ensure the package is present in the local NuGet global packages cache
        var packageFolder = await NuGetCache.EnsureCachedAsync(packageId, version);

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
    ///     <see cref="ArgumentNullException"/> when <c>packageId</c> is <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public async Task NuGetCache_EnsureCachedAsync_ThrowsForNullPackageId()
    {
        // Arrange - null packageId is an invalid argument
        const string version = "1.5.0";

        // Act & Assert - calling with null packageId must throw ArgumentNullException
        _ = await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(null!, version));
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
            async () => await NuGetCache.EnsureCachedAsync(packageId, null!));
    }

    /// <summary>
    ///     Tests that <see cref="NuGetCache.EnsureCachedAsync"/> is idempotent: calling it twice
    ///     with the same package returns the same path both times.
    /// </summary>
    [TestMethod]
    public async Task NuGetCache_EnsureCachedAsync_ReturnsSamePathWhenCalledTwice()
    {
        // Arrange - use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act - call EnsureCachedAsync twice with the same package identity
        var firstPath = await NuGetCache.EnsureCachedAsync(packageId, version);
        var secondPath = await NuGetCache.EnsureCachedAsync(packageId, version);

        // Assert - both calls must return identical paths, proving the method is idempotent
        // and does not change the cache location on subsequent calls
        Assert.AreEqual(firstPath, secondPath, "EnsureCachedAsync must return the same path on repeated calls");
    }
}
