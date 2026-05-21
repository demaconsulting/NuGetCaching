## Polyfill Verification

This document provides the verification evidence for the `Polyfill` OTS software item.
Requirements for this OTS item are defined in the Polyfill OTS Software Requirements document
(`docs/reqstream/ots/polyfill.yaml`).

### Required Functionality

The Polyfill package is a source-generator-based compatibility library. The library project
targets `netstandard2.0`; on that target Polyfill injects compatible implementations of BCL APIs
that are absent on that framework. Currently relied-upon APIs include:

1. `Path.GetRelativePath` — called inside `PathHelpers.SafePathCombine` to verify the resolved
   combined path has not escaped the base directory.
2. `ArgumentNullException.ThrowIfNull` — enabled via `<PolyArgumentExceptions>true</PolyArgumentExceptions>`
   in the project file. `SafePathCombine` calls this method to validate both parameters before any
   path logic runs.

On `net5.0` and later both APIs are provided by the BCL and Polyfill is a no-op.

### Verification Approach

Polyfill is verified by the project's unit test suite. The library project targets
`netstandard2.0`; the `net481` test-project target (Windows-only) consumes the `netstandard2.0`
library binary, making it the target on which both polyfilled APIs are active. On Windows builds
the test suite runs on four targets: `net481`, `net8.0`, `net9.0`, and `net10.0`; on non-Windows
builds it runs on three targets: `net8.0`, `net9.0`, and `net10.0`. The CI pipeline includes a
Windows runner, so polyfill coverage is obtained on every CI build.

All 12 `PathHelpersTests` scenarios exercise one or both polyfilled APIs on `net481`:

- The 2 null-argument tests exercise `ArgumentNullException.ThrowIfNull` (the call that throws
  before path logic runs).
- The remaining 10 tests exercise `Path.GetRelativePath` (called inside `SafePathCombine` after
  the null checks).

### Test Scenarios

#### PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a valid base path and a valid multi-segment
relative path. `Path.GetRelativePath` is called internally to confirm the result stays within
the base directory.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with a relative path beginning with `../`. After
combining, `Path.GetRelativePath` is called and the result starts with `..`, triggering the
traversal guard.

**Expected**: Throws `ArgumentException` with message containing `"Invalid path component"` and
`ParamName` equal to `"relativePath"`.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with a relative path that contains embedded `../..`
sequences in the middle. `Path.GetRelativePath` is called and the escaped result triggers the
traversal guard.

**Expected**: Throws `ArgumentException` with message containing `"Invalid path component"`.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with a Unix absolute path (`/etc/passwd`) as the
relative argument. `Path.GetRelativePath` is called; the result starts with `..`, triggering
the traversal guard. A Windows-style absolute path is additionally tested on Windows.

**Expected**: Throws `ArgumentException` with message containing `"Invalid path component"`.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a filename that begins with `..` but is not a
traversal segment (e.g. `"..data"`). `Path.GetRelativePath` is called and the result does not
start with a traversal sequence.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a relative path starting with `./`
(e.g. `"./subfolder/file.txt"`). `Path.GetRelativePath` is called and confirms the result is
within the base directory.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath

**Scenario**: `SafePathCombine` is called with an empty string as the relative path.
`Path.GetRelativePath` is called on the base path combined with an empty string.

**Expected**: Returns the base path unchanged without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_SimpleFilename_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a single-segment filename (e.g. `"file.txt"`).
`Path.GetRelativePath` confirms the result is within the base directory.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a multi-level nested relative path
(e.g. `"documents/work/report.pdf"`). `Path.GetRelativePath` confirms the result is within
the base directory.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_GuidBasedFilename_CombinesSuccessfully

**Scenario**: `SafePathCombine` is called with a GUID-based temporary filename.
`Path.GetRelativePath` confirms the result is within the base directory.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException

**Scenario**: `SafePathCombine` is called with `null` as `basePath`. The polyfilled
`ArgumentNullException.ThrowIfNull` is called first and throws immediately.

**Expected**: Throws `ArgumentNullException` with `ParamName` equal to `"basePath"`.

**Requirement coverage**: `Caching-OTS-Polyfill`.

#### PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException

**Scenario**: `SafePathCombine` is called with `null` as `relativePath`. The polyfilled
`ArgumentNullException.ThrowIfNull` is called and throws immediately.

**Expected**: Throws `ArgumentNullException` with `ParamName` equal to `"relativePath"`.

**Requirement coverage**: `Caching-OTS-Polyfill`.

### Requirements Coverage

- **`Caching-OTS-Polyfill`**: PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly,
  PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException,
  PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException,
  PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException,
  PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly,
  PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly,
  PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath,
  PathHelpers_SafePathCombine_SimpleFilename_CombinesCorrectly,
  PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly,
  PathHelpers_SafePathCombine_GuidBasedFilename_CombinesSuccessfully,
  PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException,
  PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException
