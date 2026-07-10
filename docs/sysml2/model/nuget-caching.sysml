package NuGetCaching {
    import OtsDependencies::*;

    doc
    /*
     * DemaConsulting NuGet Caching is a .NET library that ensures specific NuGet
     * package versions are present in the local global-packages folder, downloading
     * and installing them from configured sources when missing.
     */

    part def NuGetCachingSystem {
        doc /* The NuGet Caching library as a whole. */

        comment testRef /* Test: test/DemaConsulting.NuGet.Caching.Tests/NuGetCachingTests.cs */
        comment designRef /* Design: docs/design/nuget-caching.md */
        comment verificationRef /* Verification: docs/verification/nuget-caching.md */
        comment reqRef /* Requirements: docs/reqstream/nuget-caching.yaml */

        part nuGetCache : NuGetCache;
        part pathHelpers : PathHelpers;
        part packageSourceResolver : PackageSourceResolver;
        part packageDownloader : PackageDownloader;
        part authFailureClassifier : AuthFailureClassifier;
        part credentialServiceRegistrar : CredentialServiceRegistrar;

        part nuGet : NuGet;
        part polyfill : Polyfill;
    }
}
