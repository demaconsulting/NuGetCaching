## NuGet Client SDK Verification

This document provides the verification evidence for the NuGet Client SDK OTS software items.
Requirements for these OTS items are defined in the NuGet Client SDK OTS Software Requirements
document: `docs/reqstream/ots/nuget.yaml`.

### Required Functionality

NuGet.Protocol communicates with NuGet v2 and v3 package sources, streams package content, and
installs packages into the global packages folder. NuGet.Configuration reads the NuGet settings
hierarchy (machine-wide, user, and project-level), resolves the global packages folder path,
enumerates package sources, and reads package source mapping rules.

### Verification Approach

Both NuGet Client SDK components are verified by self-validation evidence from the CI pipeline.
Each scenario names a specific test method that exercises the NuGet SDK through the library's
public API. A passing pipeline run for all scenarios constitutes evidence that both requirements
are satisfied.

### Test Scenarios

#### NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder

**Scenario**: The library is called with a known package ID and version. The NuGet SDK must
communicate with the configured source, stream the `.nupkg` content, and install the package into
the global packages folder.

**Expected**: `EnsureCachedAsync` returns a non-null path that exists on disk and contains the
`.nupkg.metadata` sentinel file, confirming that `NuGet.Protocol` performed the download and
installation and that `NuGet.Configuration` resolved the correct global packages folder.

**Requirement coverage**: `Caching-OTS-NuGetProtocol`, `Caching-OTS-NuGetConfiguration`.

#### NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath

**Scenario**: The library is called against a WireMock v3 flat-container feed with a registered
package. The NuGet SDK must discover the `PackageBaseAddress/3.0.0` resource from the service
index, fetch the version list, download the `.nupkg`, and install the package.

**Expected**: `EnsureCachedAsync` returns a non-null path that exists on disk and contains
`.nupkg.metadata`, confirming the v3 protocol path through `NuGet.Protocol` is functional.

**Requirement coverage**: `Caching-OTS-NuGetProtocol`, `Caching-OTS-NuGetConfiguration`.

### Requirements Coverage

- **`Caching-OTS-NuGetProtocol`**: NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder,
  NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath
- **`Caching-OTS-NuGetConfiguration`**: NuGetCache_EnsureCachedAsync_ValidPackageId_ReturnsPackageFolder,
  NuGetCache_EnsureCachedAsync_V3PackageRegistered_ReturnsExistingPackagePath
