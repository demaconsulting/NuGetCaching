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

#if !NETFRAMEWORK
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
#endif
using System.Net.Http;
using NuGet.Configuration;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Tests for <see cref="NuGetCacheOptions.RequireSignedPackages"/>. The test infrastructure produces
///     unsigned packages, and invalidly signed ones by adding a corrupt signature entry. Positive
///     acceptance is covered with a real, unmodified package from nuget.org (Newtonsoft.Json 13.0.1,
///     signed by its author and countersigned by the nuget.org repository) that is downloaded from nuget.org
///     once per test run, verified against a pinned SHA-256 (a mismatch fails the tests) and removed when the
///     run ends. No binary is checked in; when the download fails (offline) the tests are skipped.
///     The signature tests run in a non-parallel collection because they set the
///     <c>NUGET_CERT_REVOCATION_MODE</c> process environment variable (to <c>offline</c>) so that no
///     test depends on network access to certificate revocation services.
/// </summary>
[Collection(SignedFixtureCollection.Name)]
public class NuGetCacheSignatureTests
{
    /// <summary>
    ///     The identifier of the signed fixture package.
    /// </summary>
    private const string SignedId = "Newtonsoft.Json";

    /// <summary>
    ///     The version of the signed fixture package.
    /// </summary>
    private const string SignedVersion = "13.0.1";

    /// <summary>
    ///     SHA-256 fingerprint of the fixture's author signing certificate.
    /// </summary>
    private const string AuthorFingerprint = "A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641";

    /// <summary>
    ///     SHA-256 fingerprint of the fixture's nuget.org repository signing certificate.
    /// </summary>
    private const string RepositoryFingerprint = "5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4";

    /// <summary>
    ///     The nuget.org flat-container URL of the signed package.
    /// </summary>
    private const string SignedPackageUrl =
        "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.1/newtonsoft.json.13.0.1.nupkg";

    /// <summary>
    ///     Pinned SHA-256 of the unmodified Newtonsoft.Json 13.0.1 package. A mismatch fails the tests.
    /// </summary>
    private const string SignedPackageSha256 = "2B6B52556E27E1B7913F33EEDEB95568110C746BD64AFFF74357F1683878323A";

    /// <summary>
    ///     A SHA-256 fingerprint that matches no certificate.
    /// </summary>
    private const string UnknownFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    ///     Tests that a real signed package is accepted when no trusted signers are configured, and is
    ///     fully installed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_SignedPackageDownload_Succeeds()
    {
        using var _ = ArrangeSignedFixture();
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package(SignedId, SignedVersion, await File.ReadAllBytesAsync(SignedFixturePath(), TestContext.Current.CancellationToken));
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var path = await NuGetCache.EnsureCachedAsync(
                SignedId, SignedVersion, settings, new NuGetCacheOptions { RequireSignedPackages = true },
                CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
            Assert.True(File.Exists(Path.Combine(path, "lib", "netstandard2.0", "Newtonsoft.Json.dll")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a real signed package is accepted when its author certificate is a trusted signer.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_TrustedAuthorSignerMatches_Succeeds()
    {
        using var _ = ArrangeSignedFixture();
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package(SignedId, SignedVersion, await File.ReadAllBytesAsync(SignedFixturePath(), TestContext.Current.CancellationToken));
            var settings = CreateSettingsWithSigners(folder, server, AuthorSigner(AuthorFingerprint));

            var path = await NuGetCache.EnsureCachedAsync(
                SignedId, SignedVersion, settings, new NuGetCacheOptions { RequireSignedPackages = true },
                CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a real signed package is accepted when the nuget.org repository certificate is a
    ///     trusted signer.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_TrustedRepositorySignerMatches_Succeeds()
    {
        using var _ = ArrangeSignedFixture();
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package(SignedId, SignedVersion, await File.ReadAllBytesAsync(SignedFixturePath(), TestContext.Current.CancellationToken));
            var settings = CreateSettingsWithSigners(folder, server, RepositorySigner(RepositoryFingerprint));

            var path = await NuGetCache.EnsureCachedAsync(
                SignedId, SignedVersion, settings, new NuGetCacheOptions { RequireSignedPackages = true },
                CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a real signed package is rejected, and not installed, when trusted signers are
    ///     configured and none of them match the package's signers.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_TrustedSignerDoesNotMatch_ThrowsInvalidOperationException()
    {
        using var _ = ArrangeSignedFixture();
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package(SignedId, SignedVersion, await File.ReadAllBytesAsync(SignedFixturePath(), TestContext.Current.CancellationToken));
            var settings = CreateSettingsWithSigners(folder, server, AuthorSigner(UnknownFingerprint));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    SignedId, SignedVersion, settings, new NuGetCacheOptions { RequireSignedPackages = true },
                    CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Contains(SignedId, exception.Message, StringComparison.Ordinal);
            Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(NuGetCache.GetCachedVersionsCore(SignedId, settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that an already-installed real signed package passes the cache-hit check when signatures
    ///     are required.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_SignedPackageAlreadyInstalled_AcceptedOnCacheHit()
    {
        using var _ = ArrangeSignedFixture();
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package(SignedId, SignedVersion, await File.ReadAllBytesAsync(SignedFixturePath(), TestContext.Current.CancellationToken));
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var installed = await NuGetCache.EnsureCachedAsync(SignedId, SignedVersion, settings, null, registrar, CancellationToken.None);
            var again = await NuGetCache.EnsureCachedAsync(
                SignedId, SignedVersion, settings, new NuGetCacheOptions { RequireSignedPackages = true }, registrar, CancellationToken.None);

            Assert.Equal(installed, again);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that an already-installed real signed package is rejected on the cache-hit path when
    ///     trusted signers are configured and none of them match.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_SignedPackageAlreadyInstalled_SignerMismatchRejectedOnCacheHit()
    {
        using var _ = ArrangeSignedFixture();
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package(SignedId, SignedVersion, await File.ReadAllBytesAsync(SignedFixturePath(), TestContext.Current.CancellationToken));
            var openSettings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;
            var installed = await NuGetCache.EnsureCachedAsync(SignedId, SignedVersion, openSettings, null, registrar, CancellationToken.None);

            var gpf = Path.GetFullPath(Path.Combine(installed, "..", ".."));
            var configFolder = Path.Combine(folder, "strict");
            Directory.CreateDirectory(configFolder);
            var strictSettings = CreateSettingsWithSigners(configFolder, server, AuthorSigner(UnknownFingerprint), gpf);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    SignedId, SignedVersion, strictSettings, new NuGetCacheOptions { RequireSignedPackages = true }, registrar, CancellationToken.None));

            Assert.Contains(SignedId, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
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
    ///     Tests that a package carrying a corrupt signature entry is rejected on a fresh download with an
    ///     <see cref="InvalidOperationException"/> and is not left installed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_InvalidlySignedPackageDownload_ThrowsInvalidOperationException()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Invalid.Signed", "1.0.0", AddCorruptSignature(NuGetPackageBuilder.CreateMinimalPackage("Invalid.Signed", "1.0.0")));
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var options = new NuGetCacheOptions { RequireSignedPackages = true };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Invalid.Signed", "1.0.0", settings, options, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Contains("Invalid.Signed", exception.Message, StringComparison.Ordinal);
            Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(NuGetCache.GetCachedVersionsCore("Invalid.Signed", settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that an installed package whose stored file carries a corrupt signature is rejected on the
    ///     cache-hit path when signatures are required.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_RequireSignedPackages_InvalidlySignedPackageAlreadyInstalled_RejectedOnCacheHit()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            var bytes = NuGetPackageBuilder.CreateMinimalPackage("Invalid.Cached", "1.0.0");
            server.RegisterV3Package("Invalid.Cached", "1.0.0", bytes);
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var installed = await NuGetCache.EnsureCachedAsync("Invalid.Cached", "1.0.0", settings, null, registrar, CancellationToken.None);
            await File.WriteAllBytesAsync(
                Path.Combine(installed, "invalid.cached.1.0.0.nupkg"),
                AddCorruptSignature(bytes),
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Invalid.Cached", "1.0.0", settings, new NuGetCacheOptions { RequireSignedPackages = true }, registrar, CancellationToken.None));

            Assert.Contains("Invalid.Cached", exception.Message, StringComparison.Ordinal);
            Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that <c>signatureValidationMode=require</c> in nuget.config is enforced on the cache-hit
    ///     path for an already-installed unsigned package even when the option is not set.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_NuGetConfigRequireMode_UnsignedPackageAlreadyInstalled_RejectedOnCacheHit()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3Package("Unsigned.ConfigCached", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Unsigned.ConfigCached", "1.0.0"));
            var openSettings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;
            var installed = await NuGetCache.EnsureCachedAsync("Unsigned.ConfigCached", "1.0.0", openSettings, null, registrar, CancellationToken.None);

            var gpf = Path.GetFullPath(Path.Combine(installed, "..", ".."));
            var configFolder = Path.Combine(folder, "strict");
            Directory.CreateDirectory(configFolder);
            await File.WriteAllTextAsync(Path.Combine(configFolder, "nuget.config"), $"""
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
            var strictSettings = Settings.LoadSpecificSettings(configFolder, "nuget.config");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await NuGetCache.EnsureCachedAsync(
                    "Unsigned.ConfigCached", "1.0.0", strictSettings, new NuGetCacheOptions(), registrar, CancellationToken.None));

            Assert.Contains("Unsigned.ConfigCached", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Returns the path of the signed package downloaded from nuget.org for this test run. Only valid
    ///     after <see cref="ArrangeSignedFixture"/> has not skipped the test.
    /// </summary>
    /// <returns>The downloaded package path.</returns>
    private static string SignedFixturePath() => SignedDownload.Value.Path!;

    /// <summary>
    ///     Downloads the signed package once per test run (thread-safe), verifies its pinned SHA-256 and
    ///     stores it in a temporary folder that is removed when the process exits. A network failure yields
    ///     a skip reason; a hash mismatch throws, because it would indicate tampering.
    /// </summary>
    private static readonly Lazy<(string? Path, string? SkipReason)> SignedDownload = new(DownloadSignedPackage);

    /// <summary>
    ///     Downloads and verifies the signed package.
    /// </summary>
    /// <returns>The package path, or a skip reason when the download fails.</returns>
    private static (string? Path, string? SkipReason) DownloadSignedPackage()
    {
        byte[] bytes;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            bytes = Task.Run(() => client.GetByteArrayAsync(SignedPackageUrl)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return (null, $"The signed package {SignedId} {SignedVersion} could not be downloaded from nuget.org " +
                          $"(offline or network error): {ex.Message}");
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        var actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        if (!string.Equals(actual, SignedPackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The downloaded {SignedId} {SignedVersion} package has SHA-256 {actual} but {SignedPackageSha256} " +
                "was expected; the package may have been tampered with.");
        }

        var folder = CreateTempFolder();
        var path = Path.Combine(folder, $"{SignedId.ToLowerInvariant()}.{SignedVersion}.nupkg");
        File.WriteAllBytes(path, bytes);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of the temporary download folder
            }
        };
        return (path, null);
    }

    /// <summary>
    ///     Builds a <c>trustedSigners</c> author entry for a certificate fingerprint.
    /// </summary>
    /// <param name="fingerprint">The SHA-256 fingerprint.</param>
    /// <returns>The XML fragment.</returns>
    private static string AuthorSigner(string fingerprint) =>
        $"""<author name="test-author"><certificate fingerprint="{fingerprint}" hashAlgorithm="SHA256" allowUntrustedRoot="false" /></author>""";

    /// <summary>
    ///     Builds a <c>trustedSigners</c> repository entry for a certificate fingerprint.
    /// </summary>
    /// <param name="fingerprint">The SHA-256 fingerprint.</param>
    /// <returns>The XML fragment.</returns>
    private static string RepositorySigner(string fingerprint) =>
        $"""<repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json"><certificate fingerprint="{fingerprint}" hashAlgorithm="SHA256" allowUntrustedRoot="false" /></repository>""";

    /// <summary>
    ///     Loads settings pointing at the test server with a <c>trustedSigners</c> allow-list.
    /// </summary>
    /// <param name="folder">The folder to hold nuget.config (and the packages folder by default).</param>
    /// <param name="server">The test server.</param>
    /// <param name="signersXml">The signer XML fragment.</param>
    /// <param name="globalPackagesFolder">The packages folder, or null to use a folder under <paramref name="folder"/>.</param>
    /// <returns>The settings.</returns>
    private static ISettings CreateSettingsWithSigners(
        string folder, NuGetTestServer server, string signersXml, string? globalPackagesFolder = null)
    {
        var gpf = globalPackagesFolder ?? Path.Combine(folder, "packages");
        File.WriteAllText(Path.Combine(folder, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{gpf}" />
              </config>
              <packageSources>
                <clear />
                <add key="test-source" value="{server.IndexUrl}" />
              </packageSources>
              <trustedSigners>
                {signersXml}
              </trustedSigners>
            </configuration>
            """);
        return Settings.LoadSpecificSettings(folder, "nuget.config");
    }

    /// <summary>
    ///     Prepares a test that uses the signed fixture: skips it when the environment cannot establish
    ///     trust in the fixture's certificate chains, and otherwise sets
    ///     <c>NUGET_CERT_REVOCATION_MODE=offline</c> for the lifetime of the returned scope so that signature
    ///     revocation checks never need network access.
    /// </summary>
    /// <returns>A scope that restores the previous environment value when disposed.</returns>
    private static IDisposable ArrangeSignedFixture()
    {
        var reason = SignedDownload.Value.SkipReason ?? TrustPreflight.Value;
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        return new EnvironmentVariableScope("NUGET_CERT_REVOCATION_MODE", "offline");
    }

    /// <summary>
    ///     One-time check that the operating system can build trusted chains for every certificate used by the
    ///     fixture's signatures (author, repository countersignature and their timestamps). The fixture's
    ///     certificates are long expired, so only the chain to a trusted root is checked, with revocation and
    ///     validity dates ignored. The check is not OS-specific: it fails only when a root the fixture needs is
    ///     really missing, for example when a Linux distribution has dropped the legacy DigiCert Assured ID
    ///     and High Assurance EV roots from its trust store.
    /// </summary>
    private static readonly Lazy<string?> TrustPreflight = new(CheckFixtureTrust);

    /// <summary>
    ///     Checks that every certificate used by the fixture's signatures chains to a trusted root.
    /// </summary>
    /// <returns>A skip reason, or <see langword="null"/> when trust can be established.</returns>
    private static string? CheckFixtureTrust()
    {
#if NETFRAMEWORK
        // .NET Framework runs only on Windows, whose trust store resolves the Microsoft root program roots on
        // demand, so no preflight is needed there (the Pkcs types are also ambiguous on this target)
        return null;
#else
        const string timestampTokenOid = "1.2.840.113549.1.9.16.2.14";
        try
        {
            byte[] signature;
            using (var archive = System.IO.Compression.ZipFile.OpenRead(SignedFixturePath()))
            using (var entryStream = archive.GetEntry(".signature.p7s")!.Open())
            using (var buffer = new MemoryStream())
            {
                entryStream.CopyTo(buffer);
                signature = buffer.ToArray();
            }

            var cms = new SignedCms();
            cms.Decode(signature);
            var primary = cms.SignerInfos[0];
            var signers = new List<SignerInfo> { primary };
            signers.AddRange(primary.CounterSignerInfos.Cast<SignerInfo>());

            var certificates = new List<(string Description, X509Certificate2 Certificate, X509Certificate2Collection Extra)>();
            foreach (var signer in signers)
            {
                if (signer.Certificate is not null)
                {
                    certificates.Add(("signature", signer.Certificate, cms.Certificates));
                }

                foreach (var attribute in signer.UnsignedAttributes.Cast<CryptographicAttributeObject>()
                    .Where(a => a.Oid?.Value == timestampTokenOid))
                {
                    var timestamp = new SignedCms();
                    timestamp.Decode(attribute.Values[0].RawData);
                    var tsCertificate = timestamp.SignerInfos[0].Certificate;
                    if (tsCertificate is not null)
                    {
                        certificates.Add(("timestamp", tsCertificate, timestamp.Certificates));
                    }
                }
            }

            // Every signing certificate must chain to a trusted root. Timestamp chains are held to a weaker
            // standard (at least one must be trusted): NuGet accepts this fixture where the legacy Symantec
            // timestamp root is missing, as long as the DigiCert-rooted timestamp chain is trusted
            string? failure = null;
            var trustedTimestamps = 0;
            var timestampCount = 0;
            foreach (var (description, certificate, extra) in certificates)
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                chain.ChainPolicy.ExtraStore.AddRange(extra);
                chain.Build(certificate);
                var trusted = !chain.ChainStatus.Any(s => s.Status is X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain);
                if (description == "timestamp")
                {
                    timestampCount++;
                    trustedTimestamps += trusted ? 1 : 0;
                }
                else if (!trusted)
                {
                    failure ??= $"the signature certificate '{certificate.Subject}' does not chain to a trusted root";
                }
            }

            if (failure is null && timestampCount > 0 && trustedTimestamps == 0)
            {
                failure = "no timestamp certificate chains to a trusted root";
            }

            return failure is null
                ? null
                : $"The signed fixture package cannot be verified in this environment: {failure}. The fixture needs the " +
                  "DigiCert Assured ID and High Assurance EV root certificates in the operating system trust store.";
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            return $"The signed fixture package signature could not be inspected for the trust preflight: {ex.Message}";
        }
#endif
    }

    /// <summary>
    ///     Temporarily sets a process environment variable.
    /// </summary>
    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EnvironmentVariableScope"/> class.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="value">The temporary value.</param>
        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        /// <inheritdoc />
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }

    /// <summary>
    ///     Adds a corrupt <c>.signature.p7s</c> entry to a package so it reads as signed but cannot verify.
    /// </summary>
    /// <param name="package">The unsigned package bytes.</param>
    /// <returns>The package bytes with a corrupt signature entry.</returns>
    private static byte[] AddCorruptSignature(byte[] package)
    {
        using var stream = new MemoryStream();
        stream.Write(package, 0, package.Length);
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            using var entryStream = archive.CreateEntry(".signature.p7s").Open();
            var garbage = new byte[] { 0x30, 0x03, 0x01, 0x02, 0x03 };
            entryStream.Write(garbage, 0, garbage.Length);
        }

        return stream.ToArray();
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

/// <summary>
///     Non-parallel collection for tests that mutate process-wide environment variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class SignedFixtureCollection
{
    /// <summary>
    ///     The collection name.
    /// </summary>
    public const string Name = "SignedFixture";
}

