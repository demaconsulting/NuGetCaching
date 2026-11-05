## BuildMark Integration Design

### Purpose

BuildMark generates build notes from repository history, including changes and known issues.

### Features Used

- Build-notes generation in markdown from Git and GitHub data.
- Self-validation producing TRX results.

### Integration Pattern

Run as a local .NET tool in the CI documentation job. Its markdown output is converted to HTML
and PDF by Pandoc and WeasyPrint. It is a development-time tool only.
