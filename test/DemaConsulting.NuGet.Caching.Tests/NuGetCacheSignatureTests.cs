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

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Tests for <see cref="NuGetCacheOptions.RequireSignedPackages"/>. Only unsigned packages can be
///     produced by the test infrastructure, so the tests cover rejection of unsigned packages and the
///     unchanged behavior when the option is off.
/// </summary>
public class NuGetCacheSignatureTests
{
    /// <summary>
    ///     Tests that an unsigned package is rejected on a fresh download and is not left installed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_UnsignedPackageDownload_ThrowsInvalidOperationException()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Unsigned.Package", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Unsigned.Package", "1.0.0"));
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var options = new NuGetCacheOptions { RequireSignedPackages = true };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Unsigned.Package", "1.0.0", settings, options, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Contains("Unsigned.Package", exception.Message, StringComparison.Ordinal);
            Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(NuGetCache.GetCachedVersionsCore("Unsigned.Package", settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that an unsigned package is accepted when signatures are not required.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_Off_AcceptsUnsignedPackage()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Unsigned.Accepted", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Unsigned.Accepted", "1.0.0"));
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var path = await NuGetCache.EnsureCachedAsync(
                "Unsigned.Accepted", "1.0.0", settings, new NuGetCacheOptions(), CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that an unsigned package that is already installed passes without the option but is
    ///     rejected on the cache-hit path when signatures are required.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Unsigned.Cached", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Unsigned.Cached", "1.0.0"));
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var installed = await NuGetCache.EnsureCachedAsync("Unsigned.Cached", "1.0.0", settings, null, registrar, CancellationToken.None);
            var again = await NuGetCache.EnsureCachedAsync("Unsigned.Cached", "1.0.0", settings, new NuGetCacheOptions(), registrar, CancellationToken.None);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Unsigned.Cached", "1.0.0", settings, new NuGetCacheOptions { RequireSignedPackages = true }, registrar, CancellationToken.None));

            Assert.Equal(installed, again);
            Assert.Contains("Unsigned.Cached", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a <c>signatureValidationMode=require</c> setting in nuget.config rejects an
    ///     unsigned package on the download path even when the option is not set.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_NuGetConfigRequireMode_UnsignedPackageDownload_ThrowsInvalidOperationException()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Unsigned.Config", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Unsigned.Config", "1.0.0"));
            var gpf = Path.Combine(folder, "packages");
            var configPath = Path.Combine(folder, "nuget.config");
            await File.WriteAllTextAsync(configPath, $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{gpf}" />
                    <add key="signatureValidationMode" value="require" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="test-source" value="{server.IndexUrl}" />
                  </packageSources>
                </configuration>
                """, TestContext.Current.CancellationToken);
            var settings = Settings.LoadSpecificSettings(folder, "nuget.config");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Unsigned.Config", "1.0.0", settings, new NuGetCacheOptions(), CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Contains("Unsigned.Config", exception.Message, StringComparison.Ordinal);
            Assert.Empty(NuGetCache.GetCachedVersionsCore("Unsigned.Config", settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Creates an empty unique temporary folder.
    /// </summary>
    /// <returns>The folder path.</returns>
    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
