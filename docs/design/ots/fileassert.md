## FileAssert Integration Design

### Purpose

FileAssert asserts properties of generated files, such as existence and content, so the
pipeline fails when expected documents are missing or malformed.

### Features Used

- File existence and content assertions against generated HTML and PDF documents and NuGet
  package contents.
- Self-validation producing TRX results.

### Integration Pattern

Run as a local .NET tool in the CI documentation job after document generation. It is a
development-time tool only.
