## SysML2Tools Integration Design

### Purpose

SysML2Tools lints, queries, and renders the SysML2 architecture model under `docs/sysml2/`.

### Features Used

- Model linting in the lint pipeline.
- Querying of software structure by AI agents.
- Rendering of the structure diagrams embedded in the design document.

### Integration Pattern

Run as a local .NET tool in lint and CI pipelines. It is a development-time tool only.
