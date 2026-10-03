# Sequential implementation roadmap

Status: ready for implementation planning/execution by a later explicitly invoked orchestrator. No production step has started. Specifications were consolidated on 2026-10-03 from the approved discussion.

## Execution contract

The orchestrator executes R01 through R08 in order, one active implementation point at a time. Every point depends on the preceding point's verified commit. Assign its linked contract, required outcome, acceptance and verification together. Do not delegate only a code fragment without its consumers, wiring and evidence obligations.

Technical behavior is defined in [01](01-symbol-identity-and-recovery.md), [02](02-dependency-graph-analysis.md), [03](03-long-running-operations-and-transport.md) and [04](04-snapshot-refresh-and-analysis-cache.md). Their specified decisions replace the earlier open questions and proposals. Internal class/file placement may follow the existing owners; that freedom does not reopen the public contract.

Before each point, inspect current HEAD/status, the linked specifications, affected definitions/callers/runtime wiring, relevant tests and repository rules. Recheck sources because earlier file links may be renamed or removed. Preserve unrelated work and stop on staged-file conflicts. No automatic push, deployment or optional agent-workflow step is included.

Every completed point must include:

- Production code, consumers, registration/configuration changes and removal of superseded paths within that point's scope.
- Requirement/regression tests, relevant current-state docs and affected agent navigation instructions in the same verified commit.
- The commit ID, executed checks and their outcomes in this roadmap's evidence log. Do not mark success from a plan, source inspection or unrun tests.
- Explicit remaining environmental limitations, if any; a mandatory unavailable check leaves the point blocked rather than silently complete.

A point may use several coherent commits when needed, but its successor starts only after all point-level acceptance has passed. R01 is the only intentionally internal preparatory point: the public reference switch and full old-handle removal happen together in R02.

If new evidence contradicts a specified requirement or exposes an architectural conflict, record the exact conflict and request a decision before dependent implementation. Routine implementation choices within the contract require no additional approval. A test stall follows the repository's five-minute rule: open a named diagnosis/correction subpoint under the current point before further feature work.

## Progress index

- [ ] R01 — [Reference primitives and exact resolvers](#r01--reference-primitives-and-exact-resolvers)
- [ ] R02 — [Switch every public route and remove handles](#r02--switch-all-public-routes-and-remove-handles)
- [ ] R03 — [Fresh snapshot identity once](#r03--fresh-snapshot-identity-once)
- [ ] R04 — [Collect each document batch once](#r04--collect-each-document-batch-once)
- [ ] R05 — [Bounded dependency reuse](#r05--bounded-reuse-of-immutable-dependency-facts)
- [ ] R06 — [Targeted outgoing traversal](#r06--targeted-outgoing-traversal)
- [ ] R07 — [Short polls, progress and transport diagnosis](#r07--short-polls-progress-and-transport-diagnosis)
- [ ] R08 — [Final gates and completed handoff](#r08--final-gates-and-completed-handoff)

These eight checkboxes are the authoritative completion state. All are unchecked because no implementation has started. Record Pending, In progress or Blocked and its concrete next action in the evidence log while the point remains unchecked. Complete means its top-level checkbox is checked.

Only the orchestrator changes checkbox states, including point-specific items; workers report evidence without ticking boxes. The orchestrator explicitly reviews each checklist item below, records executed evidence and verified implementation commit(s), then changes that point's top-level `[ ]` to `[x]`. A worker's completion message, code changes alone, tests merely defined, or a commit with missing checks never authorizes a tick. The next point cannot start until that tick is recorded. Do not pre-check future steps or tick several points from one general success statement.

Implementation acceptance and each point-specific checklist item must actually pass before its box is ticked. A blocked or unrun item remains unchecked. If later changes invalidate a completed item, reopen that item and the affected top-level point, record why, and reverify all affected successor evidence before closing them again. Record implementation commit IDs already present in Git; a following documentation commit can persist the checklist/evidence update without needing to know its own future hash.

## R01 — Reference primitives and exact resolvers

Contract: [01 — grammar, ownership, unavailable references and errors](01-symbol-identity-and-recovery.md).

Implement the shared typed source/assembly reference model and codec, deterministic Git/worktree/non-Git base selection, relative project matching, exact Roslyn declaration lookup and owner-bound assembly lookup. Keep declaration selection separate from snapshot hashing. Reuse existing metadata/provenance/session owners rather than introducing another assembly-loading subsystem.

Inventory every current handoff producer/consumer and handle-only component. Record the actual inventory in this point's evidence so R02 covers every path. Include physical-file inputs that also accept handles, candidate/body/context routes, text/Mermaid renderers, test helpers, configuration and docs.

Acceptance: all reference-level cases in specification 01 pass, including malformed escaping, no-ID declarations, exact duplicate-owner ambiguity, multi-context behavior, partial/linked sources and same-name binary owner pairing. Resolution never chooses a first match or invalidates an unchanged declaration due to content changes.

Verification: official build and affected FastTests for the new codec/source/assembly resolvers and path ownership. Add focused integration coverage where exact assembly provenance requires it. Tests for the new resolver do not depend on the old registry.

Boundary: no new public emission or dual public mode yet. The old public behavior remains until the atomic R02 switch; this preparatory commit must not be advertised as the completed migration.

Completion checklist:

- [ ] R01.1 — Shared codec, owner-base selection and exact source/assembly resolvers implement specification 01; no public dual mode is enabled.
- [ ] R01.2 — Complete producer/consumer/removal inventory is recorded from current local sources.
- [ ] R01.3 — Point acceptance and its required build/test selections passed; exact commands and results are recorded.
- [ ] R01.4 — Implementation commit(s) and the internal-only boundary are recorded; the orchestrator reviewed R01.1–R01.3.

## R02 — Switch all public routes and remove handles

Contract: [01 — output/input integration and complete removal](01-symbol-identity-and-recovery.md).

Use R01 references in every inventoried output/input and owner hop. Preserve existing field names, renderer formats and raw discovery selectors. Preserve snapshot-bound pages, analysis evidence and assembly generation leases. Replace handle-only recovery with the specified reference errors/actions.

Delete Base62 registry/counter/alphabet/persistence/locks, runtime setup/disposal, handle-only settings, old `i:` symbol serialization and obsolete tests/errors. Extract independent hashing/path/cursor behavior before removing shared classes. No compatibility flag or old-handle resolution path survives. Update the inventory with each path's completed migration/removal.

Acceptance: discovery → body/unrelated edit → follow-up → runtime restart succeeds for an unchanged exact declaration. Rename/delete/changed-ID cases fail clearly. Source/assembly/skeleton/context/graph/body/candidate paths use only new references. Byte/token budgets and outer/domain continuations remain executable; mixed batches preserve successes. Active handle machinery and counter-file I/O are absent.

Verification: official build; affected FastTests across symbols, assemblies, structures, dependencies, calls, hierarchy, formatting and schemas; source/assembly/relationship/index-scope transport-free handler contracts; narrowly selected relevant ExtendedIntegration tests under repository rules. Search active source/config/tests/docs for legacy machinery and classify each remaining legacy literal (negative test or historical artifact). Do not remove arbitrary files outside this repository, including a user's old counter file.

Update affected current-state docs, tool descriptions, navigation rule 08 and test helper assumptions in this point. Final public schemas must expose the specified input semantics without a second ID.

Completion checklist:

- [ ] R02.1 — Every R01 inventory route emits/consumes the new references; handle-only runtime/configuration/persistence paths are removed.
- [ ] R02.2 — Edit/restart/error/owner/budget/paging acceptance and required build/test selections passed; remaining legacy literals are classified.
- [ ] R02.3 — Current-state docs, schemas/descriptions, navigation rule 08 and test helpers match the public switch.
- [ ] R02.4 — Inventory disposition, executed evidence and implementation commit(s) are recorded; the orchestrator reviewed R02.1–R02.3.

## R03 — Fresh snapshot identity once

Contract: [04 — freshness, identity inputs and memoization](04-snapshot-refresh-and-analysis-cache.md).

Implement runtime-owned weak-key single-flight identity memoization for the exact immutable Solution. Route remaining identity call sites through it after R02's removals. Preserve full evidence fingerprints and get-index-scope's configured-inventory identity domain.

Verify current refresh detects loaded source/project/reference changes. Narrowly correct metadata-reference freshness in the existing fingerprint/reload owner if required by the prescribed replacement regression. Do not introduce watcher-only or timestamp-only freshness.

Acceptance: unchanged and concurrent calls compute identity once; cancelled callers do not poison another waiter; new source/config/reference context recomputes; same-timestamp changes are detected; equivalent reloads retain deterministic content identity; old Solution objects are not retained by completed memo entries.

Verification: official build; affected AnalysisSymbolIdentity, resident workspace/fingerprint and routing FastTests; workspace/index-scope/source handler integration contracts; relevant targeted extended workspace/reference tests. Record hashing versus refresh measurements separately.

Completion checklist:

- [ ] R03.1 — Freshness checks and weak-key single-flight identity memoization implement specification 04 at all remaining call sites.
- [ ] R03.2 — Same-timestamp source/reference changes, options, concurrent/cancelled waits, reload identity and lifetime acceptance passed.
- [ ] R03.3 — Required build/test selections passed; separate refresh/identity measurements and affected current-state docs are recorded.
- [ ] R03.4 — Implementation commit(s) and executed evidence are recorded; the orchestrator reviewed R03.1–R03.3.

## R04 — Collect each document batch once

Contract: [02 — separation, coverage and broad analysis](02-dependency-graph-analysis.md).

Separate immutable semantic collection from page/traversal projection in the existing Core dependency owner. Collect a selected document batch once, derive all relationship pages from it and reuse its compilation acquisitions. Update source handler document-window draining and applicable assembly scanner callers to the new separated API.

Retain broad incoming/both behavior and the public type-oriented dependency semantics. Use exact deterministic ownership/edge merge and explicit collection errors/coverage. This point does not yet depend on warm cache hits or targeted outgoing scheduling.

Acceptance: multiple relationship pages produce no repeated document scans; more-than-1,000-document coverage and late roots are correct; scope/generated filtering, exact type ownership, limits, totals/errors and cancellation are preserved.

Verification: first reproduce repeated collection with a deterministic failing counter assertion against the old behavior. Then official build, DependencyGraphScanner/traversal FastTests and source/assembly dependency handler contracts plus affected targeted extended navigation tests. Capture the controlled baseline and R04 measurements defined in specification 02. Do not use the audit EOF as proof of a scanner cause.

Completion checklist:

- [ ] R04.1 — The old repeated-collection regression was reproduced and its failing evidence preserved before correction.
- [ ] R04.2 — Collection/projection separation implements specification 02; all point-level coverage/ordering/error/cancellation acceptance passed.
- [ ] R04.3 — Required build/test selections and controlled baseline/R04 measurements completed; affected current-state docs are updated.
- [ ] R04.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R04.1–R04.3.

## R05 — Bounded reuse of immutable dependency facts

Contract: [04 — bucket identity, coverage, concurrency and retention](04-snapshot-refresh-and-analysis-cache.md), with [02 — reuse](02-dependency-graph-analysis.md).

Implement one runtime-owned Core dependency cache with specification 04's exact bucket key, per-document subscriptions, deterministic accounting/eviction and scalar facts. Reuse successful per-document facts, atomically track partial/full coverage and single-flight overlapping collection jobs. New projections share facts; new snapshots/scopes/generated settings do not.

Acceptance: warm requests whose required successful facts remain resident collect no new documents, concurrent overlaps do not duplicate work, partial facts cannot prove missing incoming edges, failure/cancellation permits retry, changed context prevents reuse, and oversized/evicted data recomputes correctly. One subscriber's cancellation cannot cancel another's job.

Verification: official build; cache/collector concurrency and time-injected lifecycle FastTests; source graph handler contracts covering root/direction/depth changes and refresh; relevant narrowly filtered extended tests. Record cold/warm collection counts and timings. Include admission accounting, TTL/LRU, source-owner eviction and runtime disposal evidence.

Completion checklist:

- [ ] R05.1 — Cache identity, partial/full coverage, single-flight subscribers and retention limits implement specification 04.
- [ ] R05.2 — Warm reuse, invalidation, concurrency/cancellation, error retry, accounting/TTL/LRU and disposal acceptance passed.
- [ ] R05.3 — Required build/test selections and cold/warm measurements completed; affected current-state docs are updated.
- [ ] R05.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R05.1–R05.3.

## R06 — Targeted outgoing traversal

Contract: [02 — outgoing algorithm and acceptance](02-dependency-graph-analysis.md).

For cold outgoing requests, schedule declaration-document work breadth-first from exact type/file roots using R05's collection facts. Include all eligible partial declarations, keep unrelated document types out of the root/frontier set and avoid terminal-depth expansion. Use full cached collection directly when available. Incoming/both continue using complete selected coverage.

Acceptance: a cold depth-1 query scans exactly required root documents; deeper scans expand only needed frontiers; file roots include every selected type; cycles/partials/linked ownership stay correct; results match broad projection by identity/evidence/order. All actual omissions and limits remain visible.

Verification: official build; targeted scanner/traversal FastTests; source dependency handler integration contracts and relevant selected extended partial/cross-project relationship cases. Execute the controlled cold/warm/broad/targeted measurements from specification 02. Record semantic counts as acceptance evidence and timings as measured observations.

Completion checklist:

- [ ] R06.1 — Root/partial/file selection and bounded breadth-first outgoing scheduling implement specification 02.
- [ ] R06.2 — Targeted/broad equivalence, exact required-document counts, depth/cycle/filter/limit and ownership acceptance passed.
- [ ] R06.3 — Required build/test selections and cold/warm/broad/targeted measurements completed; affected current-state docs are updated.
- [ ] R06.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R06.1–R06.3.

## R07 — Short polls, progress and transport diagnosis

Contract: [03 — all sections](03-long-running-operations-and-transport.md).

Separate the 15-second first wait from one-second polls. Add immutable measured progress and atomic running controls, preserving tokens/query/outer/domain paging. Wire phase/counter events through existing loading, refresh, identity, dependency collection and formatting boundaries; unrelated analyses report phases without fabricated counters.

Preflight first-call control budgets before admitting work, and implement executable poll-budget recovery that retains its admitted operation/token. Preserve cancellation, inactivity, capacity, shutdown and replay behavior with shared R05 subscriptions.

Acceptance: one job across polls; correct windows; monotonic progress; agent-visible wait/action; no busy loop in the client verification; tight-budget recovery does not lose the job; final success/error and continuations remain usable. The bounded SDK fixture transport component and actual connected-agent round-trip are both recorded.

Verification: official build; LongRunningToolCallStore/control formatter/schema FastTests; transport-free actual Navigator route contracts and the explicitly classified bounded SDK fixture transport component; affected narrowly selected extended host/response tests. Perform the specification's actual-client verification and EOF evidence capture without treating routine-excluded E2E tests as automatically passed. Diagnose/fix any reproducible cause; if historical reproduction is impossible, record the expressly allowed undetermined outcome and successful controlled evidence.

Completion checklist:

- [ ] R07.1 — Response windows, progress, token issuance, budget preflight/recovery and lifecycle implement specification 03.
- [ ] R07.2 — Required build/component/handler/transport selections passed; the bounded SDK fixture transport component completed.
- [ ] R07.3 — The actual connected-agent round-trip and EOF diagnosis outcome satisfy specification 03; no mandatory client check is unavailable.
- [ ] R07.4 — Implementation commit(s), client/build identity, linked evidence/R07.md summary, executed evidence and current-state docs are recorded; the orchestrator reviewed R07.1–R07.3.

## R08 — Final gates and completed handoff

Review the final implementation against the four contracts and the R02 migration inventory. Verify no mandatory point is blocked, no temporary old/new public paths remain and current-state docs/agent rules match implemented behavior.

Run the official build if changes since the last successful build require it, and the complete eligible routine solution gate once at completion. Finish any required narrowly selected extended checks not already covered by a valid earlier run. Do not automatically run the complete extended suite, routine-excluded E2E suite or a separate broad benchmark suite.

Review measured cold/warm/targeted evidence and client evidence without turning fixture timings into universal claims. Check task links after source removals/renames. Record final commit(s), checks, known factual limits and implemented contract coverage. A reproducible unresolved defect or unavailable mandatory client gate prevents completion; a documented undetermined historical EOF cause alone does not, under specification 03's defined evidence outcome.

This point consolidates required completion evidence; it does not add a separate testing feature or invoke another workflow. Leave deployment/push to a later explicit request.

Completion checklist:

- [ ] R08.1 — Final contract/inventory/doc review passed; R01–R07 remain checked and have no unresolved mandatory blockers.
- [ ] R08.2 — The required final eligible routine gate and any outstanding required build/targeted extended checks passed.
- [ ] R08.3 — Measurements, client evidence and factual limits were reviewed; task links remain valid and no deployment/push was performed.
- [ ] R08.4 — Final implementation commit(s), exact completed checks and handoff evidence are recorded; the orchestrator reviewed R08.1–R08.3.

## Verification commands and evidence policy

Repository [.agents/rules/04](../../.agents/rules/04-verification.mdc) owns all mandatory gates and stall handling; [.agents/rules/05](../../.agents/rules/05-git.mdc) owns automatic commits and staging. Commands:

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test-fast.ps1 -Filter '<affected eligible tests>'
pwsh -File ./scripts/test-integration.ps1 -Filter '<affected routine contracts>'
pwsh -File ./scripts/test-integration.ps1 -IncludeExtended -Filter '<relevant extended tests>'
pwsh -File ./scripts/test.ps1
```

Choose actual existing/new fully-qualified test selections after inspecting their categories; placeholders are not executable final evidence. All official scripts exclude E2EIntegration even with IncludeExtended. Keep transport-free requirements in the eligible selection. Capture actual executed commands, exit outcomes and reports. Preserve relevant `temp/*.log`/TRX evidence before a later script overwrites it. Do not claim read-but-unrun tests passed.

Documentation-only consolidation requires reviewed diffs, valid local links and `git diff --check`, not a product build.

## Evidence log

| Point | Commit(s) | Executed verification | Result / blocker |
| --- | --- | --- | --- |
| Specification consolidation | See Git history for this document's creation commit | Six documents reviewed; 53 local links/anchors and R01–R08 order checked; diff whitespace check; no production gate | Documentation checks passed; no production implementation started |
| Orchestrator checklists and concept review | See Git history for the commit adding this row | Three Luna/high read-only concept reviews and re-reviews; [finding disposition](reviews/luna-concept-review.md); seven documents reviewed; local links/anchors, R01–R08 order, 40 unchecked boxes and diff whitespace checked | All three re-reviews PASS; documentation checks passed; production gates not run |
| R01 | — | Not run | Pending |
| R02 | — | Not run | Pending |
| R03 | — | Not run | Pending |
| R04 | — | Not run | Pending |
| R05 | — | Not run | Pending |
| R06 | — | Not run | Pending |
| R07 | — | Not run | Pending |
| R08 | — | Not run | Pending |
