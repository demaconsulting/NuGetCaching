package NuGetCaching {
    private import SoftwareItemMetadata::*;

    part def AuthFailureClassifier {
        doc /* Classifies source-communication exceptions as actionable HTTP 401/403 failures. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.NuGet.Caching/AuthFailureClassifier.cs");
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/AuthFailureClassifierTests.cs");
            designFiles = ("docs/design/nuget-caching/auth-failure-classifier.md");
            verificationFiles = ("docs/verification/nuget-caching/auth-failure-classifier.md");
            requirementFiles = ("docs/reqstream/nuget-caching/auth-failure-classifier.yaml");
        }
    }
}
