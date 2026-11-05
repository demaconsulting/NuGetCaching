## Polyfill Integration Design

### Purpose

Polyfill is a source-generator package that supplies newer BCL APIs on older target frameworks.
It lets the library target `netstandard2.0` while using modern APIs.

### Features Used

- `Path.GetRelativePath` on `netstandard2.0`, used by `PathHelpers.SafePathCombine`.
- `ArgumentNullException.ThrowIfNull`, enabled with the `PolyArgumentExceptions` project
  property.

### Integration Pattern

The package is referenced with `PrivateAssets="All"` so it is a compile-time dependency only; the
generated code is built into the library and nothing flows to consumers. On .NET 5.0 and later the
BCL implementations are used and Polyfill generates nothing.
