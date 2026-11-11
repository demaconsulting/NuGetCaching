package NuGetCaching {
    private import SoftwareItemMetadata::*;

    part def PathHelpers {
        doc /* Safe path-combination utility guarding against path traversal. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.NuGet.Caching/PathHelpers.cs");
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/PathHelpersTests.cs");
            designFiles = ("docs/design/nuget-caching/path-helpers.md");
            verificationFiles = ("docs/verification/nuget-caching/path-helpers.md");
            requirementFiles = ("docs/reqstream/nuget-caching/path-helpers.yaml");
        }
    }
}
