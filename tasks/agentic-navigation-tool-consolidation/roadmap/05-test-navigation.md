# M5 — Helper-based test navigation

## Intent and scope

Find useful static test candidates reached through a short proven helper path. Implements P5 and [concept 5.7](../concept.md#57-static-test-candidates). Depends on M4.

## Executable points

- [ ] **M5.1 — Add bounded helper traversal and candidate evidence.** Extend the existing test builder with the specified depth, eligible links, request-wide bounds, shortest deterministic paths, cycle handling, direct/indirect/heuristic ordering, and owner-qualified navigation data. Preserve existing direct/implementation/fixture discovery. Acceptance: depth examples are correct; method groups/unresolved or guessed runtime links do not become helper paths; stronger direct evidence is retained; bounds produce truthful partial output.
- [ ] **M5.2 — Integrate context validation, delivery, and meaningful regressions.** Expose the new test option only for the proper section/target; preserve independent test scope, section cursors/status, and useful candidate identities. Extend existing small fixtures for W6's direct/helper/depth/cycle/name/non-path cases. Acceptance: callers can follow returned evidence without executable test commands or runtime coverage claims; existing substantive test-navigation assertions still hold. Update docs/examples.
- [ ] **M5.R — Independent Sol review, fixes, and acceptance.** Check eligible edges, helper counting, shared caps, cancellation, ordering, section independence, and the inspected static evidence. Close after required fixes/checks pass.

## Verification and non-goals

Use existing test detector/recommendation/reference/context contracts. Prove traversal cap behavior with a small deterministic fixture or existing configurable test seam rather than enormous source samples. Run official build, narrow affected tests, and relevant extended relationship tests as required. No test execution feature, runner adapter/filter syntax, runtime path simulation, second index, or broad all-framework test matrix.

Existing starting points: [TestDetectorTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/TestDetectorTests.cs) for direct/implementation/heuristic evidence and bounds, and [SourceToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceToolsContractTests.cs) for candidate ownership, partial results and section paging. Add helper-path cases to these existing owners rather than a separate test system.

## Completion evidence

Not started. Record commits, commands/results, W6 evidence/follow-ups, preserved regressions, and review/fix disposition here.
