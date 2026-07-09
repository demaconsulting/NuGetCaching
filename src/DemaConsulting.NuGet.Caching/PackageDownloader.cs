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

using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Downloads a NuGet package using an already-resolved <see cref="FindPackageByIdResource"/>
///     and installs it into the global packages folder, and owns the on-disk package-path
///     convention used to locate installed packages.
/// </summary>
/// <remarks>
///     This class owns download and installation concerns only; resolving the
///     <see cref="FindPackageByIdResource"/> to download from is the responsibility of
///     <see cref="PackageSourceResolver"/>. Authentication-failure diagnosis during download is
///     delegated to <see cref="AuthFailureClassifier"/> so that logic is not duplicated between
///     resolution and download.
/// </remarks>
internal static class PackageDownloader
{
    /// <summary>
    ///     Represents the result of a single package download attempt from one NuGet source.
    /// </summary>
    /// <param name="PackagePath">
    ///     The absolute path to the installed package folder, or <see langword="null"/> if the
    ///     package was not available or could not be downloaded from this source.
    /// </param>
    /// <param name="ErrorMessage">
    ///     A diagnostic message describing a source-level failure (e.g. protocol mismatch),
    ///     or <see langword="null"/> when the source was reachable but simply did not carry
    ///     the requested package, or when the failure is transient and non-actionable.
    /// </param>
    internal readonly record struct TryDownloadResult(string? PackagePath, string? ErrorMessage);

    /// <summary>
    ///     Downloads and installs a NuGet package using an already-resolved
    ///     <see cref="FindPackageByIdResource"/>.
    /// </summary>
    /// <param name="sourceRepository">The source repository that owns the resource.</param>
    /// <param name="resource">The resolved <see cref="FindPackageByIdResource"/> to download from.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The parsed <see cref="NuGetVersion"/> to download.</param>
    /// <param name="globalPackagesFolder">Absolute path to the NuGet global packages folder.</param>
    /// <param name="clientPolicyContext">The client signing policy context from NuGet settings.</param>
    /// <param name="cacheContext">Shared <see cref="SourceCacheContext"/> for HTTP caching.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A <see cref="TryDownloadResult"/> with the installed package path on success, an empty
    ///     result when the package is absent from this source or a transient error occurs, or an
    ///     error result when a protocol error occurs during download.
    /// </returns>
    internal static async Task<TryDownloadResult> TryDownloadAsync(
        SourceRepository sourceRepository,
        FindPackageByIdResource resource,
        string packageId,
        NuGetVersion version,
        string globalPackagesFolder,
        ClientPolicyContext clientPolicyContext,
        SourceCacheContext cacheContext,
        CancellationToken cancellationToken)
    {
        var identity = new PackageIdentity(packageId, version);
        var source = sourceRepository.PackageSource.Source;
        var sourceName = sourceRepository.PackageSource.Name;

        // Stream the .nupkg bytes into memory; returns false when the package is absent from
        // this source, and throws on transient or permanent protocol errors
        using var packageStream = new MemoryStream();
        bool found;
        try
        {
            found = await resource.CopyNupkgToStreamAsync(
                packageId,
                version,
                packageStream,
                cacheContext,
                NullLogger.Instance,
                cancellationToken);
        }
        catch (NuGetProtocolException ex) when (AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, source, out var authMessage))
        {
            // An authentication failure (401/403) is actionable during download too - the source
            // exists and the package identity is valid, but the request was rejected for lack of
            // (or incorrect) credentials. Surface this rather than treating it as "package absent".
            return new TryDownloadResult(null, authMessage);
        }
        catch (NuGetProtocolException ex)
        {
            // Protocol error during the download itself - surface a diagnostic message
            // so the caller can include it in the final not-found exception
            return new TryDownloadResult(null,
                $"{source}: Protocol error downloading package. ({ex.Message})");
        }
        catch (HttpRequestException ex) when (AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, source, out var authMessage))
        {
            // Same as above, but surfaced as a raw HttpRequestException rather than a NuGet
            // protocol exception (observed for direct v3 flat-container content downloads)
            return new TryDownloadResult(null, authMessage);
        }
        catch (HttpRequestException)
        {
            // Transient network-level failure during download - not actionable, skip silently
            return default;
        }

        // The source confirmed it does not carry this package version
        if (!found)
        {
            return default;
        }

        // Rewind the stream then install the package into the global packages folder.
        // The DownloadResourceResult is disposed automatically by the using declaration.
        packageStream.Seek(0, SeekOrigin.Begin);
        using var downloadResult = await GlobalPackagesFolderUtility.AddPackageAsync(
            sourceRepository.PackageSource.Source,
            identity,
            packageStream,
            globalPackagesFolder,
            Guid.Empty,
            clientPolicyContext,
            NullLogger.Instance,
            cancellationToken);

        // Return the conventional package path that NuGet uses on disk
        return new TryDownloadResult(GetPackagePath(globalPackagesFolder, packageId, version.ToNormalizedString()), null);
    }

    /// <summary>
    ///     Gets the conventional on-disk path for a cached NuGet package.
    /// </summary>
    /// <param name="globalPackagesFolder">Absolute path to the NuGet global packages folder.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The version string.</param>
    /// <returns>The absolute path to the package folder inside the global packages folder.</returns>
    internal static string GetPackagePath(string globalPackagesFolder, string packageId, string version)
    {
        var packageIdPath = PathHelpers.SafePathCombine(globalPackagesFolder, packageId.ToLowerInvariant());
        return PathHelpers.SafePathCombine(packageIdPath, version.ToLowerInvariant());
    }
}
