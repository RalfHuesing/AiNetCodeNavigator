# M4 — Focused dependencies and assembly output

## Intent and scope

Make defaults answer the selected question with less irrelevant output. Implements P4 and [concept 5.6](../concept.md#56-dependency-projection-and-api-output). Depends on M3.

## Executable points

- [ ] **M4.1 — Select one dependency level.** Implement the concept's type/file/namespace/project projections, root/depth/direction semantics, filters, evidence and continuation. Reuse loaded project references for the source-only project view and current semantic facts for other views. Acceptance: only the selected level is delivered, incoming coverage remains honest, project roots/linked files/cycles are handled as specified, project-only queries do not trigger a semantic scan, and assembly targets reject `level=project` with a `level` argument error.
  - [ ] **M4.1a — Focus semantic projections and representative evidence.** Deliver only the selected type/file/namespace projection from the existing admitted traversal. Preserve coverage, scheduling, owners and cache accounting; migrate consumers and meaningful regressions. The intermediate project branch must fail explicitly and is not a completed project-view capability.
  - [ ] **M4.1b — Add exact source project-reference traversal.** Complete the source-only project branch with exact file/symbol ownership, presence-sensitive filter validation, bounded directed traversal and separate root/endpoint/origin evidence, without semantic document collection. Verify small owner, empty-file, direction/depth/cycle and no-scan cases.
- [ ] **M4.2 — Make assembly overview compact by default.** Implement explicit member inclusion and associated validation, retaining type identity/signature/visibility/owner/reference/limitations. Acceptance: no unrequested member collection/serialization; detail remains reachable through existing delivery and context members; W3/W4 full default outputs are smaller without omitted requested evidence. Update docs/consumers and record one comparable output-size check.
- [ ] **M4.R — Independent Sol review, fixes, and acceptance.** Review root and projection semantics, default/detail usability, output-size evidence, retained detail, and preserved dependency-cache/ownership behavior. Close after required fixes/checks pass.

## Verification and non-goals

Reuse dependency scanner/cache/collection contracts and assembly inspection tests. Add focused selected-level and member-option checks where missing, using modest fixtures. Run official build and narrow affected tests, with relevant extended tests per rules. No giant graph fixture, new semantic collection pass for projects, generic graph package, fourth continuation protocol, or percentage-driven deletion of evidence.

Existing starting points: [Dependency tests](../../../tests/AiNetCodeNavigator.FastTests/Dependencies/), [SourceDependencyGraphCollectionContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceDependencyGraphCollectionContractTests.cs), [SourceDependencyGraphOutgoingContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceDependencyGraphOutgoingContractTests.cs), and [InspectAssemblyScannerTests](../../../tests/AiNetCodeNavigator.FastTests/Assemblies/InspectAssemblyScannerTests.cs).

## Completion evidence

Before production edits, M4.1 was split because semantic projections/cache evidence and the scan-free project-reference path are distinct bounded changes with different verification owners. The same writer implements them sequentially; both children are required for M4.1. This preserves the complete concept scope and does not add another milestone or continuation mechanism. Record commits, commands/results, W3/W4 before/after requests and full output sizes, detail follow-ups, and review/fix disposition here.
