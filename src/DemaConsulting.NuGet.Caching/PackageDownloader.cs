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
using NuGet.Packaging;
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
    internal readonly record struct TryDownloadResult(string? PackagePath, string? ErrorMessage)
    {
        /// <summary>
        ///     Gets the source that rejected the request with HTTP 401/403, or <see langword="null"/>
        ///     when the failure (if any) was not an authentication failure.
        /// </summary>
        internal AuthFailureInfo? AuthFailure { get; init; }
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
    /// <param name="logger">Optional NuGet logger; when <see langword="null"/>, NuGet messages are discarded.</param>
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
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var identity = new PackageIdentity(packageId, version);
        var source = sourceRepository.PackageSource.Source;
        var sourceName = sourceRepository.PackageSource.Name;

        // Attribute any unsatisfied challenge raised while downloading to this source only; set inline
        // so it flows to the credential service through the awaited NuGet calls
        using var sourceScope = CredentialDispatcher.BeginSource(source);

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
                logger,
                cancellationToken);
        }
        catch (OperationCanceledException ex) when (AuthFailureClassifier.IsUnsatisfiedChallenge(ex, source, cancellationToken)
            && AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, source, out var authMessage, out var authFailure))
        {
            // The credential service had no credentials for the challenge and canceled the request
            // to stop NuGet's retries; report it exactly like the 401 failure it stands for
            return new TryDownloadResult(null, authMessage) { AuthFailure = authFailure };
        }
        catch (NuGetProtocolException ex) when (AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, source, out var authMessage, out var authFailure))
        {
            // An authentication failure (401/403) is actionable during download too - the source
            // exists and the package identity is valid, but the request was rejected for lack of
            // (or incorrect) credentials. Surface this rather than treating it as "package absent".
            return new TryDownloadResult(null, authMessage) { AuthFailure = authFailure };
        }
        catch (NuGetProtocolException ex)
        {
            // Protocol error during the download itself - surface a diagnostic message
            // so the caller can include it in the final not-found exception
            return new TryDownloadResult(null,
                $"{AuthFailureClassifier.RedactUrl(source)}: Protocol error downloading package. ({AuthFailureClassifier.RedactEmbeddedUrls(ex.Message)})");
        }
        catch (HttpRequestException ex) when (AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, source, out var authMessage, out var authFailure))
        {
            // Same as above, but surfaced as a raw HttpRequestException rather than a NuGet
            // protocol exception (observed for direct v3 flat-container content downloads)
            return new TryDownloadResult(null, authMessage) { AuthFailure = authFailure };
        }
        catch (HttpRequestException)
        {
            // Transient network-level failure during download - not actionable, skip silently
            return default;
        }

        // The source confirmed it does not carry this package version, unless NuGet swallowed the
        // request that the dispatcher canceled for an unsatisfied authentication challenge
        if (!found)
        {
            return AuthFailureClassifier.TryDescribeRecordedChallenge(sourceName, source, out var recordedMessage, out var recordedFailure)
                ? new TryDownloadResult(null, recordedMessage) { AuthFailure = recordedFailure }
                : default;
        }

        // Rewind the stream then install the package into the global packages folder.
        // The DownloadResourceResult is disposed automatically by the using declaration.
        packageStream.Seek(0, SeekOrigin.Begin);
        try
        {
            // NuGet skips its own signature verification on Linux/macOS unless an environment variable
            // opts in, so a required signature is verified explicitly here (before anything is installed)
            // to fail closed on every platform
            if (clientPolicyContext.Policy == SignatureValidationMode.Require)
            {
                using (var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true))
                {
                    await VerifySignedAsync(
                        reader,
                        clientPolicyContext,
                        $"Package '{packageId}' version '{version.ToNormalizedString()}' from source '{AuthFailureClassifier.RedactUrl(source)}'",
                        cancellationToken);
                }

                packageStream.Seek(0, SeekOrigin.Begin);
            }

            // NuGet's own install-time verification (Windows) rejects 'require' mode when no trustedSigners are
            // configured. The explicit check above already enforced the contract (signed and valid, allow-list only
            // when configured), so in that case install under an accept policy to avoid a contradictory rejection
            var installPolicy = clientPolicyContext.Policy == SignatureValidationMode.Require
                && clientPolicyContext.AllowList is not { Count: > 0 }
                    ? ClientPolicyContext.GetClientPolicy(NullSettings.Instance, logger)
                    : clientPolicyContext;

            using var downloadResult = await GlobalPackagesFolderUtility.AddPackageAsync(
                sourceRepository.PackageSource.Source,
                identity,
                packageStream,
                globalPackagesFolder,
                Guid.Empty,
                installPolicy,
                logger,
                cancellationToken);
        }
        catch (SignatureException ex)
        {
            // A signature failure is a security failure, not an availability problem, so it is never
            // skipped to the next source: it is reported with a clear message and the NuGet detail
            throw new InvalidOperationException(
                $"Package '{packageId}' version '{version.ToNormalizedString()}' from source '{AuthFailureClassifier.RedactUrl(source)}' failed signature verification: {AuthFailureClassifier.RedactEmbeddedUrls(ex.Message)}",
                ex);
        }

        // Return the conventional package path that NuGet uses on disk
        return new TryDownloadResult(GetPackagePath(globalPackagesFolder, packageId, version.ToNormalizedString()), null);
    }

    /// <summary>
    ///     Creates the client signing policy used when installing packages.
    /// </summary>
    /// <remarks>
    ///     <see cref="ClientPolicyContext"/> has no public constructor, so the policy is always derived from
    ///     NuGet settings. When <paramref name="requireSigned"/> is <see langword="true"/> the settings are
    ///     wrapped so <c>signatureValidationMode</c> reads as <c>require</c> while every other setting
    ///     (notably the <c>trustedSigners</c> allow-list) is read from the original settings unchanged. The
    ///     only <c>config</c> setting the signing policy reads is the mode, so replacing that section is
    ///     sufficient. Nothing is written to the caller's configuration.
    /// </remarks>
    /// <param name="settings">The NuGet settings.</param>
    /// <param name="requireSigned">Whether signed packages are required regardless of the configured mode.</param>
    /// <param name="logger">The NuGet logger.</param>
    /// <returns>The client policy context.</returns>
    internal static ClientPolicyContext CreateClientPolicy(ISettings settings, bool requireSigned, ILogger logger) =>
        ClientPolicyContext.GetClientPolicy(requireSigned ? new RequireSignedSettings(settings) : settings, logger);

    /// <summary>
    ///     Verifies that an already-installed package is validly signed, using the package file stored in
    ///     the global packages folder.
    /// </summary>
    /// <param name="packagePath">The installed package folder.</param>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The package version.</param>
    /// <param name="clientPolicyContext">The client policy (require mode).</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>A task that completes when the package is verified.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the stored package file is missing, unsigned, or its signature is invalid.
    /// </exception>
    internal static async Task VerifyCachedPackageSignedAsync(
        string packagePath,
        string packageId,
        NuGetVersion version,
        ClientPolicyContext clientPolicyContext,
        CancellationToken cancellationToken)
    {
        var normalized = version.ToNormalizedString();
        var nupkgPath = PathHelpers.SafePathCombine(
            packagePath, $"{packageId.ToLowerInvariant()}.{normalized.ToLowerInvariant()}.nupkg");
        var prefix = $"Package '{packageId}' version '{normalized}' in the global packages folder";
        if (!File.Exists(nupkgPath))
        {
            throw new InvalidOperationException(
                $"{prefix} cannot be checked for a signature because its package file is missing; delete the folder to download it again.");
        }

        using var reader = new PackageArchiveReader(nupkgPath);
        await VerifySignedAsync(reader, clientPolicyContext, prefix, cancellationToken);
    }

    /// <summary>
    ///     Explicitly verifies that a package is signed and that its signature is valid under the client
    ///     policy, independent of the platform.
    /// </summary>
    /// <remarks>
    ///     NuGet's own install-time verification is silently skipped on Linux and macOS unless the
    ///     <c>DOTNET_NUGET_SIGNATURE_VERIFICATION</c> environment variable is <c>true</c>
    ///     (<c>CanVerifySignedPackages</c> returns <see langword="false"/>), so relying on it alone would
    ///     accept unsigned packages there. This check does not depend on that switch, so a required
    ///     signature fails closed on every platform. The verification providers mirror those NuGet uses
    ///     (integrity, the client <c>trustedSigners</c> allow-list when any signers are configured, and trust and
    ///     validity). With no <c>trustedSigners</c> configured, any validly signed package whose signature chains
    ///     to a trusted root is accepted; when signers are configured the signer must match one of them. The same
    ///     rules apply to a freshly downloaded package and to one already installed.
    /// </remarks>
    /// <param name="reader">The package reader.</param>
    /// <param name="clientPolicyContext">The client policy (require mode).</param>
    /// <param name="description">Description of the package used in error messages.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>A task that completes when the package is verified.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the package is unsigned or its signature is invalid.</exception>
    private static async Task VerifySignedAsync(
        PackageArchiveReader reader,
        ClientPolicyContext clientPolicyContext,
        string description,
        CancellationToken cancellationToken)
    {
        if (!await reader.IsSignedAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"{description} has no package signature, but signed packages are required (signature verification failed).");
        }

        // The allow-list provider is applied only when trusted signers are configured: a required
        // signature without any trustedSigners accepts any validly signed, trusted package
        var allowList = clientPolicyContext.AllowList;
        var hasAllowList = allowList is { Count: > 0 };
        var untrustedRoots = hasAllowList
            ? allowList
                .Where(e => e.AllowUntrustedRoot)
                .Select(e => new KeyValuePair<string, HashAlgorithmName>(e.Fingerprint, e.FingerprintAlgorithm))
                .ToList()
            : null;
        var providers = new List<ISignatureVerificationProvider>
        {
            new IntegrityVerificationProvider()
        };
        if (hasAllowList)
        {
            providers.Add(new AllowListVerificationProvider(
                allowList,
                true,
                "No trusted signers are configured for signature validation mode 'require'.",
                "The package signature does not match any trusted signer in the client configuration."));
        }

        providers.Add(new SignatureTrustAndValidityVerificationProvider(
            untrustedRoots is { Count: > 0 } ? untrustedRoots : null));
        var verifier = new PackageSignatureVerifier(providers);
        var result = await verifier.VerifySignaturesAsync(reader, clientPolicyContext.VerifierSettings, cancellationToken);
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"{description} failed signature verification.");
        }
    }

    /// <summary>
    ///     A read-only view of NuGet settings in which <c>signatureValidationMode</c> is <c>require</c>.
    /// </summary>
    private sealed class RequireSignedSettings : ISettings
    {
        /// <summary>
        ///     The <c>config</c> section holding only <c>signatureValidationMode=require</c>. NuGet offers no
        ///     public way to construct a settings section in memory, so it is read once from a short-lived
        ///     temporary <c>nuget.config</c> that is deleted immediately; nothing else is ever written.
        /// </summary>
        private static readonly Lazy<SettingSection?> RequireConfigSection = new(
            LoadRequireConfigSection, LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly ISettings _inner;

        private static SettingSection? LoadRequireConfigSection()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dema-nuget-caching-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "nuget.config"),
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><config>"
                    + "<add key=\"signatureValidationMode\" value=\"require\" /></config></configuration>");
                return Settings.LoadSpecificSettings(directory, "nuget.config").GetSection(ConfigurationConstants.Config);
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup of an empty temporary folder; a leftover is harmless
                }
            }
        }

        /// <summary>
        ///     Initializes a new instance wrapping <paramref name="inner"/>.
        /// </summary>
        /// <param name="inner">The settings to wrap.</param>
        internal RequireSignedSettings(ISettings inner) => _inner = inner;

        /// <inheritdoc />
        public event EventHandler? SettingsChanged
        {
            add => _inner.SettingsChanged += value;
            remove => _inner.SettingsChanged -= value;
        }

        /// <inheritdoc />
        public SettingSection? GetSection(string sectionName) =>
            string.Equals(sectionName, ConfigurationConstants.Config, StringComparison.OrdinalIgnoreCase)
                ? RequireConfigSection.Value
                : _inner.GetSection(sectionName);

        /// <inheritdoc />
        public void AddOrUpdate(string sectionName, SettingItem item) =>
            throw new NotSupportedException("These settings are read-only.");

        /// <inheritdoc />
        public void Remove(string sectionName, SettingItem item) =>
            throw new NotSupportedException("These settings are read-only.");

        /// <inheritdoc />
        public void SaveToDisk() =>
            throw new NotSupportedException("These settings are read-only.");

        /// <inheritdoc />
        public IList<string> GetConfigFilePaths() => _inner.GetConfigFilePaths();

        /// <inheritdoc />
        public IList<string> GetConfigRoots() => _inner.GetConfigRoots();
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
