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

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Helper utilities for safe path operations.
/// </summary>
internal static class PathHelpers
{
    /// <summary>
    ///     Safely combines two paths, ensuring the second path doesn't contain path traversal sequences.
    /// </summary>
    /// <param name="basePath">The base path.</param>
    /// <param name="relativePath">The relative path to combine.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentException">Thrown when relativePath contains invalid characters or path traversal sequences.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="basePath"/> or <paramref name="relativePath"/> is <see langword="null"/>.</exception>
    internal static string SafePathCombine(string basePath, string relativePath)
    {
        // Validate inputs
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        // Ensure the relative path doesn't contain path traversal sequences
        if (relativePath.Contains("..") || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException($"Invalid path component: {relativePath}", nameof(relativePath));
        }

        // This call to Path.Combine is safe because we've validated that:
        // 1. relativePath doesn't contain ".." (path traversal)
        // 2. relativePath is not an absolute path (IsPathRooted check)
        // This ensures the combined path will always be under basePath
        var combinedPath = Path.Combine(basePath, relativePath);

#if NET5_0_OR_GREATER
        // Additional security validation: ensure the combined path is still under the base path.
        // This defense-in-depth approach protects against edge cases that might bypass the
        // initial validation, ensuring the final path stays within the intended directory.
        // Note: Path.GetFullPath normalizes ".." and "." segments but does NOT resolve symbolic links.
        // Symlink-based traversal attacks are outside the scope of this string-level validation.
        var fullBasePath = Path.GetFullPath(basePath);
        var fullCombinedPath = Path.GetFullPath(combinedPath);

        // Use GetRelativePath to verify the relationship between paths
        var relativeCheck = Path.GetRelativePath(fullBasePath, fullCombinedPath);
        if (relativeCheck.StartsWith("..") || Path.IsPathRooted(relativeCheck))
        {
            throw new ArgumentException($"Invalid path component: {relativePath}", nameof(relativePath));
        }
#endif

        return combinedPath;
    }
}
