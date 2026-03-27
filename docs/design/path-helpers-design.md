# PathHelpers Design

## Overview

The `PathHelpers` class is an internal static helper class providing safe path-combination
utilities for the DemaConsulting NuGet Caching library. It is a software unit in the
sense of IEC 62304 — the smallest independently testable component responsible for
preventing path-traversal vulnerabilities when constructing file-system paths from
external inputs such as NuGet package identifiers and version strings.

The class is marked `internal` because it is an implementation detail of the library
and is not part of the public API surface.

## Design Decisions

### Internal Class

`PathHelpers` is declared `internal` to enforce encapsulation. Callers outside the
assembly have no need to perform safe path combination using this class; they interact
only with the `NuGetCache` public API. Keeping the class internal prevents accidental
coupling to an implementation detail and keeps the public API surface minimal.

### Static Class

All operations in `PathHelpers` are pure functions that depend only on their input
parameters and have no side effects. A static class is therefore appropriate: it
requires no instantiation and has no lifecycle to manage.

### Defense-in-Depth Validation

`SafePathCombine` applies two independent layers of path-traversal protection:

1. **Pre-combination checks** — the relative path is rejected immediately if it
   contains `".."` or if `Path.IsPathRooted` returns `true`. These checks cover the
   common, obvious attack vectors and are evaluated before any path manipulation.

   > Note: the `".."` check uses a substring match, so it also rejects filenames that
   > happen to contain the character sequence `..` (such as `file..txt`). This is
   > intentionally conservative: NuGet package identifiers and version strings do not
   > contain `..` as a substring, so no false-positive rejections occur in practice.

2. **Post-combination checks** (NET5.0+) — after calling `Path.Combine`, the method
   resolves both the base path and the combined path to their canonical full forms
   using `Path.GetFullPath`, then uses `Path.GetRelativePath` to verify that the
   combined path is still subordinate to the base. This guard catches edge cases that
   might survive the pre-combination checks on specific operating systems or file-system
   configurations, such as unusual Unicode representations or OS-specific separator
   handling.

   > **Limitation:** `Path.GetFullPath` normalizes `.` and `..` segments in the path
   > string but does not resolve symbolic links. A symbolic link within the base
   > directory that points outside it would not be detected by this check.
   > Symlink-based traversal attacks are therefore outside the scope of this
   > protection; callers are responsible for ensuring the base directory contains no
   > untrusted symbolic links.

The two-layer approach ensures robustness against both obvious and subtle path-traversal
attacks without relying on a single validation mechanism.

### Rejection Strategy

Rather than sanitizing or normalizing an invalid path, `SafePathCombine` throws an
`ArgumentException` immediately when a violation is detected. Failing fast with a clear
exception is preferable to silently correcting or ignoring potentially malicious input,
as it surfaces bugs in calling code during development and makes security boundaries
explicit.

### Platform Considerations

The post-combination `Path.GetFullPath` / `Path.GetRelativePath` check is conditionally
compiled for `NET5_0_OR_GREATER`. On older target frameworks, only the pre-combination
checks apply. The NuGet Caching library targets modern .NET, so in practice both
validation layers are always active in production.

## Method Descriptions

### `SafePathCombine(string basePath, string relativePath)`

Safely combines `basePath` and `relativePath`, returning the resulting path string.

The method:

1. Validates that neither argument is null (`ArgumentNullException.ThrowIfNull`),
   throwing `ArgumentNullException` for a null argument.
2. Rejects `relativePath` if it contains `".."` or is rooted (`Path.IsPathRooted`),
   throwing `ArgumentException` with a descriptive message.
3. Calls `Path.Combine(basePath, relativePath)` to produce the combined path.
4. On .NET 5 and later, resolves both the base and combined paths to canonical forms
   and uses `Path.GetRelativePath` to confirm the combined path remains under the base,
   throwing `ArgumentException` if it does not.
5. Returns the combined path string.

Satisfies requirement `Caching-PathHelpers-SafeCombine`.
