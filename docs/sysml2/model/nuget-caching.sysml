package NuGetCaching {
    import OtsDependencies::*;

    doc
    /*
     * DemaConsulting NuGet Caching is a .NET library that ensures specific NuGet
     * package versions are present in the local global-packages folder, downloading
     * and installing them from configured sources when missing. It also performs
     * version discovery: listing the available versions of a package from the
     * configured remote sources as normalized strings.
     */

    part def NuGetCachingSystem {
        doc /* The NuGet Caching library as a whole: package caching and version discovery. */

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

        part apiMark : ApiMark;
        part buildMark : BuildMark;
        part fileAssert : FileAssert;
        part pandoc : Pandoc;
        part reqStream : ReqStream;
        part reviewMark : ReviewMark;
        part sarifMark : SarifMark;
        part sonarMark : SonarMark;
        part sysml2Tools : SysML2Tools;
        part versionMark : VersionMark;
        part weasyPrint : WeasyPrint;
        part wireMockNet : WireMockNet;
        part xUnit : XUnit;
    }
}
