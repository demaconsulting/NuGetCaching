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
    ///     working directory, so repo-local package sources are honored. When the package version is
    ///     already installed in the global packages folder it is returned immediately without any
    ///     network access. The version is normalized (for example <c>1.0</c> becomes <c>1.0.0</c>), and
    ///     the returned folder follows the NuGet layout
    ///     <c>{globalPackagesFolder}/{lowercase id}/{normalized version}</c>.
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
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled; cancellation propagates to the
    ///     caller as <see cref="OperationCanceledException"/> and is never swallowed.
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
    ///     applying the supplied per-call <paramref name="options"/>.
    /// </summary>
    /// <remarks>
    ///     Behaves like <see cref="EnsureCachedAsync(string, string, string?, CancellationToken)"/>, and
    ///     additionally honors <see cref="NuGetCacheOptions"/>: interactive credential providers, a
    ///     logger, an in-memory credential callback, and an authentication timeout. The options apply
    ///     only to this call, even when other calls with different options run concurrently.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files, or
    ///     <see langword="null"/> for the current working directory.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token for the async operation. Required (and ordered before
    ///     <paramref name="options"/>) so that no existing call shape can bind to this overload.
    /// </param>
    /// <param name="options">The per-call options. Must not be <see langword="null"/>.</param>
    /// <returns>
    ///     The absolute path to the cached package folder inside the global packages folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/>, <paramref name="version"/>, or
    ///     <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="version"/> is not a valid NuGet version string.
    /// </exception>
    /// <exception cref="NuGetAuthenticationException">
    ///     Thrown when the package was not found and a source rejected the request with HTTP 401 or 403.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the package cannot be found in any configured NuGet source.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled or
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> elapses.
    /// </exception>
    public static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        string? root,
        CancellationToken cancellationToken,
        NuGetCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return await EnsureCachedAsync(
            packageId,
            version,
            Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory()),
            options,
            CredentialServiceRegistrar.DefaultCredentialRegistrar,
            cancellationToken);
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
        return await EnsureCachedAsync(packageId, version, settings, null, credentialRegistrar, cancellationToken);
    }

    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache,
    ///     using the provided NuGet <paramref name="settings"/>, per-call <paramref name="options"/>,
    ///     and an explicit <paramref name="credentialRegistrar"/>.
    /// </summary>
    /// <remarks>
    ///     All caching logic lives here. A <see langword="null"/> <paramref name="options"/> gives
    ///     exactly the 1.5.0 behavior.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="settings">The NuGet settings instance. Must not be <see langword="null"/>.</param>
    /// <param name="options">The per-call options, or <see langword="null"/> for 1.5.0 behavior.</param>
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
    ///     Thrown when the package cannot be found in any configured NuGet source; a
    ///     <see cref="NuGetAuthenticationException"/> when a source rejected the request with 401/403.
    /// </exception>
    internal static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        ISettings settings,
        NuGetCacheOptions? options,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        // Validate input parameters before performing any I/O
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialRegistrar);

        // Make the per-call options visible to NuGet's process-wide credential service
        var logger = NuGetLoggerAdapter.Create(options?.Logger);
        using var scope = CredentialDispatcher.BeginScope(options, logger);
        ILogger nugetLogger = logger ?? NullLogger.Instance;

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
        var clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, nugetLogger);

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
        WarnWhenOptionsCannotApply(options, logger);

        // Get the core V3 providers needed to communicate with NuGet v3 and v2 feeds
        var providers = Repository.Provider.GetCoreV3();

        // Determine the enabled sources permitted for this package (honoring source mapping)
        var allowedSources = GetAllowedSources(settings, packageId);

        // Accumulate per-source diagnostic messages so they can be included in the
        // final exception when all sources fail - giving callers actionable context
        var sourceErrors = new List<string>();
        AuthFailureInfo? authFailure = null;

        foreach (var packageSource in allowedSources)
        {
            // Build a source repository for this feed using the V3 provider chain
            var sourceRepository = new SourceRepository(packageSource, providers);

            // Resolve the FindPackageByIdResource, applying v2 fallback as needed
            var resolution = await PackageSourceResolver.ResolveAsync(
                sourceRepository, providers, cancellationToken);
            var (effectiveRepository, resource, resourceError) = resolution;
            authFailure ??= resolution.AuthFailure;

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
                cancellationToken,
                nugetLogger);

            if (result.PackagePath != null)
            {
                return result.PackagePath;
            }

            authFailure ??= result.AuthFailure;
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

        throw CreateFailure(fullMessage, authFailure, options);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package available from the configured remote sources.
    /// </summary>
    /// <remarks>
    ///     Reads NuGet configuration from the default settings via <c>Settings.LoadDefaultSettings</c>,
    ///     rooted at <paramref name="root"/> (or the current working directory when
    ///     <paramref name="root"/> is <see langword="null"/>). Only remote (HTTP/HTTPS) sources are
    ///     queried; local folder feeds, other non-HTTP sources, and the global packages folder are not
    ///     consulted. Versions are merged across all
    ///     permitted sources, de-duplicated, and returned as normalized strings in ascending
    ///     semantic-version order. A package that no source carries yields an empty list.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="includePrerelease">
    ///     When <see langword="true"/>, prerelease versions are included; otherwise only stable
    ///     versions are returned.
    /// </param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files. When
    ///     <see langword="null"/>, defaults to <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The normalized version strings (e.g. <c>1.0.0</c>, <c>2.1.0-beta.1</c>) in ascending
    ///     order, or an empty list when the package is not found.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled; cancellation propagates to the
    ///     caller as <see cref="OperationCanceledException"/> and is never swallowed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no versions were found and at least one source was unreachable or failed.
    /// </exception>
    public static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        string? root = null,
        CancellationToken cancellationToken = default)
    {
        // Validate before loading settings so a null package ID fails fast without I/O
        ArgumentNullException.ThrowIfNull(packageId);

        return await GetVersionsAsync(
            packageId, includePrerelease, Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory()), cancellationToken);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package available from the configured remote sources,
    ///     applying the supplied per-call <paramref name="options"/>.
    /// </summary>
    /// <remarks>
    ///     Behaves like <see cref="GetVersionsAsync(string, bool, string?, CancellationToken)"/>, and
    ///     additionally honors <see cref="NuGetCacheOptions"/>. The returned strings are
    ///     NuGet-normalized versions; a version may carry <c>+metadata</c> (build metadata) or have four
    ///     parts (for example <c>1.2.3.4</c>), so do not assume strict three-part semantic versions.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files, or
    ///     <see langword="null"/> for the current working directory.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation token for the async operation. Required (and ordered before
    ///     <paramref name="options"/>) so that no existing call shape can bind to this overload.
    /// </param>
    /// <param name="options">The per-call options. Must not be <see langword="null"/>.</param>
    /// <returns>
    ///     The normalized version strings in ascending order, or an empty list when the package is not found.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="NuGetAuthenticationException">
    ///     Thrown when no versions were found and a source rejected the request with HTTP 401 or 403.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no versions were found and at least one source was unreachable or failed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled or
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> elapses.
    /// </exception>
    public static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        string? root,
        CancellationToken cancellationToken,
        NuGetCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(options);

        return await GetVersionsAsync(
            packageId,
            includePrerelease,
            Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory()),
            options,
            CredentialServiceRegistrar.DefaultCredentialRegistrar,
            cancellationToken);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package available from the sources in the provided
    ///     NuGet <paramref name="settings"/> instance.
    /// </summary>
    /// <remarks>
    ///     This overload exists to support testing with injected settings; it delegates to the
    ///     overload accepting an <see cref="ICredentialServiceRegistrar"/> using
    ///     <see cref="CredentialServiceRegistrar.DefaultCredentialRegistrar"/>.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings used to resolve package sources and mapping.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>The normalized version strings in ascending order, or an empty list.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> or <paramref name="settings"/> is
    ///     <see langword="null"/>.
    /// </exception>
    internal static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        CancellationToken cancellationToken = default)
    {
        return await GetVersionsAsync(
            packageId, includePrerelease, settings, CredentialServiceRegistrar.DefaultCredentialRegistrar, cancellationToken);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package available from the sources in the provided
    ///     NuGet <paramref name="settings"/> instance, using an explicit
    ///     <paramref name="credentialRegistrar"/>.
    /// </summary>
    /// <remarks>
    ///     All version-discovery logic lives here. <see cref="NuGetVersion"/> is used only
    ///     internally for semantic ordering and de-duplication; the result is strings only.
    ///     A source that fails is skipped so that one unreachable feed does not hide versions
    ///     available from others. When no versions are found and at least one source failed,
    ///     an <see cref="InvalidOperationException"/> carrying per-source diagnostics is thrown,
    ///     consistent with <c>EnsureCachedAsync</c>. Cancellation is never swallowed.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings used to resolve package sources and mapping.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke first.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The normalized version strings in ascending order, or an empty list when every queried
    ///     source responds and none carries the package.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/>, <paramref name="settings"/>, or
    ///     <paramref name="credentialRegistrar"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no versions were found and at least one source failed.
    /// </exception>
    internal static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        return await GetVersionsAsync(packageId, includePrerelease, settings, null, credentialRegistrar, cancellationToken);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package available from the sources in the provided
    ///     NuGet <paramref name="settings"/> instance, using per-call <paramref name="options"/> and
    ///     an explicit <paramref name="credentialRegistrar"/>.
    /// </summary>
    /// <remarks>
    ///     All version-discovery logic lives here. A <see langword="null"/>
    ///     <paramref name="options"/> gives exactly the 1.5.0 behavior.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings used to resolve package sources and mapping.</param>
    /// <param name="options">The per-call options, or <see langword="null"/> for 1.5.0 behavior.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke first.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>The normalized version strings in ascending order, or an empty list.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/>, <paramref name="settings"/>, or
    ///     <paramref name="credentialRegistrar"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no versions were found and at least one source failed; a
    ///     <see cref="NuGetAuthenticationException"/> when a source rejected the request with 401/403.
    /// </exception>
    internal static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        NuGetCacheOptions? options,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        // Validate input parameters before performing any I/O
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialRegistrar);

        // Honor a pre-canceled token before any registration or source enumeration
        cancellationToken.ThrowIfCancellationRequested();

        // Make the per-call options visible to NuGet's process-wide credential service
        var logger = NuGetLoggerAdapter.Create(options?.Logger);
        using var scope = CredentialDispatcher.BeginScope(options, logger);
        ILogger nugetLogger = logger ?? NullLogger.Instance;

        // Register the credential service before contacting any source (see EnsureCachedAsync)
        credentialRegistrar.EnsureRegistered();
        WarnWhenOptionsCannotApply(options, logger);

        var providers = Repository.Provider.GetCoreV3();
        var allowedSources = GetAllowedSources(settings, packageId);

        // Use a fresh cache context per call so version lists are never served stale
        using var sourceCacheContext = new SourceCacheContext { NoCache = true };

        // A sorted set de-duplicates by semantic equality and keeps ascending order
        var versions = new SortedSet<NuGetVersion>();
        var sourceErrors = new List<string>();
        AuthFailureInfo? authFailure = null;

        // Version discovery queries remote (HTTP/HTTPS) sources only; an explicit HTTP predicate is
        // used (rather than !IsLocal) so non-local, non-HTTP sources are ignored, not queried
        foreach (var packageSource in allowedSources.Where(s => s.IsHttp))
        {
            // Honor cancellation between sources
            cancellationToken.ThrowIfCancellationRequested();

            var sourceRepository = new SourceRepository(packageSource, providers);

            // Resolve the FindPackageByIdResource, applying v2 fallback as needed
            var resolution = await PackageSourceResolver.ResolveAsync(
                sourceRepository, providers, cancellationToken);
            var resource = resolution.Resource;

            if (resource == null)
            {
                authFailure ??= resolution.AuthFailure;
                sourceErrors.Add(resolution.ErrorMessage ?? $"{packageSource.Source}: Source is unreachable or unsupported.");
                continue;
            }

            IEnumerable<NuGetVersion> sourceVersions;
            try
            {
                sourceVersions = await resource.GetAllVersionsAsync(
                    packageId, sourceCacheContext, nugetLogger, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or NuGetProtocolException)
            {
                // Record the failure and try the next source; reported only if nothing is found
                var effectiveSource = resolution.Repository.PackageSource;
                if (AuthFailureClassifier.TryDescribeAuthFailure(ex, effectiveSource.Name, effectiveSource.Source, out var authMessage))
                {
                    authFailure ??= new AuthFailureInfo(effectiveSource.Name, effectiveSource.Source);
                    sourceErrors.Add(authMessage);
                }
                else
                {
                    sourceErrors.Add($"{packageSource.Source}: Error retrieving versions. ({ex.Message})");
                }
                continue;
            }

            foreach (var version in sourceVersions)
            {
                if (includePrerelease || !version.IsPrerelease)
                {
                    versions.Add(version);
                }
            }
        }

        // Mirror EnsureCachedAsync: failures matter only when no source supplied any version
        if (versions.Count == 0 && sourceErrors.Count > 0)
        {
            throw CreateFailure(
                $"Unable to retrieve versions of package '{packageId}' from the configured NuGet sources."
                + Environment.NewLine
                + string.Join(Environment.NewLine, sourceErrors.Select(e => $"  - {e}")),
                authFailure,
                options);
        }

        return versions.Select(v => v.ToNormalizedString()).ToList();
    }

    /// <summary>
    ///     Creates the exception for a failed operation: a <see cref="NuGetAuthenticationException"/>
    ///     when options were supplied and a source rejected the request with 401/403, otherwise a plain
    ///     <see cref="InvalidOperationException"/> as in 1.5.0.
    /// </summary>
    /// <param name="message">The full diagnostic message.</param>
    /// <param name="authFailure">The first authentication failure, or <see langword="null"/>.</param>
    /// <param name="options">The per-call options; <see langword="null"/> for the 1.5.0 overloads.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException CreateFailure(string message, AuthFailureInfo? authFailure, NuGetCacheOptions? options)
    {
        // Only callers that opted in with options see the richer exception type; the 1.5.0 overloads
        // keep throwing exactly InvalidOperationException so their behavior is unchanged
        if (authFailure == null || options == null)
        {
            return new InvalidOperationException(message);
        }

        Uri.TryCreate(authFailure.SourceUrl, UriKind.Absolute, out var uri);
        return new NuGetAuthenticationException(message, authFailure.SourceName, uri);
    }

    /// <summary>
    ///     Logs a warning when options that need the library's credential dispatcher were supplied
    ///     but another credential service (registered earlier by the host) is in place.
    /// </summary>
    /// <param name="options">The per-call options, or <see langword="null"/>.</param>
    /// <param name="logger">The call's logger, or <see langword="null"/>.</param>
    private static void WarnWhenOptionsCannotApply(NuGetCacheOptions? options, NuGetLoggerAdapter? logger)
    {
        if (logger != null
            && options is { Interactive: true } or { CredentialCallback: not null } or { AuthenticationTimeout: not null }
            && !CredentialServiceRegistrar.IsDispatcherActive)
        {
            logger.LogWarning(
                "A different NuGet credential service is already registered in this process; " +
                "the Interactive, CredentialCallback and AuthenticationTimeout options are not applied.");
        }
    }

    /// <summary>
    ///     Gets the enabled package sources permitted for a package, honoring package source mapping.
    /// </summary>
    /// <param name="settings">The NuGet settings to read sources and mapping from.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <returns>The enabled sources the package may be obtained from.</returns>
    private static IEnumerable<PackageSource> GetAllowedSources(ISettings settings, string packageId)
    {
        // Load package source mapping; when enabled, only sources explicitly mapped to the
        // package ID are permitted - this mirrors nuget.config <packageSourceMapping> behavior
        var packageSourceMapping = PackageSourceMapping.GetPackageSourceMapping(settings);
        var sourceProvider = new PackageSourceProvider(settings);
        var enabledSources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

        // Filter sources by package source mapping when it is configured
        return packageSourceMapping.IsEnabled
            ? enabledSources.Where(s => packageSourceMapping.GetConfiguredPackageSources(packageId).Contains(s.Name))
            : enabledSources;
    }
}
