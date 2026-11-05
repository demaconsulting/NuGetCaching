## NuGet Client SDK Integration Design

### Purpose

The NuGet client SDK (`NuGet.Protocol`, `NuGet.Configuration`, and `NuGet.Credentials`) provides
package source communication, settings loading, package source mapping, credential handling,
and package download and installation. It is the only run-time OTS dependency of the library.

### Features Used

- `Settings.LoadDefaultSettings` and `PackageSourceProvider` to read the NuGet configuration
  hierarchy and package sources.
- Package source mapping to honor source mapping when selecting sources.
- `SourceRepository` resources to list package versions and download packages.
- Global-packages folder utilities to install packages.
- `NuGetVersion` for internal version parsing and ordering (never exposed publicly).
- `NuGet.Credentials` for credential providers, and `Microsoft.Extensions.Logging.Abstractions`
  (the `ILogger` accepted in `NuGetCacheOptions`, adapted to the NuGet logger).

### Integration Pattern

The library wraps the SDK behind its own internal units (`PackageSourceResolver`,
`PackageDownloader`, and `CredentialServiceRegistrar`). SDK types never appear in the public API,
which exposes only strings. SDK exceptions are classified by `AuthFailureClassifier` into
actionable diagnostics.
