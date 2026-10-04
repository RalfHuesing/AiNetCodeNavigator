# R04 — Collect each document batch once

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R03](R03-snapshot-identity.md). Next: [R05](R05-dependency-cache.md).

Contract: [02 — separation, coverage and broad analysis](../02-dependency-graph-analysis.md).

Separate immutable semantic collection from page/traversal projection in the existing Core dependency owner. Collect a selected document batch once, derive all relationship pages from it and reuse its compilation acquisitions. Update source handler document-window draining and applicable assembly scanner callers to the new separated API.

Retain broad incoming/both behavior and the public type-oriented dependency semantics. Use exact deterministic ownership/edge merge and explicit collection errors/coverage. This point does not yet depend on warm cache hits or targeted outgoing scheduling.

Acceptance: multiple relationship pages produce no repeated document scans; more-than-1,000-document coverage and late roots are correct; scope/generated filtering, exact type ownership, limits, totals/errors and cancellation are preserved.

Verification: first reproduce repeated collection with a deterministic failing counter assertion against the old behavior. Then official build, DependencyGraphScanner/traversal FastTests and source/assembly dependency handler contracts plus affected targeted extended navigation tests. Capture the controlled baseline and R04 measurements defined in specification 02. Do not use the audit EOF as proof of a scanner cause.

Completion checklist:

- [x] R04.1 — The old repeated-collection regression was reproduced and its failing evidence preserved before correction.
- [x] R04.2 — Collection/projection separation implements specification 02; all point-level coverage/ordering/error/cancellation acceptance passed.
- [x] R04.3 — Required build/test selections and controlled baseline/R04 measurements completed; affected current-state docs are updated. Expanded timing evidence is explicitly deferred as F03, not claimed PASS.
- [x] R04.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R04.1–R04.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Complete; Root reviewed all detailed acceptance and the independent audit |
| Implementation commit(s) | `c72a02b7b35ad822390ae873cd9e10a62044e3c7` |
| Executed verification | Final official build PASS; 22 affected FastTests and nine affected IntegrationTests PASS; one full Sol actual-code audit and bounded A01–A06 correction audit PASS |
| Measurements / artifacts | Actual controlled work counts and failing/corrected gates below; expanded timing F03 explicitly deferred; `temp/roadmap-evidence/R04/` |
| Blocker / next action | No confirmed functional blocker; record completion and proceed to R05 |

### Prerequisite, ownership and bounded evidence

R03 product `4713636490705528e59dacab7b115167e9fb2dd7` and completion evidence `f1aab0592c869a81cb2f9174bc7f725956aa13cb` are committed. Every R03 detail and its index checkbox is checked; the checkout/index are clean before R04. Root rereads the step, complete specification 02, relevant specification 04 and current scanner/traversal/handler consumers. R03's structural checkpoint and explicit outside-task scope carry forward.

Two fresh workers are explicitly configured: `/root/r04_collection` gpt-6-luna/high owns Core Dependencies and focused Fast dependency tests; `/root/r04_consumers` gpt-6-luna/high owns R04 Host dependency orchestration, applicable assembly callers, focused eligible Integration contracts and current-state docs. Initial production correction is held until Root runs the actual deterministic old repeated-collection regression. Workers never run gates, commit or change checkboxes; Root serializes gates. The separate `/root/roadmap_independent_audit` is reused with its expressly configured gpt-6.1-sol/medium read-only role; its actual-code audit follows the completed slice. Confirmed findings receive narrow fix/delta review.

The user-approved findings F03 deferral applies to the expanded five-scenario timing matrix, not deterministic semantic/compilation work counts or behavioral acceptance. Required original repeated-collection failure, one-collection work counts, full broad coverage, exact deterministic projection/ownership, limits/errors/cancellation and consumer wiring remain executable acceptance. Expanded optional evidence stays explicitly unrun and separate from actual checks. No R05 retention or R06 demand-driven outgoing work is implemented in R04, and no EOF root cause is inferred from scanner behavior.

### Original controlled repeated-collection failure

Only request-local compilation/document observer instrumentation and one focused regression are changed before this baseline; production collection/projection behavior is unchanged. The real two-project fixture reads two relationship pages at PageSize=1 and records actual project compilation acquisitions and semantic document collections.

`pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~DependencyGraphScannerTests.ScanSolutionAsync_DoesNotRescanDocumentsWhileProducingRelationshipPages' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failure / zero skipped, 1.5463 seconds (537 ms test). Both App and Contracts acquire their compilation twice; the once-per-project assertion fails at line 178. Later once-per-document assertions remain required and are not claimed executed in this failed baseline. The test instrumentation also observes the actual semantic collection owner, rather than inferring work from the legacy ScannedDocumentCount coverage field. Artifacts `temp/roadmap-evidence/R04/repeated-collection-baseline1/{test-fast.log,FastTests.trx}`. Root releases the scoped full collector/projector and consumer correction after preserving this failure. No stall or competing gate.

### Frozen slice and first official build

Both Luna workers freeze the persisted collector/projector, source/assembly consumers, documentation and focused tests before Root gates. The legacy page-window merger now delegates traversal/summary semantics to the common projector; identical document tuples retain deterministic duplicate ordinals and separate coverage.

`pwsh -File ./scripts/build.ps1`: actual FAIL, exit 1, 3.76 seconds, zero warnings / two compilation errors in DependencyGraphScanner.cs: IReadOnlyList folders incorrectly use Length and ImmutableArray project facts incorrectly use Count. Artifact `temp/roadmap-evidence/R04/build-first1/build.log`. Root assigns only these compile corrections to Luna, then resumes the official gates. No behavioral check or audit is claimed passed.

`pwsh -File ./scripts/build.ps1` after the two Core fixes: actual FAIL, exit 1, 6.88 seconds, zero warnings / five occurrences of the missing test JSON-body helper in the new focused Integration class. Core/Host/Fast projects compile. Artifact `temp/roadmap-evidence/R04/build-second2/build.log`. Root delegates the small test-helper compile correction to the consumer Luna; assertions remain unchanged.

### Focused gates and first actual-code audit findings

After the small compiler corrections, `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 2.44 seconds, zero warnings/errors.

`pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~DependencyGraphScannerTests' --logger 'console;verbosity=normal'`: PASS, exit 0, 17 completed / zero skipped, 2.0062 seconds. The real two-project collection records two semantic collections and one compilation acquisition per project while projecting two relationship pages. The drained fixture records exactly 1,002 document attempts/successes and two project compilation acquisitions, including its late root. Duplicate document multiplicity and the shared legacy merger contracts pass.

`pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceDependencyGraphCollectionContractTests|FullyQualifiedName~SourceRelationshipToolsContractTests.DependencyGraph_|FullyQualifiedName~SourceRelationshipToolsContractTests.SourceRelationshipHandlersReturnNavigableResultsErrorsAndBoundedProjections|FullyQualifiedName~AssemblyToolsContractTests.AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes' --logger 'console;verbosity=normal'`: FAIL, exit 1, eight completed / seven passed / one failed / zero skipped, 43.0604 seconds. Source late-window/owner/large navigability contracts, assembly seventeen-route contract, broad incoming/both beyond 1,000, linked-owner rejection and scope/generated filters PASS. The named-type file-root contract actually fails because the nested record primary-constructor dependency is absent (eight admitted seeds PASS, NestedRecord→Dependency missing). Its fixture/assertions remain unchanged. Artifacts `temp/roadmap-evidence/R04/focused-final3/{build.log,test-fast.log,FastTests.trx,test-integration.log,IntegrationTests.trx}`.

Sol actual-code review additionally confirms two concrete discrepancies: R04-A01 breadth-first frontier is insertion-ordered rather than exact-key ordinal, changing which endpoint survives the node cap; R04-A02 a nonloaded absolute file path can be redirected by suffix matching to another loaded physical file. Root assigns narrowly reproducible baseline tests before both fixes. R04-A03 is the actually failing nested-record edge attribution above. These are functional corrections, not deferred evidence. Root resumes only affected gates/delta audit after the bounded fixes; no successful broader gate is repeated without affected changes.

### Bounded audit correction baselines

The one full independent Sol audit finishes with exactly A01–A06: ordinal BFS frontier, physical file matching, nested primary-constructor owner attribution, error-versus-window cursor, shared page offsets, and preservation of edge-free declaration seeds through the compatibility merger. A04–A06 affect the retained public Core compatibility adapter, not an invented new path. Root obtains actual failures before releasing the bounded fixes.

`pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~DependencyGraphScannerTests.ScanSolutionAsync_CollectsNestedRecordPrimaryConstructorParameterDependency|FullyQualifiedName~DependencyGraphScannerTests.Traverse_AdmitsSameDepthNeighborsInOrdinalOrderBeforeApplyingNodeCap|FullyQualifiedName~DependencyGraphScannerTests.MergeAndTraverse_DoesNotInventDocumentCursorForFullyAttemptedPartialCoverage|FullyQualifiedName~DependencyGraphScannerTests.MergeAndTraverse_UsesSharedPageOffsetsWhenACollectionIsShorterThanThePageWindow|FullyQualifiedName~DependencyGraphScannerTests.MergeAndTraverse_PreservesEdgeFreeTypeSeedsFromSourceScan' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, five completed failures / zero skipped, 1.4087 seconds. Respectively returns Outer instead of NestedRecord ownership, admits Z→X instead of C→D under the cap, invents document cursor 1, rejects complete shared offsets, and counts an edge-free seed as zero.

`pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceDependencyGraphCollectionContractTests.DependencyGraph_FileSelectorRejectsForeignAbsolutePathWithLoadedBasename' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failure / zero skipped, 5.2363 seconds. On this Windows host, the nonexistent root-absolute `\Relationships.cs` is incorrectly redirected to loaded `src/App/Relationships.cs`, returning a successful graph. No POSIX execution is claimed. Artifacts `temp/roadmap-evidence/R04/audit-regressions4/{test-fast.log,FastTests.trx,test-integration.log,IntegrationTests.trx}`.

Root authorizes one bounded correction batch with unchanged assertions. Scalar internal declaration metadata needed by the existing Core merger is permitted to preserve edge-free seeds, without adding public JSON fields or Roslyn retention. Remove the unused scanner pageSize local and unused scan-symbol return field as part of the requested smallness review. The subsequent independent review covers these deltas and gates only.

### Final correction gates and acceptance review

`pwsh -File ./scripts/build.ps1`: PASS, exit 0, 4.12 seconds, zero warnings/errors.

`pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~DependencyGraphScannerTests' --logger 'console;verbosity=normal'`: PASS, exit 0, 22 completed / zero skipped, 2.0153 seconds. All five audit regressions that actually failed before correction now pass alongside work-count, filtering, ownership, paging, depth, node-cap and window-continuation regressions.

`pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceDependencyGraphCollectionContractTests|FullyQualifiedName~SourceRelationshipToolsContractTests.DependencyGraph_|FullyQualifiedName~SourceRelationshipToolsContractTests.SourceRelationshipHandlersReturnNavigableResultsErrorsAndBoundedProjections|FullyQualifiedName~AssemblyToolsContractTests.AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes' --logger 'console;verbosity=normal'`: PASS, exit 0, nine completed / zero skipped, 43.2698 seconds. The unchanged nested-record and owner-path assertions pass, together with broad late-window coverage and existing navigability/assembly contracts. Artifacts `temp/roadmap-evidence/R04/audit-corrections5/{build.log,test-fast.log,FastTests.trx,test-integration.log,IntegrationTests.trx}`.

Extended inventory was explicitly inspected: only the two workspace loading/snapshot-isolation ExtendedIntegration cases exist, and their owners were unchanged by R04 and already passed at R03. There is no applicable eligible extended graph-navigation case to select; no unrelated Extended or excluded E2E run is claimed. No test stalled, no competing gate ran, and no push/deployment occurred.

The independent gpt-6.1-sol/medium auditor read the actual implementation and complete step contract, found A01–A06, then read the correction deltas and actual gates. Final verdict PASS: all six confirmed findings closed, no confirmed open functional finding. Root independently reviews actual scalar immutable facts, one compilation/document collection per job, full internal draining, context-bound ownership, file selection before collection filters, root admission independent of edges, shared traversal/summary semantics, real window/error completeness and request-owned visible-symbol formatting. Public source/assembly consumers now collect once; the repeated drain/rescan orchestration is removed. Cancellation remains propagated through collection; no successful cancellation payload is introduced. Root explicitly checks R04.1–R04.3 and records product commit `c72a02b7b35ad822390ae873cd9e10a62044e3c7` before closing R04.4 and the index.

### Smallness and structural checkpoint

The immutable collection, collection/projection options, canonical document identity with duplicate ordinal, type declaration facts and request-local observer each enforce a concrete R04 requirement. Internal declaration metadata on the existing scan payload is necessary to preserve edge-free roots through the public Core compatibility merger; it is scalar and adds no public JSON field. The merger retains only page/window validation and delegates semantic traversal/summaries to the common projector. Removed the unused wrapper page-size local and returned symbol-map field; no completed collection retains Solution, Compilation or ISymbol. No general cache/provenance/hooking/audit framework is introduced.

Current nonempty-line counts: RelationshipTools 1,467; SourceRelationshipToolsContractTests 725; the narrowly executable new SourceDependencyGraphCollectionContractTests 218; SourceToolsContractTests 1,249; AssemblyToolsContractTests 2,236. RelationshipTools still combines several route-specific response/recovery responsibilities, and the large legacy contract classes still mix route scenarios and fixture setup. Required R05/R06 integration will place dependency retention and outgoing scheduling with their concrete owners; R07 will centralize operation progress. Blanket route/registration rewrites, line-count-driven splitting, global fixture/parser/test-framework extraction and E2E rewrites explicitly remain outside this task. The focused graph class supplies independent executable behavior contracts without waiting for that broader cleanup.

Expanded five-scenario timings remain F03 in findings.md, explicitly unrun; deterministic one-collection work counts above are actual PASS. No correctness acceptance was weakened or confirmed functional defect deferred.
