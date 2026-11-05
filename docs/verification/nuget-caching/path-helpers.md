## PathHelpers Unit Verification

This document provides the verification design for the `PathHelpers` unit. Requirements for this
unit are defined in the PathHelpers Unit Requirements document.

### Required Functionality

The `PathHelpers` unit shall safely combine a base path and a relative path that stays within the
base directory, and shall reject any combination that would escape the base directory.

### Verification Approach

Unit requirements are verified by tests that exercise `PathHelpers.SafePathCombine` across a range
of valid and invalid input combinations. Each test scenario names a specific test method that
provides evidence for the unit requirement.

### Test Environment

No setup beyond the standard test runner: the tests are pure unit tests that use the local file
system path syntax of the host OS and require no network, external service, or configuration.

### Acceptance Criteria

All test scenarios below pass on every supported target framework: valid inputs return the
expected combined path, and every traversal input is rejected with the documented exception.

### Test Scenarios

#### PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a valid base path and a valid relative path that
stays within the base.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with a relative path of `..` that would escape the base
directory.

**Expected**: Throws `ArgumentException`.

**Requirement coverage**: `Caching-PathHelpers-RejectTraversal`.

#### PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with a relative path containing an embedded `../` that
would escape the base directory.

**Expected**: Throws `ArgumentException`.

**Requirement coverage**: `Caching-PathHelpers-RejectTraversal`.

#### PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with an absolute path as the relative component.

**Expected**: Throws `ArgumentException` because the relative component must not be rooted.

**Requirement coverage**: `Caching-PathHelpers-RejectTraversal`.

#### PathHelpers_SafePathCombine_RootedPathInsideBase_ThrowsArgumentException

**Scenario**: `SafePathCombine` is called with a rooted relative component that resolves to a location
inside the base directory.

**Expected**: Throws `ArgumentException`; rooted relative paths are rejected even when contained.

**Requirement coverage**: `Caching-PathHelpers-RejectTraversal`.

#### PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a relative path whose name starts with `..` but
does not perform traversal (e.g., `..data`).

**Expected**: Returns the correctly combined path without throwing, confirming no false positive.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a relative path containing nested subdirectories
that stay within the base.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_SimpleFilename_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a simple filename as the relative component.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_GuidBasedFilename_CombinesSuccessfully

**Scenario**: `SafePathCombine` is called with a GUID-format filename as the relative component.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath

**Scenario**: `SafePathCombine` is called with an empty string as the relative component.

**Expected**: Returns the base path without throwing.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly

**Scenario**: `SafePathCombine` is called with a path starting with `./` as the relative
component.

**Expected**: Returns the correctly combined path without throwing.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException

**Scenario**: `SafePathCombine` is called with a null base path.

**Expected**: Throws `ArgumentNullException` with parameter name `basePath`.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

#### PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException

**Scenario**: `SafePathCombine` is called with a null relative path.

**Expected**: Throws `ArgumentNullException` with parameter name `relativePath`.

**Requirement coverage**: `Caching-PathHelpers-Combine`.

### Requirements Coverage

- **`Caching-PathHelpers-Combine`**:
  PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly,
  PathHelpers_SafePathCombine_DotDotPrefixedName_CombinesCorrectly,
  PathHelpers_SafePathCombine_NestedPaths_CombinesCorrectly,
  PathHelpers_SafePathCombine_SimpleFilename_CombinesCorrectly,
  PathHelpers_SafePathCombine_GuidBasedFilename_CombinesSuccessfully,
  PathHelpers_SafePathCombine_EmptyRelativePath_ReturnsBasePath,
  PathHelpers_SafePathCombine_CurrentDirectoryReference_CombinesCorrectly,
  PathHelpers_SafePathCombine_NullBasePath_ThrowsArgumentNullException,
  PathHelpers_SafePathCombine_NullRelativePath_ThrowsArgumentNullException
- **`Caching-PathHelpers-RejectTraversal`**:
  PathHelpers_SafePathCombine_PathTraversalWithDoubleDots_ThrowsArgumentException,
  PathHelpers_SafePathCombine_DoubleDotsInMiddle_ThrowsArgumentException,
  PathHelpers_SafePathCombine_AbsolutePath_ThrowsArgumentException,
  PathHelpers_SafePathCombine_RootedPathInsideBase_ThrowsArgumentException
