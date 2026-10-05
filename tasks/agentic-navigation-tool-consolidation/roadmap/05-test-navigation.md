# M5 — Helper-based test navigation

## Intent and scope

Find useful static test candidates reached through a short proven helper path. Implements P5 and [concept 5.7](../concept.md#57-static-test-candidates). Depends on M4.

## Executable points

- [x] **M5.1 — Add bounded helper traversal and candidate evidence.** Extend the existing test builder with the specified depth, eligible links, request-wide bounds, shortest deterministic paths, cycle handling, direct/indirect/heuristic ordering, and owner-qualified navigation data. Preserve existing direct/implementation/fixture discovery. Acceptance: depth examples are correct; method groups/unresolved or guessed runtime links do not become helper paths; stronger direct evidence is retained; bounds produce truthful partial output.
- [ ] **M5.2 — Integrate context validation, delivery, and meaningful regressions.** Expose the new test option only for the proper section/target; preserve independent test scope, section cursors/status, and useful candidate identities. Extend existing small fixtures for W6's direct/helper/depth/cycle/name/non-path cases. Acceptance: callers can follow returned evidence without executable test commands or runtime coverage claims; existing substantive test-navigation assertions still hold. Update docs/examples.
- [ ] **M5.R — Independent Sol review, fixes, and acceptance.** Check eligible edges, helper counting, shared caps, cancellation, ordering, section independence, and the inspected static evidence. Close after required fixes/checks pass.

## Verification and non-goals

Use existing test detector/recommendation/reference/context contracts. Prove traversal cap behavior with a small deterministic fixture or existing configurable test seam rather than enormous source samples. Run official build, narrow affected tests, and relevant extended relationship tests as required. No test execution feature, runner adapter/filter syntax, runtime path simulation, second index, or broad all-framework test matrix.

Existing starting points: [TestDetectorTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/TestDetectorTests.cs) for direct/implementation/heuristic evidence and bounds, and [SourceToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceToolsContractTests.cs) for candidate ownership, partial results and section paging. Add helper-path cases to these existing owners rather than a separate test system.

## Completion evidence

### M5.1 � Core helper traversal

Implemented from clean HEAD `0879d8bb5b6dfb0760bb6732f38d594b324f1f8c`. `TestRecommendationBuilder` adds validated Core depth 0..2 (default 1), request-local breadth-first helper discovery and reference reuse. Direct target/implementation uses remain intact before helper traversal, including supported bound non-call uses. Further links require exact source binding plus invocation/constructor evidence; method groups, candidate/unresolved bindings and unknown delegate targets do not become helper calls. Static virtual dispatch stays a static target with an explicit limitation. Existing fixture name heuristics and 64-implementation/256-fixture thresholds remain.

All seeds share the 200 expanded-helper and 4,096 reference-inspection bounds. Helper cache/visited keys include the actual `BoundUse.Document.Project.Id` and declaration sort key; inspected-location reuse includes actual DocumentId, span and declaration identity. Carrying ProjectId directly makes existing proven ownership explicit, not a claim that a generated-owner collision was reproduced. Shortest paths are selected by BFS depth and deterministic project/location ordering; cycles terminate, direct evidence is retained and results rank direct before indirect before heuristics. Additive models include static path steps, available caller/target declaration references, actual test project paths, qualified fixture/method names and method-specific source locations. Core reports helper depth/count/limit separately.

The MCP consumer intentionally passes Core depth **0** in this intermediate slice, preserving its current direct-only contract. M5.2 must replace that explicit argument with the public validated `testHelperDepth`, extend query/cursor binding and propagate helper limit/count/depth through tests-section delivery. No public helper option is claimed yet. Current-state `docs/navigation/test-context.md` documents this implemented boundary.

Verification:

- Official `scripts/build.ps1` passed, zero warnings/errors; final run 5.70 seconds (`temp/build.log`).
- `scripts/test-fast.ps1 -Filter 'FullyQualifiedName~TestDetectorTests'`: 36 passed. Original direct-only assertions now run explicitly at depth 0 and the same fixture additionally proves default-depth helper discovery without weakening assertions. New small fixtures cover depths 0/1/2, direct/indirect/name ordering, shortest path and cycle, final non-call use, method-group/candidate/delegate rejection, constructor/static virtual evidence, shared implementation/helper/reference caps, invalid depth and equal helper names in distinct source owners.
- First new fixture run failed 3 handoff assertions because its in-memory workspace had no virtual solution/project path. The fixture was corrected using the existing TestWorkspaceBuilder virtual-path seam; handoff assertions remain. Failure artifacts preserved as `temp/m5-fast-initial-failure.log` and `.trx`; no production failure was disguised.
- Narrow official Integration filter selected `TestCandidates_FreshCapturedProjectReferencesMapBoundMethodsToExactSourceDefinitions`, `GetContextTestsPagesAllCandidatesAndKeepsExpansionLimitPartialOnFinalPage`, and `GetContextSelectsSectionsSharesIdentityAndContinuesOnlyTheCursorSection`: 3 passed in 1m13s (`temp/test-integration.log`, `TestResults/IntegrationTests.trx`). These protect fresh exact source binding and preserved context paging/partial limits.
- After the final explicit ProjectId helper-key change, reran the official build and `(FullyQualifiedName~TestDetectorTests&FullyQualifiedName~Helper)|FullyQualifiedName~TestRecommendationBuilder_FindsDirectContractUseInDifferentlyNamedTestMethod`: 9 passed (`temp/test-fast.log`, `TestResults/FastTests.trx`). The prior Integration scenarios use depth zero or no helper and were not invalidated. No broad routine/E2E/benchmark or Exploration run; no relevant ExtendedIntegration helper contract exists (the two current extended workspace loader tests do not exercise this owner). Scripts were serialized and monitored below one-minute intervals; no suspected stall.

Diff reviewed and `git diff --check` passed. M5.2 and M5.R remain open; final public W6 Exploration and independent review follow those points.
