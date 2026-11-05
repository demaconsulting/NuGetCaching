package OtsDependencies {
    doc /* Off-the-shelf (OTS) items used by NuGet Caching: runtime dependencies plus build, test, and compliance tooling. */

    part def NuGet {
        doc /* OTS: NuGet client SDK (NuGet.Protocol and NuGet.Configuration). */

        comment verificationRef /* Verification: docs/verification/ots/nuget.md */
        comment reqRef /* Requirements: docs/reqstream/ots/nuget.yaml */
    }

    part def Polyfill {
        doc /* OTS: Polyfill compatibility shim (e.g. Path.GetRelativePath for netstandard2.0). */

        comment verificationRef /* Verification: docs/verification/ots/polyfill.md */
        comment reqRef /* Requirements: docs/reqstream/ots/polyfill.yaml */
    }

    part def BuildMark {
        doc /* OTS: BuildMark tool that generates build notes. */

        comment verificationRef /* Verification: docs/verification/ots/buildmark.md */
        comment reqRef /* Requirements: docs/reqstream/ots/buildmark.yaml */
    }

    part def FileAssert {
        doc /* OTS: FileAssert tool that asserts properties of generated files. */

        comment verificationRef /* Verification: docs/verification/ots/fileassert.md */
        comment reqRef /* Requirements: docs/reqstream/ots/fileassert.yaml */
    }

    part def Pandoc {
        doc /* OTS: Pandoc document converter used to compile documentation to HTML. */

        comment verificationRef /* Verification: docs/verification/ots/pandoc.md */
        comment reqRef /* Requirements: docs/reqstream/ots/pandoc.yaml */
    }

    part def ReqStream {
        doc /* OTS: ReqStream requirements trace and enforcement tool. */

        comment verificationRef /* Verification: docs/verification/ots/reqstream.md */
        comment reqRef /* Requirements: docs/reqstream/ots/reqstream.yaml */
    }

    part def ReviewMark {
        doc /* OTS: ReviewMark file-review planning and evidence tool. */

        comment verificationRef /* Verification: docs/verification/ots/reviewmark.md */
        comment reqRef /* Requirements: docs/reqstream/ots/reviewmark.yaml */
    }

    part def SarifMark {
        doc /* OTS: SarifMark tool that converts SARIF analysis results into reports. */

        comment verificationRef /* Verification: docs/verification/ots/sarifmark.md */
        comment reqRef /* Requirements: docs/reqstream/ots/sarifmark.yaml */
    }

    part def SonarMark {
        doc /* OTS: SonarMark tool that reports SonarCloud quality-gate results. */

        comment verificationRef /* Verification: docs/verification/ots/sonarmark.md */
        comment reqRef /* Requirements: docs/reqstream/ots/sonarmark.yaml */
    }

    part def SysML2Tools {
        doc /* OTS: SysML2Tools linter, query engine, and renderer for the architecture model. */

        comment verificationRef /* Verification: docs/verification/ots/sysml2tools.md */
        comment reqRef /* Requirements: docs/reqstream/ots/sysml2tools.yaml */
    }

    part def VersionMark {
        doc /* OTS: VersionMark tool that captures tool versions used in a build. */

        comment verificationRef /* Verification: docs/verification/ots/versionmark.md */
        comment reqRef /* Requirements: docs/reqstream/ots/versionmark.yaml */
    }

    part def WeasyPrint {
        doc /* OTS: WeasyPrint HTML-to-PDF renderer for compliance documents. */

        comment verificationRef /* Verification: docs/verification/ots/weasyprint.md */
        comment reqRef /* Requirements: docs/reqstream/ots/weasyprint.yaml */
    }

    part def WireMockNet {
        doc /* OTS: WireMock.Net HTTP mock server used by the test suite to simulate NuGet feeds. */

        comment verificationRef /* Verification: docs/verification/ots/wiremock.md */
        comment reqRef /* Requirements: docs/reqstream/ots/wiremock.yaml */
    }

    part def XUnit {
        doc /* OTS: xUnit test framework used to run the test suite. */

        comment verificationRef /* Verification: docs/verification/ots/xunit.md */
        comment reqRef /* Requirements: docs/reqstream/ots/xunit.yaml */
    }
}
