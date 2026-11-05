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

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     The exception thrown when an operation fails because a package source rejected the request
///     with HTTP 401 (Unauthorized) or 403 (Forbidden).
/// </summary>
/// <remarks>
///     This type derives from <see cref="InvalidOperationException"/>, so a
///     <c>catch (InvalidOperationException)</c> handler also catches it. It is thrown on every
///     <see cref="NuGetCache"/> call, whether or not <see cref="NuGetCacheOptions"/> were supplied,
///     when the whole operation fails (the package was not found, or no versions were found) and at
///     least one source failed with an authentication error. The message is a diagnostic text that
///     never contains credential values.
/// </remarks>
public sealed class NuGetAuthenticationException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NuGetAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="source">The configured name of the package source that rejected the request.</param>
    /// <param name="uri">The URL of the endpoint that rejected the request, when known.</param>
    public NuGetAuthenticationException(string message, string? source, Uri? uri)
        : base(message)
    {
        Source = source;
        Uri = uri;
    }

    /// <summary>
    ///     Gets or sets the configured name of the package source (from <c>nuget.config</c>) that
    ///     rejected the request.
    /// </summary>
    /// <remarks>
    ///     This overrides <see cref="Exception.Source"/> so the property reports the package source
    ///     rather than the originating assembly.
    /// </remarks>
    public override string? Source { get; set; }

    /// <summary>
    ///     Gets the URL of the package source endpoint that rejected the request, or
    ///     <see langword="null"/> when it is not known.
    /// </summary>
    public Uri? Uri { get; }
}
