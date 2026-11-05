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

using Microsoft.Extensions.Logging;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Per-call options for the <see cref="NuGetCache"/> methods.
/// </summary>
/// <remarks>
///     <para>
///         All properties are optional. A default instance behaves exactly like passing no options
///         (non-interactive credential providers, no logging, no callback, no timeout, sources from
///         <c>nuget.config</c>, signatures not required).
///     </para>
///     <para>
///         The options apply only to the call they are passed to, even when several calls with
///         different options run concurrently. They are carried to NuGet's credential handling
///         with <see cref="System.Threading.AsyncLocal{T}"/>, which NuGet's process-wide credential
///         service cannot see otherwise. If the host application already registered its own
///         NuGet credential service before the first call, that service stays in place and
///         <see cref="Interactive"/>, <see cref="CredentialCallback"/> and
///         <see cref="AuthenticationTimeout"/> are not applied.
///     </para>
/// </remarks>
public sealed class NuGetCacheOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether NuGet credential-provider plugins may prompt the
    ///     user (for example with a device-code or browser sign-in).
    /// </summary>
    /// <value>The default is <see langword="false"/>.</value>
    public bool Interactive { get; set; }

    /// <summary>
    ///     Gets or sets the explicit package sources to use instead of the sources discovered from
    ///     <c>nuget.config</c>.
    /// </summary>
    /// <value>The default is <see langword="null"/>, which uses the discovered, enabled sources.</value>
    /// <remarks>
    ///     <para>
    ///         Each entry, consulted in order, is the name of a source configured in <c>nuget.config</c>
    ///         (case-insensitive; naming a disabled source enables it for this call), an absolute
    ///         HTTP/HTTPS URL, or a rooted local or UNC folder path (or <c>file:</c> URI). An entry equal to a
    ///         configured source reuses that source, so its <c>packageSourceCredentials</c> apply.
    ///         Duplicates are collapsed. Explicit sources bypass <c>packageSourceMapping</c>, because the
    ///         caller named them deliberately. Credentials, <see cref="CredentialCallback"/> and
    ///         <see cref="Interactive"/> work as usual.
    ///     </para>
    ///     <para>
    ///         An entry that is empty, white space, or malformed makes the call throw
    ///         <see cref="ArgumentException"/> before any work starts; an entry that is not a configured name,
    ///         URL, or rooted path (including a relative path) throws <see cref="ArgumentException"/> once
    ///         the settings are loaded. An empty list is valid and means no sources.
    ///         The list does not apply to <see cref="NuGetCache.GetCachedVersions"/>.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<string>? Sources { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether version listing also queries local folder sources.
    /// </summary>
    /// <value>The default is <see langword="false"/>.</value>
    /// <remarks>
    ///     This affects <c>GetVersionsAsync</c> and <c>ListVersionsAsync</c>
    ///     for discovered sources; local folder and UNC feeds are skipped unless it is set. Local feeds
    ///     named in <see cref="Sources"/> are always queried. <c>EnsureCachedAsync</c>
    ///     always consults local sources, whatever this value is.
    /// </remarks>
    public bool IncludeLocalSources { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether <c>EnsureCachedAsync</c> requires
    ///     the package to be validly signed.
    /// </summary>
    /// <value>
    ///     The default is <see langword="false"/>, which follows the <c>signatureValidationMode</c> from
    ///     <c>nuget.config</c> (NuGet's default accepts unsigned packages).
    /// </value>
    /// <remarks>
    ///     When set, a package that is unsigned or whose signature is invalid is rejected with
    ///     <see cref="InvalidOperationException"/> (the inner exception carries NuGet's signature error)
    ///     instead of being installed; a package already in the global packages folder is verified from its
    ///     stored package file, which costs some local I/O. A signature failure is never skipped to the next
    ///     source. Trusted-signer configuration in <c>nuget.config</c> applies when downloading. The signature
    ///     check follows NuGet's platform support for signature verification.
    /// </remarks>
    public bool RequireSignedPackages { get; set; }

    /// <summary>
    ///     Gets or sets the logger that receives NuGet diagnostic messages for the call.
    /// </summary>
    /// <value>The default is <see langword="null"/>, which discards NuGet messages.</value>
    /// <remarks>
    ///     NuGet message levels are mapped to <see cref="LogLevel"/> as: Debug to Debug, Verbose to
    ///     Trace, Information to Information, Minimal to Information, Warning to Warning and Error
    ///     to Error. Credential values are never logged.
    /// </remarks>
    public ILogger? Logger { get; set; }

    /// <summary>
    ///     Gets or sets a callback that supplies credentials when a package source answers HTTP 401
    ///     or 403.
    /// </summary>
    /// <value>The default is <see langword="null"/>, which means no callback.</value>
    /// <remarks>
    ///     <para>
    ///         The callback is consulted before any NuGet credential provider. It receives the URI
    ///         that NuGet was challenged on (usually the source URL or a path below it) and a
    ///         cancellation token. Returning <see langword="null"/> falls through to the credential
    ///         providers.
    ///     </para>
    ///     <para>
    ///         If the same URI is challenged again within the same call after the callback already
    ///         supplied a credential, the credential was rejected, and the callback is not
    ///         consulted again; no credential is supplied, so the call fails instead of looping.
    ///         Credentials returned by the callback are held in memory only and are never persisted.
    ///     </para>
    /// </remarks>
    public Func<Uri, CancellationToken, Task<NuGetCredential?>>? CredentialCallback { get; set; }

    /// <summary>
    ///     Gets or sets the maximum time a single credential exchange (the callback or a credential
    ///     provider) may take.
    /// </summary>
    /// <value>The default is <see langword="null"/>, which means no limit.</value>
    /// <remarks>
    ///     When the limit is exceeded the call throws <see cref="OperationCanceledException"/> (never
    ///     <see cref="NuGetAuthenticationException"/>, even if NuGet retries or wraps the failed request).
    ///     The value must be greater than <see cref="TimeSpan.Zero"/> and at most
    ///     <see cref="int.MaxValue"/> milliseconds (about 24.8 days), or <see cref="Timeout.InfiniteTimeSpan"/>
    ///     (equivalent to <see langword="null"/>, no limit). Any other value makes the
    ///     <see cref="NuGetCache"/> call throw <see cref="ArgumentOutOfRangeException"/> before any work
    ///     starts. The
    ///     token passed to the callback is canceled, but a callback that ignores its token is
    ///     abandoned rather than interrupted. A credential-provider plugin process is created by NuGet
    ///     (without a cancellation token) and owned by NuGet's process-wide plugin manager, not by this library:
    ///     canceling or timing out a credential request does not terminate it. The plugin exits on NuGet's
    ///     idle timeout (30 minutes by default; set the <c>NUGET_PLUGIN_IDLE_TIMEOUT_IN_SECONDS</c>
    ///     environment variable to shorten it), when it notices that this process has exited, or at process
    ///     end; an interactive prompt it opened may therefore remain visible after a timeout. This library
    ///     does not dispose the shared plugin manager, because NuGet cannot recreate it and later calls
    ///     would break.
    /// </remarks>
    public TimeSpan? AuthenticationTimeout { get; set; }

    /// <summary>
    ///     Validates the structure of <see cref="Sources"/> (no I/O): entries must be non-empty and, when
    ///     they carry a scheme, be a well-formed absolute HTTP, HTTPS, or file URI.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when an entry is structurally invalid.</exception>
    private void ValidateSources()
    {
        if (Sources == null)
        {
            return;
        }

        foreach (var entry in Sources)
        {
            // The offending value is a property, not a parameter, so name it explicitly
#pragma warning disable S3928
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new ArgumentException("Sources must not contain null, empty, or white-space entries.", nameof(Sources));
            }

            var trimmed = entry.Trim();
            if ((trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                && !(Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                    && (uri.Scheme is "http" or "https" or "file")))
            {
                throw new ArgumentException(
                    $"Source '{entry}' is not a well-formed absolute http, https, or file URI.", nameof(Sources));
            }
#pragma warning restore S3928
        }
    }

    /// <summary>
    ///     Validates the option values.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when <see cref="Sources"/> contains a null, empty, white-space, or malformed entry.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="AuthenticationTimeout"/> is zero, negative (other than
    ///     <see cref="Timeout.InfiniteTimeSpan"/>) or longer than <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    internal void Validate()
    {
        ValidateSources();

        var timeout = AuthenticationTimeout;
        if (timeout == null || timeout.Value == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        if (timeout.Value <= TimeSpan.Zero || timeout.Value.TotalMilliseconds > int.MaxValue)
        {
            // The offending value is a property, not a parameter, so name it explicitly
#pragma warning disable S3928
            throw new ArgumentOutOfRangeException(
                nameof(AuthenticationTimeout),
                timeout.Value,
                "AuthenticationTimeout must be greater than zero and at most Int32.MaxValue milliseconds, or Timeout.InfiniteTimeSpan.");
#pragma warning restore S3928
        }
    }
}
