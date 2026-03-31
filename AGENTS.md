# Agent Quick Reference

Project-specific guidance for agents working on DemaConsulting NuGet Caching - a library
for caching NuGet packages on the local PC.

## Standards Application (ALL Agents Must Follow)

Before performing any work, agents must read and apply the relevant standards from `.github/standards/`:

- **`csharp-language.md`** - For C# code development (literate programming, XML docs, dependency injection)
- **`csharp-testing.md`** - For C# test development (AAA pattern, naming, MSTest anti-patterns)
- **`reqstream-usage.md`** - For requirements management (traceability, semantic IDs, source filters)
- **`reviewmark-usage.md`** - For file review management (review-sets, file patterns, enforcement)
- **`software-items.md`** - For software categorization (system/subsystem/unit/OTS classification)
- **`technical-documentation.md`** - For documentation creation and maintenance (structure, Pandoc, README best practices)

Load only the standards relevant to your specific task scope and apply their
quality checks and guidelines throughout your work.

## Agent Delegation Guidelines

The default agent should handle simple, straightforward tasks directly.
Delegate to specialized agents only for specific scenarios:

- **Light development work** (small fixes, simple features) → Call @developer agent
- **Light quality checking** (linting, basic validation) → Call @quality agent
- **Formal feature implementation** (complex, multi-step) → Call the `@implementation` agent
- **Formal bug resolution** (complex debugging, systematic fixes) → Call the `@implementation` agent
- **Formal reviews** (compliance verification, detailed analysis) → Call @code-review agent
- **Template consistency** (downstream repository alignment) → Call @repo-consistency agent

## Available Specialized Agents

- **code-review** - Performs formal reviews using standardized review processes
- **developer** - General-purpose development agent applying appropriate standards
- **implementation** - Orchestrator agent for complex multi-step implementations
- **quality** - Quality assurance agent grading work against DEMA standards
- **repo-consistency** - Ensures downstream repositories remain consistent with template patterns

## Tech Stack

- C# 12, .NET 8.0/9.0/10.0, dotnet CLI, NuGet

## Key Files

- **`requirements.yaml`** - All requirements with test linkage (enforced via `dotnet reqstream --enforce`)
- **`.editorconfig`** - Code style (file-scoped namespaces, 4-space indent, UTF-8, LF endings)
- **`.cspell.yaml`, `.markdownlint-cli2.yaml`, `.yamllint.yaml`** - Linting configs

### Spell check word list policy

**Never** add a word to the `.cspell.yaml` word list in order to silence a spell-checking failure.
Doing so defeats the purpose of spell-checking and reduces the quality of the repository.

- If cspell flags a word that is **misspelled**, fix the spelling in the source file.
- If cspell flags a word that is a **genuine technical term** (tool name, project identifier, etc.) and is
  spelled correctly, raise a **proposal** (e.g. comment in a pull request) explaining why the word
  should be added. The proposal must be reviewed and approved before the word is added to the list.

## Requirements

- All requirements MUST be linked to tests
- Not all tests need to be linked to requirements (tests may exist for corner cases, design testing, failure-testing, etc.)
- Enforced in CI: `dotnet reqstream --requirements requirements.yaml --tests "artifacts/**/*.trx" --enforce`
- When adding features: add requirement + link to test

## Test Source Filters

Test links in `requirements.yaml` can include a source filter prefix to restrict which test results count as
evidence. This is critical for platform and framework requirements - **do not remove these filters**.

- `windows@TestName` - proves the test passed on a Windows platform
- `ubuntu@TestName` - proves the test passed on a Linux (Ubuntu) platform
- `net8.0@TestName` - proves the test passed under the .NET 8 runtime
- `net9.0@TestName` - proves the test passed under the .NET 9 runtime
- `net10.0@TestName` - proves the test passed under the .NET 10 runtime

Without the source filter, a test result from any platform/framework satisfies the requirement. Adding the filter
ensures the CI evidence comes specifically from the required environment.

## Testing

- **Test Naming**: `ClassName_MethodUnderTest_Scenario_ExpectedBehavior` for unit tests
- **Test Framework**: Uses MSTest for unit testing
- **Code Coverage**: Maintain high code coverage for library APIs

## Code Style

- **XML Docs**: On ALL members (public/internal/private) with spaces after `///` in summaries
- **Namespace**: File-scoped namespaces only
- **Using Statements**: Top of file only (no nested using declarations except for IDisposable)
- **String Formatting**: Use interpolated strings ($"") for clarity

## Project Structure

- **DemoClass.cs**: Placeholder class (NuGet caching API will be added here)
- **DemaConsulting.NuGet.Caching.csproj**: Project configuration with NuGet package settings

## Build and Test

```bash
# Build the project
dotnet build --configuration Release

# Run unit tests
dotnet test --configuration Release

# Use convenience scripts
./build.sh    # Linux/macOS
build.bat     # Windows
```

## Documentation

- **User Guide**: `docs/guide/guide.md`
- **Requirements**: `requirements.yaml` -> auto-generated docs
- **Build Notes**: Auto-generated via BuildMark
- **Code Quality**: Auto-generated via CodeQL and SonarMark
- **Trace Matrix**: Auto-generated via ReqStream
- **CHANGELOG.md**: Not present - changes are captured in the auto-generated build notes

## Markdown Link Style

- **AI agent markdown files** (`.github/agents/*.md`): Use inline links `[text](url)` so URLs are visible in agent context
- **README.md**: Use absolute URLs (shipped in NuGet package)
- **All other markdown files**: Use reference-style links `[text][ref]` with `[ref]: url` at document end

## CI/CD

- **Quality Checks**: Markdown lint, spell check, YAML lint
- **Build**: Multi-platform (Windows/Linux/macOS)
- **CodeQL**: Security scanning
- **Documentation**: Auto-generated via Pandoc + Weasyprint

## Common Tasks

```bash
# Format code
dotnet format

# Run all linters
./lint.sh     # Linux/macOS
lint.bat      # Windows

# Pack as NuGet tool
dotnet pack --configuration Release
```

## Agent Report Files

When agents need to write report files to communicate with each other or the user, follow these guidelines:

- **Naming Convention**: Use the pattern `AGENT_REPORT_xxxx.md` (e.g., `AGENT_REPORT_analysis.md`, `AGENT_REPORT_results.md`)
- **Purpose**: These files are for temporary inter-agent communication and should not be committed
- **Exclusions**: Files matching `AGENT_REPORT_*.md` are automatically:
  - Excluded from git (via .gitignore)
  - Excluded from markdown linting
  - Excluded from spell checking

## Agent Log Files

Agents may store detailed logs and intermediate work in the `.agent-logs/` folder:

- **Location**: `.agent-logs/[agent-name]-[subject]-[unique-id].md`
- **Purpose**: Detailed work summaries, research results, and completion reports
- **Exclusions**: The `.agent-logs/` folder is automatically:
  - Excluded from git (via .gitignore)
  - Excluded from markdown linting
  - Excluded from spell checking
  - Excluded from YAML linting
