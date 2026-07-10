package NuGetCaching {
    part def CredentialServiceRegistrar {
        doc /* Registers the NuGet SDK default credential service once per process. */

        comment sourceRef /* Source: src/DemaConsulting.NuGet.Caching/CredentialServiceRegistrar.cs */
        comment designRef /* Design: docs/design/nuget-caching/credential-service-registrar.md */
        comment verificationRef /* Verification: docs/verification/nuget-caching/credential-service-registrar.md */
        comment reqRef /* Requirements: docs/reqstream/nuget-caching/credential-service-registrar.yaml */

        // No dedicated test file: exercised indirectly through
        // test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheServerTests.cs (testRef omitted per SysML2 Modeling standard).
    }
}
