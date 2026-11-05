## ApiMark Integration Design

### Purpose

ApiMark is an API documentation generator that produces Markdown documentation from the NuGet
Caching library assembly and its XML documentation file. It is chosen because it generates
hierarchical per-type and per-member Markdown files that can be bundled into the NuGet package,
making all public types and members discoverable by consumers and automated agents.

### Features Used

- **Assembly-based API documentation generation**: reads the compiled library assembly
  (`DemaConsulting.NuGet.Caching.dll`) and its companion XML documentation file to extract public
  type and member documentation comments, including the namespace overview in `NamespaceDoc.cs`.
- **Hierarchical Markdown output**: emits a top-level API index (`api.md`), namespace pages, type
  pages, and member pages using a folder structure that mirrors the namespace hierarchy.
- **NuGet package inclusion**: the generated Markdown files are included in the `api/` folder of
  the NuGet package so package consumers can read API documentation without visiting an external
  site.

### Integration Pattern

ApiMark is integrated through the `DemaConsulting.ApiMark.MSBuild` NuGet package reference in
`DemaConsulting.NuGet.Caching.csproj`. It runs during `dotnet build` for the `netstandard2.0`
target framework and writes Markdown output to `bin/{Configuration}/apimark-docs`, which is
excluded from source control by the `bin/` ignore rule. During `dotnet pack`, ApiMark packs those
files into the package under `api/` when `ApiMarkPackDocs=true`. FileAssert validates the
resulting `.nupkg` archive to confirm that `api/DemaConsulting.NuGet.Caching.md`, `api/api.md`,
and related files are present.
