package NuGetCaching {
    part def CredentialServiceRegistrar {
        doc /* Registers the NuGet SDK default credential service once per process. */

        comment sourceRef /* Source: src/DemaConsulting.NuGet.Caching/CredentialServiceRegistrar.cs */
        comment sourceRef /* Source: src/DemaConsulting.NuGet.Caching/CredentialDispatcher.cs */
        comment sourceRef /* Source: src/DemaConsulting.NuGet.Caching/NuGetLoggerAdapter.cs */
        comment designRef /* Design: docs/design/nuget-caching/credential-service-registrar.md */
        comment verificationRef /* Verification: docs/verification/nuget-caching/credential-service-registrar.md */
        comment reqRef /* Requirements: docs/reqstream/nuget-caching/credential-service-registrar.yaml */

        comment testRef /* Test: test/DemaConsulting.NuGet.Caching.Tests/CredentialDispatcherTests.cs */
        comment testRef /* Test: test/DemaConsulting.NuGet.Caching.Tests/NuGetCacheServerTests.cs */
    }
}
