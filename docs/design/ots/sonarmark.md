## SonarMark Integration Design

### Purpose

SonarMark reports SonarCloud quality-gate results as a markdown report.

### Features Used

- Retrieval of quality-gate status and issues from SonarCloud.
- Self-validation producing TRX results.

### Integration Pattern

Run as a local .NET tool in the CI documentation job. Its report feeds the Code Quality
document. It is a development-time tool only.
