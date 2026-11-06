## NuGetCache Design

![NuGetCache Structure](NuGetCachingView.svg)

### Purpose

The `NuGetCache` class is a public static class providing NuGet package caching
functionality for the DemaConsulting NuGet Caching library. It is a software unit
in the sense of IEC 62304 — the smallest independently testable component responsible
for ensuring a specific NuGet package version is available in the local global packages
cache before use.

`NuGetCache` is a thin orchestrator: it validates input, checks the cache-hit fast path,
registers the NuGet credential service, and enumerates configured NuGet sources, but
delegates all source-resolution, download, authentication-failure classification, and
credential-service registration detail to four sibling internal units —
`PackageSourceResolver`, `PackageDownloader`, `AuthFailureClassifier`, and
`CredentialServiceRegistrar` — described in their own design documents. This design
document therefore focuses on the orchestration responsibilities that remain in
`NuGetCache` itself; it references, rather than duplicates, the internal design detail
of the sibling units.

The class reads NuGet configuration (package sources and the global packages folder
path) from the default machine settings, mirroring the behavior of the `dotnet` CLI
and Visual Studio package restore. It communicates with configured NuGet sources using
the NuGet client SDK, via `PackageSourceResolver` and `PackageDownloader`, to download
packages when they are not already present locally.

### Data Model

`NuGetCache` is a static class with no fields, properties, or instance state; all state is
local to each call. It exposes four public operations, each with one public method:
`EnsureCachedAsync(packageId, version, root = null, options = null, cancellationToken = default)`
returning the absolute package folder (`Task<string>`);
`GetVersionsAsync(packageId, includePrerelease = false, root = null, options = null, cancellationToken = default)`
returning normalized version strings (`Task<IReadOnlyList<string>>`, ascending semantic order, no
duplicates); `ListVersionsAsync` with the same parameters returning `Task<IReadOnlyList<NuGetPackageVersion>>`
(version, source, listed flag, publication date); and the synchronous, offline
`GetCachedVersions(packageId, root = null)` returning installed version strings. Public parameters use
only BCL types plus `NuGetCacheOptions` and the `NuGetPackageVersion` record. Internally `NuGetVersion` is
used for parsing, sorting, and normalization but never appears in a public signature.

The internal cores (`EnsureCachedAsync`, `GetVersionsAsync`, `ListVersionsAsync` taking `ISettings`,
`NuGetCacheOptions?`, and an `ICredentialServiceRegistrar`, plus `GetCachedVersionsCore`) are the single
implementation of each operation and the injection seams for tests. A few thin internal overloads (settings
only, or settings plus registrar, without options) exist purely so existing tests need not spell out
`null` options; they hold no logic.

### Design Decisions

#### Static Class

`NuGetCache` is designed as a static class because it provides a service — ensuring
a package is cached — that does not require instance state. All configuration is
read from the machine-level NuGet settings on each call, making the class naturally
stateless. A static class avoids unnecessary object instantiation and provides a
simple, flat API surface for callers. Because all state is local to each call,
`NuGetCache` is safe for concurrent use.

#### Async Approach

The primary method `EnsureCachedAsync` is asynchronous because NuGet source
communication involves network I/O. Using `async`/`await` throughout allows the
calling thread to be returned to the thread pool during network waits, keeping the
library cooperative in concurrent or UI-driven environments.

#### NuGet Settings Integration

Rather than accepting source URIs directly, `NuGetCache` reads from the default
NuGet settings via `Settings.LoadDefaultSettings(root)`, where `root` is a caller-supplied
directory (typically the directory containing the caller's project or `packages.config`
file) defaulting to the current working directory when omitted. Rooting the lookup this
way - rather than passing a literal `null` root - ensures the library respects the same
`nuget.config` hierarchy (machine-wide, user, and project-level, discovered by walking up
from `root`) as the `dotnet` CLI and Visual Studio, including authenticated feeds, proxy
settings, and package source mapping. A literal `null` root skips the walk-up entirely and
silently loses any repo-local package sources - this was the root cause of GitHub
issue #37, where `nuget-installer` could not see sources defined only in a
repo-local `nuget.config`.

#### Early-Exit on Cache Hit

The method checks for the presence of the `.nupkg.metadata` sentinel file (using the
on-disk path computed by `PackageDownloader.GetPackagePath`) before attempting any
network communication. NuGet writes this file as the final step of package extraction,
so its presence is a reliable indicator that the package is fully installed. Checking
for this file rather than the directory avoids a race condition where a
partially-extracted package directory is mistaken for a complete installation.

#### Credential Service Registration

Before resolving any `SourceRepository`, the internal `EnsureCachedAsync` core calls
`ICredentialServiceRegistrar.EnsureRegistered()` on an injected registrar (see the
`CredentialServiceRegistrar` design document for the full registration rationale and
memoization strategy). This registers the NuGet SDK's default credential service,
mirroring the setup performed internally by the `dotnet` CLI and MSBuild restore
pipeline, so `HttpSourceAuthenticationHandler` can retry an HTTP 401 challenge using
credentials resolved from a NuGet credential-provider plugin. Static
`packageSourceCredentials` configured in `nuget.config` are applied directly to the
underlying `HttpClientHandler` by the NuGet SDK and so are honored on the first request
regardless of credential-service registration; the credential service becomes relevant
when those static credentials are absent, incorrect, or when a credential-provider
plugin must be consulted. `NuGetCache` uses `CredentialServiceRegistrar.DefaultCredentialRegistrar`
(a single static instance shared by every real, non-test call) as the default registrar for
the public methods, injected through the internal
`ICredentialServiceRegistrar` seam (mirroring the existing `ISettings` injection seam) so
tests can substitute a spy double instead of depending on any shared, process-wide
test-only state.

#### Package Source Mapping Support

When `PackageSourceMapping` is enabled in the NuGet configuration, `NuGetCache`
filters the set of queried sources to only those explicitly mapped to the requested
package ID. This mirrors the security and governance behavior of the NuGet toolchain,
ensuring packages are only fetched from their authorized feeds.

#### Collaboration with Sibling Units

For each enabled, mapped package source, `NuGetCache`:

1. Constructs a `SourceRepository` for the source using the V3 provider chain.
2. Calls `PackageSourceResolver.ResolveAsync` to resolve the `FindPackageByIdResource`
   for that source, including automatic v2 OData fallback and actionable 401/403
   authentication-failure diagnosis (delegated internally to `AuthFailureClassifier`).
   See the `PackageSourceResolver` design document for the full resolution algorithm,
   including the v2-fallback candidate-construction strategy and its interaction with
   resolution-time failure masking.
3. When a resource is resolved, calls `PackageDownloader.TryDownloadAsync` to download
   the `.nupkg` bytes and install the package into the global packages folder,
   including its own actionable 401/403 authentication-failure diagnosis (again
   delegated to `AuthFailureClassifier`). See the `PackageDownloader` design document
   for the full download algorithm and the on-disk package-path convention.

`NuGetCache` accumulates any diagnostic message returned by either collaborator in a
list, so that if no source ultimately provides the package, those messages can be
included in the final exception. This separation keeps `EnsureCachedAsync` at a high
level of abstraction: it orchestrates *when* resolution and download are attempted and
*how* their results feed into the final outcome, while the sibling units own *how*
resolution and download are actually performed.

If no source has the package, an `InvalidOperationException` is thrown. When at least one
source produced a diagnostic message, those messages are appended to the exception in the
form:

```text
Package 'X' version '1.0.0' was not found in any configured NuGet source.
  - https://feed/index.json: Failed to load source index. (...)
```

This allows callers to distinguish a genuine "package absent" outcome from a feed
misconfiguration, without requiring additional logging infrastructure.

#### Version Discovery

`GetVersionsAsync` and `ListVersionsAsync` complement `EnsureCachedAsync` and share one private
implementation (`QueryVersionsAsync`). Design decisions: `GetVersionsAsync` is string-only (no `NuGetVersion` in
public signatures) so consumers need no NuGet dependency, and it uses the cheap flat version list;
`ListVersionsAsync` reads package metadata (`PackageMetadataResource`, unlisted versions included)
to report the listed flag and publication date, falling back to the flat list (listed, no date) when
a source has no metadata; `NuGetVersion` is used internally for semantic sort and de-duplication, and
the first source that reports a version wins; the global packages folder is never queried;
by default only remote (`PackageSource.IsHttp`) sources are queried, local folder sources join when
`IncludeLocalSources` is set or when they are named in `Sources`, and any other non-HTTP/HTTPS, non-local
source is excluded; an unknown package yields an empty
list; unreachable sources are skipped like in `EnsureCachedAsync`, with an
`InvalidOperationException` carrying per-source diagnostics thrown only when nothing was found and
a source failed; and source selection
(including package source mapping) is shared through a private `GetAllowedSources` helper
to prevent drift between the operations.

#### Explicit Sources

When `NuGetCacheOptions.Sources` is set, `ResolveSources` uses exactly those sources instead of the
configured ones and bypasses package source mapping. Each entry is resolved, in order, as a configured
source name (even a disabled one), an http(s) URL, a `file:` URI, or a rooted path; an entry equal to a
configured source reuses it so its credentials and protocol version apply; duplicates are collapsed; anything
else throws `ArgumentException` (parameter name `Sources`). `NuGetCacheOptions.Validate()` rejects
null, empty, white-space, and malformed-URI entries up front.

#### Signature Requirement

`NuGetCacheOptions.RequireSignedPackages` makes `EnsureCachedAsync` require signed packages for this call
only. A `ClientPolicyContext` cannot be constructed publicly, so `PackageDownloader.CreateClientPolicy` builds
one from an `ISettings` decorator (`RequireSignedSettings`) that overrides only the `config` section with
`signatureValidationMode=require`; the section is read once from a short-lived temporary `nuget.config`
(the in-memory section types are internal to NuGet), and other sections, including `trustedSigners`,
pass through to the real settings. NuGet's own install-time verification is a silent no-op on Linux and
macOS unless `DOTNET_NUGET_SIGNATURE_VERIFICATION=true` is set (`CanVerifySignedPackages` returns false), so
the library never relies on it alone: whenever the effective policy is `require` - from the option or from
`signatureValidationMode=require` in `nuget.config` - `PackageDownloader` verifies the package explicitly
(must be signed; integrity, the `trustedSigners` allow-list only when signers are configured, trust and
validity) before installing it, on every
platform. With no `trustedSigners` configured, any validly signed package with a trusted signature is
accepted; the allow-list is never applied as an empty set. When the explicit check passes with no signers
configured, the install itself uses an accept-mode policy so NuGet's own Windows check cannot reject it for
the missing allow-list. A failure, including a `SignatureException` from NuGet, surfaces as `InvalidOperationException`
naming the package, version and source (it is not treated as "not in this source"). On the cache-hit fast
path, `NuGetCache` builds the policy only when a requirement applies - the option or
`signatureValidationMode=require` read from `nuget.config` - so ordinary hits create no policy context, and
`PackageDownloader.VerifyCachedPackageSignedAsync` then applies the same explicit verification (including the
same allow-list rule) to the installed package. A call without the option follows the `nuget.config` setting as before.

#### Testability via Injected Settings

Each public method is a thin wrapper that calls the internal core
with `Settings.LoadDefaultSettings(root)`. The internal core accepts an `ISettings`
parameter that replaces the call to `LoadDefaultSettings`. This design gives tests full
control over the NuGet source and global packages folder without touching the developer's
real machine configuration:

- A test can point the settings at a local WireMock server running on a random port.
- A test can specify a disposable temp directory as the global packages folder, preventing
  cross-test pollution and avoiding accidental use of the developer's real package cache.
- All caching logic lives in one place (the internal core), so there is a single
  implementation path shared by both the production (default-settings) call and all tests.

The internal members are only accessible to the `DemaConsulting.NuGet.Caching.Tests`
assembly, enforced via `InternalsVisibleTo` in the library project file. This keeps the
public API surface minimal while enabling complete test coverage of the download path.
The internal cores additionally accept an explicit
`ICredentialServiceRegistrar`, letting tests inject a spy double to directly verify
credential-service registration invocation (see "Credential Service Registration" above)
without depending on shared, process-wide static state.

### Key Methods

#### `EnsureCachedAsync(packageId, version, root, options, cancellationToken)` (public)

Thin wrapper that delegates immediately to the internal core,
passing `Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory())` as the
`settings` argument. Callers should pass the directory containing their project or
`packages.config` file as `root` so that a repo-local `nuget.config` is discovered the
same way `dotnet restore` discovers it; omitting `root` falls back to the current working
directory. See the internal core description below for the complete processing steps.

Returns the absolute path to the cached package folder.

Satisfies requirements `Caching-NuGetCache-EnsureCached`, `Caching-NuGetCache-SingleApiShape`,
`Caching-NuGetCache-NullPackageId`,
`Caching-NuGetCache-NullVersion`, `Caching-NuGetCache-InvalidVersion`,
`Caching-NuGetCache-TransientFailure`, `Caching-NuGetCache-MultiSource`,
`Caching-NuGetCache-V2Fallback`, `Caching-NuGetCache-CacheHit`, `Caching-NuGetCache-NotFound`,
`Caching-NuGetCache-HonorCredentials`, `Caching-NuGetCache-AuthDiagnostic`,
`Caching-NuGetCache-Sources`, `Caching-NuGetCache-IncludeLocalSources`, and
`Caching-NuGetCache-RequireSignedPackages`.

#### `EnsureCachedAsync(..., ISettings, NuGetCacheOptions?, ICredentialServiceRegistrar, CancellationToken)` (internal)

Contains all orchestration logic for the public method. The method:

1. Validates that `packageId` is not null, empty or white space, that `version`, `settings`, and
   `credentialRegistrar` are not null, and that `options` (when supplied) pass `Validate()`.
2. Opens the per-call credential scope and parses the `version` string using `NuGetVersion.Parse`,
   throwing `ArgumentException` when it is not a valid NuGet version.
3. Resolves the global packages folder from the injected `settings` and builds the client policy
   context (see "Signature Requirement").
4. Computes the expected on-disk package path (via `PackageDownloader.GetPackagePath`) and
   returns it immediately if the `.nupkg.metadata` sentinel file exists (cache-hit fast
   path) - after verifying the signature when `RequireSignedPackages` is set - and otherwise
   skipping the remaining steps below, including credential-service registration, entirely.
5. Calls `credentialRegistrar.EnsureRegistered()` so any subsequently resolved
   `SourceRepository` can consult the NuGet credential service for authenticated
   sources.
6. Resolves the sources (explicit `Sources`, or enabled and mapped configured sources; local sources
   are always consulted). For each source, calls
   `PackageSourceResolver.ResolveAsync` to resolve the resource (applying v2 fallback as
   needed), then `PackageDownloader.TryDownloadAsync` to download and install the package.
   Source-level diagnostic messages are accumulated in a list.
7. Throws `InvalidOperationException` if no source provided the package, or
   `NuGetAuthenticationException` when a source answered 401/403. When at
   least one source returned a diagnostic message, those messages are appended to
   the exception so the caller can identify the misconfigured source.

Returns the absolute path to the cached package folder.

#### `GetVersionsAsync(packageId, includePrerelease, root, options, cancellationToken)` (public)

Validates `packageId` and delegates to the internal core with
`Settings.LoadDefaultSettings(root ?? Directory.GetCurrentDirectory())`. The public signature
uses only BCL types (`Task<IReadOnlyList<string>>`); `NuGetVersion` never appears in it, so
callers need no dependency on NuGet types.

#### `ListVersionsAsync(packageId, includePrerelease, root, options, cancellationToken)` (public)

Identical in shape and source handling to `GetVersionsAsync` but returns `NuGetPackageVersion` records
carrying the normalized version, the source name that supplied it, whether it is listed, and the
publication date when known.

#### `GetCachedVersions(packageId, root)` (public, synchronous)

Validates `packageId`, loads the default settings rooted at `root`, and calls the internal
`GetCachedVersionsCore`, which enumerates `{globalPackagesFolder}/{id.lower}/*`, keeps folders that
contain the `.nupkg.metadata` completeness marker and parse as versions, and returns the normalized strings
in ascending order. It performs no network access and never registers a credential service.

#### Version query core (internal)

The internal `GetVersionsAsync` and `ListVersionsAsync` cores delegate to the private
`QueryVersionsAsync`, which:

1. Validates `packageId`, `settings`, `credentialRegistrar`, and `options`, and honors a pre-canceled token.
2. Opens the credential scope, calls `credentialRegistrar.EnsureRegistered()`, and resolves the sources
   with the same rules as above, keeping local sources only for `IncludeLocalSources` or explicit `Sources`.
3. For each source, resolves the resource through `PackageSourceResolver.ResolveAsync`, then reads the
   versions (flat list, or package metadata for `ListVersionsAsync`) using a fresh `SourceCacheContext`.
   `HttpRequestException` and `NuGetProtocolException` skip the source (an authentication failure is
   recorded); cancellation propagates.
4. Adds versions to a `SortedDictionary<NuGetVersion, NuGetPackageVersion>` (semantic ordering; the first
   source wins), skipping prerelease versions unless `includePrerelease` is true.
5. Returns the list; empty when nothing is found.

A package that no source carries yields an empty list rather than an exception. Cancellation is honored:
the token is checked between sources and `OperationCanceledException` propagates rather
than being treated as a skipped source.

Satisfies requirements `Caching-NuGetCache-GetVersions`, `Caching-NuGetCache-GetVersionsNormalization`,
`Caching-NuGetCache-GetVersionsOrdering`, `Caching-NuGetCache-GetVersionsDeduplication`,
`Caching-NuGetCache-GetVersionsSources`,
`Caching-NuGetCache-GetVersionsAuth`, `Caching-NuGetCache-GetVersionsSourceFailure`,
`Caching-NuGetCache-GetVersionsAllSourcesFail`,
`Caching-NuGetCache-GetVersionsCancellation`, `Caching-NuGetCache-GetVersionsPrerelease`,
`Caching-NuGetCache-GetVersionsNotFound`, `Caching-NuGetCache-GetVersionsNullArguments`,
`Caching-NuGetCache-GetCachedVersions`, and `Caching-NuGetCache-ListVersions`.

### Options

Every method takes the optional `NuGetCacheOptions` argument (`Sources`, `IncludeLocalSources`,
`RequireSignedPackages`, `Interactive`, `CredentialCallback`, `Logger`, `AuthenticationTimeout`). A call that
passes `null` runs with every option off. Because `root`, `options`, and the token are all optional and the
options precede the token, positional use such as `EnsureCachedAsync(id, version, null, null, token)` is
unambiguous.

When a call runs, `NuGetCache`:

1. Opens a `CredentialDispatcher` scope (an `AsyncLocal` call context holding the options and
   the adapted logger) for the duration of the call, so the process-wide dispatching credential
   service can see this call's options and no other call's.
2. Passes the logger (adapted from `Microsoft.Extensions.Logging.ILogger` by
   `NuGetLoggerAdapter`) to the NuGet SDK in place of the null logger.
3. Records the first authentication failure reported by the sibling units
   (`AuthFailureInfo`: source name and URL) and, if the operation fails, always throws
   `NuGetAuthenticationException` (derived from `InvalidOperationException`) instead of a plain
   `InvalidOperationException`, whether or not options were supplied.
4. Logs a warning when `Interactive`, `CredentialCallback`, or `AuthenticationTimeout` was supplied
   but a host application had already registered its own credential service (the host's service wins).
5. Records an authentication failure against the effective repository (which may be the v2 fallback)
   so `NuGetAuthenticationException.Uri` identifies the endpoint that rejected the request. The URI
   is redacted (scheme, host, port and path only): `AuthFailureInfo.SourceUrl` is redacted when the
   record is created and the exception constructor redacts again, so user info, query string and
   fragment never reach the caller.
6. Marks the package source being operated on (`CredentialDispatcher.BeginSource`) around each
   source's resolve, download and version-listing calls (in `PackageSourceResolver`,
   `PackageDownloader` and the version-listing loop), so an unsatisfied challenge is attributed only
   to that source.

Limitations: the credential service is process-wide, so a host-registered service bypasses the
callback, timeout, and interactive mode. `AuthenticationTimeout` cancels the wait and the token
given to the callback or provider. Plugin lifetime was verified by decompiling NuGet.Protocol 7.3.1 and
re-checked against the 7.9.0 source: the
shared `PluginManager` (a static `Lazy` instance) starts a plugin with `CancellationToken.None`, so
cancelling or timing out a request does not terminate the plugin process; it exits on NuGet's idle timeout
(30 minutes by default, `NUGET_PLUGIN_IDLE_TIMEOUT_IN_SECONDS`), when it detects that the parent exited, or
when the process ends. The library does not dispose the shared manager, because that would affect other users
of NuGet in the same process and the SDK has no reset hook. Versions are returned as NuGet-normalized strings
and may carry `+metadata` or four parts.

### Error Handling

`EnsureCachedAsync`:

- **Thrown to the caller**: `ArgumentNullException` (null `packageId`, `version`, `settings`, or
  registrar), `ArgumentException` (empty or white-space `packageId`, an invalid `Sources` entry, or an invalid
  version string propagated from `NuGetVersion.Parse`), `ArgumentOutOfRangeException` (an
  `AuthenticationTimeout` that is zero, negative other than `Timeout.InfiniteTimeSpan`, or longer than
  `int.MaxValue` milliseconds; checked by `NuGetCacheOptions.Validate()` before any I/O),
  `InvalidOperationException` when no source supplies the package (message includes
  accumulated per-source diagnostics) or when `RequireSignedPackages` rejects the package,
  `NuGetAuthenticationException` when a source returned 401/403, and `OperationCanceledException` when the token is
  canceled or `AuthenticationTimeout` elapses (cancellation propagates unchanged; a timeout takes
  priority over any failure NuGet reported instead, via the scope's `ThrowIfTimedOut()` checks).
- **Handled locally**: transient source failures are absorbed by the sibling units, which
  return an empty or diagnostic result so the next source is tried.

`GetVersionsAsync` and `ListVersionsAsync`:

- **Thrown to the caller**: `ArgumentNullException`/`ArgumentException` for invalid arguments,
  `ArgumentOutOfRangeException` for an invalid timeout, `InvalidOperationException` (or
  `NuGetAuthenticationException` for 401/403) when nothing was found and a source failed, and
  `OperationCanceledException` when the token is canceled (never treated as a skipped source).
- **Handled locally**: `HttpRequestException` and `NuGetProtocolException` while enumerating a
  source skip that source; a package carried by no source yields an empty list, not an exception.

`GetCachedVersions`: throws `ArgumentNullException`/`ArgumentException` for an invalid `packageId`; otherwise
never throws for a missing package or folder (empty list).

### Dependencies

- `PackageSourceResolver` (resource resolution, version operations and `EnsureCachedAsync`).
- `PackageDownloader` (download, signature policy, and package-path convention, `EnsureCachedAsync`).
- `CredentialServiceRegistrar` / `ICredentialServiceRegistrar` (credential service registration)
  and `CredentialDispatcher` (per-call scope).
- `NuGetLoggerAdapter` (adapts `Microsoft.Extensions.Logging.ILogger` for the NuGet SDK).
- `NuGetPackageVersion` (result record of `ListVersionsAsync`).
- `PathHelpers` (safe path combination for the sentinel-file path).
- `AuthFailureClassifier` (used indirectly through the resolver and downloader).
- **NuGet** (OTS): `NuGet.Configuration` (`Settings`, `SettingsUtility`, `PackageSourceProvider`,
  package source mapping) and `NuGet.Protocol` (`SourceRepository`, `FindPackageByIdResource`,
  `PackageMetadataResource`, `SourceCacheContext`).

### Callers

- External consumers of the library (the public API surface), such as build tools and
  automation that need a package cached or its available versions listed.
- The test assembly, through the internal cores exposed via `InternalsVisibleTo`.
