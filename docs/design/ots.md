# OTS Integration Design

This section describes how the DemaConsulting NuGet Caching library integrates each
Off-The-Shelf (OTS) item. OTS items are third-party components that are not developed in-house.
Each item has its own design document in this section covering its purpose, the features used,
and the integration pattern.

The OTS items fall into two groups:

- **Library dependencies**: NuGet (run time) and Polyfill (compile time) contribute to the
  shipped library.
- **Development-time tooling**: BuildMark, FileAssert, Pandoc, ReqStream, ReviewMark, SarifMark,
  SonarMark, SysML2Tools, VersionMark, WeasyPrint, WireMock.Net, and xUnit support building,
  testing, documenting, and demonstrating compliance of the library. They are never loaded by
  the shipped library.

The requirements for each OTS item are in the OTS Software Requirements documents, and the
verification evidence is in the OTS Software Verification documents.
