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

using System.Text.RegularExpressions;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
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
///     This class is stateless — all state is local to each <c>EnsureCachedAsync</c> call — and is
///     safe for concurrent use.
/// </remarks>
public static class NuGetCache
{
    /// <summary>
    ///     Ensures a specific NuGet package version is available in the local global packages cache.
    /// </summary>
    /// <remarks>
    ///     Reads NuGet configuration from the default machine settings via
    ///     <c>Settings.LoadDefaultSettings</c>, mirroring the behavior of the <c>dotnet</c>
    ///     CLI and Visual Studio package restore. All caching logic is delegated to the internal
    ///     overload that accepts an explicit <see cref="ISettings"/> instance.
    /// </remarks>
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
        // Delegate to the overload that accepts an explicit ISettings instance, using the
        // default machine / user NuGet configuration as the settings source
        return await EnsureCachedAsync(packageId, version, Settings.LoadDefaultSettings(null), cancellationToken);
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
        return await EnsureCachedAsync(packageId, version, settings, DefaultCredentialRegistrar, cancellationToken);
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
    ///     <see cref="DefaultCredentialRegistrar"/> - a single static instance shared by every real
    ///     call in the process, preserving the required once-per-process registration semantics.
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
            var (effectiveRepository, resource, resourceError) = await GetFindPackageByIdResourceAsync(
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
            var result = await TryDownloadFromResourceAsync(
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
    ///     Resolves the <see cref="FindPackageByIdResource"/> for a source repository, with automatic
    ///     v2 OData fallback when a v3 <c>/index.json</c> URL fails with a protocol error.
    /// </summary>
    /// <param name="sourceRepository">The source repository to resolve a resource for.</param>
    /// <param name="providers">NuGet resource providers used when creating a v2 fallback repository.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A tuple of the effective <see cref="SourceRepository"/> to use (may be a v2 fallback),
    ///     the resolved <see cref="FindPackageByIdResource"/> (or <see langword="null"/> on failure),
    ///     and an optional diagnostic error message when the failure is actionable.
    /// </returns>
    private static async Task<(SourceRepository Repository, FindPackageByIdResource? Resource, string? ErrorMessage)>
        GetFindPackageByIdResourceAsync(
            SourceRepository sourceRepository,
            IEnumerable<Lazy<INuGetResourceProvider>> providers,
            CancellationToken cancellationToken)
    {
        var originalSource = sourceRepository.PackageSource.Source;
        var sourceName = sourceRepository.PackageSource.Name;
        var candidates = BuildCandidateRepositories(sourceRepository, providers);
        string? protocolErrorMessage = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var resource = await candidate.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                if (resource != null)
                {
                    return (candidate, resource, null);
                }
            }
            catch (HttpRequestException ex) when (TryDescribeAuthFailure(ex, sourceName, originalSource, out var authMessage))
            {
                // An authentication failure (401/403) is actionable - it means the source requires
                // credentials that were not supplied or were rejected, not that the source is simply
                // unreachable. Surface this so callers can distinguish it from a transient network
                // failure or a genuine "package not found" outcome.
                return (sourceRepository, null, authMessage);
            }
            catch (HttpRequestException)
            {
                // Transient network-level failure on this candidate - not actionable on its own,
                // but preserve any actionable 401/403 diagnostic already captured from an earlier
                // candidate (e.g. the v3 service index) so a later candidate's unrelated transient
                // failure (e.g. the v2 fallback) doesn't downgrade a real authentication failure
                // into a generic, indistinguishable "not found" result.
                return (sourceRepository, null, protocolErrorMessage);
            }
            catch (NuGetProtocolException ex) when (TryDescribeAuthFailure(ex, sourceName, originalSource, out var authMessage))
            {
                // Same as above, but the NuGet SDK wrapped the 401/403 as a protocol exception
                // (e.g. while loading the v3 service index) rather than a raw HttpRequestException
                protocolErrorMessage ??= authMessage;
            }
            catch (NuGetProtocolException ex)
            {
                // Capture the first (configured URL's) error message; try the next candidate if available
                protocolErrorMessage ??= $"{originalSource}: Failed to load package source. ({ex.Message})";
            }
        }

        return (sourceRepository, null, protocolErrorMessage);
    }

    /// <summary>
    ///     Builds the ordered list of candidate repositories to try when resolving a
    ///     <see cref="FindPackageByIdResource"/> for a package source.
    /// </summary>
    /// <param name="sourceRepository">The configured source repository.</param>
    /// <param name="providers">NuGet resource providers used when creating the v2 fallback repository.</param>
    /// <returns>
    ///     A single-element list containing <paramref name="sourceRepository"/> when its URL does not
    ///     end in <c>/index.json</c>, or a two-element list of <paramref name="sourceRepository"/>
    ///     followed by a v2 OData fallback repository when it does.
    /// </returns>
    private static IReadOnlyList<SourceRepository> BuildCandidateRepositories(
        SourceRepository sourceRepository,
        IEnumerable<Lazy<INuGetResourceProvider>> providers)
    {
        var source = sourceRepository.PackageSource.Source;

        if (!source.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
        {
            return [sourceRepository];
        }

        // When the configured URL ends in '/index.json', also try the base URL as a v2 OData
        // endpoint. Some feeds (e.g. JFrog Artifactory) expose a v2-only feed at the base URL
        // even though the administrator configured a v3-style '/index.json' URL.
        var baseUrl = source[..^"/index.json".Length];
        var fallbackSource = new PackageSource(baseUrl, sourceRepository.PackageSource.Name)
        {
            Credentials = sourceRepository.PackageSource.Credentials,
        };

        return [sourceRepository, new SourceRepository(fallbackSource, providers)];
    }

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
    private static async Task<TryDownloadResult> TryDownloadFromResourceAsync(
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
        catch (NuGetProtocolException ex) when (TryDescribeAuthFailure(ex, sourceName, source, out var authMessage))
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
        catch (HttpRequestException ex) when (TryDescribeAuthFailure(ex, sourceName, source, out var authMessage))
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
    ///     Matches an HTTP status code embedded in a NuGet SDK exception message, e.g.
    ///     <c>"Response status code does not indicate success: 401 (Unauthorized)."</c>
    /// </summary>
    /// <remarks>
    ///     Requires the status code to be immediately followed by its standard HTTP reason phrase
    ///     in parentheses (e.g. <c>401 (Unauthorized)</c> / <c>403 (Forbidden)</c>) - the exact text
    ///     format emitted for a failed <see cref="HttpRequestException"/> and surfaced through
    ///     NuGet's wrapped protocol exceptions - rather than a bare <c>\b(401|403)\b</c> match, which
    ///     could misclassify unrelated standalone numbers elsewhere in a message (e.g. a port
    ///     number) as an authentication failure.
    /// </remarks>
    private static readonly Regex HttpStatusCodePattern =
        new(@"\b(401)\s*\(Unauthorized\)|\b(403)\s*\(Forbidden\)", RegexOptions.Compiled);

    /// <summary>
    ///     Abstracts NuGet SDK credential-service registration so it can be substituted with a test
    ///     double, letting a test assert that <c>EnsureCachedAsync</c> invokes registration without
    ///     observing or resetting any shared, static process-wide state.
    /// </summary>
    internal interface ICredentialServiceRegistrar
    {
        /// <summary>
        ///     Ensures the NuGet SDK's default credential service is registered.
        /// </summary>
        void EnsureRegistered();
    }

    /// <summary>
    ///     Default <see cref="ICredentialServiceRegistrar"/> implementation that registers the
    ///     NuGet SDK's default credential service, mirroring the setup performed internally by the
    ///     <c>dotnet</c> CLI and MSBuild restore pipeline. Static <c>packageSourceCredentials</c>
    ///     configured in <c>nuget.config</c> are applied directly to the underlying
    ///     <c>HttpClientHandler</c> and are honored on a source's HTTP 401 challenge regardless of
    ///     whether a credential service is registered. Registration instead matters when a NuGet
    ///     credential-provider plugin must be consulted, or an <see cref="ICredentialService"/>-
    ///     mediated retry is required (e.g. for JFrog Artifactory or Azure Artifacts), which static
    ///     credentials alone do not exercise.
    /// </summary>
    /// <remarks>
    ///     Registration work is memoized per instance via <see cref="Lazy{T}"/> with
    ///     <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>, so
    ///     <see cref="EnsureRegistered"/> is cheap and thread-safe to call repeatedly on the same
    ///     instance. <see cref="DefaultCredentialServiceUtility.SetupDefaultCredentialService"/> is
    ///     itself idempotent (it only assigns <c>HttpHandlerResourceV3.CredentialService</c> when it
    ///     is still <see langword="null"/>), but it always re-creates the delegating logger, so
    ///     memoizing avoids that redundant work on every call. A single static instance
    ///     (<see cref="DefaultCredentialRegistrar"/>) is shared by every real (non-test)
    ///     <c>EnsureCachedAsync</c> call in the process, giving the required once-per-process
    ///     registration semantics; a freshly constructed instance (as used by tests) naturally
    ///     starts unregistered.
    /// </remarks>
    private sealed class CredentialServiceRegistrar : ICredentialServiceRegistrar
    {
        private readonly Lazy<bool> _registered = new(
            () =>
            {
                // nonInteractive: true - this is a library used in build tooling, not an
                // interactive CLI, so credential providers must not attempt to show a UI prompt
                DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
                return true;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public void EnsureRegistered() => _ = _registered.Value;
    }

    /// <summary>
    ///     The single, process-wide <see cref="ICredentialServiceRegistrar"/> instance used by every
    ///     real (non-test) <c>EnsureCachedAsync</c> call, giving once-per-process registration
    ///     semantics for the real NuGet SDK credential service.
    /// </summary>
    private static readonly ICredentialServiceRegistrar DefaultCredentialRegistrar = new CredentialServiceRegistrar();

    /// <summary>
    ///     Determines whether <paramref name="exception"/> (or any exception in its
    ///     <see cref="Exception.InnerException"/> chain) represents an HTTP 401 (Unauthorized) or
    ///     403 (Forbidden) response, and if so, builds an actionable diagnostic message identifying
    ///     the source and the authentication failure.
    /// </summary>
    /// <param name="exception">The exception thrown while communicating with the source.</param>
    /// <param name="sourceName">The configured name of the package source (from <c>nuget.config</c>).</param>
    /// <param name="source">The URL of the package source.</param>
    /// <param name="message">
    ///     When this method returns <see langword="true"/>, an actionable diagnostic message
    ///     identifying the source and the HTTP status code; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when a 401 or 403 status code was detected anywhere in the
    ///     exception chain; otherwise <see langword="false"/> (the failure should be treated as a
    ///     non-actionable transient error, preserving prior behavior).
    /// </returns>
    private static bool TryDescribeAuthFailure(
        Exception exception,
        string sourceName,
        string source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? message)
    {
        if (!TryGetHttpStatusCode(exception, out var statusCode))
        {
            message = null;
            return false;
        }

        var statusText = statusCode == 403 ? "403 (Forbidden)" : "401 (Unauthorized)";
        message =
            $"{sourceName} ({source}): HTTP {statusText} - the source requires authentication or the " +
            $"configured credentials were rejected. Check packageSourceCredentials in nuget.config, or " +
            $"any configured NuGet credential provider, for this source. ({exception.Message})";
        return true;
    }

    /// <summary>
    ///     Walks <paramref name="exception"/> and its <see cref="Exception.InnerException"/> chain
    ///     looking for an HTTP 401 or 403 status code.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <param name="statusCode">When found, the detected status code (401 or 403).</param>
    /// <returns><see langword="true"/> when a 401 or 403 status code was found.</returns>
    /// <remarks>
    ///     The NuGet SDK does not consistently expose the failing HTTP status code as a strongly
    ///     typed property: <c>HttpRequestException.StatusCode</c> is only available on
    ///     net5.0+ (not <c>netstandard2.0</c>), and protocol-level failures are frequently wrapped
    ///     in a <c>NuGetProtocolException</c> (e.g. <c>FatalProtocolException</c>) whose message -
    ///     or whose <see cref="Exception.InnerException"/>'s message - simply embeds text such as
    ///     <c>"Response status code does not indicate success: 401 (Unauthorized)."</c>. This method
    ///     checks the strongly typed property where available and falls back to matching that text
    ///     across the whole exception chain so detection is consistent on every target framework.
    /// </remarks>
    private static bool TryGetHttpStatusCode(Exception? exception, out int statusCode)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
#if !NETSTANDARD2_0
            if (current is HttpRequestException { StatusCode: not null } httpRequestException)
            {
                var code = (int)httpRequestException.StatusCode.Value;
                if (code is 401 or 403)
                {
                    statusCode = code;
                    return true;
                }
            }
#endif

            var match = HttpStatusCodePattern.Match(current.Message);
            if (match.Success)
            {
                statusCode = match.Groups[1].Success ? 401 : 403;
                return true;
            }
        }

        statusCode = 0;
        return false;
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
