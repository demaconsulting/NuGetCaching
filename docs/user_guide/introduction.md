# Introduction

## Purpose

This document is the user guide for DemaConsulting NuGet Caching, a library for ensuring
NuGet packages are available in the local global packages cache and for discovering the
versions of a package that are available from the configured NuGet sources.

![NuGetCaching architecture: your application calling NuGetCache, with nuget.config, NuGet feeds and the global
packages folder](architecture.svg)

## Scope

This user guide covers:

- Installation of the library
- Basic usage and examples, including package caching and version discovery
- API reference

## References

- [REF-1] Continuous Compliance Methodology, DEMA Consulting,
  <https://github.com/demaconsulting/ContinuousCompliance>
- [REF-2] NuGet Documentation, Microsoft, <https://learn.microsoft.com/nuget/>

# Continuous Compliance

This library follows the [Continuous Compliance][continuous-compliance] methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: markdownlint, cspell, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis run on every build

# Installation

## Prerequisites

The library targets the following platforms and runtimes:

- **Platforms**: Windows, Linux, and macOS
- **Runtimes**: .NET Standard 2.0, .NET 8, .NET 9, and .NET 10

## Installing the Package

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
settings, rooted at a caller-supplied (or current working) directory, mirroring the behavior of
the `dotnet` CLI - including discovery of a project/repo-local `nuget.config`. Package source
mapping is fully supported.

## API Reference

### NuGetCache

The `NuGetCache` static class provides methods for ensuring NuGet packages are available in
the local global packages cache and for discovering the versions of a package that exist.

#### Methods

##### EnsureCachedAsync

```csharp
public static async Task<string> EnsureCachedAsync(
    string packageId,
    string version,
    string? root = null,
    CancellationToken cancellationToken = default)
```

Ensures a specific NuGet package version is available in the local global packages cache.

**Parameters:**

- `packageId` (string): The NuGet package identifier (e.g. `Newtonsoft.Json`). Must not be null.

- `version` (string): The exact version string (e.g. `13.0.3`). Must not be null.

- `root` (string?): The directory from which to begin discovering `nuget.config` files (walking
  up through ancestor directories, then falling back to machine/user-wide settings), matching the
  `dotnet` CLI's behavior. Typically the directory containing the caller's project or
  `packages.config` file. When null, defaults to the current working directory.

- `cancellationToken` (CancellationToken): Optional cancellation token for the async operation.

**Returns:**

The absolute path to the cached package folder inside the global packages folder.

**Exceptions:**

- `ArgumentNullException`: Thrown when `packageId` or `version` is null.

- `ArgumentException`: Thrown when `version` is not a valid NuGet version string.

- `InvalidOperationException`: Thrown when the package cannot be found in any configured
  NuGet source.

- `OperationCanceledException`: Thrown when `cancellationToken` is canceled; cancellation
  propagates to the caller as `OperationCanceledException`.

**Example:**

```csharp
string path = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", "13.0.3");
// path = "/home/user/.nuget/packages/newtonsoft.json/13.0.3"
```

##### GetVersionsAsync

```csharp
public static async Task<IReadOnlyList<string>> GetVersionsAsync(
    string packageId,
    bool includePrerelease = false,
    string? root = null,
    CancellationToken cancellationToken = default)
```

Gets the versions of a NuGet package available from the configured remote (HTTP/HTTPS)
sources. Local folder sources, other non-HTTP sources, and the local global packages folder are not consulted.

**Parameters:**

- `packageId` (string): The NuGet package identifier. Must not be null.

- `includePrerelease` (bool): When true, prerelease versions are included. Defaults to false.

- `root` (string?): The directory from which to begin discovering `nuget.config` files, as for
  `EnsureCachedAsync`. When null, defaults to the current working directory.

- `cancellationToken` (CancellationToken): Optional cancellation token for the async operation.

**Returns:**

Normalized version strings (e.g. `1.0.0`), de-duplicated across sources and sorted in ascending
semantic-version order. An empty list is returned when every queried source responds and none
carries the package. If no versions are found and at least one source was unreachable or
rejected the request, an `InvalidOperationException` describing each failed source is thrown.

**Exceptions:**

- `ArgumentNullException`: Thrown when `packageId` is null.

- `OperationCanceledException`: Thrown when `cancellationToken` is canceled; cancellation
  propagates to the caller as `OperationCanceledException`.

- `InvalidOperationException`: Thrown when no versions were found and at least one source was
  unreachable or rejected the request. The message describes each failed source.

**Example:**

```csharp
IReadOnlyList<string> versions = await NuGetCache.GetVersionsAsync("Newtonsoft.Json");
if (versions.Count > 0)
{
    string latest = versions[^1];
    string path = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", latest);
}
else
{
    Console.WriteLine("No versions found.");
}
```

##### Overloads with NuGetCacheOptions

```csharp
public static Task<string> EnsureCachedAsync(
    string packageId, string version, string? root,
    CancellationToken cancellationToken, NuGetCacheOptions options)

public static Task<IReadOnlyList<string>> GetVersionsAsync(
    string packageId, bool includePrerelease, string? root,
    CancellationToken cancellationToken, NuGetCacheOptions options)
```

Added in 1.6.0. Existing overloads are unchanged, and calls without options behave exactly as in 1.5.0.
The cancellation token is required and `options` comes last, so that no 1.5.0 call shape (including a
positional `default` token) can bind to these overloads; pass `CancellationToken.None` if you have no token.
`options` is required (`ArgumentNullException` when null). When a source rejects a request with 401 or
403 and the operation fails, these overloads throw `NuGetAuthenticationException` (derived from
`InvalidOperationException`, with `Source` and `Uri`); the 1.5.0 overloads still throw a plain
`InvalidOperationException`.

##### NuGetCacheOptions

- `Interactive` (bool, default false): allows credential providers to show prompts or dialogs.
- `Logger` (`Microsoft.Extensions.Logging.ILogger?`): receives NuGet SDK messages. Secrets are never logged.
- `CredentialCallback` (`Func<Uri, CancellationToken, Task<NuGetCredential?>>?`): called when a source
  needs credentials, before any credential provider. Return a `NuGetCredential` to use it, or null to
  fall through to the providers. If a credential is rejected, the callback is not asked again for the
  same address within the call.
- `AuthenticationTimeout` (`TimeSpan?`): maximum time for credential acquisition. When exceeded, the call
  throws `OperationCanceledException` (never `NuGetAuthenticationException`, even when NuGet retries or
  wraps the failed request). This applies to both `EnsureCachedAsync` and `GetVersionsAsync`. The value
  must be greater than zero and at most `int.MaxValue` milliseconds (about 24.8 days), or
  `Timeout.InfiniteTimeSpan` (same as null, no limit); any other value makes the call throw
  `ArgumentOutOfRangeException` before any work starts.

`NuGetCredential(userName, secret)` is a record whose `ToString()` shows `Secret = ***`.

```csharp
var options = new NuGetCacheOptions
{
    CredentialCallback = (uri, ct) =>
        Task.FromResult<NuGetCredential?>(new NuGetCredential("user", Environment.GetEnvironmentVariable("FEED_TOKEN")!)),
    AuthenticationTimeout = TimeSpan.FromSeconds(30),
};

try
{
    string path = await NuGetCache.EnsureCachedAsync(
        "Private.Package", "1.0.0", null, CancellationToken.None, options);
}
catch (NuGetAuthenticationException ex)
{
    Console.WriteLine($"Authentication failed for {ex.Source} ({ex.Uri})");
}
```

**Limitations:**

- The NuGet credential service is process-wide. If your application registered its own before the first
  call, it is kept, the options cannot apply (`Interactive`, `CredentialCallback`, and
  `AuthenticationTimeout` are ignored), and a warning is logged when a logger is supplied.
- `AuthenticationTimeout` abandons the wait and cancels the token given to your callback. It does not
  guarantee that an external credential-provider plugin process is terminated.
- Versions are NuGet-normalized strings and may carry `+metadata` or four parts.

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
    cancellationToken: cts.Token);

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
