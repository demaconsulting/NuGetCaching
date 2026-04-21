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
[TestClass]
public class NuGetCachingTests
{
    /// <summary>
    ///     Gets or sets the test context provided by the MSTest framework before each test runs.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    ///     Tests that the library provides programmatic NuGet package caching by ensuring a known
    ///     package is available in the local global packages cache after calling the library.
    /// </summary>
    /// <remarks>
    ///     This test proves Caching-Sys-PackageCaching: the system provides a .NET library for
    ///     programmatic NuGet package caching.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task NuGetCaching_EnsureCachedAsync_WhenKnownPackageAndVersionProvided_ReturnsPackageFolder()
    {
        // Arrange: use a small, known package that is reliably available on nuget.org
        const string packageId = "DemaConsulting.TestResults";
        const string version = "1.5.0";

        // Act: invoke the library's package caching capability
        var packageFolder = await NuGetCache.EnsureCachedAsync(packageId, version, TestContext.CancellationToken);

        // Assert: the library returned a valid path to the cached package on disk
        Assert.IsNotNull(packageFolder, "EnsureCachedAsync should not return null");
        Assert.IsTrue(
            Directory.Exists(packageFolder),
            $"Expected package folder to exist at: {packageFolder}");

        // Assert: a fully installed package should include the NuGet installation sentinel file
        var metadataFilePath = Path.Combine(packageFolder, ".nupkg.metadata");

        Assert.IsTrue(
            File.Exists(metadataFilePath),
            $"Expected package folder to contain .nupkg.metadata at: {metadataFilePath}");
    }

    /// <summary>
    ///     Tests that the library throws <see cref="InvalidOperationException"/> when the requested
    ///     package cannot be found in any configured NuGet source.
    /// </summary>
    /// <remarks>
    ///     This test proves the error-path behavior of Caching-Sys-PackageCaching: the system
    ///     correctly signals when a package version is unavailable across all configured sources.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task NuGetCaching_EnsureCachedAsync_WhenPackageNotFound_ThrowsInvalidOperationException()
    {
        // Arrange: use a package ID and version combination that does not exist on any source
        const string packageId = "DemaConsulting.NonExistentPackage.DoesNotExist";
        const string version = "99.99.99";

        // Act & Assert: the library should throw when the package cannot be found
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => NuGetCache.EnsureCachedAsync(packageId, version, TestContext.CancellationToken));
    }
}
