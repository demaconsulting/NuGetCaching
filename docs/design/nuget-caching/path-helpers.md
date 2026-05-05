## PathHelpers Design

### Overview

`PathHelpers` is an internal static utility class providing a safe path-combination method
in the DemaConsulting NuGet Caching library. It protects callers against path-traversal
attacks by verifying the resolved combined path stays within the base directory. Note that
`Path.GetFullPath` normalizes `.`/`..` segments but does not resolve symlinks or reparse
points, so this check guards against string-level traversal only.

The class is marked `internal` because it is an implementation detail of the library and is
not part of the public API surface.

### Class Structure

#### SafePathCombine Method

```csharp
internal static string SafePathCombine(string basePath, string relativePath)
```

Combines `basePath` and `relativePath` safely, ensuring the resulting path remains within
the base directory.

**Validation steps:**

1. Reject null inputs via `ArgumentNullException.ThrowIfNull`.
2. Combine the paths with `Path.Combine` to produce the candidate path (preserving the
   caller's relative/absolute style).
3. Resolve both `basePath` and the candidate to absolute form with `Path.GetFullPath`.
4. Compute `Path.GetRelativePath(absoluteBase, absoluteCombined)` and reject the input if
   the result is exactly `".."`, starts with `".."` followed by
   `Path.DirectorySeparatorChar` or `Path.AltDirectorySeparatorChar`, or is itself rooted
   (absolute), which would indicate the combined path escapes the base directory.

### Design Decisions

#### Internal Class

`PathHelpers` is declared `internal` to enforce encapsulation. Callers outside the
assembly have no need to perform safe path combination using this class; they interact
only with the `NuGetCache` public API. Keeping the class internal prevents accidental
coupling to an implementation detail and keeps the public API surface minimal.

#### Static Class

All operations in `PathHelpers` are pure functions that depend only on their input
parameters and have no side effects. A static class is therefore appropriate: it
requires no instantiation and has no lifecycle to manage.

#### `Path.GetRelativePath` for Containment Check

Using `GetRelativePath` to verify containment handles root paths (e.g. `/`, `C:\`),
platform case-sensitivity, and directory-separator normalization natively. The containment
test treats `..` as an escaping segment only when it is the entire relative result or is
followed by a directory separator, avoiding false positives for valid in-base names such
as `..data`.

#### Post-Combine Canonical-Path Check

Resolving paths after combining handles all traversal patterns — `../`, embedded `/../`,
absolute-path overrides, and platform edge cases — without fragile pre-combine string
inspection of `relativePath`.

#### .NET Standard 2.0 Compatibility

`Path.GetRelativePath` is available on all supported target frameworks via the Polyfill
library, which provides a compatible implementation for `netstandard2.0`.

> **Limitation:** `Path.GetFullPath` normalizes `.` and `..` segments in the path string
> but does not resolve symbolic links. A symbolic link within the base directory that
> points outside it would not be detected by this check. Symlink-based traversal attacks
> are therefore outside the scope of this protection; callers are responsible for ensuring
> the base directory contains no untrusted symbolic links.

#### Rejection Strategy

Rather than sanitizing or normalizing an invalid path, `SafePathCombine` throws an
`ArgumentException` immediately when a violation is detected. Failing fast with a clear
exception is preferable to silently correcting or ignoring potentially malicious input,
as it surfaces bugs in calling code during development and makes security boundaries
explicit.

#### No Logging or Error Accumulation

`SafePathCombine` is a pure utility method that throws on invalid input; it does not
interact with any context or output mechanism.

### Method Descriptions

#### `SafePathCombine(string basePath, string relativePath)`

Safely combines `basePath` and `relativePath`, returning the resulting path string.

The method:

1. Validates that neither argument is null (`ArgumentNullException.ThrowIfNull`),
   throwing `ArgumentNullException` for a null argument.
2. Calls `Path.Combine(basePath, relativePath)` to produce the combined path.
3. Resolves both the base and combined paths to canonical forms using `Path.GetFullPath`.
4. Uses `Path.GetRelativePath` to confirm the combined path remains under the base,
   throwing `ArgumentException` if it does not.
5. Returns the combined path string.

The method propagates `NotSupportedException` from `Path.GetFullPath` when a supplied path
contains an unsupported format, and propagates `PathTooLongException` from `Path.GetFullPath`
or `Path.Combine` when the combined or resolved path exceeds the system-defined maximum length.

Satisfies requirement `Caching-PathHelpers-SafeCombine`.
