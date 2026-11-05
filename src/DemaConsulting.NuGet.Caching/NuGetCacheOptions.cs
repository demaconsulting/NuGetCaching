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
///     Per-call options for the <see cref="NuGetCache"/> overloads that accept them.
/// </summary>
/// <remarks>
///     <para>
///         All properties are optional. A default instance behaves exactly like the overloads that
///         take no options (non-interactive credential providers, no logging, no callback, no
///         timeout).
///     </para>
///     <para>
///         The options apply only to the call they are passed to, even when several calls with
///         different options run concurrently. They are carried to NuGet's credential handling
///         with <see cref="System.Threading.AsyncLocal{T}"/>, which NuGet's process-wide credential
///         service cannot see otherwise. If the host application already registered its own
///         NuGet credential service before the first call, that service stays in place and
///         <see cref="Interactive"/> and <see cref="CredentialCallback"/> are not applied.
///     </para>
/// </remarks>
public sealed class NuGetCacheOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether NuGet credential-provider plugins may prompt the
    ///     user (for example with a device-code or browser sign-in).
    /// </summary>
    /// <value>The default is <see langword="false"/>, the 1.5.0 behavior.</value>
    public bool Interactive { get; set; }

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
    ///     <see cref="NuGetAuthenticationException"/>, even if NuGet retries or wraps the failed request). The
    ///     token passed to the callback is canceled, but a callback that ignores its token is
    ///     abandoned rather than interrupted. A credential-provider plugin process is owned by NuGet,
    ///     not by this library, and may stay alive after a timeout until NuGet reclaims it; an
    ///     interactive prompt it opened may therefore remain visible.
    /// </remarks>
    public TimeSpan? AuthenticationTimeout { get; set; }
}
