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
///     The <c>DemaConsulting.NuGet.Caching</c> namespace provides a small API for making NuGet packages
///     available in the local global packages cache and for discovering which versions of a package exist.
/// </summary>
/// <remarks>
///     <para>
///         The entry point is the static <see cref="NuGetCache"/> class. It offers two operations:
///         <list type="bullet">
///             <item>
///                 <see cref="NuGetCache.GetVersionsAsync(string, bool, string?, CancellationToken)"/> lists the
///                 versions of a package that are available from the configured remote sources, as version
///                 strings in ascending order.
///             </item>
///             <item>
///                 <see cref="NuGetCache.EnsureCachedAsync(string, string, string?, CancellationToken)"/> makes
///                 sure one exact version of a package is present in the global packages folder, downloading
///                 it when needed, and returns the absolute path of the package folder, following the NuGet layout
///                 <c>{globalPackagesFolder}/{lowercase id}/{normalized version}</c>. A version that is
///                 already installed is returned without any network access.
///             </item>
///         </list>
///     </para>
///     <para>
///         Typical usage - choose a version, then cache it:
///         <code>
///         using DemaConsulting.NuGet.Caching;
///         using System.Collections.Generic;
///
///         // List the stable versions of a package (strings in ascending order, so the newest is last)
///         IReadOnlyList&lt;string&gt; versions = await NuGetCache.GetVersionsAsync("Newtonsoft.Json");
///         string latest = versions[versions.Count - 1];
///
///         // Make sure that version is in the global packages cache and get its folder
///         string packagePath = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", latest);
///         Console.WriteLine($"Package cached at: {packagePath}");
///         </code>
///     </para>
///     <para>
///         Typical usage - include prerelease versions and honor a project's configuration:
///         <code>
///         using DemaConsulting.NuGet.Caching;
///         using System.Collections.Generic;
///         using System.IO;
///
///         // Discover nuget.config files starting from the project directory, as the dotnet CLI does
///         string projectDirectory = Path.GetFullPath("src/MyProject");
///
///         IReadOnlyList&lt;string&gt; all = await NuGetCache.GetVersionsAsync(
///             "MyCompany.Tool",
///             includePrerelease: true,
///             root: projectDirectory);
///         </code>
///     </para>
///     <para>
///         Configuration: there is nothing to configure in code. Package sources, package source
///         mapping, credentials, and the global packages folder are read from the standard NuGet settings,
///         starting at the optional <c>root</c> directory (the current directory when omitted) and walking up
///         through ancestor directories to the user and machine settings. This mirrors the behavior of the
///         <c>dotnet</c> CLI, so a repository-local <c>nuget.config</c> works as expected.
///     </para>
///     <para>
///         Errors: a package source that is unreachable or rejects the request is skipped as long as
///         another source can answer. When no source can answer, the call throws
///         <see cref="InvalidOperationException"/> whose message lists each source failure, including
///         guidance for authentication failures (HTTP 401 and 403). A package that no source has is reported
///         as an empty list by <c>GetVersionsAsync</c> and as <see cref="InvalidOperationException"/> by
///         <c>EnsureCachedAsync</c>. Invalid arguments throw <see cref="ArgumentNullException"/> or
///         <see cref="ArgumentException"/>, and a canceled token throws <see cref="OperationCanceledException"/>.
///     </para>
///     <para>
///         Thread safety: all members of <see cref="NuGetCache"/> are stateless and safe to call
///         concurrently.
///     </para>
/// </remarks>
internal static class NamespaceDoc
{
}
