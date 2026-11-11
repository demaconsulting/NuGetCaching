# Introduction

## Purpose

This document is the user guide for DemaConsulting NuGet Caching, a library for ensuring
NuGet packages are available in the local global packages cache and for discovering the
versions of a package that are available from the configured NuGet sources.

The figure below shows how the pieces fit together: your application calls `NuGetCache`, optionally passing
`NuGetCacheOptions`, and `NuGetCache` reads `nuget.config`, talks to the NuGet feeds, and installs packages into the
global packages folder.

![NuGetCaching architecture: your application calling NuGetCache with optional NuGetCacheOptions, plus nuget.config,
NuGet feeds and the global packages folder](architecture.svg)

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
- **Linting Enforcement**: markdownlint, TextLint, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis run on every build

# Installation

## Prerequisites

The library targets the following platforms and runtimes:

- **Platforms**: Windows, Linux, and macOS
- **Runtimes**: .NET Standard 2.0, .NET 8, .NET 9, .NET 10, and .NET 11

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
public static Task<string> EnsureCachedAsync(
    string packageId,
    string version,
    string? root = null,
    NuGetCacheOptions? options = null,
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

- `options` (NuGetCacheOptions?): Optional per-call options (see `NuGetCacheOptions`). When null,
  every option is off.

- `cancellationToken` (CancellationToken): Optional cancellation token for the async operation.

**Returns:**

The absolute path to the cached package folder inside the global packages folder.

**Exceptions:**

- `ArgumentNullException`: Thrown when `packageId` or `version` is null.

- `ArgumentException`: Thrown when `version` is not a valid NuGet version string, or when an entry
  of `options.Sources` is invalid.

- `NuGetAuthenticationException` (derived from `InvalidOperationException`): Thrown when a source
  answered HTTP 401 or 403 and the package could not be obtained. It carries `Source` and `Uri`; `Uri`
  holds only the scheme, host, port and path of the rejecting endpoint (user info, query string and
  fragment are always removed because they may carry secrets); a URL-like `Source` name is redacted the
  same way. When several sources share a host, an
  authentication failure is attributed only to the source that actually issued the challenge.

- `InvalidOperationException`: Thrown when the package cannot be found in any configured
  NuGet source, or when `RequireSignedPackages` rejects the package.

- `OperationCanceledException`: Thrown when `cancellationToken` is canceled; cancellation
  propagates to the caller as `OperationCanceledException`.

**Example:**

```csharp
string path = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", "13.0.3");
// path = "/home/user/.nuget/packages/newtonsoft.json/13.0.3"
```

##### GetVersionsAsync

```csharp
public static Task<IReadOnlyList<string>> GetVersionsAsync(
    string packageId,
    bool includePrerelease = false,
    string? root = null,
    NuGetCacheOptions? options = null,
    CancellationToken cancellationToken = default)
```

Gets the versions of a NuGet package available from the configured remote (HTTP/HTTPS)
sources. Local folder sources are consulted only when `options.IncludeLocalSources` is true or
they are named in `options.Sources`; other non-HTTP sources and the local global packages folder are
never consulted.

**Parameters:**

- `packageId` (string): The NuGet package identifier. Must not be null.

- `includePrerelease` (bool): When true, prerelease versions are included. Defaults to false.

- `root` (string?): The directory from which to begin discovering `nuget.config` files, as for
  `EnsureCachedAsync`. When null, defaults to the current working directory.

- `options` (NuGetCacheOptions?): Optional per-call options. When null, every option is off.

- `cancellationToken` (CancellationToken): Optional cancellation token for the async operation.

**Returns:**

Normalized version strings (e.g. `1.0.0`), de-duplicated across sources and sorted in ascending
semantic-version order. An empty list is returned when every queried source responds and none
carries the package. If no versions are found and at least one source was unreachable or
rejected the request, an exception describing each failed source is thrown.

**Exceptions:**

- `ArgumentNullException`: Thrown when `packageId` is null.

- `OperationCanceledException`: Thrown when `cancellationToken` is canceled; cancellation
  propagates to the caller as `OperationCanceledException`.

- `NuGetAuthenticationException`: Thrown instead of `InvalidOperationException` when no versions
  were found and a source answered HTTP 401 or 403.

- `InvalidOperationException`: Thrown when no versions were found and at least one source was
  unreachable or rejected the request. The message describes each failed source.

**Example:**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DemaConsulting.NuGet.Caching;

IReadOnlyList<string> versions = await NuGetCache.GetVersionsAsync("Newtonsoft.Json");
if (versions.Count > 0)
{
    // Note: GetVersionsAsync includes unlisted versions; use ListVersionsAsync and IsListed to skip them
    string latest = versions[^1];
    string path = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", latest);
}
else
{
    Console.WriteLine("No versions found.");
}
```

##### ListVersionsAsync

```csharp
public static Task<IReadOnlyList<NuGetPackageVersion>> ListVersionsAsync(
    string packageId,
    bool includePrerelease = false,
    string? root = null,
    NuGetCacheOptions? options = null,
    CancellationToken cancellationToken = default)
```

Like `GetVersionsAsync`, but returns `NuGetPackageVersion` records
(`Version`, `Source`, `IsListed`, `Published`) in ascending order. Unlisted versions are included and
flagged. `Published` is null when the source supplies no publication date. When a source offers no
package metadata, its versions are reported as listed with no date. A version offered by several
sources is reported once, from the first source that supplied it. Exceptions are the same as for
`GetVersionsAsync`.

##### GetCachedVersions

```csharp
public static IReadOnlyList<string> GetCachedVersions(string packageId, string? root = null)
```

Synchronously lists the versions of a package already installed in the global packages folder, as
normalized strings in ascending order. It performs no network access. Incomplete installs (no
`.nupkg.metadata` file) are ignored, and an empty list is returned when nothing is installed. The
global packages folder is the `NUGET_PACKAGES` environment variable when set, otherwise the
`globalPackagesFolder` setting in `nuget.config`, otherwise `.nuget/packages` in the user profile; `root` only
chooses where `nuget.config` discovery starts. Throws `ArgumentNullException` or
`ArgumentException` for a null or blank `packageId`.

##### NuGetCacheOptions

- `Sources` (`IReadOnlyList<string>?`): when set, exactly these sources are used instead of the configured
  ones, and package source mapping is bypassed. Each entry is a configured source name, an http(s) URL, a
  `file:` URI, or a rooted folder path; duplicates are collapsed and any other entry throws
  `ArgumentException`. A v3 feed entry must be the full `.../index.json` service-index URL (a bare
  host or API root is treated as v2). A URL with a query string is not rewritten and a warning is
  logged when a logger is supplied.
- `IncludeLocalSources` (bool, default false): includes local folder sources in `GetVersionsAsync` and
  `ListVersionsAsync`. `EnsureCachedAsync` always consults local sources.
- `RequireSignedPackages` (bool, default false): requires the package to be signed for this call. An
  unsigned or invalidly signed package is rejected with `InvalidOperationException` and not installed, and
  an already-installed package is verified on the cache-hit path. Verification is performed explicitly by
  the library on every platform (NuGet's own check is skipped on Linux and macOS unless
  `DOTNET_NUGET_SIGNATURE_VERIFICATION=true`), checks integrity and signature validity, and, when
  `trustedSigners` are configured in `nuget.config`, that the signer matches one of them (with no
  `trustedSigners` configured, any validly signed package with a trusted signature is accepted), so unsigned
  packages are never silently accepted. The same explicit check applies, on download and on already-installed
  packages, when `nuget.config` sets `signatureValidationMode=require`. When false, the `nuget.config` signature
  validation mode applies as before.
- `Interactive` (bool, default false): allows credential providers to show prompts or dialogs. Text a
  provider emits for the user (device-code instructions, sign-in URLs) is delivered only through `Logger`
  (at Information level), so supply a logger to show it. Without a logger it is discarded.
- `Logger` (`Microsoft.Extensions.Logging.ILogger?`): receives NuGet SDK messages with the template
  `{Message}` and no added category. Secrets are never logged. The logger can be called from background
  threads, so it must be thread-safe and a GUI application must marshal to its UI thread.
- `CredentialCallback` (`Func<Uri, CancellationToken, Task<NuGetCredential?>>?`): called when a source
  needs credentials, before any credential provider, at most once per (URL, request type) per call.
  Return a `NuGetCredential` to use it, or null to fall through to the providers (the null answer is
  remembered for the call). If no credential can be supplied, or a supplied credential is rejected, the
  call fails fast with `NuGetAuthenticationException`.
- `AuthenticationTimeout` (`TimeSpan?`): maximum time for each credential exchange (not the whole call).
  When exceeded, the call
  throws `OperationCanceledException` (never `NuGetAuthenticationException`, even when NuGet retries or
  wraps the failed request) without further retries. This applies to every async method. The value
  must be greater than zero and at most `int.MaxValue` milliseconds (about 24.8 days), or
  `Timeout.InfiniteTimeSpan` (same as null, no limit); any other value makes the call throw
  `ArgumentOutOfRangeException` before any work starts.

`NuGetCredential(userName, secret)` is a record whose `ToString()` shows `Secret = ***`.

```csharp
using System;
using System.Threading.Tasks;
using DemaConsulting.NuGet.Caching;

var options = new NuGetCacheOptions
{
    CredentialCallback = (uri, ct) =>
        Task.FromResult<NuGetCredential?>(new NuGetCredential("user", Environment.GetEnvironmentVariable("FEED_TOKEN")!)),
    AuthenticationTimeout = TimeSpan.FromSeconds(30),
};

try
{
    string path = await NuGetCache.EnsureCachedAsync("Private.Package", "1.0.0", options: options);
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
  terminate an external credential-provider plugin process: NuGet starts plugins without a cancellation
  token and stops them when they have been idle for 30 minutes (configurable with the
  `NUGET_PLUGIN_IDLE_TIMEOUT_IN_SECONDS` environment variable), when the parent process exits, or when
  the process ends. The library does not dispose NuGet's shared plugin manager.
- Versions are NuGet-normalized strings and may carry `+metadata` or four parts.
- When a flat-container package download is rejected after credentials were refused, NuGet's own
  download retry loop (about 5 s) runs before the authentication error is reported.

##### Migrating from 1.x

Version 2.0 replaces the 1.x method families with one method per operation. The 1.5.0 overloads and the
required-options overloads are removed: pass `options` as a named or positional argument before the
cancellation token (for example `EnsureCachedAsync(id, version, options: options, cancellationToken: ct)`).
A 401 or 403 now always throws `NuGetAuthenticationException`, which still derives from
`InvalidOperationException`, so existing `catch` blocks keep working.

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
