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

using NuGet.Packaging;
using NuGet.Versioning;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Builds minimal valid NuGet package (.nupkg) byte arrays in memory for use in tests.
/// </summary>
/// <remarks>
///     <c>NuGet.Packaging.PackageBuilder</c> produces a standards-compliant .nupkg
///     archive containing a nuspec manifest and no content files. The resulting bytes are
///     accepted by the NuGet global packages folder utility during test-driven download
///     simulations. Keeping this logic in a dedicated class avoids duplicating the
///     package-construction boilerplate across multiple test files.
/// </remarks>
internal static class NuGetPackageBuilder
{
    /// <summary>
    ///     Creates a minimal valid .nupkg byte array for the given package identity.
    /// </summary>
    /// <remarks>
    ///     The package contains the required nuspec metadata (id, version, description, and at
    ///     least one author) plus a single placeholder content file. The content file is required
    ///     because <c>NuGet.Packaging.PackageBuilder</c> refuses to serialize a package that has
    ///     neither dependencies nor content. The placeholder file is not meaningful to the caching
    ///     tests — only the package identity metadata matters.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>TestPackage</c>).</param>
    /// <param name="version">The package version string (e.g. <c>1.0.0</c>).</param>
    /// <returns>A byte array containing a valid .nupkg archive.</returns>
    internal static byte[] CreateMinimalPackage(string packageId, string version)
    {
        // Construct a minimal package with only the required nuspec fields
        var builder = new PackageBuilder
        {
            Id = packageId,
            Version = new NuGetVersion(version),
            Description = "Minimal test package created by NuGetPackageBuilder.",
        };

        // At least one author is required by the nuspec schema
        builder.Authors.Add("Test");

        // PackageBuilder requires at least one dependency or content file; add a placeholder
        var placeholderContent = new byte[] { 0 };
        using var contentStream = new MemoryStream(placeholderContent);
        var physicalFile = new PhysicalPackageFile(contentStream)
        {
            TargetPath = "lib/netstandard2.0/_placeholder.txt",
        };
        builder.Files.Add(physicalFile);

        // Serialize the package into an in-memory stream and return the bytes
        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();
    }
}
