package OtsDependencies {
    private import SoftwareItemMetadata::*;

    doc /* Off-the-shelf (OTS) items used by NuGet Caching: runtime dependencies plus build, test, and compliance tooling. */

    part def NuGet {
        doc /* OTS: NuGet client SDK (NuGet.Protocol and NuGet.Configuration). */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/nuget.md");
            requirementFiles = ("docs/reqstream/ots/nuget.yaml");
        }
    }

    part def LoggingAbstractions {
        doc /* OTS: Microsoft.Extensions.Logging.Abstractions, the ILogger contract accepted in NuGetCacheOptions. */

        metadata info : SoftwareInfo {
            designFiles = ("docs/design/ots/nuget.md");
            verificationFiles = ("docs/verification/ots/nuget.md");
            requirementFiles = ("docs/reqstream/ots/nuget.yaml");
        }
    }

    part def Polyfill {
        doc /* OTS: Polyfill compatibility shim (e.g. Path.GetRelativePath for netstandard2.0). */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/polyfill.md");
            requirementFiles = ("docs/reqstream/ots/polyfill.yaml");
        }
    }

    part def ApiMark {
        doc /* OTS: ApiMark MSBuild tool that generates API documentation packed into the NuGet package. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/apimark.md");
            requirementFiles = ("docs/reqstream/ots/apimark.yaml");
        }
    }

    part def BuildMark {
        doc /* OTS: BuildMark tool that generates build notes. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/buildmark.md");
            requirementFiles = ("docs/reqstream/ots/buildmark.yaml");
        }
    }

    part def FileAssert {
        doc /* OTS: FileAssert tool that asserts properties of generated files. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/fileassert.md");
            requirementFiles = ("docs/reqstream/ots/fileassert.yaml");
        }
    }

    part def Pandoc {
        doc /* OTS: Pandoc document converter used to compile documentation to HTML. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/pandoc.md");
            requirementFiles = ("docs/reqstream/ots/pandoc.yaml");
        }
    }

    part def ReqStream {
        doc /* OTS: ReqStream requirements trace and enforcement tool. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/reqstream.md");
            requirementFiles = ("docs/reqstream/ots/reqstream.yaml");
        }
    }

    part def ReviewMark {
        doc /* OTS: ReviewMark file-review planning and evidence tool. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/reviewmark.md");
            requirementFiles = ("docs/reqstream/ots/reviewmark.yaml");
        }
    }

    part def SarifMark {
        doc /* OTS: SarifMark tool that converts SARIF analysis results into reports. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/sarifmark.md");
            requirementFiles = ("docs/reqstream/ots/sarifmark.yaml");
        }
    }

    part def SonarMark {
        doc /* OTS: SonarMark tool that reports SonarCloud quality-gate results. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/sonarmark.md");
            requirementFiles = ("docs/reqstream/ots/sonarmark.yaml");
        }
    }

    part def SysML2Tools {
        doc /* OTS: SysML2Tools linter, query engine, and renderer for the architecture model. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/sysml2tools.md");
            requirementFiles = ("docs/reqstream/ots/sysml2tools.yaml");
        }
    }

    part def VersionMark {
        doc /* OTS: VersionMark tool that captures tool versions used in a build. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/versionmark.md");
            requirementFiles = ("docs/reqstream/ots/versionmark.yaml");
        }
    }

    part def WeasyPrint {
        doc /* OTS: WeasyPrint HTML-to-PDF renderer for compliance documents. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/weasyprint.md");
            requirementFiles = ("docs/reqstream/ots/weasyprint.yaml");
        }
    }

    part def WireMockNet {
        doc /* OTS: WireMock.Net HTTP mock server used by the test suite to simulate NuGet feeds. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/wiremock.md");
            requirementFiles = ("docs/reqstream/ots/wiremock.yaml");
        }
    }

    part def XUnit {
        doc /* OTS: xUnit test framework used to run the test suite. */

        metadata info : SoftwareInfo {
            verificationFiles = ("docs/verification/ots/xunit.md");
            requirementFiles = ("docs/reqstream/ots/xunit.yaml");
        }
    }
}
