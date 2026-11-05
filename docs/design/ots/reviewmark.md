## ReviewMark Integration Design

### Purpose

ReviewMark plans file reviews and tracks review evidence, ensuring that every in-scope file
belongs to a review-set and that review evidence is current.

### Features Used

- `.reviewmark.yaml` review-set definitions with file patterns.
- The lint mode to validate configuration and file coverage.
- Generation of the review plan and review report.

### Integration Pattern

Run as a local .NET tool in lint and CI pipelines. It is a development-time tool and is never
loaded by the shipped library.
