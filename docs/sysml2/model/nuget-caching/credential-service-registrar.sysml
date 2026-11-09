package NuGetCaching {
    private import SoftwareItemMetadata::*;

    part def CredentialServiceRegistrar {
        doc /* Registers the NuGet SDK default credential service once per process. */

        metadata info : SoftwareInfo {
            description = "Registers the NuGet SDK default credential service once per process.";
            sourceFiles = ("src/DemaConsulting.NuGet.Caching/CredentialServiceRegistrar.cs", "src/DemaConsulting.NuGet.Caching/CredentialDispatcher.cs", "src/DemaConsulting.NuGet.Caching/NuGetLoggerAdapter.cs");
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/CredentialDispatcherTests.cs", "test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheServerTests.cs");
            designFiles = ("docs/design/nuget-caching/credential-service-registrar.md");
            verificationFiles = ("docs/verification/nuget-caching/credential-service-registrar.md");
            requirementFiles = ("docs/reqstream/nuget-caching/credential-service-registrar.yaml");
        }
    }
}
