package OtsDependencies {
    doc /* Off-the-shelf (OTS) dependencies used by NuGet Caching at runtime. */

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
}
