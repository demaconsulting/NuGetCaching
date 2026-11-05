## ReqStream Integration Design

### Purpose

ReqStream processes the requirements YAML files, maps them to test results, and enforces that
every requirement is satisfied by passing tests.

### Features Used

- Root `requirements.yaml` with includes for system, unit, and OTS requirement files.
- Test-result mapping from TRX files, and the `--enforce` traceability check.
- Generation of the requirements document and trace matrix.

### Integration Pattern

Run as a local .NET tool from `.config/dotnet-tools.json` in lint and CI pipelines. It is a
development-time tool and is never loaded by the shipped library.
