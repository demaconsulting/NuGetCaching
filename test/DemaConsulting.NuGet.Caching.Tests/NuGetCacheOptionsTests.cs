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
    ///     Tests that a default <see cref="NuGetCacheOptions"/> equals the 1.5.0 behavior.
    /// </summary>
    [Fact]
    public void NuGetCacheOptions_Default_MatchesVersion150Behavior()
    {
        var options = new NuGetCacheOptions();

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
            await NuGetCache.EnsureCachedAsync("Any.Package", "1.0.0", null, CancellationToken.None, options));
        var versions = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await NuGetCache.GetVersionsAsync("Any.Package", false, null, CancellationToken.None, options));

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
                await NuGetCache.GetVersionsAsync("Any.Package", false, root, CancellationToken.None, good));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await NuGetCache.EnsureCachedAsync("Any.Package", "1.0.0", root, CancellationToken.None, bad));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await NuGetCache.GetVersionsAsync("Any.Package", false, root, CancellationToken.None, bad));
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
    }

    /// <summary>
    ///     Tests that the public options overloads reject null options and null package IDs.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NuGetCache_OptionsOverloads_NullArguments_Throw()
    {
        string? noRoot = null;
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.EnsureCachedAsync("Package", "1.0.0", noRoot, TestContext.Current.CancellationToken, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.GetVersionsAsync("Package", false, noRoot, TestContext.Current.CancellationToken, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await NuGetCache.GetVersionsAsync(null!, false, noRoot, TestContext.Current.CancellationToken, new NuGetCacheOptions()));
    }

    /// <summary>
    ///     Tests that call forms valid in 1.5.0, including a positional <c>default</c> token, still bind
    ///     to the 1.5.0 overloads (a source-compatibility check): the calls compile unambiguously and
    ///     behave as in 1.5.0 against a root whose <c>nuget.config</c> clears all sources, instead of
    ///     failing for missing options.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NuGetCache_Version150CallForms_BindToVersion150Overloads()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
#pragma warning disable S6966 // File.WriteAllTextAsync is unavailable on net481
            File.WriteAllText(
                Path.Combine(root, "nuget.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear /></packageSources></configuration>");
#pragma warning restore S6966

#pragma warning disable xUnit1051 // the default token is the point of this call-form test
            var versions = await NuGetCache.GetVersionsAsync("Package", false, root, default);

            // The 1.5.0 overload reports a missing package as exactly InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await NuGetCache.EnsureCachedAsync("Not.Cached." + Guid.NewGuid().ToString("N"), "1.0.0", root, default));
#pragma warning restore xUnit1051

            Assert.Empty(versions);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Tests that the public options overloads take a required token followed by the options, last.
    /// </summary>
    [Fact]
    public void NuGetCache_OptionsOverloads_Signature_HasRequiredTokenThenOptionsLast()
    {
        var ensure = typeof(NuGetCache).GetMethod(
            "EnsureCachedAsync",
            [typeof(string), typeof(string), typeof(string), typeof(CancellationToken), typeof(NuGetCacheOptions)])!;
        var versions = typeof(NuGetCache).GetMethod(
            "GetVersionsAsync",
            [typeof(string), typeof(bool), typeof(string), typeof(CancellationToken), typeof(NuGetCacheOptions)])!;

        Assert.NotNull(ensure);
        Assert.NotNull(versions);
        Assert.All([ensure, versions], method => Assert.All(method.GetParameters(), p => Assert.False(p.HasDefaultValue)));
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

            // The 1.5.0 overloads keep throwing exactly InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
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
