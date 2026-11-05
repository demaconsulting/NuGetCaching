## xUnit Integration Design

### Purpose

xUnit v3 is the unit-testing framework used to run the library's test suite.

### Features Used

- Test attributes (`Fact`, `Theory`) and assertions for unit, integration, and system tests.
- The Visual Studio test runner for `dotnet test` execution and TRX result output.

### Integration Pattern

The test project references xUnit as a test-only dependency. Test names are mapped to
requirements by ReqStream through the TRX results. Assembly-wide test parallelization is
disabled because some tests change process-wide NuGet state.
