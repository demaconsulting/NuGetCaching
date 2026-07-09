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

using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Resolves the <see cref="FindPackageByIdResource"/> for a configured NuGet package source,
///     including the ordered candidate-repository construction needed to support automatic v2
///     OData fallback for feeds configured with a v3 <c>/index.json</c> URL.
/// </summary>
/// <remarks>
///     This class owns source-resolution concerns only; it does not download package content. Once
///     a resource has been resolved, <see cref="PackageDownloader"/> uses it to download and install
///     the package. Authentication-failure diagnosis is delegated to
///     <see cref="AuthFailureClassifier"/> so that logic is not duplicated between resolution and
///     download.
/// </remarks>
internal static class PackageSourceResolver
{
    /// <summary>
    ///     Represents the result of resolving a <see cref="FindPackageByIdResource"/> for a
    ///     configured package source.
    /// </summary>
    /// <param name="Repository">
    ///     The effective <see cref="SourceRepository"/> to use for subsequent operations (may be a
    ///     v2 fallback repository rather than the originally configured one).
    /// </param>
    /// <param name="Resource">
    ///     The resolved <see cref="FindPackageByIdResource"/>, or <see langword="null"/> when
    ///     resolution failed.
    /// </param>
    /// <param name="ErrorMessage">
    ///     A diagnostic message describing a source-level failure (e.g. protocol mismatch or an
    ///     actionable authentication failure), or <see langword="null"/> when the source was
    ///     reachable but simply did not resolve a resource, or when the failure is transient and
    ///     non-actionable.
    /// </param>
    internal readonly record struct PackageSourceResolution(
        SourceRepository Repository,
        FindPackageByIdResource? Resource,
        string? ErrorMessage);

    /// <summary>
    ///     Resolves the <see cref="FindPackageByIdResource"/> for a source repository, with automatic
    ///     v2 OData fallback when a v3 <c>/index.json</c> URL fails with a protocol error.
    /// </summary>
    /// <param name="sourceRepository">The source repository to resolve a resource for.</param>
    /// <param name="providers">NuGet resource providers used when creating a v2 fallback repository.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A <see cref="PackageSourceResolution"/> describing the effective repository (may be a v2
    ///     fallback), the resolved resource (or <see langword="null"/> on failure), and an optional
    ///     diagnostic error message when the failure is actionable.
    /// </returns>
    internal static async Task<PackageSourceResolution> ResolveAsync(
        SourceRepository sourceRepository,
        IEnumerable<Lazy<INuGetResourceProvider>> providers,
        CancellationToken cancellationToken)
    {
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
                    return new PackageSourceResolution(candidate, resource, null);
                }
            }
            catch (HttpRequestException ex) when (AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, candidate.PackageSource.Source, out var authMessage))
            {
                // An authentication failure (401/403) is actionable - it means the source requires
                // credentials that were not supplied or were rejected, not that the source is simply
                // unreachable. Surface this so callers can distinguish it from a transient network
                // failure or a genuine "package not found" outcome. Use the failing candidate's own
                // URL (which may be the v2 fallback, not the originally configured source) so the
                // diagnostic identifies the endpoint that actually rejected the request.
                return new PackageSourceResolution(candidate, null, authMessage);
            }
            catch (HttpRequestException)
            {
                // Transient network-level failure on this candidate - not actionable on its own,
                // but preserve any actionable 401/403 diagnostic already captured from an earlier
                // candidate (e.g. the v3 service index) so a later candidate's unrelated transient
                // failure (e.g. the v2 fallback) doesn't downgrade a real authentication failure
                // into a generic, indistinguishable "not found" result.
                return new PackageSourceResolution(sourceRepository, null, protocolErrorMessage);
            }
            catch (NuGetProtocolException ex) when (AuthFailureClassifier.TryDescribeAuthFailure(ex, sourceName, candidate.PackageSource.Source, out var authMessage))
            {
                // Same as above, but the NuGet SDK wrapped the 401/403 as a protocol exception
                // (e.g. while loading the v3 service index) rather than a raw HttpRequestException.
                // Use the failing candidate's own URL for the same reason as above.
                protocolErrorMessage ??= authMessage;
            }
            catch (NuGetProtocolException ex)
            {
                // Capture the first error message using the failing candidate's own URL (which may
                // be the v2 fallback, not the originally configured source); try the next candidate
                // if available
                protocolErrorMessage ??= $"{candidate.PackageSource.Source}: Failed to load package source. ({ex.Message})";
            }
        }

        return new PackageSourceResolution(sourceRepository, null, protocolErrorMessage);
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
}
