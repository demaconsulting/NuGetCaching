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

using System.Net;
using System.Net.Http;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Unit tests for the <see cref="AuthFailureClassifier"/> class.
/// </summary>
/// <remarks>
///     These are pure unit tests with no WireMock server or network I/O: they construct exceptions
///     directly and call <see cref="AuthFailureClassifier.TryDescribeAuthFailure"/> to exercise every
///     detection branch (message-text matching, strongly typed status code, inner-exception chain
///     walking, and non-matching inputs).
/// </remarks>
public class AuthFailureClassifierTests
{
    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> detects a 401 status code embedded in the
    ///     exception message text and returns an actionable diagnostic message.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_401InMessage_ReturnsTrueWithActionableMessage()
    {
        // Arrange - a message shape typical of a NuGet SDK wrapped protocol exception
        var exception = new InvalidOperationException(
            "Response status code does not indicate success: 401 (Unauthorized).");

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert - detected, and the message identifies the source name, URL, and status code
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("test-source", message, StringComparison.Ordinal);
        Assert.Contains("https://example.test/index.json", message, StringComparison.Ordinal);
        Assert.Contains("401", message, StringComparison.Ordinal);
        Assert.Contains("Unauthorized", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> detects a 403 status code embedded in the
    ///     exception message text and returns an actionable diagnostic message.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_403InMessage_ReturnsTrueWithActionableMessage()
    {
        // Arrange
        var exception = new InvalidOperationException(
            "Response status code does not indicate success: 403 (Forbidden).");

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("403", message, StringComparison.Ordinal);
        Assert.Contains("Forbidden", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> returns <see langword="false"/> and a
    ///     <see langword="null"/> message when the exception message contains no recognizable
    ///     401/403 status-code text.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_NoMatchInMessage_ReturnsFalse()
    {
        // Arrange - a generic transient failure message with no status code
        var exception = new InvalidOperationException("Connection refused.");

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> returns <see langword="false"/> when the message
    ///     contains a standalone number that resembles 401/403 but is not immediately followed by the
    ///     expected HTTP reason phrase (e.g. a port number), avoiding a false positive.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_UnrelatedNumberInMessage_ReturnsFalse()
    {
        // Arrange - 401 appears here only as part of an unrelated port number, not a status code
        var exception = new InvalidOperationException("Unable to connect to localhost:40100.");

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> walks the <see cref="Exception.InnerException"/>
    ///     chain to find a 401 status code when the outer exception's own message does not contain it.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_StatusCodeInInnerException_ReturnsTrue()
    {
        // Arrange - the outer exception wraps an inner exception carrying the status code text,
        // mirroring how the NuGet SDK wraps a FatalProtocolException around the root cause
        var inner = new InvalidOperationException(
            "Response status code does not indicate success: 401 (Unauthorized).");
        var outer = new InvalidOperationException("Failed to load package source.", inner);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            outer, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("401", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> walks multiple levels of nested inner exceptions
    ///     to find a 403 status code buried several levels deep in the chain.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_StatusCodeInDeeplyNestedInnerException_ReturnsTrue()
    {
        // Arrange - three levels of nesting, with the status code only in the innermost exception
        var innermost = new InvalidOperationException(
            "Response status code does not indicate success: 403 (Forbidden).");
        var middle = new InvalidOperationException("Middle wrapper.", innermost);
        var outer = new InvalidOperationException("Outer wrapper.", middle);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            outer, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("403", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> returns <see langword="false"/> when neither the
    ///     exception nor any exception in its inner-exception chain contains a 401/403 status code.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_NoMatchInFullChain_ReturnsFalse()
    {
        // Arrange - a chain of exceptions, none of which mention a 401/403 status code
        var inner = new InvalidOperationException("DNS resolution failed.");
        var outer = new InvalidOperationException("Failed to load package source.", inner);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            outer, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }

#if NET5_0_OR_GREATER
    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> detects a 401 status code exposed via the
    ///     strongly typed <see cref="HttpRequestException.StatusCode"/> property, independent of the
    ///     exception message text.
    /// </summary>
    /// <remarks>
    ///     Compiled only for target frameworks where the library's strongly typed
    ///     <c>HttpRequestException.StatusCode</c> detection branch is compiled in (guarded by
    ///     <c>#if !NETSTANDARD2_0</c> in <see cref="AuthFailureClassifier"/>), and where the 3-argument
    ///     <see cref="HttpRequestException"/> constructor accepting an <see cref="HttpStatusCode"/> is
    ///     available (introduced in .NET 5).
    /// </remarks>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithStatusCodeProperty_ReturnsTrue()
    {
        // Arrange - the message text itself carries no recognizable status-code text; only the
        // strongly typed StatusCode property identifies this as a 401
        var exception = new HttpRequestException("The request failed.", null, HttpStatusCode.Unauthorized);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("401", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> detects a 403 status code exposed via the
    ///     strongly typed <see cref="HttpRequestException.StatusCode"/> property.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithForbiddenStatusCodeProperty_ReturnsTrue()
    {
        // Arrange
        var exception = new HttpRequestException("The request failed.", null, HttpStatusCode.Forbidden);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains("403", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> returns <see langword="false"/> when an
    ///     <see cref="HttpRequestException"/> carries an unrelated status code (e.g. 500), which
    ///     is neither a 401 nor a 403 and should be treated as a non-actionable transient failure.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_HttpRequestExceptionWithUnrelatedStatusCodeProperty_ReturnsFalse()
    {
        // Arrange
        var exception = new HttpRequestException("The request failed.", null, HttpStatusCode.InternalServerError);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert
        Assert.False(result);
        Assert.Null(message);
    }
#endif

    /// <summary>
    ///     Tests that <c>TryDescribeAuthFailure</c> includes the original exception's message as
    ///     part of the actionable diagnostic message, so callers retain the full detail while also
    ///     getting the summarized, actionable framing.
    /// </summary>
    [Fact]
    public void AuthFailureClassifier_TryDescribeAuthFailure_MatchFound_MessageIncludesOriginalExceptionText()
    {
        // Arrange
        const string originalText = "Response status code does not indicate success: 401 (Unauthorized).";
        var exception = new InvalidOperationException(originalText);

        // Act
        var result = AuthFailureClassifier.TryDescribeAuthFailure(
            exception, "test-source", "https://example.test/index.json", out var message);

        // Assert - the original exception message text is preserved within the actionable message
        Assert.True(result);
        Assert.NotNull(message);
        Assert.Contains(originalText, message, StringComparison.Ordinal);
    }
}
