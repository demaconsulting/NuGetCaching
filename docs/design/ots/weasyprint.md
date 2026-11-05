## WeasyPrint Integration Design

### Purpose

WeasyPrint renders the HTML documents produced by Pandoc into PDF compliance documents.

### Features Used

- HTML and CSS to PDF rendering.

### Integration Pattern

Run through its local .NET tool wrapper in the CI documentation job. FileAssert then verifies
the produced PDFs. It is a development-time tool only.
