## WireMock.Net Integration Design

### Purpose

WireMock.Net is an HTTP mock server used by the test suite to simulate NuGet feeds without
network access to nuget.org.

### Features Used

- In-process HTTP server with request matching and canned responses.
- Responses for the NuGet V3 service index, flat-container version lists, and package downloads,
  plus V2 and failure responses (401, 403, 500).

### Integration Pattern

The `NuGetTestServer` test helper wraps WireMock.Net and registers the feed endpoints each test
needs. Tests generate a temporary `nuget.config` that points at the mock feed so all tests are
deterministic and offline. WireMock.Net is a test-only dependency.
