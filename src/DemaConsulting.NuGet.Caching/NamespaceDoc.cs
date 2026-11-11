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
///         <b>Typical usage</b> - choose a version, then cache it:
///         <code lang="csharp">
///         using System;
///         using System.Collections.Generic;
///         using System.Linq;
///         using System.Threading.Tasks;
///         using DemaConsulting.NuGet.Caching;
///
///         // Pick the newest listed version (strings in ascending order, so the newest is last).
///         // GetVersionsAsync also returns unlisted versions, so use ListVersionsAsync and IsListed to skip them.
///         IReadOnlyList&lt;NuGetPackageVersion&gt; versions = await NuGetCache.ListVersionsAsync("Newtonsoft.Json");
///         NuGetPackageVersion? latest = versions.LastOrDefault(v =&gt; v.IsListed);
///         if (latest is null)
///         {
///             // An empty list means no source has the package (or no listed stable version exists)
///             Console.WriteLine("No listed versions found.");
///             return;
///         }
///
///         // Make sure that version is in the global packages cache and get its folder
///         string packagePath = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", latest.Version);
///         Console.WriteLine($"Package cached at: {packagePath}");
///         </code>
///     </para>
///     <para>
///         <b>Typical usage</b> - include prerelease versions and honor a project's configuration:
///         <code lang="csharp">
///         using System;
///         using System.Collections.Generic;
///         using System.IO;
///         using System.Threading.Tasks;
///         using DemaConsulting.NuGet.Caching;
///
///         // Discover nuget.config files starting from the project directory, as the dotnet CLI does
///         string projectDirectory = Path.GetFullPath("src/MyProject");
///
///         IReadOnlyList&lt;string&gt; all = await NuGetCache.GetVersionsAsync(
///             "MyCompany.Tool",
///             includePrerelease: true,
///             root: projectDirectory);
///         Console.WriteLine($"{all.Count} versions found");
///         </code>
///     </para>
///     <para>
///         <b>Configuration</b>: package sources, package source mapping, credentials, and the global packages
///         folder are read from the standard NuGet settings, starting at the optional <c>root</c> directory
///         (the current directory when omitted) and walking up through ancestor directories to the user and
///         machine settings. This mirrors the behavior of the <c>dotnet</c> CLI, so a repository-local
///         <c>nuget.config</c> works as expected. <see cref="NuGetCacheOptions.Sources"/> replaces the
///         discovered sources for one call, and <see cref="NuGetCacheOptions.IncludeLocalSources"/> adds
///         local folder feeds to version listing. A NuGet V3 source must be its <c>index.json</c> service index URL
///         with no query string; NuGet treats any other URL as a V2 feed.
///     </para>
///     <para>
///         <b>Errors</b>: a package source that is unreachable or rejects the request is skipped as long as
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
///         <b>Exceptions at a glance</b>:
///         <list type="table">
///             <listheader>
///                 <term>Situation</term>
///                 <description>Exception</description>
///             </listheader>
///             <item>
///                 <term>A source answers HTTP 401 or 403 and no versions or package were found</term>
///                 <description>
///                     <see cref="NuGetAuthenticationException"/> from all of <c>EnsureCachedAsync</c>,
///                     <c>GetVersionsAsync</c>, and <c>ListVersionsAsync</c>.
///                 </description>
///             </item>
///             <item>
///                 <term><c>EnsureCachedAsync</c>: package or version not found, or sources unreachable</term>
///                 <description>
///                     <see cref="InvalidOperationException"/> in both cases; the two cannot be distinguished
///                     except by the per-source diagnostics in the message, which may be absent.
///                 </description>
///             </item>
///             <item>
///                 <term><c>GetVersionsAsync</c> / <c>ListVersionsAsync</c>: package not found on any reachable source</term>
///                 <description>An empty list, no exception.</description>
///             </item>
///             <item>
///                 <term><c>GetVersionsAsync</c> / <c>ListVersionsAsync</c>: nothing found and a source was unreachable or failed</term>
///                 <description><see cref="InvalidOperationException"/>; some versions found means no exception.</description>
///             </item>
///             <item>
///                 <term>Required signature missing or invalid (<c>EnsureCachedAsync</c>)</term>
///                 <description><see cref="InvalidOperationException"/>.</description>
///             </item>
///             <item>
///                 <term>Canceled token, or <see cref="NuGetCacheOptions.AuthenticationTimeout"/> elapsed</term>
///                 <description><see cref="OperationCanceledException"/>.</description>
///             </item>
///             <item>
///                 <term>Null package ID or version</term>
///                 <description><see cref="ArgumentNullException"/>.</description>
///             </item>
///             <item>
///                 <term>Empty or white-space package ID, invalid version string, bad entry in <c>Sources</c></term>
///                 <description><see cref="ArgumentException"/>.</description>
///             </item>
///             <item>
///                 <term>Invalid <see cref="NuGetCacheOptions.AuthenticationTimeout"/></term>
///                 <description><see cref="ArgumentOutOfRangeException"/>.</description>
///             </item>
///         </list>
///         Catch <see cref="NuGetAuthenticationException"/> before <see cref="InvalidOperationException"/>,
///         because the former derives from the latter:
///         <code lang="csharp">
///         using System;
///         using System.Threading;
///         using System.Threading.Tasks;
///         using DemaConsulting.NuGet.Caching;
///
///         using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
///         try
///         {
///             string path = await NuGetCache.EnsureCachedAsync(
///                 "Private.Package", "1.0.0", cancellationToken: cts.Token);
///             Console.WriteLine(path);
///         }
///         catch (NuGetAuthenticationException ex)
///         {
///             // 401: supply credentials (see below). 403: the account is known but not permitted, so
///             // another sign-in cannot help; ServerReason says why. Uri never includes user info, query or fragment
///             Console.WriteLine($"Access to {ex.Source} ({ex.Uri}) failed with {ex.StatusCode}: {ex.ServerReason}");
///         }
///         catch (InvalidOperationException ex)
///         {
///             // Not found, unreachable, or signature rejected (EnsureCachedAsync cannot tell the first two apart)
///             Console.WriteLine(ex.Message);
///         }
///         catch (OperationCanceledException)
///         {
///             Console.WriteLine("Canceled or timed out.");
///         }
///         </code>
///     </para>
///     <para>
///         <b>Credentials, logging, and timeouts</b>: pass a <see cref="NuGetCacheOptions"/> as the
///         <c>options</c> argument.
///         <see cref="NuGetCacheOptions.CredentialCallback"/> supplies a <see cref="NuGetCredential"/> when a source
///         answers HTTP 401 or 403 (it is consulted before any credential provider; returning null falls through to the
///         providers; the callback is invoked at most once per URL and request type per call, so a null answer or a
///         rejected credential is not requested again for the same address),
///         <see cref="NuGetCacheOptions.Interactive"/> allows credential providers to prompt,
///         <see cref="NuGetCacheOptions.Logger"/> receives NuGet messages, and
///         <see cref="NuGetCacheOptions.AuthenticationTimeout"/> bounds each credential exchange, not the whole call (the call then throws
///         <see cref="OperationCanceledException"/>, never <see cref="NuGetAuthenticationException"/>, even when NuGet
///         retries or wraps the failed request). When no credential is available for a challenge, the call fails
///         promptly with <see cref="NuGetAuthenticationException"/> rather than waiting through NuGet's retries. Caveats: the NuGet credential service is
///         process-wide, so options apply only when this library registered it (a host application's own
///         registration wins, and a warning is logged), and a timeout abandons the wait but does not terminate an
///         external credential-provider process: NuGet owns it and ends it on its idle timeout
///         (<c>NUGET_PLUGIN_IDLE_TIMEOUT_IN_SECONDS</c>, 30 minutes by default) or when this process exits.
///         Messages from credential providers (such as device-code instructions when
///         <see cref="NuGetCacheOptions.Interactive"/> is set) are delivered only through
///         <see cref="NuGetCacheOptions.Logger"/>, at Information level, possibly from a background thread.
///     </para>
///     <para>
///         <b>Credential callback example</b>:
///         <code lang="csharp">
///         using System;
///         using System.Threading.Tasks;
///         using DemaConsulting.NuGet.Caching;
///
///         var options = new NuGetCacheOptions
///         {
///             // Called on HTTP 401 or 403 with the challenged URI; return null to fall through to credential providers
///             CredentialCallback = (uri, cancellationToken) =>
///                 Task.FromResult&lt;NuGetCredential?&gt;(
///                     new NuGetCredential("user", Environment.GetEnvironmentVariable("FEED_TOKEN")!)),
///             AuthenticationTimeout = TimeSpan.FromSeconds(30),
///         };
///
///         string path = await NuGetCache.EnsureCachedAsync("Private.Package", "1.0.0", options: options);
///         Console.WriteLine(path);
///         </code>
///     </para>
///     <para>
///         <b>Signatures</b>: <see cref="NuGetCacheOptions.RequireSignedPackages"/> makes <c>EnsureCachedAsync</c> reject
///         unsigned or invalidly signed packages with <see cref="InvalidOperationException"/>, including for
///         packages already installed. Without it, the <c>signatureValidationMode</c> from <c>nuget.config</c> applies.
///         The package must carry a valid primary signature (author or repository) that is intact and chains to a
///         trusted root, and, when <c>trustedSigners</c> are configured, that matches one of them; certificate
///         chain, timestamp, and revocation handling follow NuGet's defaults. I/O errors reading the package or
///         writing the global packages folder (<see cref="IOException"/>,
///         <see cref="UnauthorizedAccessException"/>) propagate unchanged.
///     </para>
///     <para>
///         <b>Versions</b>: returned strings are NuGet-normalized and may carry <c>+metadata</c> or four parts.
///     </para>
///     <para>
///         <b>Thread safety</b>: all members of <see cref="NuGetCache"/> are stateless and safe to call
///         concurrently, including with different options.
///     </para>
/// </remarks>
internal static class NamespaceDoc
{
}
