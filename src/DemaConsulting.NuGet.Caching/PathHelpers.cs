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
    ///     Safely combines two paths, ensuring the resolved combined path stays within the base directory.
    /// </summary>
    /// <param name="basePath">
    ///     The absolute or relative base directory path. Must not be <see langword="null"/>. The path
    ///     is resolved to its full absolute form before the containment check.
    /// </param>
    /// <param name="relativePath">
    ///     The relative path to append to <paramref name="basePath"/>. Must not be <see langword="null"/>,
    ///     must be a relative path (not rooted), and must not contain traversal sequences (such as
    ///     <c>..</c>) that would resolve to a location outside <paramref name="basePath"/>. An empty
    ///     string is accepted and returns the base path unchanged.
    /// </param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="basePath"/> or <paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the resolved combined path escapes the base directory, or when a supplied path is invalid.
    /// </exception>
    /// <exception cref="NotSupportedException">Thrown when a supplied path contains an unsupported format.</exception>
    /// <exception cref="PathTooLongException">Thrown when the combined or resolved path exceeds the system-defined maximum length.</exception>
    /// <remarks>
    ///     <para>
    ///         This method exists to prevent directory traversal attacks. Without this check, a caller
    ///         supplying a crafted relative path (such as <c>../../etc/passwd</c>) could read or write
    ///         files outside the intended base directory. The method rejects rather than sanitises such
    ///         paths: if the resolved path escapes the base, an <see cref="ArgumentException"/> is thrown
    ///         instead of silently normalising the path to something safe.
    ///     </para>
    ///     <para>
    ///         The containment check is performed at the string level using
    ///         <see cref="Path.GetFullPath(string)"/> to normalise both paths. Symbolic links are
    ///         <em>not</em> resolved: a symlink inside the base directory that points outside it will
    ///         pass this check. Callers that require symlink-safe behaviour must perform additional
    ///         validation after resolving real paths (e.g., via the OS).
    ///     </para>
    ///     <para>
    ///         This method is stateless and does not perform any file-system I/O. It is safe to call
    ///         concurrently from multiple threads.
    ///     </para>
    /// </remarks>
    internal static string SafePathCombine(string basePath, string relativePath)
    {
        // Validate inputs
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        // Combine the paths (preserves the caller's relative/absolute style)
        var combinedPath = Path.Combine(basePath, relativePath);

        // Security check: resolve both paths to absolute form and verify the combined
        // path is still inside the base directory. Path.GetFullPath normalizes ".." and
        // "." segments but does not resolve symbolic links; symlink-based traversal attacks
        // are outside the scope of this string-level validation.
        var absoluteBase = Path.GetFullPath(basePath);
        var absoluteCombined = Path.GetFullPath(combinedPath);

        // Path.GetRelativePath handles root paths, platform case-sensitivity, and
        // directory-separator normalization natively. The containment test treats ".."
        // as an escaping segment only when it is the entire relative result or is
        // followed by a directory separator, avoiding false positives for valid in-base
        // names such as "..data".
        var checkRelative = Path.GetRelativePath(absoluteBase, absoluteCombined);

        if (string.Equals(checkRelative, "..", StringComparison.Ordinal)
            || checkRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || checkRelative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(checkRelative))
        {
            throw new ArgumentException($"Invalid path component: {relativePath}", nameof(relativePath));
        }

        return combinedPath;
    }
}
