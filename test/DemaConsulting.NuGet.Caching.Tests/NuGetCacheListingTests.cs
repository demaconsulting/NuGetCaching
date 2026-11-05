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
///     Tests for <c>GetCachedVersions</c>, <see cref="NuGetCacheOptions.IncludeLocalSources"/>,
///     <c>ListVersionsAsync</c> and <see cref="NuGetPackageVersion"/>.
/// </summary>
public class NuGetCacheListingTests
{
    /// <summary>
    ///     Tests that only completely installed versions are returned, sorted, case-insensitively by ID.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetCachedVersions_InstalledVersions_ReturnsCompleteOnesSorted()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var packageFolder = Path.Combine(folder, "cached.package");
            CreateInstalledVersion(packageFolder, "10.0.0", complete: true);
            CreateInstalledVersion(packageFolder, "2.0.0-beta", complete: true);
            CreateInstalledVersion(packageFolder, "2.0.0", complete: true);
            CreateInstalledVersion(packageFolder, "3.0.0", complete: false);
            CreateInstalledVersion(packageFolder, "not-a-version", complete: true);

            var versions = NuGetCache.GetCachedVersionsCore("Cached.Package", settings);

            Assert.Equal(["2.0.0-beta", "2.0.0", "10.0.0"], versions);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a package that is not installed yields an empty list and no network access occurs.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetCachedVersions_NotInstalled_ReturnsEmptyWithoutNetworkAccess()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var versions = NuGetCache.GetCachedVersionsCore("Missing.Package", settings);

            Assert.Empty(versions);
            Assert.Empty(server.LogEntries);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that the public method reads the global packages folder from a <c>nuget.config</c>
    ///     discovered under the supplied root.
    /// </summary>
    [Fact]
    public void NuGetCache_GetCachedVersions_RootWithConfig_ReadsConfiguredGlobalPackagesFolder()
    {
        var root = CreateTempFolder();
        try
        {
            var packages = Path.Combine(root, "packages");
            CreateInstalledVersion(Path.Combine(packages, "root.package"), "1.2.3", complete: true);
#pragma warning disable S6966 // File.WriteAllTextAsync is unavailable on net481
            File.WriteAllText(
                Path.Combine(root, "nuget.config"),
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><config><add key=\"globalPackagesFolder\" value=\"{packages}\" /></config></configuration>");
#pragma warning restore S6966

            var versions = NuGetCache.GetCachedVersions("Root.Package", root);

            Assert.Equal(["1.2.3"], versions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that local sources are skipped by version listing by default, are included with
    ///     <see cref="NuGetCacheOptions.IncludeLocalSources"/>, and are used by explicit sources.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_GetVersionsAsync_LocalSource_RequiresIncludeLocalSourcesOrExplicitSource()
    {
        var folder = CreateTempFolder();
        var feed = CreateTempFolder();
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(feed, "Local.Package.1.0.0.nupkg"),
                NuGetPackageBuilder.CreateMinimalPackage("Local.Package", "1.0.0"),
                TestContext.Current.CancellationToken);
            await using var server = new NuGetTestServer();
            var settings = server.CreateSettings(folder, feed);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var byDefault = await NuGetCache.GetVersionsAsync("Local.Package", false, settings, null, registrar, CancellationToken.None);
            var included = await NuGetCache.GetVersionsAsync(
                "Local.Package", false, settings, new NuGetCacheOptions { IncludeLocalSources = true }, registrar, CancellationToken.None);
            var explicitLocal = await NuGetCache.GetVersionsAsync(
                "Local.Package", false, settings, new NuGetCacheOptions { Sources = [feed] }, registrar, CancellationToken.None);
            var listed = await NuGetCache.ListVersionsAsync(
                "Local.Package", false, settings, new NuGetCacheOptions { IncludeLocalSources = true }, registrar, CancellationToken.None);

            Assert.Empty(byDefault);
            Assert.Equal(["1.0.0"], included);
            Assert.Equal(["1.0.0"], explicitLocal);
            var entry = Assert.Single(listed);
            Assert.Equal("1.0.0", entry.Version);
            Assert.Equal("test-source", entry.Source);
            Assert.True(entry.IsListed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(feed, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that <c>EnsureCachedAsync</c> always consults local sources, regardless of
    ///     <see cref="NuGetCacheOptions.IncludeLocalSources"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_EnsureCachedAsync_LocalSource_IsAlwaysConsulted()
    {
        var folder = CreateTempFolder();
        var feed = CreateTempFolder();
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(feed, "Local.Ensure.1.0.0.nupkg"),
                NuGetPackageBuilder.CreateMinimalPackage("Local.Ensure", "1.0.0"),
                TestContext.Current.CancellationToken);
            await using var server = new NuGetTestServer();
            var settings = server.CreateSettings(folder, feed);

            var path = await NuGetCache.EnsureCachedAsync(
                "Local.Ensure", "1.0.0", settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(path, ".nupkg.metadata")));
            Assert.Equal(["1.0.0"], NuGetCache.GetCachedVersionsCore("Local.Ensure", settings));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(feed, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that listing returns listed state and publication date from the registration metadata,
    ///     including unlisted versions, and honors the prerelease flag.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_ListVersionsAsync_RegistrationMetadata_ReturnsListedStateAndPublishedDate()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            var published = new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero);
            server.RegisterV3PackageVersions("Meta.Package", "1.0.0", "2.0.0-beta", "3.0.0");
            server.RegisterV3PackageMetadata(
                "Meta.Package",
                ("1.0.0", true, published),
                ("2.0.0-beta", true, published.AddDays(1)),
                ("3.0.0", false, published.AddDays(2)));
            var settings = server.CreateSettings(folder, server.IndexUrl);
            var registrar = CredentialServiceRegistrar.DefaultCredentialRegistrar;

            var stable = await NuGetCache.ListVersionsAsync("Meta.Package", false, settings, null, registrar, CancellationToken.None);
            var all = await NuGetCache.ListVersionsAsync("Meta.Package", true, settings, null, registrar, CancellationToken.None);

            Assert.Equal(["1.0.0", "3.0.0"], stable.Select(v => v.Version));
            Assert.Equal(["1.0.0", "2.0.0-beta", "3.0.0"], all.Select(v => v.Version));
            Assert.All(all, v => Assert.Equal("test-source", v.Source));
            Assert.Equal([true, true, false], all.Select(v => v.IsListed));
            Assert.Equal(published, all[0].Published);
            Assert.Equal(published.AddDays(2), all[2].Published);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a source without registration metadata falls back to the flat version list:
    ///     versions are reported as listed with an unknown publication date.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_ListVersionsAsync_NoRegistrationMetadata_FallsBackToFlatList()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersions("Flat.Package", "1.0.0", "2.0.0");
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var listed = await NuGetCache.ListVersionsAsync(
                "Flat.Package", false, settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.Equal(["1.0.0", "2.0.0"], listed.Select(v => v.Version));
            Assert.All(listed, v =>
            {
                Assert.True(v.IsListed);
                Assert.Null(v.Published);
                Assert.Equal("test-source", v.Source);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that, across sources, the first source that reports a version wins.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_ListVersionsAsync_VersionInTwoSources_FirstSourceWins()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var primary = new NuGetTestServer();
            primary.RegisterV3PackageVersions("Both.Package", "1.0.0");
            await using var secondary = new NuGetTestServer();
            secondary.RegisterV3PackageVersions("Both.Package", "1.0.0", "2.0.0");
            var settings = primary.CreateSettings(folder, primary.IndexUrl, secondary.IndexUrl);

            var listed = await NuGetCache.ListVersionsAsync(
                "Both.Package", false, settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None);

            Assert.Equal(["1.0.0", "2.0.0"], listed.Select(v => v.Version));
            Assert.Equal("test-source-primary", listed[0].Source);
            Assert.Equal("test-source-secondary", listed[1].Source);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a 401 from every source surfaces as <see cref="NuGetAuthenticationException"/>
    ///     from <c>ListVersionsAsync</c>.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_ListVersionsAsync_Unauthorized_ThrowsNuGetAuthenticationException()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageWithBasicAuth(
                "Denied.List", "1.0.0", NuGetPackageBuilder.CreateMinimalPackage("Denied.List", "1.0.0"), "user", "password");
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.ListVersionsAsync(
                    "Denied.List", false, settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Equal("test-source", exception.Source);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests that a 401 from the registration endpoint is not swallowed into the flat-list
    ///     fallback: the source is recorded as failed and, with nothing found, the call surfaces
    ///     <see cref="NuGetAuthenticationException"/> even though the flat container is public.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task NuGetCache_ListVersionsAsync_UnauthorizedRegistration_ThrowsNuGetAuthenticationException()
    {
        var folder = CreateTempFolder();
        try
        {
            await using var server = new NuGetTestServer();
            server.RegisterV3PackageVersionsWithUnauthorizedMetadata("Meta.Denied", "1.0.0");
            var settings = server.CreateSettings(folder, server.IndexUrl);

            var exception = await Assert.ThrowsAsync<NuGetAuthenticationException>(async () =>
                await NuGetCache.ListVersionsAsync(
                    "Meta.Denied", false, settings, null, CredentialServiceRegistrar.DefaultCredentialRegistrar, CancellationToken.None));

            Assert.Equal("test-source", exception.Source);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Tests the value semantics of <see cref="NuGetPackageVersion"/>.
    /// </summary>
    [Fact]
    public void NuGetPackageVersion_Record_HasValueSemantics()
    {
        var published = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var first = new NuGetPackageVersion("1.0.0", "source", true, published);
        var same = new NuGetPackageVersion("1.0.0", "source", true, published);
        var unlisted = first with { IsListed = false };

        Assert.Equal(first, same);
        Assert.NotEqual(first, unlisted);
        Assert.Equal("1.0.0", first.Version);
        Assert.Equal("source", first.Source);
        Assert.Equal(published, first.Published);
        Assert.Null(new NuGetPackageVersion("1.0.0", "source", true, null).Published);
    }

    /// <summary>
    ///     Creates an installed-version folder, optionally with the completeness marker.
    /// </summary>
    /// <param name="packageFolder">The lower-case package folder.</param>
    /// <param name="version">The version folder name.</param>
    /// <param name="complete">Whether to write the <c>.nupkg.metadata</c> marker.</param>
    private static void CreateInstalledVersion(string packageFolder, string version, bool complete)
    {
        var versionFolder = Path.Combine(packageFolder, version);
        Directory.CreateDirectory(versionFolder);
        if (complete)
        {
#pragma warning disable S6966 // File.WriteAllTextAsync is unavailable on net481
            File.WriteAllText(Path.Combine(versionFolder, ".nupkg.metadata"), "{}");
#pragma warning restore S6966
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
