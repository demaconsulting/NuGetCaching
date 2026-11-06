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

using System;
using System.Text.RegularExpressions;
using System.Threading;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Identifies the package source that rejected a request with HTTP 401 or 403.
/// </summary>
/// <param name="SourceName">The configured name of the package source.</param>
/// <param name="SourceUrl">The URL of the endpoint that rejected the request.</param>
internal sealed record AuthFailureInfo(string SourceName, string SourceUrl)
{
    /// <summary>Gets the source name with any query string, user info or fragment removed.</summary>
    public string SourceName { get; } = AuthFailureClassifier.RedactSourceName(SourceName);
}

/// <summary>
///     Classifies exceptions raised while communicating with a NuGet source as an actionable HTTP
///     401 (Unauthorized) or 403 (Forbidden) authentication failure, or as a non-actionable
///     transient error.
/// </summary>
/// <remarks>
///     This class is pure logic with no I/O: it only inspects exception messages and (where
///     available) strongly typed status-code properties. It is used by both
///     <see cref="PackageSourceResolver"/> (while resolving a <c>FindPackageByIdResource</c>) and
///     <see cref="PackageDownloader"/> (while downloading the <c>.nupkg</c> bytes), so the detection
///     logic is centralized in a single unit rather than duplicated across both call sites.
/// </remarks>
internal static class AuthFailureClassifier
{
    /// <summary>
    ///     Determines whether <paramref name="exception"/> (or any exception in its inner chain) is the
    ///     dispatcher's fail-fast <see cref="CredentialDispatcher.AuthChallengeUnsatisfiedException"/>.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <param name="source">The URL of the package source the failed operation targeted.</param>
    /// <param name="cancellationToken">
    ///     The caller's cancellation token; a cancellation it requested is never reported as an
    ///     authentication failure.
    /// </param>
    /// <returns><see langword="true"/> when an unsatisfied authentication challenge is in the chain.</returns>
    internal static bool IsUnsatisfiedChallenge(Exception exception, string source, CancellationToken cancellationToken)
    {
        // Some runtimes (notably .NET Framework) replace the dispatcher's exception with a plain
        // TaskCanceledException inside NuGet's retry handler; the call's recorded challenge for this
        // source's host identifies it, unless the caller itself canceled the operation
        if (exception is OperationCanceledException
            && !cancellationToken.IsCancellationRequested
            && CredentialDispatcher.FindAmbientUnsatisfiedChallenge(source) != null)
        {
            return true;
        }

        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is CredentialDispatcher.AuthChallengeUnsatisfiedException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the current call recorded an authentication challenge that could not be
    ///     satisfied for the host of <paramref name="source"/>, and if so builds the actionable message.
    /// </summary>
    /// <remarks>
    ///     NuGet's own retry loops (for example while downloading a package from a flat container) can swallow
    ///     the dispatcher's fail-fast exception and report the package as not found, so callers also consult
    ///     this record after such an outcome.
    /// </remarks>
    /// <param name="sourceName">The configured name of the package source.</param>
    /// <param name="source">The URL of the package source.</param>
    /// <param name="message">The actionable diagnostic message when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when an unsatisfied challenge for the source's host was recorded.</returns>
    internal static bool TryDescribeRecordedChallenge(
        string sourceName,
        string source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? message)
    {
        if (CredentialDispatcher.FindAmbientUnsatisfiedChallenge(source) is { } challenge)
        {
            return TryDescribeAuthFailure(
                new CredentialDispatcher.AuthChallengeUnsatisfiedException(challenge), sourceName, source, out message);
        }

        message = null;
        return false;
    }

    /// <summary>
    ///     Redacts a package-source name that may be a URL (NuGet defaults an unnamed source's name to
    ///     its URL), so a query-string secret in it is never surfaced.
    /// </summary>
    /// <param name="sourceName">The source name.</param>
    /// <returns>The name, redacted when it is URL-like or contains a query or fragment delimiter.</returns>
    internal static string RedactSourceName(string sourceName) =>
        sourceName.IndexOfAny(['?', '#', '@']) >= 0 || sourceName.Contains("://", StringComparison.Ordinal)
            ? RedactUrl(sourceName)
            : sourceName;

    /// <summary>
    ///     Returns <paramref name="url"/> without its user info, query string and fragment, which may
    ///     carry secrets (for example a SAS token), so it is safe to log or surface in diagnostics.
    /// </summary>
    /// <param name="url">The URL to redact.</param>
    /// <returns>The redacted URL, or the input when it is not an absolute URI with such parts.</returns>
    internal static string RedactUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Not a parseable URL: drop anything after a query or fragment delimiter
            var index = url.IndexOfAny(['?', '#']);
            return index >= 0 ? url[..index] : url;
        }

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
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
        new(@"\b(401)\s*\(Unauthorized\)|\b(403)\s*\(Forbidden\)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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
    internal static bool TryDescribeAuthFailure(
        Exception exception,
        string sourceName,
        string source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? message)
    {
        if (exception is OperationCanceledException and not CredentialDispatcher.AuthChallengeUnsatisfiedException
            && CredentialDispatcher.FindAmbientUnsatisfiedChallenge(source) is { } recorded)
        {
            exception = new CredentialDispatcher.AuthChallengeUnsatisfiedException(recorded);
        }

        if (!TryGetHttpStatusCode(exception, out var statusCode))
        {
            message = null;
            return false;
        }

        var statusText = statusCode == 403 ? "403 (Forbidden)" : "401 (Unauthorized)";
        message =
            $"{RedactSourceName(sourceName)} ({RedactUrl(source)}): HTTP {statusText} - the source requires authentication or the " +
            $"configured credentials were rejected. Check packageSourceCredentials in nuget.config, or " +
            $"any configured NuGet credential provider, for this source. ({RedactEmbeddedUrls(exception.Message)})";
        return true;
    }

    private static readonly Regex EmbeddedUrlQueryPattern =
        new(@"(https?://[^\s""'<>?#]*)[?#][^\s""'<>]*", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    ///     Removes the query string and fragment from every absolute HTTP(S) URL embedded in
    ///     <paramref name="text"/>, since SDK exception messages quote the requested URL.
    /// </summary>
    internal static string RedactEmbeddedUrls(string text) => EmbeddedUrlQueryPattern.Replace(text, "$1");

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
            // The dispatcher's fail-fast exception stands for an unsatisfied 401 challenge
            if (current is CredentialDispatcher.AuthChallengeUnsatisfiedException)
            {
                statusCode = 401;
                return true;
            }

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
}
