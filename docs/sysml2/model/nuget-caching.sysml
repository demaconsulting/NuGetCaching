package NuGetCaching {
    private import SoftwareItemMetadata::*;
    private import OtsDependencies::*;
    public import NuGetCaching_AuthFailureClassifier::*;
    public import NuGetCaching_CredentialServiceRegistrar::*;
    public import NuGetCaching_NuGetCache::*;
    public import NuGetCaching_PackageDownloader::*;
    public import NuGetCaching_PackageSourceResolver::*;
    public import NuGetCaching_PathHelpers::*;

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

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.NuGet.Caching.Tests/NuGetCachingTests.cs");
            designFiles = ("docs/design/nuget-caching.md");
            verificationFiles = ("docs/verification/nuget-caching.md");
            requirementFiles = ("docs/reqstream/nuget-caching.yaml");
        }

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
