## ApiMark Verification

This document provides the verification evidence for the `ApiMark` OTS software item.
Requirements for this OTS item are defined in the ApiMark OTS Software Requirements document
(`docs/reqstream/ots/apimark.yaml`).

### Required Functionality

ApiMark shall generate Markdown API documentation from the library assembly and its XML
documentation file, and the documentation shall be packed into the `api/` folder of the NuGet
package.

### Verification Approach

ApiMark is verified through artifact content validation using FileAssert. After the NuGet package
is built, FileAssert asserts that the `.nupkg` zip archive contains
`api/DemaConsulting.NuGet.Caching.md`, `api/api.md`, and at least four documentation files under
`api/**/*.md`. Passing this assertion proves ApiMark executed correctly and produced meaningful
output that was bundled into the package. The test is named in the OTS requirements and tracked by
ReqStream.

### Test Environment

The standard CI pipeline environment after the Release build produces the library assembly, XML
documentation file, and NuGet package. No network access or external services are required.

### Acceptance Criteria

- The FileAssert NuGet package assertion passes without error.
- The `.nupkg` archive contains `api/DemaConsulting.NuGet.Caching.md` (exactly one match).
- The `.nupkg` archive contains `api/api.md` (exactly one match).
- The `.nupkg` archive contains at least four files matching `api/**/*.md`.
- The requirement `Caching-OTS-ApiMark` is linked to the named test identifier in the
  ReqStream trace matrix.

### Test Scenarios

#### ApiMark_NuGetPackageContainsDocs

**Scenario**: The NuGet package is built in Release configuration and FileAssert inspects the
resulting `.nupkg` archive for the generated API documentation.

**Expected**: The archive contains exactly one `api/DemaConsulting.NuGet.Caching.md`, exactly one
`api/api.md`, and at least four `api/**/*.md` files.

**Requirement coverage**: `Caching-OTS-ApiMark`.

### Requirements Coverage

- **`Caching-OTS-ApiMark`**: ApiMark_NuGetPackageContainsDocs
