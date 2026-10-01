# Debt reduction roadmap

## Decision criteria

Use the production, tests, and mixed `changed-files` findings as review signals. Verify each selected change against local contracts, documentation, callers, and tests. Prioritize concrete duplicated responsibilities, fragile state or error handling, and hard-to-follow production control flow. File size, public APIs without local callers, declarative test data, and heuristic missing-test evidence alone do not justify changes. Preserve features and observable contracts.

Baseline: `9645d17511b855a6b8b4a6c615ededeeb6e40629`; working tree clean.

## Work queue

- [x] Read the changed-files reports in all three areas, assess concrete candidate groups, and select independently actionable slices. This is prioritization, not individual clearance of all 482 findings.
- [x] Establish a green baseline with the official build and routine test scripts: 0 warnings/errors, 560 fast and 27 routine integration tests passed; no skips.
- [x] Centralize the repeated assembly identity matching rules used by `AssemblyReferenceClosureSession` and `RelationshipTools`; adjacent source-declaration policies remain separate.
- [x] Extract cross-owner assembly call-tree construction from `RelationshipTools` into a focused collaborator with explicit traversal, merge, and projection phases.
- [ ] Separate semantic phases in `DependencyGraphScanner.ScanSolutionAsync`, retaining ordering, paging, project-qualified identity, cancellation, and recoverable errors.
- [ ] Reassess remaining signals and record deferred work and false positives with concrete reasons.

## Execution policy

Only one implementation subagent runs at a time. The orchestrator reviews each diff and owns commits. Use `gpt-6-luna` with high reasoning effort for subagents. Run `scripts/build.ps1` and `scripts/test.ps1` for each code slice, plus narrowly selected extended integration tests when shared host, workspace, symbol, response, or Git impact dependencies are affected. Update this roadmap in the same commit as the verified slice. Never mark a slice complete before its checks pass.

## Assessment and results

The triage subagent read all commissioned reports: production 436, tests 46, mixed 0. The `all-findings` views are outside this working set.

Selected debt is supported by concrete responsibilities: assembly identity comparison has two exact duplicate pairs; assembly call-tree closure construction combines owner BFS, graph merging, projection, handoffs and response formatting; dependency graph scanning combines input selection/paging and per-document semantic scanning. These changes target navigation and maintenance costs for agents, not thresholds.

Repeated assertions in the integration test file and local `Work` functions in cancellation/deadline tests currently provide independently understandable scenarios. No mechanical consolidation is planned. Other test size/structure signals need case-specific evidence before changes.

The 312 missing-test-evidence entries have not been individually verified. Their complete-snapshot heuristic is not proof of absent tests. Nonselected production size/control-flow signals and detailed test signals remain open for later assessment.

Triage found two callers for `ResolveEffectiveStatus` through semantic references: the dead-code signal is a false positive. The other dead-code candidates (`AssemblyDiagnosticSeverityExtensions`, `TrackingSolutionFactory.DisposalsFor`) are deferred until usage and observability are checked; no API or helper is removed based on the signal alone.

## Verification records

- Initial roadmap/baseline: `pwsh -File ./scripts/build.ps1` and `pwsh -File ./scripts/test.ps1` passed (560 fast + 27 routine integration tests). Documentation diff checked before commit.
- Slice 1: shared MCP-layer `AssemblyIdentityMatcher` replaces both duplicate comparison pairs; source-declaration checks remain separate. Four focused identity contract tests passed. Official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests; selected extended `ImpactZeroLimitsUseTheSameDefaultsAsOmittedLimits` passed 1/1. No skips. Current-state documentation checked; no observable contract change. The concurrent `.gitkeep` cleanup was committed separately outside this work and is excluded from this slice.
- Slice 2: `AssemblyCallTreeClosureBuilder` separates owner expansion, graph merging, and global projection. `AssemblyHandoffFormatting` provides concrete shared handoff formatting without depending on the tool handler. Root-handoff validation and its typed stale-snapshot failure stay in the handler. Updated the primary assembly navigation document. Official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests; selected extended `GitImpactReportsCallerAndRepositoryCompletenessThroughPublicStdioTools` passed 1/1. No skips. Existing public cross-owner/cap tests verify the preserved behavior; no tests of private phase boundaries were added.
