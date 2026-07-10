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
- **PackageSourceResolver** — the internal source-resolution utility, including v2 fallback
- **PackageDownloader** — the internal download-and-install utility and package-path convention
- **AuthFailureClassifier** — the internal authentication-failure classification utility
- **CredentialServiceRegistrar** — the internal credential-service registration utility

The following topics are out of scope:

- Test infrastructure (xUnit framework, test helpers)
- Build pipeline and CI/CD configuration

The following OTS items are also covered:

- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **NuGet Client SDK** — NuGet.Protocol and NuGet.Configuration packages used at runtime
- **Pandoc** — Markdown-to-HTML conversion tool
- **Polyfill** — `Path.GetRelativePath` compatibility shim for netstandard2.0
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model validation and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **WireMock.Net** — HTTP stub server (test-only; not shipped with the production library)
- **xUnit** — unit-testing framework

## Companion Artifact Structure

In-house items have corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system-name}.yaml`, `docs/reqstream/{system-name}/.../{item}.yaml`
- Design: `docs/design/{system-name}.md`, `docs/design/{system-name}/.../{item}.md`
- Verification: `docs/verification/{system-name}.md`, `docs/verification/{system-name}/.../{item}.md`
- Source code: `src/{SystemName}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{SystemName}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml`
- Verification: `docs/verification/ots/{ots-name}.md`

Review-sets: defined in `.reviewmark.yaml`
