---
name: Verification Documentation
description: Follow these standards when creating software verification design documentation.
globs: ["docs/verification/**/*.md"]
---

# Required Standards

Read these standards first before applying this standard:

- **`technical-documentation.md`** - General technical documentation standards
- **`software-items.md`** - Software categorization (System/Subsystem/Unit/OTS)

# Core Principles

Verification design is the bridge between requirements and tests - it documents HOW
requirements will be verified, enabling reviewers to confirm test completeness without
reading implementation code.

# Required Structure and Documents

Organize under `docs/verification/` mirroring the software item hierarchy:

```text
docs/verification/
├── introduction.md              # Document overview - heading depth #
├── {system-name}.md             # System-level verification - heading depth #
├── {system-name}/               # System folder (one per system)
│   ├── {subsystem-name}.md      # Subsystem verification - heading depth ##
│   ├── {subsystem-name}/        # Subsystem folder (kebab-case); may nest recursively
│   │   ├── {child-subsystem}.md # Child subsystem verification - heading depth ###
│   │   ├── {child-subsystem}/   # Child subsystem folder (same structure as parent)
│   │   └── {unit-name}.md       # Unit verification - heading depth ###
│   └── {unit-name}.md           # System-level unit verification - heading depth ##
├── ots.md                       # OTS section overview - heading depth # (MANDATORY if OTS items exist)
└── ots/                         # OTS items - parallel to system folders (not inside them)
    └── {ots-name}.md            # OTS item verification evidence - heading depth ##
```

Each scope's overview file lives in its **parent** folder, not inside the scope's own
subfolder - this keeps artifact locations consistent with design and requirements trees
so any item's files are deterministically locatable, and aligns heading depth with folder
depth for correct PDF structure (see Heading Depth Rule in `technical-documentation.md`).

## introduction.md (MANDATORY)

Follow the standard `introduction.md` format from `technical-documentation.md`. Scope
covers all software items including OTS items (via self-validation if appropriate).

Include a Companion Artifact Structure note so agents and reviewers can navigate from any
artifact to all related files:

```text
In-house items have parallel artifacts in:
- Requirements: `docs/reqstream/{system-name}.yaml`, `docs/reqstream/{system-name}/.../{item}.yaml`
- Design:        `docs/design/{system-name}.md`, `docs/design/{system-name}/.../{item}.md`
- Verification:  `docs/verification/{system-name}.md`, `docs/verification/{system-name}/.../{item}.md`
- Source:        `src/{SystemName}/.../{Item}.{ext}` (cased per language)
- Tests:         `test/{SystemName}.Tests/.../{Item}Tests.{ext}` (cased per language)

OTS items (no design documentation) have artifacts parallel to system folders:
- Requirements: `docs/reqstream/ots/{ots-name}.yaml`
- Verification: `docs/verification/ots/{ots-name}.md`
- Tests (if required): `test/{OtsSoftwareTests}/...` (cased per language - see `software-items.md`)

Review-sets: defined in `.reviewmark.yaml`
```

If the verification design references external documents (standards, specifications), include
a `## References` section in `introduction.md` only - do not add one to any other verification file.

## System Verification Design (MANDATORY)

For each system, create `{system-name}.md` at `docs/verification/` root and a
`{system-name}/` folder for subsystems. Cover:

- System verification strategy and overall test approach
- Test environments and configuration required
- External interface simulation and test-harness design
- End-to-end and integration test scenarios covering system requirements
- Acceptance criteria and pass/fail conditions at the system boundary
- Coverage mapping of system requirements to system-level test scenarios

## Subsystem Verification Design (MANDATORY)

For each subsystem, place `{subsystem-name}.md` in the parent (system or subsystem)
folder and create a `{subsystem-name}/` folder for its units. Cover:

- Subsystem verification strategy and integration test approach
- Dependencies that must be mocked or stubbed at the subsystem boundary
- Integration test scenarios covering subsystem requirements
- Coverage mapping of subsystem requirements to subsystem-level test scenarios

## Unit Verification Design (MANDATORY)

Place `{unit-name}.md` in the parent (system or subsystem) folder. Cover:

- Verification approach for each unit requirement
- Named test scenarios including boundary conditions, error paths, and normal-operation cases
- Which dependencies are mocked and how they are configured
- Coverage mapping of every unit requirement to at least one named test scenario

## OTS Verification Evidence (when OTS items are used)

Create `docs/verification/ots.md` at the collection root with a `#` top-level heading. This
file introduces the OTS verification approach and ensures OTS items compile as a top-level
section in the PDF rather than as subsystems of the last in-house system.

For each OTS item, create `docs/verification/ots/{ots-name}.md` covering:

- The OTS item's required functionality (reference `docs/reqstream/ots/{ots-name}.yaml`)
- Verification of each requirement (using self-validation evidence if appropriate)
- Coverage mapping of OTS requirements to test scenarios

# Writing Guidelines

- **Test Coverage**: Map every requirement to at least one named test scenario so
  reviewers can verify completeness without reading test code
- **Scenario Clarity**: Name each scenario clearly - "Valid input returns parsed result" not "Test 1"
- **Boundary Conditions**: Call out boundary values, error inputs, and edge cases explicitly
- **Isolation Strategy**: Describe what is mocked or stubbed and why at each level
- **Traceability**: Link to requirements where applicable using ReqStream patterns
- **Verbal Cross-References**: Reference other documents by name - do not use markdown
  hyperlinks, which break in compiled PDFs

Mermaid diagrams may supplement text descriptions where test flow benefits from visual
representation, but must not replace text content.

# Quality Checks

Before submitting verification documentation, verify:

- [ ] Every requirement at each level is mapped to at least one named test scenario
- [ ] System verification documents cover end-to-end and integration scenarios
- [ ] Subsystem verification documents identify mocked boundaries and integration scenarios
- [ ] Unit verification documents identify individual scenarios including boundary and error paths
- [ ] Files organized under `docs/verification/` following the folder structure pattern above
- [ ] Each file's top-level heading depth matches its folder depth per the Heading Depth Rule
- [ ] All documentation folders use kebab-case names mirroring source code structure
- [ ] All documents follow technical documentation formatting standards
- [ ] Content is current with requirements and test implementation
- [ ] Every OTS item has `docs/verification/ots/{ots-name}.md` with requirement coverage
- [ ] `docs/verification/ots.md` exists with a `#` heading when OTS items are present
- [ ] Documents are integrated into ReviewMark review-sets for formal review
