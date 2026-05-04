# Introduction

This document provides the verification design for the DemaConsulting NuGet Caching library, a
.NET library for programmatic NuGet package caching.

## Purpose

The purpose of this document is to describe how each requirement for the NuGetCaching library is
verified. For every software item — system and unit — this document names the verification
approach, identifies the test scenarios (including boundary conditions and error paths), describes
what is mocked or stubbed, and maps each requirement to at least one named test scenario. The
document does not restate design; it explains how the design is proven correct.

## Scope

This document covers the verification design for the same software items described in the
*NuGetCaching Software Design Document*:

- **NuGetCaching** — the system as a whole
- **NuGetCache** — the public API unit providing `EnsureCachedAsync`
- **PathHelpers** — the internal safe path-combination utility

The following topics are out of scope:

- Test infrastructure (xUnit framework, test helpers)
- Build pipeline and CI/CD configuration

The following OTS items are also covered:

- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

## Software Structure

The following tree shows the software items covered by this document:

```text
NuGetCaching (System)
├── NuGetCache (Unit)
└── PathHelpers (Unit)

OTS Items
├── BuildMark
├── FileAssert
├── Pandoc
├── ReqStream
├── ReviewMark
├── SarifMark
├── SonarMark
├── VersionMark
├── WeasyPrint
└── xUnit
```

## Companion Artifact Structure

In-house items have corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml` (kebab-case)
- Verification: `docs/verification/ots/{ots-name}.md` (kebab-case)

Review-sets: defined in `.reviewmark.yaml`
