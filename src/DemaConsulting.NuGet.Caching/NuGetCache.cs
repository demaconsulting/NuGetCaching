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

// The public operations are grouped first (so their documentation reads together) and the internal
// test seams and shared cores follow, so same-named overloads are intentionally not adjacent
#pragma warning disable S4136

/// <summary>
///     Static class providing NuGet package caching functionality.
/// </summary>
/// <remarks>
///     This class reads NuGet configuration (sources and global packages folder) from
///     the default NuGet settings, rooted at a caller-supplied (or current working) directory,
///     mirroring the behavior of the <c>dotnet</c> CLI and Visual Studio package restore -
///     including discovery of a project/repo-local <c>nuget.config</c>.
///     This class is stateless — all state is local to each call — and is safe for concurrent use.
/// </remarks>
public static class NuGetCache
{
    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache.
    /// </summary>
    /// <remarks>
    ///     Reads NuGet configuration from the default settings via <c>Settings.LoadDefaultSettings</c>,
    ///     rooted at <paramref name="root"/> (or the current working directory when
    ///     <paramref name="root"/> is <see langword="null"/>), so a project/repo-local
    ///     <c>nuget.config</c> is honored like the <c>dotnet</c> CLI does. When the package version is
    ///     already installed in the global packages folder it is returned immediately without any
    ///     network access (unless <see cref="NuGetCacheOptions.RequireSignedPackages"/> is set, which
    ///     verifies the stored package file). The version is normalized (for example <c>1.0</c> becomes
    ///     <c>1.0.0</c>), and the returned folder follows the NuGet layout
    ///     <c>{globalPackagesFolder}/{lowercase id}/{normalized version}</c>. Every permitted source is
    ///     consulted, local folder feeds included. The <paramref name="options"/> apply only to this call,
    ///     even when other calls with different options run concurrently.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="version">The exact version string (e.g. <c>13.0.3</c>).</param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files (walking up
    ///     through ancestor directories, then falling back to machine/user-wide settings). When
    ///     <see langword="null"/>, defaults to <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// <param name="options">
    ///     The per-call options, or <see langword="null"/> for the defaults (non-interactive, no
    ///     logging, no callback, no timeout, sources from <c>nuget.config</c>, signatures not required).
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The absolute path to the cached package folder inside the global packages folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> or <paramref name="version"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="packageId"/> is empty or white space, <paramref name="version"/>
    ///     is not a valid NuGet version string, or <see cref="NuGetCacheOptions.Sources"/> contains an
    ///     invalid or unknown entry.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="NuGetCacheOptions.AuthenticationTimeout"/> is zero, negative (other
    ///     than <see cref="Timeout.InfiniteTimeSpan"/>) or longer than <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="NuGetAuthenticationException">
    ///     Thrown when the package was not found and a source rejected the request with HTTP 401 or 403.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the package cannot be found in any configured NuGet source, or when
    ///     <see cref="NuGetCacheOptions.RequireSignedPackages"/> is set and the package is not validly signed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled or
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> elapses; cancellation is never swallowed.
    /// </exception>
    public static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        string? root = null,
        NuGetCacheOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Validate everything that needs no I/O before loading settings so invalid input fails fast
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);
        options?.Validate();

        return await EnsureCachedAsync(
            packageId,
            version,
            LoadSettings(root),
            options,
            CredentialServiceRegistrar.DefaultCredentialRegistrar,
            cancellationToken);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package available from the configured sources.
    /// </summary>
    /// <remarks>
    ///     Reads NuGet configuration like <c>EnsureCachedAsync</c>. Only remote (HTTP/HTTPS)
    ///     sources are queried by default; local folder feeds are queried when
    ///     <see cref="NuGetCacheOptions.IncludeLocalSources"/> is set or the source is named in
    ///     <see cref="NuGetCacheOptions.Sources"/>. Other non-HTTP sources and the global packages folder
    ///     are not consulted (see <see cref="GetCachedVersions"/> for the latter). Versions are merged across
    ///     all permitted sources, de-duplicated, and returned as normalized strings in ascending
    ///     semantic-version order, including unlisted versions. A package that no source carries yields an
    ///     empty list. Use <c>ListVersionsAsync</c> for listing and publication details.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="includePrerelease">
    ///     When <see langword="true"/>, prerelease versions are included; otherwise only stable
    ///     versions are returned.
    /// </param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files, or
    ///     <see langword="null"/> for the current working directory.
    /// </param>
    /// <param name="options">The per-call options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     The normalized version strings (e.g. <c>1.0.0</c>, <c>2.1.0-beta.1</c>) in ascending
    ///     order, or an empty list when the package is not found. A version may carry <c>+metadata</c>
    ///     or have four parts, so do not assume strict three-part semantic versions.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="packageId"/> is empty or white space, or
    ///     <see cref="NuGetCacheOptions.Sources"/> contains an invalid or unknown entry.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="NuGetCacheOptions.AuthenticationTimeout"/> is zero, negative (other
    ///     than <see cref="Timeout.InfiniteTimeSpan"/>) or longer than <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="NuGetAuthenticationException">
    ///     Thrown when no versions were found and a source rejected the request with HTTP 401 or 403.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no versions were found and at least one source was unreachable or failed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled or
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> elapses; cancellation is never swallowed.
    /// </exception>
    public static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        string? root = null,
        NuGetCacheOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        options?.Validate();

        return await GetVersionsAsync(
            packageId,
            includePrerelease,
            LoadSettings(root),
            options,
            CredentialServiceRegistrar.DefaultCredentialRegistrar,
            cancellationToken);
    }

    /// <summary>
    ///     Lists the versions of a NuGet package available from the configured sources, with listing
    ///     and publication details.
    /// </summary>
    /// <remarks>
    ///     Sources, filtering, ordering, de-duplication, and failure handling are identical to
    ///     <c>GetVersionsAsync</c>; the difference is the richer result. Listing information comes
    ///     from the source's package metadata (registration) resource. When a source offers none, or the
    ///     metadata request fails for a reason other than authentication, the versions are read from the
    ///     source's flat version list instead and reported with <see cref="NuGetPackageVersion.IsListed"/>
    ///     <see langword="true"/> and <see cref="NuGetPackageVersion.Published"/>
    ///     <see langword="null"/>. When several sources carry a version, the first source in query order
    ///     is reported.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files, or
    ///     <see langword="null"/> for the current working directory.
    /// </param>
    /// <param name="options">The per-call options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>
    ///     One <see cref="NuGetPackageVersion"/> per distinct version in ascending semantic-version order,
    ///     or an empty list when the package is not found.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="packageId"/> is empty or white space, or
    ///     <see cref="NuGetCacheOptions.Sources"/> contains an invalid or unknown entry.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="NuGetCacheOptions.AuthenticationTimeout"/> is zero, negative (other
    ///     than <see cref="Timeout.InfiniteTimeSpan"/>) or longer than <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="NuGetAuthenticationException">
    ///     Thrown when no versions were found and a source rejected the request with HTTP 401 or 403.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no versions were found and at least one source was unreachable or failed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled or
    ///     <see cref="NuGetCacheOptions.AuthenticationTimeout"/> elapses; cancellation is never swallowed.
    /// </exception>
    public static async Task<IReadOnlyList<NuGetPackageVersion>> ListVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        string? root = null,
        NuGetCacheOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        options?.Validate();

        return await QueryVersionsAsync(
            packageId,
            includePrerelease,
            LoadSettings(root),
            options,
            CredentialServiceRegistrar.DefaultCredentialRegistrar,
            includeMetadata: true,
            cancellationToken);
    }

    /// <summary>
    ///     Gets the versions of a NuGet package that are already installed in the global packages folder.
    /// </summary>
    /// <remarks>
    ///     This call is synchronous and offline: it never contacts a package source and never registers a
    ///     credential service. It reads the global packages folder from the default NuGet settings rooted
    ///     at <paramref name="root"/> and returns every installed version (prerelease versions included)
    ///     whose installation is complete, using the same completeness marker as
    ///     <c>EnsureCachedAsync</c>. The package ID is matched case-insensitively.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier (e.g. <c>Newtonsoft.Json</c>).</param>
    /// <param name="root">
    ///     The directory from which to begin discovering <c>nuget.config</c> files, or
    ///     <see langword="null"/> for the current working directory.
    /// </param>
    /// <returns>
    ///     The normalized installed version strings in ascending order, or an empty list when the
    ///     package is not installed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="packageId"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="packageId"/> is empty or white space.
    /// </exception>
    public static IReadOnlyList<string> GetCachedVersions(string packageId, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return GetCachedVersionsCore(packageId, LoadSettings(root));
    }

    /// <summary>
    ///     Test seam: ensures a package is cached using explicit settings and the default registrar.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The exact version string.</param>
    /// <param name="settings">The NuGet settings instance.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path to the cached package folder.</returns>
    internal static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        ISettings settings,
        CancellationToken cancellationToken = default)
    {
        return await EnsureCachedAsync(
            packageId, version, settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, cancellationToken);
    }

    /// <summary>
    ///     Test seam: ensures a package is cached using explicit settings and an explicit registrar.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The exact version string.</param>
    /// <param name="settings">The NuGet settings instance.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The absolute path to the cached package folder.</returns>
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
    ///     Test seam: gets versions using explicit settings and the default registrar.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings instance.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The normalized version strings in ascending order.</returns>
    internal static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        CancellationToken cancellationToken = default)
    {
        return await GetVersionsAsync(
            packageId, includePrerelease, settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, cancellationToken);
    }

    /// <summary>
    ///     Test seam: gets versions using explicit settings and an explicit registrar.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings instance.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The normalized version strings in ascending order.</returns>
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
    ///     The single core of <see cref="EnsureCachedAsync(string, string, string?, NuGetCacheOptions?, CancellationToken)"/>,
    ///     taking explicit NuGet settings and an explicit credential-service registrar.
    /// </summary>
    /// <remarks>
    ///     Injecting <see cref="ISettings"/> lets tests exercise the full download-and-cache behavior against
    ///     a local test server and a dedicated global packages folder; injecting the registrar lets a test
    ///     assert registration without observing process-wide static state. All non-test callers use the
    ///     public method, which passes <see cref="CredentialServiceRegistrar.DefaultCredentialRegistrar"/>.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The exact version string.</param>
    /// <param name="settings">The NuGet settings instance. Must not be <see langword="null"/>.</param>
    /// <param name="options">The per-call options, or <see langword="null"/> for the defaults.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke before resolving any source.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>The absolute path to the cached package folder inside the global packages folder.</returns>
    internal static async Task<string> EnsureCachedAsync(
        string packageId,
        string version,
        ISettings settings,
        NuGetCacheOptions? options,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        // Validate input parameters before performing any I/O
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialRegistrar);
        options?.Validate();

        // Make the per-call options visible to NuGet's process-wide credential service
        var logger = NuGetLoggerAdapter.Create(options?.Logger);
        using var scope = CredentialDispatcher.BeginScope(options, logger);
        ILogger nugetLogger = logger ?? NullLogger.Instance;

        // Parse the version string early to validate it and obtain the normalized form;
        // NuGet stores packages using the normalized version (e.g. "1.0" becomes "1.0.0")
        var nugetVersion = NuGetVersion.Parse(version);
        var requireSigned = options?.RequireSignedPackages == true;

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
            // The signature requirement is the per-call option or signatureValidationMode=require in
            // nuget.config; only when it applies is the policy context built, so ordinary hits stay fast
            if (requireSigned || SettingsUtility.GetSignatureValidationMode(settings) == SignatureValidationMode.Require)
            {
                var cachedPolicy = PackageDownloader.CreateClientPolicy(settings, requireSigned, nugetLogger);
                await PackageDownloader.VerifyCachedPackageSignedAsync(
                    packagePath, packageId, nugetVersion, cachedPolicy, cancellationToken);
            }

            return packagePath;
        }

        // Build the client policy context used for package signing validation on download
        var clientPolicyContext = PackageDownloader.CreateClientPolicy(settings, requireSigned, nugetLogger);

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

        // Determine the sources permitted for this package (explicit sources, or enabled configured
        // sources honoring source mapping); local sources are always consulted when downloading
        var allowedSources = ResolveSources(settings, packageId, options);

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
            scope.ThrowIfTimedOut();
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
            scope.ThrowIfTimedOut();

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

        // An authentication timeout takes priority over any failure NuGet reported instead
        scope.ThrowIfTimedOut();

        // Build the final exception message; append per-source diagnostics when available
        // so callers can identify which sources failed and why (e.g. v2-only feed misconfiguration)
        var baseMessage = $"Package '{packageId}' version '{version}' was not found in any configured NuGet source.";
        var fullMessage = sourceErrors.Count > 0
            ? baseMessage + Environment.NewLine + string.Join(Environment.NewLine, sourceErrors.Select(e => $"  - {e}"))
            : baseMessage;

        throw CreateFailure(fullMessage, authFailure);
    }

    /// <summary>
    ///     The core of <see cref="GetVersionsAsync(string, bool, string?, NuGetCacheOptions?, CancellationToken)"/>,
    ///     taking explicit NuGet settings and an explicit credential-service registrar.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings used to resolve package sources and mapping.</param>
    /// <param name="options">The per-call options, or <see langword="null"/> for the defaults.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke first.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>The normalized version strings in ascending order, or an empty list.</returns>
    internal static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        NuGetCacheOptions? options,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        // The plain version list never needs listing metadata, so the cheap flat list is queried
        var versions = await QueryVersionsAsync(
            packageId, includePrerelease, settings, options, credentialRegistrar, includeMetadata: false, cancellationToken);
        return versions.Select(v => v.Version).ToList();
    }

    /// <summary>
    ///     The core of <c>ListVersionsAsync</c> taking explicit NuGet settings and an explicit
    ///     credential-service registrar.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings used to resolve package sources and mapping.</param>
    /// <param name="options">The per-call options, or <see langword="null"/> for the defaults.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke first.</param>
    /// <param name="cancellationToken">Optional cancellation token for the async operation.</param>
    /// <returns>The versions with listing details in ascending order, or an empty list.</returns>
    internal static async Task<IReadOnlyList<NuGetPackageVersion>> ListVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        NuGetCacheOptions? options,
        ICredentialServiceRegistrar credentialRegistrar,
        CancellationToken cancellationToken = default)
    {
        return await QueryVersionsAsync(
            packageId, includePrerelease, settings, options, credentialRegistrar, includeMetadata: true, cancellationToken);
    }

    /// <summary>
    ///     Gets the installed versions of a package from the global packages folder resolved from
    ///     <paramref name="settings"/>.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="settings">The NuGet settings used to resolve the global packages folder.</param>
    /// <returns>The normalized installed versions in ascending order, or an empty list.</returns>
    internal static IReadOnlyList<string> GetCachedVersionsCore(string packageId, ISettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(settings);

        var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
        var packageFolder = PathHelpers.SafePathCombine(globalPackagesFolder, packageId.ToLowerInvariant());
        if (!Directory.Exists(packageFolder))
        {
            return [];
        }

        // A version folder counts only when NuGet finished installing it (completeness marker)
        var versions = new SortedSet<NuGetVersion>();
        foreach (var directory in Directory.EnumerateDirectories(packageFolder))
        {
            if (File.Exists(PathHelpers.SafePathCombine(directory, ".nupkg.metadata"))
                && NuGetVersion.TryParse(Path.GetFileName(directory), out var parsed))
            {
                versions.Add(parsed);
            }
        }

        return versions.Select(v => v.ToNormalizedString()).ToList();
    }

    /// <summary>
    ///     Shared version-discovery logic for <c>GetVersionsAsync</c> and <c>ListVersionsAsync</c>.
    /// </summary>
    /// <remarks>
    ///     <see cref="NuGetVersion"/> is used internally for semantic ordering and de-duplication. A source
    ///     that fails is skipped so that one unreachable feed does not hide versions available from others.
    ///     When no versions are found and at least one source failed, an exception carrying per-source
    ///     diagnostics is thrown, consistent with <c>EnsureCachedAsync</c>. Cancellation is never swallowed.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="settings">The NuGet settings.</param>
    /// <param name="options">The per-call options, or <see langword="null"/>.</param>
    /// <param name="credentialRegistrar">The credential-service registrar to invoke first.</param>
    /// <param name="includeMetadata">
    ///     Whether to query package metadata (listing, publication) rather than only the flat version list.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions in ascending order.</returns>
    private static async Task<IReadOnlyList<NuGetPackageVersion>> QueryVersionsAsync(
        string packageId,
        bool includePrerelease,
        ISettings settings,
        NuGetCacheOptions? options,
        ICredentialServiceRegistrar credentialRegistrar,
        bool includeMetadata,
        CancellationToken cancellationToken)
    {
        // Validate input parameters before performing any I/O
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialRegistrar);
        options?.Validate();

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
        var allowedSources = ResolveSources(settings, packageId, options);
        var explicitSources = options?.Sources != null;
        var includeLocal = options?.IncludeLocalSources == true;

        // Use a fresh cache context per call so version lists are never served stale
        using var sourceCacheContext = new SourceCacheContext { NoCache = true };

        // A sorted dictionary de-duplicates by semantic equality and keeps ascending order; the first
        // source (in query order) that reports a version wins
        var versions = new SortedDictionary<NuGetVersion, NuGetPackageVersion>();
        var sourceErrors = new List<string>();
        AuthFailureInfo? authFailure = null;

        // Version discovery queries remote (HTTP/HTTPS) sources, plus local folder sources when asked
        // for (or named explicitly); an explicit predicate is used (rather than !IsLocal) so other
        // non-local, non-HTTP sources are ignored, not queried
        foreach (var packageSource in allowedSources.Where(s => s.IsHttp || (s.IsLocal && (includeLocal || explicitSources))))
        {
            // Honor cancellation between sources
            cancellationToken.ThrowIfCancellationRequested();

            var sourceRepository = new SourceRepository(packageSource, providers);

            // Resolve the FindPackageByIdResource, applying v2 fallback as needed
            var resolution = await PackageSourceResolver.ResolveAsync(
                sourceRepository, providers, cancellationToken);
            scope.ThrowIfTimedOut();
            var resource = resolution.Resource;

            if (resource == null)
            {
                authFailure ??= resolution.AuthFailure;
                sourceErrors.Add(resolution.ErrorMessage ?? $"{packageSource.Source}: Source is unreachable or unsupported.");
                continue;
            }

            var effectiveSource = resolution.Repository.PackageSource;
            IReadOnlyList<NuGetPackageVersion> sourceVersions;
            try
            {
                var found = includeMetadata
                    ? await TryGetMetadataVersionsAsync(
                        resolution.Repository, packageId, includePrerelease, sourceCacheContext, nugetLogger, cancellationToken)
                    : null;

                // Without listing metadata, fall back to the flat version list (all listed, no dates)
                sourceVersions = found ?? (await resource.GetAllVersionsAsync(
                        packageId, sourceCacheContext, nugetLogger, cancellationToken))
                    .Select(v => new NuGetPackageVersion(v.ToNormalizedString(), effectiveSource.Name, true, null))
                    .ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or NuGetProtocolException)
            {
                // An authentication timeout takes priority over the failure NuGet reported instead
                scope.ThrowIfTimedOut();

                // Record the failure and try the next source; reported only if nothing is found
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

            foreach (var entry in sourceVersions)
            {
                var parsed = NuGetVersion.Parse(entry.Version);
                if ((includePrerelease || !parsed.IsPrerelease) && !versions.ContainsKey(parsed))
                {
                    versions.Add(parsed, entry);
                }
            }
        }

        // Mirror EnsureCachedAsync: failures matter only when no source supplied any version
        scope.ThrowIfTimedOut();
        if (versions.Count == 0 && sourceErrors.Count > 0)
        {
            throw CreateFailure(
                $"Unable to retrieve versions of package '{packageId}' from the configured NuGet sources."
                + Environment.NewLine
                + string.Join(Environment.NewLine, sourceErrors.Select(e => $"  - {e}")),
                authFailure);
        }

        return versions.Values.ToList();
    }

    /// <summary>
    ///     Reads the versions of a package, with listing details, from a source's package metadata resource.
    /// </summary>
    /// <param name="repository">The effective source repository.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="includePrerelease">Whether prerelease versions are included.</param>
    /// <param name="cacheContext">The source cache context.</param>
    /// <param name="logger">The NuGet logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///     The versions, or <see langword="null"/> when the source offers no metadata resource or the
    ///     metadata request failed for a reason other than authentication (the caller falls back to
    ///     the flat version list). Authentication failures are rethrown.
    /// </returns>
    private static async Task<IReadOnlyList<NuGetPackageVersion>?> TryGetMetadataVersionsAsync(
        SourceRepository repository,
        string packageId,
        bool includePrerelease,
        SourceCacheContext cacheContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var sourceName = repository.PackageSource.Name;
        var source = repository.PackageSource.Source;
        try
        {
            var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
            if (metadataResource == null)
            {
                return null;
            }

            var metadata = await metadataResource.GetMetadataAsync(
                packageId, includePrerelease, includeUnlisted: true, cacheContext, logger, cancellationToken);
            var list = metadata
                .Select(m => new NuGetPackageVersion(m.Identity.Version.ToNormalizedString(), sourceName, m.IsListed, m.Published))
                .ToList();

            // An empty answer (for example no registration endpoint) falls back to the flat version list
            return list.Count == 0 ? null : list;
        }
        catch (Exception ex) when ((ex is HttpRequestException or NuGetProtocolException)
            && !AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, source, out _))
        {
            // Not an authentication failure: the flat version list may still work
            return null;
        }
    }

    /// <summary>
    ///     Loads the default NuGet settings rooted at <paramref name="root"/> (or the current directory).
    /// </summary>
    /// <param name="root">The directory to start discovering <c>nuget.config</c> files from, or <see langword="null"/>.</param>
    /// <returns>The loaded settings.</returns>
    private static ISettings LoadSettings(string? root) =>
        Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory());

    /// <summary>
    ///     Creates the exception for a failed operation: a <see cref="NuGetAuthenticationException"/>
    ///     when a source rejected the request with 401/403, otherwise a plain
    ///     <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="message">The full diagnostic message.</param>
    /// <param name="authFailure">The first authentication failure, or <see langword="null"/>.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException CreateFailure(string message, AuthFailureInfo? authFailure)
    {
        if (authFailure == null)
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
    ///     Resolves the package sources to use for a call.
    /// </summary>
    /// <remarks>
    ///     Without <see cref="NuGetCacheOptions.Sources"/> this is the enabled configured sources permitted
    ///     for the package (honoring package source mapping). With explicit sources, each entry (in order)
    ///     is a configured source name (enabled or not), an absolute HTTP/HTTPS URL, or a rooted local
    ///     path or <c>file:</c> URI; entries equal to a configured source reuse it (so its credentials
    ///     apply) and duplicates are collapsed. Explicit sources bypass package source mapping.
    /// </remarks>
    /// <param name="settings">The NuGet settings to read sources and mapping from.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="options">The per-call options, or <see langword="null"/>.</param>
    /// <returns>The sources to consult, in order.</returns>
    /// <exception cref="ArgumentException">Thrown when an explicit entry cannot be resolved.</exception>
    private static List<PackageSource> ResolveSources(ISettings settings, string packageId, NuGetCacheOptions? options)
    {
        var explicitEntries = options?.Sources;
        if (explicitEntries == null)
        {
            return GetAllowedSources(settings, packageId).ToList();
        }

        var configured = new PackageSourceProvider(settings).LoadPackageSources().ToList();
        var result = new List<PackageSource>();
        foreach (var entry in explicitEntries)
        {
            var source = ResolveExplicitSource(entry.Trim(), configured);
            if (!result.Exists(s => string.Equals(s.Source, source.Source, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(source);
            }
        }

        return result;
    }

    /// <summary>
    ///     Resolves one explicit source entry to a package source.
    /// </summary>
    /// <param name="entry">The trimmed entry.</param>
    /// <param name="configured">The configured sources (enabled and disabled).</param>
    /// <returns>The package source.</returns>
    /// <exception cref="ArgumentException">Thrown when the entry is not a name, URL, or rooted path.</exception>
    private static PackageSource ResolveExplicitSource(string entry, List<PackageSource> configured)
    {
        // A configured source name wins, even when the source is disabled in nuget.config
        var byName = configured.Find(s => string.Equals(s.Name, entry, StringComparison.OrdinalIgnoreCase));
        if (byName != null)
        {
            return byName;
        }

        string location;
        if (entry.IndexOf("://", StringComparison.Ordinal) >= 0 || entry.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            {
                throw InvalidSource(entry);
            }

            location = uri.Scheme switch
            {
                "http" or "https" => entry,
                "file" => uri.LocalPath,
                _ => throw InvalidSource(entry),
            };
        }
        else if (Path.IsPathRooted(entry))
        {
            location = entry;
        }
        else
        {
            throw InvalidSource(entry);
        }

        // Reuse an equal configured source so its credentials and protocol version apply
        var byLocation = configured.Find(s => string.Equals(
            s.Source.TrimEnd('/', '\\'), location.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase));
        return byLocation ?? new PackageSource(location);
    }

    /// <summary>
    ///     Creates the exception for an explicit source entry that cannot be used.
    /// </summary>
    /// <param name="entry">The offending entry.</param>
    /// <returns>The exception to throw.</returns>
    private static ArgumentException InvalidSource(string entry)
    {
        // The offending value is a property of the options, not a parameter, so name it explicitly
#pragma warning disable S3928
        return new ArgumentException(
            $"Source '{entry}' is not a configured source name, an absolute http(s) URL, or a rooted local path.",
            nameof(NuGetCacheOptions.Sources));
#pragma warning restore S3928
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
