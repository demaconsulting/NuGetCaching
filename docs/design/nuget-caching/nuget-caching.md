# System Design

## Overview

The DemaConsulting NuGet Caching library is a .NET library that provides programmatic
NuGet package caching functionality. The system ensures specific NuGet package versions
are available in the local global packages cache before use, enabling tools and automation
workflows to depend on locally cached packages without requiring the .NET SDK restore
workflow.

## System Architecture

The system comprises two software units:

- **NuGetCache**: The public API surface providing the `EnsureCachedAsync` method
- **PathHelpers**: An internal utility class providing safe path-combination operations

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

## Data Flow

1. Caller invokes `NuGetCache.EnsureCachedAsync(packageId, version)`
2. The system loads NuGet configuration from machine/user settings
3. The system checks for a cached package using the sentinel file (`.nupkg.metadata`)
4. If not cached, the system queries configured NuGet sources sequentially
5. On successful download, the package is installed into the global packages folder
6. The absolute path to the cached package folder is returned

## Error Handling

The system communicates error states to callers through exceptions:

- **`ArgumentNullException`**: Thrown when `packageId` or `version` is `null`.
- **`InvalidOperationException`**: Thrown when the requested package version cannot
  be found in any configured NuGet source. The exception message identifies the
  package identifier and version.

Transient source failures — unreachable hosts, network errors, or unsupported NuGet
protocol versions — are handled gracefully by skipping the affected source and
continuing to the next configured source. An exception is only raised after all
sources have been exhausted without a successful download.

## Design Constraints

- The library targets .NET Standard 2.0 for maximum compatibility, plus .NET 8.0,
  9.0, and 10.0 for modern runtime support, with additional security validation
  enabled on builds targeting .NET 5.0 or later frameworks
- The system reads from the same NuGet configuration hierarchy as the `dotnet` CLI
  and Visual Studio, ensuring consistent behavior
- All path operations use `PathHelpers.SafePathCombine` to prevent path traversal
  vulnerabilities when processing package identifiers and version strings from
  external NuGet feeds

Satisfies requirements `Caching-Sys-PackageCaching`, `Caching-PLT-Windows`, `Caching-PLT-Linux`,
`Caching-PLT-MacOS`, `Caching-PLT-Net8`, `Caching-PLT-Net9`, `Caching-PLT-Net10`, and
`Caching-PLT-NetStd20`.
