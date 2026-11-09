package NuGetCaching {
    private import SoftwareItemMetadata::*;

    part def NuGetCache {
        doc /* Public package-caching orchestrator exposing EnsureCachedAsync, GetVersionsAsync, ListVersionsAsync and GetCachedVersions. */

        metadata info : SoftwareInfo {
            description = "Public package-caching orchestrator exposing EnsureCachedAsync, GetVersionsAsync, ListVersionsAsync and GetCachedVersions.";
            sourceFiles = ("src/DemaConsulting.NuGet.Caching/NuGetCache.cs", "src/DemaConsulting.NuGet.Caching/NuGetCacheOptions.cs", "src/DemaConsulting.NuGet.Caching/NuGetPackageVersion.cs", "src/DemaConsulting.NuGet.Caching/NuGetCredential.cs", "src/DemaConsulting.NuGet.Caching/NuGetAuthenticationException.cs");
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheTests.cs", "test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheOptionsTests.cs", "test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheSourcesTests.cs", "test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheListingTests.cs", "test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheSignatureTests.cs");
            designFiles = ("docs/design/nuget-caching/nuget-cache.md");
            verificationFiles = ("docs/verification/nuget-caching/nuget-cache.md");
            requirementFiles = ("docs/reqstream/nuget-caching/nuget-cache.yaml");
        }
    }
}
