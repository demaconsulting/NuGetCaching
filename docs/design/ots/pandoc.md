## Pandoc Integration Design

### Purpose

Pandoc converts the markdown design, requirements, verification, and report documents to HTML.

### Features Used

- Markdown to HTML conversion driven by `definition.yaml` input lists and an HTML template.
- Table of contents and section numbering.

### Integration Pattern

Run through its local .NET tool wrapper in the CI documentation job. The HTML output is then
rendered to PDF by WeasyPrint. It is a development-time tool only.
