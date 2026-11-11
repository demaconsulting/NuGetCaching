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
///     Tests for <see cref="NuGetCacheOptions"/>, <see cref="NuGetCredential"/>,
///     <see cref="NuGetAuthenticationException"/>, and the new options overloads of <c>NuGetCache</c>.
/// </summary>
public class NuGetCacheOptionsTests
{
    /// <summary>
    ///     Tests that a default <see cref="NuGetCacheOptions"/> has every option switched off.
    /// </summary>
    [Fact]
    public void NuGetCacheOptions_Default_HasAllOptionsOff()
    {
        var options = new NuGetCacheOptions();

        Assert.Null(options.Sources);
        Assert.False(options.IncludeLocalSources);
        Assert.False(options.RequireSignedPackages);
        Assert.False(options.Interactive);
        Assert.Null(options.Logger);
        Assert.Null(options.CredentialCallback);
        Assert.Null(options.AuthenticationTimeout);
    }

    /// <summary>
    ///     Tests that valid <see cref="NuGetCacheOptions.AuthenticationTimeout"/> values are accepted.
    /// </summary>
    [Fact]
    public void NuGetCacheOptions_Validate_ValidTimeouts_DoNotThrow()
    {
        new NuGetCacheOptions().Validate();
        new NuGetCacheOptions { AuthenticationTimeout = Timeout.InfiniteTimeSpan }.Validate();
        new NuGetCacheOptions { AuthenticationTimeout = TimeSpan.FromTicks(1) }.Validate();
        new NuGetCacheOptions { AuthenticationTimeout = TimeSpan.FromMilliseconds(int.MaxValue) }.Validate();
    }

    /// <summary>
    ///     Tests that zero, negative (non-infinite) and oversized timeouts are rejected by both options
    ///     overloads before any work starts.
    /// </summary>
    /// <param name="milliseconds">The invalid timeout in milliseconds.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(-1000.0)]
    [InlineData(2147483648.0)]
    public async Task NuGetCache_InvalidAuthenticationTimeout_ThrowsArgumentOutOfRangeException(double milliseconds)
    {
        var options = new NuGetCacheOptions { AuthenticationTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        var ensure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await NuGetCache.EnsureCachedAsync("Any.Package", "1.0.0", null, options, CancellationToken.None));
        var versions = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await NuGetCache.GetVersionsAsync("Any.Package", false, null, options, CancellationToken.None));

        Assert.Equal(nameof(NuGetCacheOptions.AuthenticationTimeout), ensure.ParamName);
        Assert.Equal(nameof(NuGetCacheOptions.AuthenticationTimeout), versions.ParamName);
    }

    /// <summary>
    ///     Tests that an invalid timeout is rejected before settings are loaded: the root contains a
    ///     malformed <c>nuget.config</c> that would make settings loading throw a different exception.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NuGetCache_InvalidAuthenticationTimeout_ValidatedBeforeSettingsLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "nuget-cache-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "nuget.config"), "<configuration><broken", TestContext.Current.CancellationToken);
            var bad = new NuGetCacheOptions { AuthenticationTimeout = TimeSpan.Zero };
            var good = new NuGetCacheOptions();

            // Precondition: settings loading really fails for this root with valid options
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await NuGetCache.GetVersionsAsync("Any.Package", false, root, good, CancellationToken.None));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await NuGetCache.EnsureCachedAsync("Any.Package", "1.0.0", root, bad, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await NuGetCache.GetVersionsAsync("Any.Package", false, root, bad, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await NuGetCache.ListVersionsAsync("Any.Package", false, root, bad, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Tests that the secret is redacted from <c>ToString</c> and the user name is kept.
    /// </summary>
    [Fact]
    public void NuGetCredential_ToString_RedactsSecret()
    {
        var credential = new NuGetCredential("alice", "s3cr3t-value");

        var text = credential.ToString();

        Assert.Contains("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-value", text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
        Assert.Equal("s3cr3t-value", credential.Secret);
    }

    /// <summary>
    ///     Tests that interpolating or formatting the record never exposes the secret.
    /// </summary>
    [Fact]
    public void NuGetCredential_Formatted_DoesNotExposeSecret()
    {
        var credential = new NuGetCredential("alice", "s3cr3t-value");

        var text = $"{credential}";

        Assert.DoesNotContain("s3cr3t-value", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that null arguments are rejected.
    /// </summary>
    [Fact]
    public void NuGetCredential_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new NuGetCredential(null!, "secret"));
        Assert.Throws<ArgumentNullException>(() => new NuGetCredential("user", null!));
    }

    /// <summary>
    ///     Tests that the exception is an <see cref="InvalidOperationException"/> carrying the source and URI.
    /// </summary>
    [Fact]
    public void NuGetAuthenticationException_Properties_AreExposed()
    {
        var uri = new Uri("https://example.test/index.json");

        var exception = new NuGetAuthenticationException("denied", "my-source", uri);

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal("denied", exception.Message);
        Assert.Equal("my-source", exception.Source);
        Assert.Equal(uri, exception.Uri);
        Assert.Null(exception.StatusCode);
        Assert.Null(exception.ServerReason);
    }

    /// <summary>
    ///     Tests that the status code and the server's reason are exposed, and that the reason is reduced to a
    ///     single short line without URL secrets.
    /// </summary>
    [Fact]
    public void NuGetAuthenticationException_StatusAndReason_AreExposedAndSanitized()
    {
        var exception = new NuGetAuthenticationException(
            "denied", "my-source", new Uri("https://example.test/index.json"), 403,
            "You need ReadPackages.\r\nSee https://user:pw@example.test/p?sig=Secret123");

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("You need ReadPackages. See https://example.test/p", exception.ServerReason);
    }

    /// <summary>
    ///     Tests that the public methods reject null or blank package IDs (and null versions).
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NuGetCache_PublicMethods_InvalidArguments_Throw()
    {
        var token = TestContext.Current.CancellationToken;
        string? noRoot = null;
        NuGetCacheOptions? noOptions = null;
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync(null!, "1.0.0", noRoot, noOptions, token));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await NuGetCache.EnsureCachedAsync(" ", "1.0.0", noRoot, noOptions, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync("Package", null!, noRoot, noOptions, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.GetVersionsAsync(null!, false, noRoot, noOptions, token));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await NuGetCache.GetVersionsAsync("", false, noRoot, noOptions, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.ListVersionsAsync(null!, false, noRoot, noOptions, token));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await NuGetCache.ListVersionsAsync("\t", false, noRoot, noOptions, token));
        Assert.Throws<ArgumentNullException>(() => NuGetCache.GetCachedVersions(null!));
        Assert.Throws<ArgumentException>(() => NuGetCache.GetCachedVersions(" "));
    }

    /// <summary>
    ///     Tests that the public methods have a single shape with optional root, options and token,
    ///     the options coming before the token.
    /// </summary>
    [Fact]
    public void NuGetCache_PublicMethods_Signature_HasOptionalRootOptionsThenToken()
    {
        var methods = typeof(NuGetCache).GetMethods().Where(m => m.IsStatic && m.Name != "GetCachedVersions").ToList();
        var ensure = Assert.Single(methods, m => m.Name == "EnsureCachedAsync");
        var versions = Assert.Single(methods, m => m.Name == "GetVersionsAsync");
        var list = Assert.Single(methods, m => m.Name == "ListVersionsAsync");

        foreach (var method in new[] { ensure, versions, list })
        {
            var parameters = method.GetParameters();
            var count = parameters.Length;
            Assert.Equal(typeof(CancellationToken), parameters[count - 1].ParameterType);
            Assert.Equal(typeof(NuGetCacheOptions), parameters[count - 2].ParameterType);
            Assert.Equal(typeof(string), parameters[count - 3].ParameterType);
            Assert.All(parameters.Skip(count - 3), p => Assert.True(p.HasDefaultValue));
        }

        Assert.Equal(5, ensure.GetParameters().Length);
        Assert.Equal(5, versions.GetParameters().Length);
        Assert.True(versions.GetParameters()[1].HasDefaultValue);
        Assert.Equal(5, list.GetParameters().Length);
    }

    /// <summary>
    ///     Tests that the options overloads invoke credential-service registration exactly once.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_WithOptions_InvokesCredentialServiceRegistrar()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Options.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Options.Package", "1.0.0"));
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = new CountingRegistrar();

            await NuGetCache.EnsureCachedAsync(
                "Options.Package", "1.0.0", settings, new NuGetCacheOptions(), registrar, CancellationToken.None);

            Assert.Equal(1, registrar.Count);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that a 401 without any credentials surfaces as <see cref="NuGetAuthenticationException"/>
    ///     from both <c>EnsureCachedAsync</c> and <c>GetVersionsAsync</c>, with the source identified.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_UnauthorizedWithoutCredentials_ThrowsNuGetAuthenticationException()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Denied.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Denied.Package", "1.0.0"), "user", "password");
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var options = new NuGetCacheOptions();
            var ensure = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Denied.Package", "1.0.0", settings, options, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));
            var versions = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.GetVersionsAsync(
                    "Denied.Package", false, settings, options, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            // Without any options the exception type is the same
            await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.EnsureCachedAsync("Denied.Package", "1.0.0", settings, CancellationToken.None));

            Assert.Equal("test-source", ensure.Source);
            Assert.NotNull(ensure.Uri);
            Assert.Equal("test-source", versions.Source);
            Assert.Contains("401", ensure.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     A registrar test double that counts registrations.
    /// </summary>
    private sealed class CountingRegistrar : ICredentialServiceRegistrar
    {
        /// <summary>
        ///     Gets the number of registrations requested.
        /// </summary>
        public int Count { get; private set; }

        /// <inheritdoc />
        public void EnsureRegistered() => Count++;
    }
}
