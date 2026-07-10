# Introduction

## Purpose

This document provides the design documentation for the DemaConsulting NuGet Caching
library. It serves as the bridge between requirements and implementation for formal
code review and compliance auditing. Reviewers use this document to verify that the
implementation matches the design, and auditors use it to trace requirements through
design to code.

## Scope

This document covers the following software items:

Local items:

- **NuGetCaching**: system and unit design for all local components.

OTS items:

- **BuildMark**: integration and usage design.
- **FileAssert**: integration and usage design.
- **NuGet**: integration and usage design.
- **Pandoc**: integration and usage design.
- **Polyfill**: integration and usage design.
- **ReqStream**: integration and usage design.
- **ReviewMark**: integration and usage design.
- **SarifMark**: integration and usage design.
- **SonarMark**: integration and usage design.
- **SysML2Tools**: integration and usage design.
- **VersionMark**: integration and usage design.
- **WeasyPrint**: integration and usage design.
- **WireMock.Net**: integration and usage design.
- **xUnit**: integration and usage design.

The following topics are out of scope:

- NuGet protocol internals and third-party library design
- Build and packaging infrastructure
- Platform-specific NuGet configuration details
- Test projects

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` (see
`docs/sysml2/model/**/*.sysml`) and rendered to the diagram below by SysML2Tools as part
of the build pipeline. The SysML2 model — not this diagram or prose — is the authoritative,
machine-queryable source of structure. AI agents should query the SysML2 model directly
(see the `sysml2tools-query` skill) rather than parsing this diagram before deep-diving into
source code.

![Software Structure](SoftwareStructureView.svg)

## Folder Layout

- **src/** - source files and projects
  - **DemaConsulting.NuGet.Caching/** - NuGetCaching system source

## Audience

This document is intended for:

- Software developers working on DemaConsulting NuGet Caching
- Quality assurance teams validating design against requirements
- Code reviewers assessing correctness and security of the implementation
- Auditors verifying design traceability
