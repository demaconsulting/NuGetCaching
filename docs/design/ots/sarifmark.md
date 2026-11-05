## SarifMark Integration Design

### Purpose

SarifMark converts SARIF static-analysis results, such as CodeQL output, into a readable
quality report.

### Features Used

- SARIF to markdown report generation.
- Self-validation producing TRX results.

### Integration Pattern

Run as a local .NET tool in the CI documentation job. Its report feeds the Code Quality
document. It is a development-time tool only.
