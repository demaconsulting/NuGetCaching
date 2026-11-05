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
///     Describes one version of a NuGet package as reported by <c>NuGetCache.ListVersionsAsync</c>.
/// </summary>
/// <param name="Version">
///     The NuGet-normalized version string (for example <c>1.0.0</c> or <c>2.1.0-beta.1</c>). It may
///     carry <c>+metadata</c> or have four parts.
/// </param>
/// <param name="Source">
///     The name of the package source that reported this version: the first source, in query order,
///     that carries it. For a source given by URL or path in <see cref="NuGetCacheOptions.Sources"/>
///     this is the URL or path itself.
/// </param>
/// <param name="IsListed">
///     <see langword="true"/> when the version is listed; <see langword="false"/> when it was unlisted
///     by its publisher. It is <see langword="true"/> when the source offers no listing information.
/// </param>
/// <param name="Published">
///     The time the version was published, or <see langword="null"/> when the source does not report it.
/// </param>
public sealed record NuGetPackageVersion(string Version, string Source, bool IsListed, DateTimeOffset? Published);
