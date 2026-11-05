## VersionMark Integration Design

### Purpose

VersionMark captures and publishes the versions of the tools used in each build job.

### Features Used

- Capture of tool versions per CI job.
- Publication of a consolidated versions report.

### Integration Pattern

Run as a local .NET tool in CI jobs and in lint. Its report is included in the build notes. It
is a development-time tool only.
