# R06 — Targeted outgoing traversal

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R05](R05-dependency-cache.md). Next: [R07](R07-polling-and-transport.md).

Contract: [02 — outgoing algorithm and acceptance](../02-dependency-graph-analysis.md).

For cold outgoing requests, schedule declaration-document work breadth-first from exact type/file roots using R05's collection facts. Include all eligible partial declarations, keep unrelated document types out of the root/frontier set and avoid terminal-depth expansion. Use full cached collection directly when available. Incoming/both continue using complete selected coverage.

Acceptance: a cold depth-1 query scans exactly required root documents; deeper scans expand only needed frontiers; file roots include every selected type; cycles/partials/linked ownership stay correct; results match broad projection by identity/evidence/order. All actual omissions and limits remain visible.

Verification: official build; targeted scanner/traversal FastTests; source dependency handler integration contracts and relevant selected extended partial/cross-project relationship cases. Execute the controlled cold/warm/broad/targeted measurements from specification 02. Record semantic counts as acceptance evidence and timings as measured observations.

Completion checklist:

- [x] R06.1 — Root/partial/file selection and bounded breadth-first outgoing scheduling implement specification 02.
- [x] R06.2 — Targeted/broad equivalence, exact required-document counts, depth/cycle/filter/limit and ownership acceptance passed.
- [x] R06.3 — Required build/test selections and cold/warm/broad/targeted measurements completed; affected current-state docs are updated.
- [x] R06.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R06.1–R06.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Complete; Root acceptance and independent actual-code audit PASS |
| Implementation commit(s) | 3e30409bf81e5ff03d69927da07d3a2d61a3b819 |
| Executed verification | Official build PASS; affected Fast 47 PASS; eligible Integration 15 PASS; independent actual-code audit PASS; exact commands below |
| Measurements / artifacts | Actual required semantic counts 2/3/4 at depths 1/2/3; late root 1 of 1,003 eligible; F03 expanded timings deferred; artifacts temp/roadmap-evidence/R06/frozen1 |
| Blocker / next action | No functional blocker; F03 extra timing evidence deferred; proceed to R07 after this completion commit |

### Prerequisite, assignment and evidence disposition

Root verifies clean HEAD `6d4172ee1ecf36ec17e77bf00b788ec2b224a6b7`, R05 implementation `e57e15e0c31aad7952d740dce94b9fbad2e46deb`, detailed evidence `33c428a` and completed index/checklist update `6d4172e`. Root reads the full R06/specification 02 outgoing contract and specification 04 cache boundary before assigning the complete slice.

Existing `/root/sol_implementation` was explicitly started gpt-6.1-sol/medium; it receives the full linked contracts/rules, all acceptance, current prerequisite state, narrow file boundaries and test/docs/removal obligations. It owns the complete production/test correction slice; Root alone owns official serialized gates, task evidence, checkboxes and commits. The distinct read-only Sol/medium auditor is reused after freeze.

F03's expanded five-run cold/warm/broad/targeted phase timing matrix is explicitly deferred for R06 under the user's delivery-priority policy. Actual cold depth-1 required-document count, fewer scans than broad and common-projector equivalent dependency evidence remain mandatory focused executable acceptance. R07 implementation does not start before R06 is verified and recorded complete. No gate or audit is claimed passed yet.

To finish the complete slice efficiently, Root starts a second implementation worker `r06_source_contracts` explicitly gpt-6.1-sol/medium with fork none and the full R06 contracts/rules/acceptance. It owns only the new SourceDependencyGraphOutgoingContractTests.cs actual-handler class; the original Sol owns all product/Fast/other-document files. Both are forbidden gates/commits/checklist edits. The production scheduler reuses the common traversal's admitted distances so node ordering/caps are not reimplemented; the new Core collector owns only declaration-document scheduling. All eligible document identities/duplicate ordinals are prepared before selecting required subsets. Root reviews partial retained-bucket lookup access timing before freeze. Final gates/audit remain pending.

### Frozen slice and first official gates

Both explicitly configured Sol/medium implementation workers freeze all product/test/doc files. The new Core DependencyGraphOutgoingCollector schedules declaration documents from the common traversal's admitted distances, so seed/frontier ordering, node cap and hidden/minimum-depth semantics remain shared. It resolves destinations only by exact scalar owner/context/declaration coordinates. Eligible sorted document identities and duplicate ordinals precede any subset. Cache full lookup touches valid partial hits and merges immutable facts outside the cache lock. The old visited set is replaced by admitted distances rather than duplicating node state; source file outline is resolved once. Required needs, including failed ones, are scheduled once per fresh operation. Root reads the actual new collector/cache/traversal/consumer and focused tests before gates.

`pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, 10.58 seconds. `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~DependencyGraphScannerTests|FullyQualifiedName~DependencyGraphCacheTests|FullyQualifiedName~DependencyGraphCacheRetentionTests|FullyQualifiedName~DependencyGraphCacheCancellationTests|FullyQualifiedName~DependencyGraphOutgoingCollectorTests' --logger 'console;verbosity=normal'`: PASS, exit 0, 47 tests / zero skipped, 2.5598 seconds. Artifacts `temp/roadmap-evidence/R06/frozen1/{build.log,test-fast.log,FastTests.trx}`. This includes 11 new outgoing cases and affected scanner/cache regressions.

Actual new Core assertions prove partial cross-project root documents: depth 1 collects 2, depth 2 collects 3 and depth 3 collects 4; unrelated/terminal documents are not collected and identity/file/order/summaries match broad projection. Late-root selection in 1,003 eligible documents collects exactly 1 and has equivalent evidence. Further passing contracts cover partial warm reuse/broad fill, filters/eligible partials, all named root kinds/empty files, seed/neighbor cap admission before scheduling, cycles, failure-once/future retry, exact linked owners and stable duplicate ordinals across subsets/broad collection. F03's additional repeated phase timing matrix remains deferred, not PASS.

The eligible actual-handler Integration selection and separate read-only Sol/medium frozen-code audit now run/are assigned. No E2E or concurrent gate runs. Actual category inventory finds only two eligible ExtendedIntegration cases, both WorkspaceLoadingIntegrationTests with owners unchanged by R06 and already PASS in R05; there is no new extended partial/cross-project relationship test to invent or broad rerun. Actual source/assembly partial/cross-project handler tests are included in the eligible focused selection.

### Final Integration, audit and Root acceptance

`pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceDependencyGraphOutgoingContractTests|FullyQualifiedName~SourceDependencyGraphCacheContractTests|FullyQualifiedName~SourceDependencyGraphCollectionContractTests|FullyQualifiedName~SourceRelationshipToolsContractTests.DependencyGraph_|FullyQualifiedName~SourceRelationshipToolsContractTests.SourceRelationshipHandlersReturnNavigableResultsErrorsAndBoundedProjections|FullyQualifiedName~AssemblyToolsContractTests.AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes' --logger 'console;verbosity=normal'`: PASS, exit 0, 15 tests / zero skipped, reported 1.0829 minutes; archived `temp/roadmap-evidence/R06/frozen1/{test-integration.log,IntegrationTests.trx}`. Per-test progress was monitored; no stall occurred. This includes all five new actual Source handler cases plus warm/fresh and existing broad/assembly graph integration.

The independent read-only gpt-6.1-sol/medium auditor reads the full actual diff/new files against specification 02 and affected 04 contracts: PASS, no confirmed functional finding. It verifies exact roots/owner/partials, scope/generated anchors, common distance admissions before scans, complete same-distance needs, terminal avoidance, failure-once, linked/duplicate identity, true coverage/omissions, R05 single-flight/lifetime preservation, full-resident shortcut and broad fill, scalar destination resolution and visible current handoffs. Actual test assertions and current docs match those contracts. No audit or worker gate ran concurrently with Root's official selections.

Root independently checks the same-distance collect/merge boundary, exact declaration resolution, stable eligible-plan identity before subsets, cached scalar representation, concrete count/evidence assertions and absence of invented document limits. The source fixture has independently fixed eight documents: partial cold work is exactly 2 or 3, a broad request fills the eligible eight without repeated collection, later root projections add zero, and a same-timestamp content edit selects a new snapshot and collects its 2 root declarations. The preserved R05 contract now has targeted cold/refreshed count 1 rather than broad 2; its warm-zero-work/reference behavior is unchanged. No acceptance was weakened.

Smallness checkpoint: new outgoing Core collector has 124 nonempty lines, RelationshipTools 1,497 and new separately executable source contract class 262. Scheduling is the only extracted responsibility; shared traversal's admitted-distance dictionary replaces the visited set, no duplicate traversal policy/state. No incoming index, generic scheduler, transport field, public setting or assembly demand-driven path is introduced. Old duplicate file outlining is removed. Remaining mixed route recovery/formatting and large pre-existing contract fixtures retain the already recorded out-of-task scope; blanket handler/registration or test-framework restructuring is not required for R06. F03 is the only deferred additional measurement matrix here, explicitly not PASS. Root accepts R06.1–R06.3 and commits the verified slice before recording its hash and checking R06.4/index.

Verified implementation commit: `3e30409bf81e5ff03d69927da07d3a2d61a3b819`. Root consciously checks R06.1 algorithm/roots/admission, R06.2 executed count/equivalence/ownership/limits acceptance, R06.3 official gates/docs with explicit F03 measurement deferral and R06.4 commit/evidence/audit/Root review, then the single R06 index item. No R07 implementation starts before this record.
