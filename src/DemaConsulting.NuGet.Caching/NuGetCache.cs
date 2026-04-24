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
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Static class providing NuGet package caching functionality.
/// </summary>
/// <remarks>
///     This class reads NuGet configuration (sources and global packages folder) from
///     the default NuGet settings on the local machine, mirroring the behavior of
///     the <c>dotnet</c> CLI and Visual Studio package restore.
/// </remarks>
public static class NuGetCache
{
    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The absolute path to the cached package folder inside the global packages folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> or <paramref name="version"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="version"/> is not a valid NuGet version string.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the package cannot be found in any configured NuGet source.
    /// </exception>
    public static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        // Validate input parameters before performing any I/O
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);

        // Parse the version string early to validate it and obtain the normalized form;
        // NuGet stores packages using the normalized version (e.g. "1.0" becomes "1.0.0")
        var nugetVersion = NuGetVersion.Parse(version);

        // Load the default NuGet settings from the machine / user configuration files
        var settings = Settings.LoadDefaultSettings(null);
        var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);

        // Compute the expected on-disk path for the package; NuGet stores packages under
        // {globalPackagesFolder}/{packageId.lower}/{normalizedVersion.lower}/
        var packagePath = GetPackagePath(globalPackagesFolder, packageId, nugetVersion.ToNormalizedString());

        // Return immediately when the package is fully installed - the common hot path.
        // Checking for the .nupkg.metadata file (written by NuGet as the last extraction step)
        // rather than the directory avoids a race condition where concurrent callers see the
        // directory before extraction is complete and return a partially-populated path.
        if (File.Exists(PathHelpers.SafePathCombine(packagePath, ".nupkg.metadata")))
        {
            return packagePath;
        }

        // Build the client policy context used for package signing validation
        var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, NullLogger.Instance);

        // Create a shared source cache context for all download attempts in this call
        using var sourceCacheContext = new SourceCacheContext();

        // Get the core V3 providers needed to communicate with NuGet v3 and v2 feeds
        var providers = Repository.Provider.GetCoreV3();

        // Load package source mapping; when enabled, only sources explicitly mapped to the
        // package ID are permitted - this mirrors nuget.config <packageSourceMapping> behavior
        var packageSourceMapping = PackageSourceMapping.GetPackageSourceMapping(settings);
        var sourceProvider = new PackageSourceProvider(settings);
        var enabledSources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

        // Filter sources by package source mapping when it is configured
        var allowedSources = packageSourceMapping.IsEnabled
            ? enabledSources.Where(s => packageSourceMapping.GetConfiguredPackageSources(packageId).Contains(s.Name))
            : enabledSources;

        foreach (var packageSource in allowedSources)
        {
            // Build a source repository for this feed using the V3 provider chain
            var sourceRepository = new SourceRepository(packageSource, providers);

            // Attempt to download the package from this source; null means not found here
            var downloadedPath = await TryDownloadPackageAsync(
                sourceRepository,
                packageId,
                nugetVersion,
                globalPackagesFolder,
                clientPolicyContext,
                sourceCacheContext,
                cancellationToken);

            // Return the installed package path on the first successful download
            if (downloadedPath != null)
            {
                return downloadedPath;
            }
        }

        // No configured source contained the requested package
        throw new InvalidOperationException(
            $"Package '{packageId}' version '{version}' was not found in any configured NuGet source.");
    }

    /// <summary>
    ///     Attempts to download a NuGet package from a single source repository and install it
    ///     into the global packages folder.
    /// </summary>
    /// <param name="sourceRepository">The source repository to query.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The parsed <see cref="NuGetVersion"/> to download.</param>
    /// <param name="globalPackagesFolder">Absolute path to the NuGet global packages folder.</param>
    /// <param name="clientPolicyContext">The client signing policy context from NuGet settings.</param>
    /// <param name="cacheContext">Shared <see cref="SourceCacheContext"/> for HTTP caching.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     The absolute path to the installed package directory, or <see langword="null"/> if the
    ///     package was not available from this source or the source could not be reached.
    /// </returns>
    private static async Task<string?> TryDownloadPackageAsync(
        SourceRepository sourceRepository,
        string packageId,
        NuGetVersion version,
        string globalPackagesFolder,
        ClientPolicyContext clientPolicyContext,
        SourceCacheContext cacheContext,
        CancellationToken cancellationToken)
    {
        // Build the package identity from the provided packageId and version
        var identity = new PackageIdentity(packageId, version);

        // Obtain the FindPackageByIdResource; some source types may not support it or may
        // be unreachable - in either case skip this source and move on to the next
        FindPackageByIdResource? resource;
        try
        {
            resource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        }
        catch (NuGetProtocolException)
        {
            // Source is unreachable or misconfigured - skip it and try the next one
            return null;
        }
        catch (HttpRequestException)
        {
            // Network-level failure talking to this source - skip it and try the next one
            return null;
        }

        // A null resource means this source does not support the required protocol
        if (resource == null)
        {
            return null;
        }

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
        catch (NuGetProtocolException)
        {
            // Download failure from this source - skip it and try the next one
            return null;
        }
        catch (HttpRequestException)
        {
            // Network-level failure during download - skip it and try the next one
            return null;
        }

        // The source confirmed it does not carry this package version
        if (!found)
        {
            return null;
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
        return GetPackagePath(globalPackagesFolder, packageId, version.ToNormalizedString());
    }

    /// <summary>
    ///     Gets the conventional on-disk path for a cached NuGet package.
    /// </summary>
    /// <param name="globalPackagesFolder">Absolute path to the NuGet global packages folder.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The version string.</param>
    /// <returns>The absolute path to the package folder inside the global packages folder.</returns>
    private static string GetPackagePath(string globalPackagesFolder, string packageId, string version)
    {
        var packageIdPath = PathHelpers.SafePathCombine(globalPackagesFolder, packageId.ToLowerInvariant());
        return PathHelpers.SafePathCombine(packageIdPath, version.ToLowerInvariant());
    }
}
