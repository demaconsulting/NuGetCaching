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

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Unit tests for the <see cref="NuGetCredential"/> record.
/// </summary>
public class NuGetCredentialTests
{
    private const string UserName = "test-user";
    private const string Secret = "s3cr3t-value";

    /// <summary>
    ///     Tests that <c>ToString</c> includes the user name but redacts the secret.
    /// </summary>
    [Fact]
    public void NuGetCredential_ToString_IncludesUserNameAndRedactsSecret()
    {
        // Arrange
        var credential = new NuGetCredential(UserName, Secret);

        // Act
        var text = credential.ToString();

        // Assert
        Assert.Contains(UserName, text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that string interpolation (which calls <c>ToString</c>) never exposes the secret.
    /// </summary>
    [Fact]
    public void NuGetCredential_Interpolation_DoesNotExposeSecret()
    {
        // Arrange
        var credential = new NuGetCredential(UserName, Secret);

        // Act
        var text = $"credential: {credential}";

        // Assert
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that the properties return the values supplied to the constructor.
    /// </summary>
    [Fact]
    public void NuGetCredential_Constructor_ExposesSuppliedValues()
    {
        // Act
        var credential = new NuGetCredential(UserName, Secret);

        // Assert
        Assert.Equal(UserName, credential.UserName);
        Assert.Equal(Secret, credential.Secret);
    }

    /// <summary>
    ///     Tests that two credentials with identical values are equal and share a hash code.
    /// </summary>
    [Fact]
    public void NuGetCredential_Equality_SameValues_AreEqual()
    {
        // Arrange
        var first = new NuGetCredential(UserName, Secret);
        var second = new NuGetCredential(UserName, Secret);

        // Assert
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>
    ///     Tests that credentials differing in user name or secret are not equal.
    /// </summary>
    [Fact]
    public void NuGetCredential_Equality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var credential = new NuGetCredential(UserName, Secret);

        // Assert
        Assert.NotEqual(credential, new NuGetCredential("other-user", Secret));
        Assert.NotEqual(credential, new NuGetCredential(UserName, "other-secret"));
    }

    /// <summary>
    ///     Tests that a null user name is rejected.
    /// </summary>
    [Fact]
    public void NuGetCredential_Constructor_NullUserName_ThrowsArgumentNullException()
    {
        // Act and Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new NuGetCredential(null!, Secret));
        Assert.Equal("userName", exception.ParamName);
    }

    /// <summary>
    ///     Tests that a null secret is rejected.
    /// </summary>
    [Fact]
    public void NuGetCredential_Constructor_NullSecret_ThrowsArgumentNullException()
    {
        // Act and Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new NuGetCredential(UserName, null!));
        Assert.Equal("secret", exception.ParamName);
    }
}
