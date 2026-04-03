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
///     Unit tests for the PathHelpers class.
/// </summary>
[TestClass]
public class PathHelpersTests
{
    /// <summary>
    ///     Test that SafePathCombine successfully combines valid paths.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly()
    {
        // Arrange - set up valid base and relative paths
        var basePath = Path.GetTempPath();
        var relativePath = "documents/file.txt";

        // Act - combine paths using SafePathCombine
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - combined path matches standard path combination
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for path traversal with double dots.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException()
    {
        // Arrange - set up a path traversal attempt using double dots
        var basePath = Path.GetTempPath();
        var relativePath = "../etc/passwd";

        // Act & Assert - SafePathCombine must reject path traversal with ArgumentException
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            PathHelpers.SafePathCombine(basePath, relativePath));
        Assert.Contains("Invalid path component", exception.Message);
        Assert.AreEqual("relativePath", exception.ParamName);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for path with double dots in middle.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException()
    {
        // Arrange - set up a path with embedded double dots in the middle
        var basePath = Path.GetTempPath();
        var relativePath = "documents/../../../etc/passwd";

        // Act & Assert - SafePathCombine must reject traversal sequences with ArgumentException
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            PathHelpers.SafePathCombine(basePath, relativePath));
        Assert.Contains("Invalid path component", exception.Message);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentException for absolute paths.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException()
    {
        // Arrange - set up Unix absolute path as relative argument
        var unixBasePath = Path.GetTempPath();
        var unixRelativePath = "/etc/passwd";

        // Act & Assert - SafePathCombine must reject an absolute Unix path
        var unixException = Assert.ThrowsExactly<ArgumentException>(() =>
            PathHelpers.SafePathCombine(unixBasePath, unixRelativePath));
        Assert.Contains("Invalid path component", unixException.Message);

        // Arrange - set up Windows-style absolute path (only validated on Windows)
        // Test a Windows-style absolute path; Path.IsPathRooted returns false for this format on Linux/macOS
        if (OperatingSystem.IsWindows())
        {
            var windowsBasePath = "C:\\Users\\User";
            var windowsRelativePath = "C:\\Windows\\System32";

            // Act & Assert - SafePathCombine must reject an absolute Windows path
            var windowsException = Assert.ThrowsExactly<ArgumentException>(() =>
                PathHelpers.SafePathCombine(windowsBasePath, windowsRelativePath));
            Assert.Contains("Invalid path component", windowsException.Message);
        }
    }

    /// <summary>
    ///     Test that SafePathCombine accepts a filename that starts with ".." but is not a traversal sequence.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly()
    {
        // Arrange - set up a filename starting with ".." that is not a traversal
        var basePath = Path.GetTempPath();
        var relativePath = "..data";

        // Act - combine paths with the ".." prefixed filename
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - result matches standard path combination for this valid filename
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles current directory reference.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly()
    {
        // Arrange - set up a relative path with a leading current-directory reference
        var basePath = Path.GetTempPath();
        var relativePath = "./subfolder/file.txt";

        // Act - combine paths using SafePathCombine
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - result matches standard path combination
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine correctly handles empty relative path.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath()
    {
        // Arrange - use an empty string as the relative path
        var basePath = Path.GetTempPath();
        var relativePath = "";

        // Act - combine paths with an empty relative path
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - result equals the base path when relative path is empty
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine accepts simple filename.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_SimpleFilename_CombinesCorrectly()
    {
        // Arrange - set up a simple single-segment filename
        var basePath = Path.GetTempPath();
        var relativePath = "file.txt";

        // Act - combine base path with a simple filename
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - result matches standard path combination
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine accepts path with subdirectories.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly()
    {
        // Arrange - set up a multi-segment relative path with nested subdirectories
        var basePath = Path.GetTempPath();
        var relativePath = "documents/work/report.pdf";

        // Act - combine base path with a nested relative path
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - result matches standard path combination for nested paths
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine accepts GUID-based filename.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_GuidBasedFilename_CombinesSuccessfully()
    {
        // Arrange - set up a GUID-based temporary filename
        var basePath = Path.GetTempPath();
        var guid = Guid.NewGuid();
        var relativePath = $"test-{guid}.tmp";

        // Act - combine base path with the GUID-based filename
        var result = PathHelpers.SafePathCombine(basePath, relativePath);

        // Assert - result matches standard path combination for GUID-based filenames
        Assert.AreEqual(Path.Combine(basePath, relativePath), result);
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentNullException for null basePath.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException()
    {
        // Arrange - use null as the basePath argument
        const string? basePath = null;
        var relativePath = "file.txt";

        // Act & Assert - SafePathCombine must throw ArgumentNullException for null basePath
        _ = Assert.ThrowsExactly<ArgumentNullException>(() =>
            PathHelpers.SafePathCombine(basePath!, relativePath));
    }

    /// <summary>
    ///     Test that SafePathCombine throws ArgumentNullException for null relativePath.
    /// </summary>
    [TestMethod]
    public void PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException()
    {
        // Arrange - use null as the relativePath argument
        var basePath = Path.GetTempPath();
        const string? relativePath = null;

        // Act & Assert - SafePathCombine must throw ArgumentNullException for null relativePath
        _ = Assert.ThrowsExactly<ArgumentNullException>(() =>
            PathHelpers.SafePathCombine(basePath, relativePath!));
    }
}
