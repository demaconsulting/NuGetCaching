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

using System.Text;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     A user name and secret (password or token) supplied by a
///     <see cref="NuGetCacheOptions.CredentialCallback"/> for a package source.
/// </summary>
/// <remarks>
///     The credential is held in memory only and is never written to disk or to a log. The
///     <see cref="Secret"/> is redacted from <see cref="ToString"/> and from the record's
///     printed members, so accidentally logging an instance does not leak it.
/// </remarks>
public sealed record NuGetCredential
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NuGetCredential"/> record.
    /// </summary>
    /// <param name="userName">The user name presented to the package source.</param>
    /// <param name="secret">The password or access token presented to the package source.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="userName"/> or <paramref name="secret"/> is <see langword="null"/>.
    /// </exception>
    public NuGetCredential(string userName, string secret)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(secret);
        UserName = userName;
        Secret = secret;
    }

    /// <summary>
    ///     Gets the user name presented to the package source.
    /// </summary>
    public string UserName { get; init; }

    /// <summary>
    ///     Gets the password or access token presented to the package source.
    /// </summary>
    /// <remarks>
    ///     This value is redacted from <see cref="object.ToString"/> output.
    /// </remarks>
    public string Secret { get; init; }

    /// <summary>
    ///     Appends the printable members of the record, redacting the <see cref="Secret"/>.
    /// </summary>
    /// <param name="builder">The builder that receives the member text.</param>
    /// <returns><see langword="true"/> because members were appended.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserName = ").Append(UserName).Append(", Secret = ***");
        return true;
    }
}
