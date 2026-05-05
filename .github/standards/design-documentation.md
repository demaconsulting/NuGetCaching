---
name: Design Documentation
description: Follow these standards when creating design documentation.
globs: ["docs/design/**/*.md"]
---

# Design Documentation Standards

This document defines standards for design documentation within Continuous
Compliance environments, extending the general technical documentation
standards with specific requirements for software design artifacts.

## Required Standards

Read these standards first before applying this standard:

- **`technical-documentation.md`** - General technical documentation standards
- **`software-items.md`** - Software categorization (System/Subsystem/Unit/OTS)

# Core Principles

Design documentation serves as the bridge between requirements and
implementation, providing detailed technical specifications that enable:

- **Formal Code Review**: Reviewers can verify implementation matches design
- **Compliance Evidence**: Auditors can trace requirements through design to code
- **Maintenance Support**: Developers can understand system structure and interactions
- **Quality Assurance**: Testing teams can validate against detailed specifications

# Required Structure and Documents

Design documentation must be organized under `docs/design/` with folder structure
mirroring source code organization because reviewers need clear navigation from
design to implementation:

```text
docs/design/
├── introduction.md              # Document overview - heading depth #
├── {system-name}.md             # System-level design - heading depth #
└── {system-name}/               # System folder (one per system)
    ├── {subsystem-name}.md      # Subsystem overview - heading depth ##
    ├── {subsystem-name}/        # Subsystem folder (kebab-case); may nest recursively
    │   ├── {child-subsystem}.md # Child subsystem overview - heading depth ###
    │   ├── {child-subsystem}/   # Child subsystem folder (same structure as parent)
    │   └── {unit-name}.md       # Unit design - heading depth ###
    └── {unit-name}.md           # System-level unit design - heading depth ##
```

Each scope's overview file lives in its **parent** folder, not inside the scope's own
subfolder - this aligns heading depth with folder depth so the compiled PDF has a
meaningful multi-level outline (see Heading Depth Rule in `technical-documentation.md`).

## introduction.md (MANDATORY)

The `introduction.md` file serves as the design entry point and MUST include
these sections because auditors need clear scope boundaries and architectural
overview:

### Purpose Section

Clear statement of the design document's purpose, audience, and regulatory
or compliance drivers.

### Scope Section

Define what software items are covered and what is explicitly excluded.
Design documentation must NOT include test projects, test classes, or test
infrastructure because design documentation documents the architecture of
shipping product code, not ancillary content used to validate it.

### Software Structure Section (MANDATORY)

Include a text-based tree diagram showing the software organization across
System, Subsystem, and Unit levels. Agents MUST read `software-items.md`
to understand these classifications before creating this section.

Example format:

```text
Project1Name (System)
├── ComponentA (Subsystem)
│   ├── SubComponentP (Subsystem)
│   │   └── ClassW (Unit)
│   ├── ClassX (Unit)
│   └── ClassY (Unit)
├── ComponentB (Subsystem)
│   └── ClassZ (Unit)
└── UtilityClass (Unit)

Project2Name (System)
└── HelperClass (Unit)
```

### Folder Layout Section (MANDATORY)

Include a text-based tree diagram showing how the source code folders
mirror the software structure, with file paths and brief descriptions.

Example format:

```text
src/Project1Name/
├── ComponentA/
│   ├── SubComponentP/
│   │   └── ClassW.cs           - Specialized processing engine
│   ├── ClassX.cs               - Core business logic handler
│   └── ClassY.cs               - Data validation service
├── ComponentB/
│   └── ClassZ.cs               - Integration interface
└── UtilityClass.cs             - Common utility functions

src/Project2Name/
└── HelperClass.cs              - Helper functions
```

### References Section (RECOMMENDED)

If the design references external documents (standards, specifications), include
a `## References` section in `introduction.md`. This is the **only** place in the
design document collection where a References section should appear - do not add
one to any other design file.

### Companion Artifact Structure (RECOMMENDED)

Include a brief note explaining that each software item has parallel artifacts
across the repository, so agents and reviewers can navigate from any one
artifact to all related files:

Example format:

```text
Each in-house software item has corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system-name}.yaml`, `docs/reqstream/{system-name}/.../{item}.yaml`
- Design docs:  `docs/design/{system-name}.md`, `docs/design/{system-name}/.../{item}.md`
- Verification: `docs/verification/{system-name}.md`, `docs/verification/{system-name}/.../{item}.md`
- Source code:  `src/{SystemName}/.../{Item}.{ext}` (cased per language - see `software-items.md`)
- Tests:        `test/{SystemName}.Tests/.../{Item}Tests.{ext}` (cased per language)

OTS items have no design documentation; their artifacts sit parallel to system folders:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml`
- Verification: `docs/verification/ots/{ots-name}.md`
- Tests (optional): `test/{OtsSoftwareTests}/...` (cased per language - see `software-items.md`)

Review-sets: defined in `.reviewmark.yaml`
```

## System Design Documentation (MANDATORY)

For each system identified in the repository:

- Create `{system-name}.md` directly under `docs/design/` (heading depth `#`)
- Create a kebab-case folder `{system-name}/` to hold its subsystems and units
- `{system-name}.md` must cover:
  - System architecture and major components
  - External interfaces and dependencies
  - Data flow and control flow
  - System-wide design constraints and decisions
  - Integration patterns and communication protocols

## Subsystem and Unit Design Documents

For each subsystem identified in the software structure:

- Place `{subsystem-name}.md` inside the **parent** folder (the system folder, or parent
  subsystem folder) - not inside its own subfolder
- Create a kebab-case folder `{subsystem-name}/` to hold its child units and subsystems
- `{subsystem-name}.md` must cover subsystem overview and design

For every unit identified in the software structure:

- Place `{unit-name}.md` inside its parent scope's folder (system or subsystem folder)
- Document data models, algorithms, and key methods
- Describe interactions with other units
- Include sufficient detail for formal code review

Follow the Heading Depth Rule from `technical-documentation.md` - a file's top-level
heading depth equals its folder depth under `docs/design/`.

# Software Items Integration (CRITICAL)

Read `software-items.md` before creating design documentation - correct
System/Subsystem/Unit categorization is required for software structure
diagrams and folder layout.

# Writing Guidelines

Design documentation must be technical and specific because it serves as the
implementation specification for formal code review:

- **Implementation Detail**: Provide sufficient detail for code review and implementation
- **Architectural Clarity**: Clearly define component boundaries and interfaces
- **Traceability**: Link to requirements where applicable using ReqStream patterns
- **Verbal Cross-References**: Reference other parts of the design by name (e.g.,
  "See *Parser Design* for more details") - do not use markdown hyperlinks, which
  break in compiled PDFs

# Mermaid Diagram Integration

Use Mermaid diagrams to supplement text descriptions (diagrams must not replace text content).

# Quality Checks

Before submitting design documentation, verify:

- [ ] `introduction.md` includes both Software Structure and Folder Layout sections
- [ ] Software structure correctly categorizes items as System/Subsystem/Unit per `software-items.md`
- [ ] Folder layout mirrors software structure organization
- [ ] Files organized under `docs/design/` following the folder structure pattern above
- [ ] Each file's top-level heading depth matches its folder depth per the Heading Depth Rule
- [ ] Design documents provide sufficient detail for code review
- [ ] System documentation provides comprehensive system-level design
- [ ] All documentation folders use kebab-case names mirroring source code structure
- [ ] All documents follow technical documentation formatting standards
- [ ] Content is current with implementation and requirements
- [ ] Documents are integrated into ReviewMark review-sets for formal review
