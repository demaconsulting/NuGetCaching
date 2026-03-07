# Introduction

## Purpose

This document is the user guide for DemaConsulting NuGet Caching, a library for ensuring
NuGet packages are available in the local global packages cache.

## Scope

This user guide covers:

- Installation of the library
- Basic usage and examples
- API reference

# Continuous Compliance

This library follows the [Continuous Compliance][continuous-compliance] methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: markdownlint, cspell, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build

# Installation

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.NuGet.Caching
```

# Usage

## Basic Usage

```csharp
using DemaConsulting.NuGet.Caching;

// Ensure a specific NuGet package version is present in the local global packages cache
string packagePath = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", "13.0.3");
Console.WriteLine($"Package cached at: {packagePath}");
```

The method reads NuGet configuration (sources and global packages folder) from the default NuGet
settings on the local machine, mirroring the behavior of the `dotnet` CLI. Package source mapping
is fully supported.

## API Reference

### NuGetCache

The `NuGetCache` static class provides methods for ensuring NuGet packages are available in
the local global packages cache.

#### Methods

##### EnsureCachedAsync

```csharp
public static async Task<string> EnsureCachedAsync(
    string packageId,
    string version,
    CancellationToken cancellationToken = default)
```

Ensures a specific NuGet package version is available in the local global packages cache.

**Parameters:**

- `packageId` (string): The NuGet package identifier (e.g. `Newtonsoft.Json`). Must not be null.

- `version` (string): The exact version string (e.g. `13.0.3`). Must not be null.

- `cancellationToken` (CancellationToken): Optional cancellation token for the async operation.

**Returns:**

The absolute path to the cached package folder inside the global packages folder.

**Exceptions:**

- `ArgumentNullException`: Thrown when `packageId` or `version` is null.

- `InvalidOperationException`: Thrown when the package cannot be found in any configured
  NuGet source.

**Example:**

```csharp
string path = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", "13.0.3");
// path = "/home/user/.nuget/packages/newtonsoft.json/13.0.3"
```

# Examples

## Example 1: Ensure a Package is Cached

```csharp
using DemaConsulting.NuGet.Caching;

string packagePath = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", "13.0.3");
Console.WriteLine($"Package available at: {packagePath}");
```

## Example 2: Using a Cancellation Token

```csharp
using DemaConsulting.NuGet.Caching;

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

string packagePath = await NuGetCache.EnsureCachedAsync(
    "Newtonsoft.Json",
    "13.0.3",
    cts.Token);

Console.WriteLine($"Package available at: {packagePath}");
```

## Example 3: Error Handling When Package Not Found

```csharp
using DemaConsulting.NuGet.Caching;

try
{
    string packagePath = await NuGetCache.EnsureCachedAsync("My.Package", "1.0.0");
    Console.WriteLine($"Package available at: {packagePath}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Package not found in any configured NuGet source: {ex.Message}");
}
```

## Example 4: Caching Multiple Packages

```csharp
using DemaConsulting.NuGet.Caching;

string[] packages = ["Newtonsoft.Json:13.0.3", "System.Text.Json:8.0.0"];

foreach (string entry in packages)
{
    string[] parts = entry.Split(':');
    string path = await NuGetCache.EnsureCachedAsync(parts[0], parts[1]);
    Console.WriteLine($"{parts[0]} {parts[1]} -> {path}");
}
```

<!-- Link References -->
[continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
