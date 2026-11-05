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
///         The entry point is the static <see cref="NuGetCache"/> class. It offers four operations:
///         <list type="bullet">
///             <item>
///                 <c>GetVersionsAsync</c> lists the versions of a package that are available from the
///                 configured sources, as version strings in ascending order.
///             </item>
///             <item>
///                 <c>ListVersionsAsync</c> does the same but returns a <see cref="NuGetPackageVersion"/> per
///                 version, adding the source, whether the version is listed, and when it was published.
///             </item>
///             <item>
///                 <c>EnsureCachedAsync</c> makes sure one exact version of a package is present in the
///                 global packages folder, downloading it when needed, and returns the absolute path of the
///                 package folder, following the NuGet layout
///                 <c>{globalPackagesFolder}/{lowercase id}/{normalized version}</c>. A version that is
///                 already installed is returned without any network access.
///             </item>
///             <item>
///                 <c>GetCachedVersions</c> synchronously lists the versions already installed in the global
///                 packages folder, without any network access.
///             </item>
///         </list>
///         Each asynchronous operation has exactly one public method with the shape
///         <c>(..., string? root = null, NuGetCacheOptions? options = null, CancellationToken cancellationToken = default)</c>.
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
///         Configuration: package sources, package source mapping, credentials, and the global packages
///         folder are read from the standard NuGet settings, starting at the optional <c>root</c> directory
///         (the current directory when omitted) and walking up through ancestor directories to the user and
///         machine settings. This mirrors the behavior of the <c>dotnet</c> CLI, so a repository-local
///         <c>nuget.config</c> works as expected. <see cref="NuGetCacheOptions.Sources"/> replaces the
///         discovered sources for one call, and <see cref="NuGetCacheOptions.IncludeLocalSources"/> adds
///         local folder feeds to version listing.
///     </para>
///     <para>
///         Errors: a package source that is unreachable or rejects the request is skipped as long as
///         another source can answer. When no source can answer, the call throws
///         <see cref="InvalidOperationException"/> whose message lists each source failure. When a source
///         rejected the request with HTTP 401 or 403, the call always throws
///         <see cref="NuGetAuthenticationException"/> (a subclass of <see cref="InvalidOperationException"/>),
///         whether or not options were supplied. A package that no source has is reported as an empty list
///         by the version operations and as <see cref="InvalidOperationException"/> by <c>EnsureCachedAsync</c>.
///         Invalid arguments throw <see cref="ArgumentNullException"/> or <see cref="ArgumentException"/>
///         (a package ID must not be null, empty, or white space), and a canceled token throws
///         <see cref="OperationCanceledException"/>.
///     </para>
///     <para>
///         Credentials, logging, and timeouts: pass a <see cref="NuGetCacheOptions"/> as the
///         <c>options</c> argument.
///         <see cref="NuGetCacheOptions.CredentialCallback"/> supplies a <see cref="NuGetCredential"/> when a source
///         answers HTTP 401 or 403 (it is consulted before any credential provider; returning null falls through to the
///         providers, and a rejected credential is not requested again for the same address),
///         <see cref="NuGetCacheOptions.Interactive"/> allows credential providers to prompt,
///         <see cref="NuGetCacheOptions.Logger"/> receives NuGet messages, and
///         <see cref="NuGetCacheOptions.AuthenticationTimeout"/> bounds credential acquisition (the call then throws
///         <see cref="OperationCanceledException"/>, never <see cref="NuGetAuthenticationException"/>, even when NuGet
///         retries or wraps the failed request). Caveats: the NuGet credential service is
///         process-wide, so options apply only when this library registered it (a host application's own
///         registration wins, and a warning is logged), and a timeout abandons the wait but does not terminate an
///         external credential-provider process: NuGet owns it and ends it on its idle timeout
///         (<c>NUGET_PLUGIN_IDLE_TIMEOUT_IN_SECONDS</c>, 30 minutes by default) or when this process exits.
///     </para>
///     <para>
///         Signatures: <see cref="NuGetCacheOptions.RequireSignedPackages"/> makes <c>EnsureCachedAsync</c> reject
///         unsigned or invalidly signed packages with <see cref="InvalidOperationException"/>, including for
///         packages already installed. Without it, the <c>signatureValidationMode</c> from <c>nuget.config</c> applies.
///     </para>
///     <para>
///         Versions: returned strings are NuGet-normalized and may carry <c>+metadata</c> or four parts.
///     </para>
///     <para>
///         Thread safety: all members of <see cref="NuGetCache"/> are stateless and safe to call
///         concurrently, including with different options.
///     </para>
/// </remarks>
internal static class NamespaceDoc
{
}
