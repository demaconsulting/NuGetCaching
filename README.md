# DemaConsulting NuGet Caching

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

DEMA Consulting library for caching NuGet packages on the local global packages cache and
discovering the available versions of a package.

![NuGetCaching architecture: your application calling NuGetCache, with nuget.config, NuGet feeds and the global packages folder][architecture]

## Features

- 📦 **Package Caching** - Ensure NuGet packages are in the global packages cache
- 🔍 **Version Discovery** - List available package versions as normalized strings
- ⚙️ **Config Aware** - Uses your NuGet sources and global packages folder, like `dotnet`
- 🗺️ **Source Mapping** - Honors NuGet package source mapping
- 🚨 **Clear Errors** - Failures list each source's error when none can answer
- 🔄 **Multi-Target** - Supports .NET Standard 2.0, .NET 8, 9, and 10
- 🖥️ **Cross-Platform** - Runs on Windows, Linux, and macOS
- ✅ **Continuous Compliance** - Requirements traceability and compliance evidence on every CI run

## API Documentation

Detailed API documentation for all public types and members is distributed in the `api/` folder
of the NuGet package.

## Installation

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.NuGet.Caching
```

## Usage

```csharp
using DemaConsulting.NuGet.Caching;

// Ensure a specific NuGet package version is available in the local global packages cache
string packagePath = await NuGetCache.EnsureCachedAsync("Newtonsoft.Json", "13.0.3");
Console.WriteLine($"Package cached at: {packagePath}");

// Discover available versions (normalized strings, ascending); stable versions only by default
IReadOnlyList<string> versions = await NuGetCache.GetVersionsAsync("Newtonsoft.Json");
Console.WriteLine(versions.Count > 0
    ? $"Latest stable version: {versions[^1]}"
    : "No stable versions found.");
```

## Documentation

Generated documentation includes:

- **Build Notes**: Release information and changes
- **User Guide**: Comprehensive usage documentation
- **Code Quality Report**: CodeQL and SonarCloud analysis results
- **Requirements**: Functional and non-functional requirements
- **Requirements Justifications**: Detailed requirement rationale
- **Trace Matrix**: Requirements to test traceability

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup and
contribution guidelines.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/NuGetCaching?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/NuGetCaching?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/NuGetCaching?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/NuGetCaching?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/NuGetCaching/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_NuGetCaching&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_NuGetCaching&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.NuGet.Caching?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/NuGetCaching/network/members
[link-stars]: https://github.com/demaconsulting/NuGetCaching/stargazers
[link-contributors]: https://github.com/demaconsulting/NuGetCaching/graphs/contributors
[link-license]: https://github.com/demaconsulting/NuGetCaching/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/NuGetCaching/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_NuGetCaching
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_NuGetCaching
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.NuGet.Caching
[architecture]: https://raw.githubusercontent.com/demaconsulting/NuGetCaching/main/docs/images/architecture.png
[link-contributing]: https://github.com/demaconsulting/NuGetCaching/blob/main/CONTRIBUTING.md
