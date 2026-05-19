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

        // Accumulate per-source diagnostic messages so they can be included in the
        // final exception when all sources fail - giving callers actionable context
        var sourceErrors = new List<string>();

        foreach (var packageSource in allowedSources)
        {
            // Build a source repository for this feed using the V3 provider chain
            var sourceRepository = new SourceRepository(packageSource, providers);

            // Attempt to download the package from this source
            var result = await TryDownloadPackageAsync(
                sourceRepository,
                packageId,
                nugetVersion,
                globalPackagesFolder,
                clientPolicyContext,
                sourceCacheContext,
                cancellationToken);

            // Return the installed package path on the first successful download
            if (result.PackagePath != null)
            {
                return result.PackagePath;
            }

            // Record any source-level diagnostic message for inclusion in the final exception
            if (result.ErrorMessage != null)
            {
                sourceErrors.Add(result.ErrorMessage);
            }
        }

        // Build the final exception message; append per-source diagnostics when available
        // so callers can identify which sources failed and why (e.g. v2-only feed misconfiguration)
        var baseMessage = $"Package '{packageId}' version '{version}' was not found in any configured NuGet source.";
        var fullMessage = sourceErrors.Count > 0
            ? baseMessage + Environment.NewLine + string.Join(Environment.NewLine, sourceErrors.Select(e => $"  - {e}"))
            : baseMessage;

        throw new InvalidOperationException(fullMessage);
    }

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
    private readonly record struct TryDownloadResult(string? PackagePath, string? ErrorMessage);

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
    ///     A <see cref="TryDownloadResult"/> whose <c>PackagePath</c> is the absolute path to the
    ///     installed package directory on success, or <see langword="null"/> when the package was
    ///     not found or a non-actionable transient error occurred. <c>ErrorMessage</c> is populated
    ///     with a diagnostic string when the source failed in an actionable way (e.g. protocol
    ///     mismatch) so the caller can surface it in the final exception.
    /// </returns>
    private static async Task<TryDownloadResult> TryDownloadPackageAsync(
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
        catch (NuGetProtocolException ex)
        {
            // Protocol error loading the service index - most commonly caused by a v2-only
            // feed being configured with a v3 URL ending in '.json'; surface a diagnostic
            // message so the caller can include it in the final not-found exception
            var source = sourceRepository.PackageSource.Source;
            return new TryDownloadResult(null,
                $"{source}: Failed to load source index. The feed may be v2-only; use the base URL without '/index.json'. ({ex.Message})");
        }
        catch (HttpRequestException)
        {
            // Transient network-level failure talking to this source - not actionable,
            // so skip silently and try the next source
            return default;
        }

        // A null resource means this source does not support the required protocol
        if (resource == null)
        {
            return default;
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
        catch (NuGetProtocolException ex)
        {
            // Protocol error during the download itself - surface a diagnostic message
            // so the caller can include it in the final not-found exception
            var source = sourceRepository.PackageSource.Source;
            return new TryDownloadResult(null,
                $"{source}: Protocol error downloading package. ({ex.Message})");
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
    private static string GetPackagePath(string globalPackagesFolder, string packageId, string version)
    {
        var packageIdPath = PathHelpers.SafePathCombine(globalPackagesFolder, packageId.ToLowerInvariant());
        return PathHelpers.SafePathCombine(packageIdPath, version.ToLowerInvariant());
    }
}
