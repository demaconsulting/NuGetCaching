package NuGetCaching {
    private import SoftwareItemMetadata::*;

    part def PackageDownloader {
        doc /* Downloads a package via a resolved resource and installs it into the global packages folder. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.NuGet.Caching/PackageDownloader.cs");
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/PackageDownloaderTests.cs", "test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheSignatureTests.cs");
            designFiles = ("docs/design/nuget-caching/package-downloader.md");
            verificationFiles = ("docs/verification/nuget-caching/package-downloader.md");
            requirementFiles = ("docs/reqstream/nuget-caching/package-downloader.yaml");
        }
    }
}
