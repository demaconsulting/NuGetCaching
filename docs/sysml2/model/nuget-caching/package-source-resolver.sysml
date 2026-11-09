package NuGetCaching {
    private import SoftwareItemMetadata::*;

    part def PackageSourceResolver {
        doc /* Resolves the FindPackageByIdResource for a source, with v2 fallback. */

        metadata info : SoftwareInfo {
            description = "Resolves the FindPackageByIdResource for a source, with v2 fallback.";
            sourceFiles = ("src/DemaConsulting.NuGet.Caching/PackageSourceResolver.cs");
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/PackageSourceResolverTests.cs");
            designFiles = ("docs/design/nuget-caching/package-source-resolver.md");
            verificationFiles = ("docs/verification/nuget-caching/package-source-resolver.md");
            requirementFiles = ("docs/reqstream/nuget-caching/package-source-resolver.yaml");
        }
    }
}
