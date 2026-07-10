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
///     the default NuGet settings, rooted at a caller-supplied (or current working) directory,
///     mirroring the behavior of the <c>dotnet</c> CLI and Visual Studio package restore -
///     including discovery of a project/repo-local <c>nuget.config</c>.
///     This class is stateless — all state is local to each <c>EnsureCachedAsync</c> call — and is
///     safe for concurrent use.
/// </remarks>
public static class NuGetCache
{
    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache.
    /// </summary>
    /// <remarks>
    ///     Reads NuGet configuration from the default settings via <c>Settings.LoadDefaultSettings</c>,
    ///     rooted at <paramref name="root"/> (or the current working directory when
    ///     <paramref name="root"/> is <see langword="null"/>). Rooting the settings lookup this way
    ///     mirrors the behavior of the <c>dotnet</c> CLI and Visual Studio package restore, which
    ///     both discover a project/repo-local <c>nuget.config</c> by walking up from an ambient
    ///     working directory - passing a literal <see langword="null"/> root (as earlier versions of
    ///     this method did) skips that walk entirely and silently loses any repo-local package
    ///     sources. All caching logic is delegated to the internal overload that accepts an explicit
    ///     <see cref="ISettings"/> instance.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files (walking up
    ///     through ancestor directories, then falling back to machine/user-wide settings), matching
    ///     the <c>dotnet</c> CLI's behavior. Typically the directory containing the caller's project
    ///     or <c>packages.config</c> file. When <see langword="null"/>, defaults to
    ///     <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
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
        string? root = null,
        CancellationToken cancellationToken = default)
    {
        // Delegate to the overload that accepts an explicit ISettings instance, rooting the
        // default settings lookup at the provided directory (or the current working directory)
        // so that a project/repo-local nuget.config is discovered the same way `dotnet restore` does
        return await EnsureCachedAsync(
            packageId, version, Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory()), cancellationToken);
    }

    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache,
    ///     using the provided NuGet <paramref name="settings"/> instance.
    /// </summary>
    /// <remarks>
    ///     This overload exists to support testing: by injecting a custom <see cref="ISettings"/>
    ///     (e.g. one pointing at a local WireMock test server with a dedicated global packages
    ///     folder), the full download-and-cache behavior can be exercised without touching the
    ///     developer's real global packages cache or contacting external NuGet feeds. All caching
    ///     logic lives here so there is a single implementation path shared by the public overload.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="settings">
    ///     The NuGet settings instance used to resolve package sources, the global packages folder,
    ///     and package source mapping. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The absolute path to the cached package folder inside the global packages folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/>, <paramref name="version"/>, or
    ///     <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="version"/> is not a valid NuGet version string.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the package cannot be found in any configured NuGet source.
    /// </exception>
    internal static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        ISettings settings,
        CancellationToken cancellationToken = default)
    {
        return await EnsureCachedAsync(
            packageId, version, settings, CredentialServiceRegistrar.DefaultCredentialRegistrar, cancellationToken);
    }

    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache,
    ///     using the provided NuGet <paramref name="settings"/> instance and an explicit
    ///     <paramref name="credentialRegistrar"/>.
    /// </summary>
    /// <remarks>
    ///     This overload exists to support testing: injecting a test double
    ///     <see cref="ICredentialServiceRegistrar"/> lets a test assert that
    ///     <c>EnsureCachedAsync</c> invokes credential-service registration, without observing or
    ///     resetting any shared, process-wide static state. All non-test callers use the
    ///     <see cref="ISettings"/>-only overload, which delegates here with
    ///     <see cref="CredentialServiceRegistrar.DefaultCredentialRegistrar"/> - a single static
    ///     instance shared by every real call in the process, preserving the required
    ///     once-per-process registration semantics.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="settings">
    ///     The NuGet settings instance used to resolve package sources, the global packages folder,
    ///     and package source mapping. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="credentialRegistrar">
    ///     The credential-service registrar to invoke before resolving any source.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The absolute path to the cached package folder inside the global packages folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/>, <paramref name="version"/>,
    ///     <paramref name="settings"/>, or <paramref name="credentialRegistrar"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="version"/> is not a valid NuGet version string.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the package cannot be found in any configured NuGet source.
    /// </exception>
    internal static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        ISettings settings,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        // Validate input parameters before performing any I/O
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialRegistrar);

        // Parse the version string early to validate it and obtain the normalized form;
        // NuGet stores packages using the normalized version (e.g. "1.0" becomes "1.0.0")
        var nugetVersion = NuGetVersion.Parse(version);

        // Resolve the global packages folder from the injected settings
        var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);

        // Compute the expected on-disk path for the package; NuGet stores packages under
        // {globalPackagesFolder}/{packageId.lower}/{normalizedVersion.lower}/
        var packagePath = PackageDownloader.GetPackagePath(globalPackagesFolder, packageId, nugetVersion.ToNormalizedString());

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

        // Register the NuGet credential service, mirroring what the dotnet CLI and MSBuild do
        // internally before performing a restore. Static packageSourceCredentials configured in
        // nuget.config are applied directly to the underlying HttpClientHandler and are honored
        // regardless of whether a credential service is registered. Registration matters for
        // scenarios that need a credential-provider plugin or an ICredentialService-mediated
        // retry (e.g. Azure Artifacts or JFrog credential-provider plugins), which are not
        // exercised by static credentials alone.
        credentialRegistrar.EnsureRegistered();

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

            // Resolve the FindPackageByIdResource, applying v2 fallback as needed
            var (effectiveRepository, resource, resourceError) = await PackageSourceResolver.ResolveAsync(
                sourceRepository, providers, cancellationToken);

            if (resource == null)
            {
                if (resourceError != null)
                {
                    sourceErrors.Add(resourceError);
                }
                continue;
            }

            // Download and install the package from this source
            var result = await PackageDownloader.TryDownloadAsync(
                effectiveRepository,
                resource,
                packageId,
                nugetVersion,
                globalPackagesFolder,
                clientPolicyContext,
                sourceCacheContext,
                cancellationToken);

            if (result.PackagePath != null)
            {
                return result.PackagePath;
            }

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
}
