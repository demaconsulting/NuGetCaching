# System Design

![NuGetCaching System Structure](NuGetCachingView.svg)

## Overview

The DemaConsulting NuGet Caching library is a .NET library that provides programmatic
NuGet package caching functionality. The system ensures specific NuGet package versions
are available in the local global packages cache before use, enabling tools and automation
workflows to depend on locally cached packages without requiring the .NET SDK restore
workflow. It also provides version discovery: listing the available versions of a package
from the configured remote sources as normalized strings, so callers can choose a version
to cache without depending on NuGet types.

## System Architecture

The system comprises six software units:

- **NuGetCache**: The public API surface providing the `EnsureCachedAsync`, `GetVersionsAsync`,
  `ListVersionsAsync`, and `GetCachedVersions` methods, each taking optional `root` and
  `NuGetCacheOptions` arguments; a thin
  orchestrator that validates input, checks the cache-hit fast path, registers the credential
  service, and enumerates configured sources, delegating resolution and download to the
  units below
- **PathHelpers**: An internal utility class providing safe path-combination operations
- **PackageSourceResolver**: An internal utility class resolving the `FindPackageByIdResource`
  for a configured source, including automatic v2 OData fallback
- **PackageDownloader**: An internal utility class downloading and installing a resolved
  package into the global packages folder, and owning the on-disk package-path convention
- **AuthFailureClassifier**: An internal utility class classifying an exception raised while
  communicating with a source as an actionable HTTP 401/403 authentication failure or a
  non-actionable transient error
- **CredentialServiceRegistrar**: An internal class registering the NuGet SDK's default
  credential service once per process, wrapped in a dispatching service that applies per-call
  `NuGetCacheOptions` (credential callback, interactive mode, authentication timeout)

## External Interfaces

### NuGet Protocol (NuGet.Protocol)

The system communicates with NuGet package sources using the NuGet client SDK
(`NuGet.Protocol`). This provides:

- `SourceRepository` for connecting to NuGet v2 and v3 feeds
- `FindPackageByIdResource` for locating and downloading packages
- `GlobalPackagesFolderUtility` for installing packages into the global cache

### NuGet Configuration (NuGet.Configuration)

The system reads NuGet configuration from the local machine using `NuGet.Configuration`.
This provides:

- `Settings.LoadDefaultSettings` for reading the NuGet configuration hierarchy
- `SettingsUtility.GetGlobalPackagesFolder` for resolving the cache directory
- `PackageSourceProvider` for enumerating enabled package sources
- `PackageSourceMapping` for respecting package source mapping rules

### Polyfill

The system uses the `Polyfill` library to provide `Path.GetRelativePath` on
`netstandard2.0` targets, where the method is not available in the BCL.
On .NET 5.0 and later, the BCL implementation is used directly.

## Dependencies

The system depends on the following OTS items in the shipped library:

- **NuGet** (`NuGet.Protocol` and `NuGet.Configuration`): source communication, package
  download and installation, settings, and package source mapping (a run-time dependency).
- **Polyfill**: `Path.GetRelativePath` on `netstandard2.0` (a compile-time source generator whose
  output is built into the library).

The system also relies on build, documentation, test, and compliance tooling OTS items:
BuildMark, FileAssert, Pandoc, ReqStream, ReviewMark, SarifMark, SonarMark, SysML2Tools,
VersionMark, WeasyPrint, WireMock.Net, and xUnit. These are modeled as OTS dependencies of the
system, consistent with the SysML2 model, but they are development-time dependencies only and are
never loaded by the shipped library. The integration design for every OTS item is described in the
*OTS Integration Design* document.

The system uses no Shared Packages.

## Risk Control Measures

N/A - the system performs no safety-critical function and no software item is segregated for
risk control (IEC 62304 §5.3.3). Security-relevant hardening is applied within the single
library: all path operations on externally supplied package identifiers and versions go through
`PathHelpers.SafePathCombine`, which rejects path traversal, and authentication failures are
reported as actionable diagnostics rather than silently ignored.

## Data Flow

1. Caller invokes `NuGetCache.EnsureCachedAsync(packageId, version)`
2. The system validates inputs are not null, throwing `ArgumentNullException`
3. The system loads NuGet configuration from machine/user settings
4. The system checks for a cached package using the sentinel file (`.nupkg.metadata`) via
   `PackageDownloader.GetPackagePath`
5. If not cached, the system registers the NuGet credential service via
   `CredentialServiceRegistrar` and queries configured NuGet sources sequentially, resolving
   each source's `FindPackageByIdResource` via `PackageSourceResolver` (which applies v2
   fallback and consults `AuthFailureClassifier` to detect actionable 401/403 responses)
6. On a resolved resource, `PackageDownloader` downloads the `.nupkg` bytes (again consulting
   `AuthFailureClassifier` for download-time authentication failures) and installs the package
   into the global packages folder
7. The absolute path to the cached package folder is returned

The returned path follows the NuGet global packages folder convention:
`{globalPackagesFolder}/{packageId.lower}/{normalizedVersion.lower}`.

### Version Discovery (`GetVersionsAsync`)

1. Caller invokes `NuGetCache.GetVersionsAsync(packageId, includePrerelease)`
2. The system validates `packageId` is not null, throwing `ArgumentNullException`
3. The system loads NuGet configuration and registers the credential service via
   `CredentialServiceRegistrar`
4. Source selection: enabled sources filtered by package source mapping are chosen (or exactly the
   `NuGetCacheOptions.Sources` when supplied); local folder sources are excluded unless
   `IncludeLocalSources` is set or the source is named explicitly (the global packages
   folder is not consulted)
5. Enumeration: for each remote source, `PackageSourceResolver` resolves the
   `FindPackageByIdResource`, and all versions the source advertises for the package are read
6. Prerelease filtering: prerelease versions are discarded unless `includePrerelease` is true
7. Sorting: versions are collected into a semantically ordered, de-duplicated set (ascending)
8. The normalized version strings are returned as a read-only list of strings; the list is empty
   when no source carries the package

`ListVersionsAsync` follows the same flow but reads package metadata and returns
`NuGetPackageVersion` records (version, source, listed flag, publication date).
`GetCachedVersions` is an offline, synchronous scan of the global packages folder for completely
installed versions of a package.

## Error Handling

The system communicates error states to callers through exceptions:

- **`ArgumentNullException`**: Thrown when `packageId` or `version` is `null`.
- **`ArgumentException`**: Thrown when `version` is not a valid NuGet version string
  (propagated from `NuGetVersion.Parse`).
- **`InvalidOperationException`**: Thrown when the requested package version cannot
  be found in any configured NuGet source, or when `RequireSignedPackages` rejects the package. The
  exception message identifies the package identifier and version.
- **`NuGetAuthenticationException`** (derived from `InvalidOperationException`): Thrown instead
  whenever a source answered HTTP 401 or 403, with or without options.

Transient source failures — unreachable hosts, network errors, or unsupported NuGet
protocol versions — are handled gracefully by skipping the affected source and
continuing to the next configured source. An exception is only raised after all
sources have been exhausted without a successful download.

`GetVersionsAsync` follows a parallel contract for the same failures:

- **`ArgumentNullException`**: Thrown when `packageId` is `null`.
- **Cancellation propagates**: `OperationCanceledException` is never treated as a skipped
  source; it is thrown to the caller.
- **Failing sources are skipped, then reported**: Network errors, NuGet protocol errors, and
  authentication failures for a source cause that source to be skipped and enumeration continues
  with the next source. Failures are only raised if no versions were found.
- **`InvalidOperationException`**: Thrown, with per-source diagnostics, when no versions were
  found and at least one source failed, consistent with `EnsureCachedAsync`.
- **Empty list when not found**: If every queried source responds and none carries the package,
  an empty list is returned rather than an exception.

## Design Constraints

- The library targets .NET Standard 2.0 for maximum compatibility, plus .NET 8.0,
  9.0, and 10.0 for modern runtime support, with additional security validation
  enabled on builds targeting .NET 5.0 or later frameworks
- The system reads from the same NuGet configuration hierarchy as the `dotnet` CLI
  and Visual Studio, ensuring consistent behavior
- All path operations use `PathHelpers.SafePathCombine` to prevent path traversal
  vulnerabilities when processing package identifiers and version strings from
  external NuGet feeds

Satisfies requirements `Caching-Sys-PackageCaching`, `Caching-Sys-VersionDiscovery`,
`Caching-Sys-NullValidation`,
`Caching-PLT-Windows`, `Caching-PLT-Linux`, `Caching-PLT-MacOS`, `Caching-PLT-Net8`,
`Caching-PLT-Net9`, `Caching-PLT-Net10`, and `Caching-PLT-NetStd20`.
