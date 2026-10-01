# Debt reduction roadmap

## Decision criteria

Use the production, tests, and mixed `changed-files` findings as review signals. Verify each selected change against local contracts, documentation, callers, and tests. Prioritize concrete duplicated responsibilities, fragile state or error handling, and hard-to-follow production control flow. File size, public APIs without local callers, declarative test data, and heuristic missing-test evidence alone do not justify changes. Preserve features and observable contracts.

Baseline: `9645d17511b855a6b8b4a6c615ededeeb6e40629`; working tree clean.

## Work queue

- [x] Read the changed-files reports in all three areas, assess concrete candidate groups, and select independently actionable slices. This is prioritization, not individual clearance of all 482 findings.
- [x] Establish a green baseline with the official build and routine test scripts: 0 warnings/errors, 560 fast and 27 routine integration tests passed; no skips.
- [x] Centralize the repeated assembly identity matching rules used by `AssemblyReferenceClosureSession` and `RelationshipTools`; adjacent source-declaration policies remain separate.
- [x] Extract cross-owner assembly call-tree construction from `RelationshipTools` into a focused collaborator with explicit traversal, merge, and projection phases.
- [x] Separate semantic phases in `DependencyGraphScanner.ScanSolutionAsync`, retaining ordering, paging, project-qualified identity, cancellation, and recoverable errors.
- [x] Reuse the identical source-declaration ownership check between assembly context and assembly handoff formatting. Preserve the closure session's different source-tree/path policy.
- [x] Verify all three dead-code candidates against actual references and remove only confirmed unused internal helpers without changing live status, severity, or test-observability behavior.
- [x] Reassess the reviewed groups and record deferred, unreviewed, and false-positive signals with concrete reasons. No exhaustive clearance of the audit is claimed.

## Execution policy

Only one implementation subagent runs at a time. The orchestrator reviews each diff and owns commits. Use `gpt-6-luna` with high reasoning effort for subagents. Run `scripts/build.ps1` and `scripts/test.ps1` for each code slice, plus narrowly selected extended integration tests when shared host, workspace, symbol, response, or Git impact dependencies are affected. Update this roadmap in the same commit as the verified slice. Never mark a slice complete before its checks pass.

## Assessment and results

The triage subagent read all commissioned reports: production 436, tests 46, mixed 0. The `all-findings` views are outside this working set.

Selected debt is supported by concrete responsibilities: assembly identity comparison has two exact duplicate pairs; assembly call-tree closure construction combines owner BFS, graph merging, projection, handoffs and response formatting; dependency graph scanning combines input selection/paging and per-document semantic scanning. These changes target navigation and maintenance costs for agents, not thresholds.

Repeated assertions in the integration test file and local `Work` functions in cancellation/deadline tests currently provide independently understandable scenarios. No mechanical consolidation is planned. Other test size/structure signals need case-specific evidence before changes.

The 312 missing-test-evidence entries have not been individually verified. Their complete-snapshot heuristic is not proof of absent tests. Nonselected production size/control-flow signals and detailed test signals remain open for later assessment.

The initial triage's false-positive classification of `ResolveEffectiveStatus` was not confirmed by the orchestrator's independent check: semantic references returned no callers, and targeted source search found only the declaration. Follow-up verification of all three dead-code candidates found no callers or reflective/string-based use. `ResolveEffectiveStatus`, `AssemblyDiagnosticSeverityExtensions`, and `TrackingSolutionFactory.DisposalsFor` are confirmed unused internal helpers. The getter's per-solution disposal dictionary was only written for that getter; remove this orphaned bookkeeping while retaining the used aggregate counter and callback. Live status/severity formatting and enum surfaces remain unchanged.

Follow-up semantic inspection confirmed that `AssemblyTools.HasRootSourceDeclaration` and the check moved to `AssemblyHandoffFormatting.HasSourceDeclaration` use identical document-path/root acceptance rules. Reuse is actionable. `AssemblyReferenceClosureSession.HasSourceDeclaration` instead checks the syntax-tree path, requires a path strictly below the root, and only verifies document membership: retain that distinct policy rather than treating structural similarity as equivalence.

The remaining exact `IsWithin` pair in `RelationshipTools` and `StructureTools` is confirmed duplication of a short BCL `Path.GetRelativePath` containment predicate. It is deferred as lower-value local duplication rather than labeled a false positive. It has none of the multi-field identity or document-ownership policy maintained across the selected paths. The stronger extraction candidates were addressed first.

Other structural production signals in caller traversal, reference/implementation results, namespace counts, and source position resolution remain unreviewed in detail. No shared abstraction is introduced from statement shape alone. The 312 missing-test signals and nonselected size/control-flow candidates remain open; further changes require local contract/test evidence, not a metric target.

## Verification records

- Initial roadmap/baseline: `pwsh -File ./scripts/build.ps1` and `pwsh -File ./scripts/test.ps1` passed (560 fast + 27 routine integration tests). Documentation diff checked before commit.
- Slice 1: shared MCP-layer `AssemblyIdentityMatcher` replaces both duplicate comparison pairs; source-declaration checks remain separate. Four focused identity contract tests passed. Official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests; selected extended `ImpactZeroLimitsUseTheSameDefaultsAsOmittedLimits` passed 1/1. No skips. Current-state documentation checked; no observable contract change. The concurrent `.gitkeep` cleanup was committed separately outside this work and is excluded from this slice.
- Slice 2: `AssemblyCallTreeClosureBuilder` separates owner expansion, graph merging, and global projection. `AssemblyHandoffFormatting` provides concrete shared handoff formatting without depending on the tool handler. Root-handoff validation and its typed stale-snapshot failure stay in the handler. Updated the primary assembly navigation document. Official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests; selected extended `GitImpactReportsCallerAndRepositoryCompletenessThroughPublicStdioTools` passed 1/1. No skips. Existing public cross-owner/cap tests verify the preserved behavior; no tests of private phase boundaries were added.
- Slice 3: dependency graph scanning now exposes validation, ordered/filter-first document selection, project-reference collection, cached-compilation semantic scanning, and result projection as named phases. Public APIs, validation order/messages, traversal, paging, and visible-only handoff allocation are preserved. Updated the primary dependency graph document. Focused scanner tests passed 14/14; official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests. No skips. Only the dependency scanner is changed; no shared Git impact dependency is modified, so no additional extended Git selection is needed for this slice.
- Slice 4: assembly context handoffs reuse `AssemblyHandoffFormatting.HasSourceDeclaration`; the exact duplicate private helper is removed. The closure-session predicate and shared helper semantics are unchanged. Updated the primary assembly navigation document. Official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests. No skips. This local assembly-context call-site change does not modify a shared Git impact dependency.
- Slice 5: removed the three confirmed unused internal helper candidates and the unconsumed per-solution disposal dictionary/writes. Semantic references and targeted source/test/doc/contract searches confirmed non-use. Existing status wire/completeness/persistence helpers, severity enum and comparisons, aggregate disposal counts, and callbacks remain. Focused assembly/session/project-registry tests passed 24/24 with no skips; official build passed with 0 warnings/errors; routine gate passed 564 fast + 27 integration tests with no skips. Current-state docs checked; live behavior unchanged. No live shared Git impact dependency changed.

All five selected implementation slices are complete and verified. This roadmap records the reviewed working set and remaining signals; it does not certify all 482 audit findings or the whole product contract matrix.
