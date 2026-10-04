# R07 — Short polls, progress and transport diagnosis

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R06](R06-targeted-outgoing.md). Next: [R08](R08-final-verification.md).

Contract: [03 — all sections](../03-long-running-operations-and-transport.md).

Separate the 15-second first wait from one-second polls. Add immutable measured progress and atomic running controls, preserving tokens/query/outer/domain paging. Wire phase/counter events through existing loading, refresh, identity, dependency collection and formatting boundaries; unrelated analyses report phases without fabricated counters.

Preflight first-call control budgets before admitting work, and implement executable poll-budget recovery that retains its admitted operation/token. Preserve cancellation, inactivity, capacity, shutdown and replay behavior with shared R05 subscriptions.

Acceptance: one job across polls; correct windows; monotonic progress; agent-visible wait/action; no busy loop in the client verification; tight-budget recovery does not lose the job; final success/error and continuations remain usable. The bounded SDK fixture transport component and actual connected-agent round-trip are both recorded.

Verification: official build; LongRunningToolCallStore/control formatter/schema FastTests; transport-free actual Navigator route contracts and the explicitly classified bounded SDK fixture transport component; affected narrowly selected extended host/response tests. Perform the specification's actual-client verification and EOF evidence capture without treating routine-excluded E2E tests as automatically passed. Diagnose/fix any reproducible cause; if historical reproduction is impossible, record the expressly allowed undetermined outcome and successful controlled evidence.

Completion checklist:

- [x] R07.1 — Response windows, progress, token issuance, budget preflight/recovery and lifecycle implement specification 03.
- [x] R07.2 — Required build/component/handler/transport selections passed; the bounded SDK fixture transport component completed.
- [x] R07.3 — The actual connected-agent round-trip and EOF diagnosis outcome satisfy specification 03, or remaining evidence is explicitly deferred under the user-approved execution policy; no confirmed functional defect remains.
- [x] R07.4 — Implementation commit(s), client/build identity, linked evidence/R07.md summary, executed evidence and current-state docs are recorded; the orchestrator reviewed R07.1–R07.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Complete; focused gates and independent full/delta audit closed; F05 deferred evidence |
| Implementation commit(s) | `a3efc4cba3b5af0c33f3bc6d24941d207f3f44a6` |
| Executed verification | Official builds, 114 affected Fast cases via passing selection/correction, 15 affected Integration cases via passing selection/correction, 2 selected Extended cases; exact commands below |
| Measurements / artifacts | [Tracked operation/SDK evidence](../evidence/R07.md), raw R07 gate/component directories below; F05 connected-client/expanded EOF deferred |
| Blocker / next action | No confirmed defect; begin R08 final routine gate and handoff; F01–F05 remain explicit deferred evidence |

### Prerequisite and complete scoped assignment

Root verifies clean R06 completion HEAD `f08bb3f2baed70ca1636dc035a0f8f1a1900adf4`, implementation `3e30409bf81e5ff03d69927da07d3a2d61a3b819` and checked R06 detail/index. Root reads full specification 03 and the R07 contract, including actual-client/EOF clauses and affected specification 04 subscription/lifetime and 02 coverage.

The existing explicitly started gpt-6.1-sol/medium main worker owns the complete R07 production/Fast/transport-free actual-handler/docs slice. The second explicitly started Sol/medium worker owns only the new bounded LongRunningTransportComponentTests.cs with actual SDK in-process host/frames/fixture tool/real store+formatter, no SDK client/child process/MSBuild or full product flow, first-frame 20-second and poll-frame 5-second bounds. Both receive complete rules/contracts/acceptance/file boundaries and no gates/commits/checklist authority. The separate Sol/medium read-only auditor prepares the requirement matrix and waits for freeze. Root owns all serialized gates and evidence. No new public task/cancel/poll-window API or general progress framework is authorized.

### Live-client evidence disposition

Root performs read-only process/image inspection before any R07 gate. All six observed AiNetCodeNavigator.exe processes run `C:\Daten\Tools\AiNetCodeNavigator-win-x64\AiNetCodeNavigator.exe`, outside this checkout's build directory. Their deployed AiNetCodeNavigator.dll has SHA-256 `C7244D9B860C9F69BC2766802AB4A943A68385CB82B1624726699C37B217B327`, last-write UTC 2026-10-03T16:09:05.4689535Z. The current R06-verified local Debug image has SHA-256 `C14D0EC7E40601F76BE46FA9D1AAF58F31CFEA4741C935D1E2C73FB9A7E658FF`, last-write UTC 2026-10-04T10:33:10.7230035Z. No specific process-to-connector mapping/client version is exposed by the callable navigation tool metadata. Thus existing connected tools cannot be claimed to test the new verified build.

The user authorizes no deployment and explicitly permits postponing evidence to findings.md while prioritizing complete implementation. Root records F05 for the new-build connected-agent round-trip and expanded original/generated-target EOF investigation rather than changing an external installation, restarting unrelated processes or calling an old deployment a new-build PASS. Historical EOF trace is present, but its cause remains undetermined; shorter polls alone are not a cause correction. Required automated store/formatter/actual-handler behavior and bounded SDK fixture transport remain executable acceptance, not deferred. R07/R08 completion uses execution.md's user-approved evidence disposition; deferred checks themselves are never marked PASS. Final tracked evidence/R07.md will distinguish actual automated artifacts and F05 precisely.
The R07.3 checklist and R08 client-evidence completion wording explicitly reference this user-approved F05 disposition; this changes only completion evidence handling, not the operation/control/transport product contract or automated acceptance.

### Concrete implementation ownership and parallel test boundary

Root reads the emerging operation store/control formatter and narrow progress types. The existing store reserves collision-checked values from the existing opaque 39-digit generator before any job admission. Required controls use actual reserved-token UTF-8/tokenizer measurements at maximum Int64 elapsed for every phase; optional counters do not affect the admission pair. First and poll windows are separate internal durations; production poll remains one second when only the first-window test override is supplied.

The request-owned host adapter publishes immutable snapshots from typed Core dependency events and monotonic time. Its scoped AsyncLocal integration avoids changing all existing tool delegate signatures; Core keeps no MCP reference. The actual per-request callback captures the adapter instance before cache work, rather than resolving ambient state from a shared worker's inherited context. This is forced by independently counting fulfilled needs for every subscriber of one cache task. Core events identify successful new/shared/cached needs by ticket/project/document; failed needs are not successful progress. Deep outgoing totals remain unpublished throughout frontier growth. No new public progress/task subsystem is introduced.

To finish the finite slice efficiently, Root explicitly starts another gpt-6.1-sol/medium worker with fork none for only the new LongRunningProgressContractTests.cs focused Fast class, complete specifications/rules/acceptance and no gate/commit/checklist authority. Main Sol retains all production/existing test compatibility, actual Navigator-handler Integration, docs and navigation-rule edits. The SDK component worker freezes its one new file. File ownership is disjoint; Root waits for all files to freeze before any official gate. No executed R07 verification is yet claimed.

### Frozen first build and failing regression before correction

All three implementation/test slices are frozen. `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, 7.42 seconds, archived `temp/roadmap-evidence/R07/baseline1/build.log`.

The new Fast worker identifies one concrete adapter atomicity defect by actual source inspection: DependencyDocumentSatisfied adds a distinct need before rejecting published-total overflow, so the rejected event corrupts later snapshots. Root deliberately keeps the cause unfixed until its executable regression runs. `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.FastTests.Mcp.LongRunningProgressContractTests' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, 9 cases / 8 PASS / 1 FAIL / zero skipped, 3.2455 seconds. CoveredNeedsUseExactTicketProjectDocumentIdentityAndNeverInventAGrowingTotal expects 4 after rejecting a fifth distinct event but observes 5 (line 244). The exact-identity/immutable-total assertions are retained. Artifacts `temp/roadmap-evidence/R07/baseline1/{test-fast.log,FastTests.trx}`.

The eight passing cases already exercise independent one-second default polls, worst-phase/max-Int64 byte/token admission pair and fixed 39-digit cost, executable first/poll budget recovery with one job, live token collision, unrepresentable-error no admission, immutable concurrent phases/saturating elapsed, request-specific new/shared/cached counts and failed-need retry without false coverage. Root releases only the minimal pre-mutation overflow check in the existing adapter lock. Final build/affected tests/actual-handler/SDK component/audit remain pending; no failing gate is claimed PASS.

### Completed gates and fix rounds

All gates below are Root-owned, serialized with frozen product/test sources. No concurrent build/test or stalled test occurred; all selections have zero skipped cases. Artifacts are Git-ignored/nonportable, with log and TRX preserved under `temp/roadmap-evidence/R07/<directory>/`.

| Directory | Exact command | Executed result |
| --- | --- | --- |
| baseline1 | `pwsh -File ./scripts/build.ps1` | PASS 0; 7.42 s; zero warnings/errors |
| baseline1 | `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.FastTests.Mcp.LongRunningProgressContractTests' --logger 'console;verbosity=normal'` | FAIL 1; 8 PASS/1 FAIL, 3.2455 s; rejected over-total event mutated adapter identity set |
| verified2 | `pwsh -File ./scripts/build.ps1` | PASS 0; 1.62 s; zero warnings/errors after pre-mutation adapter check |
| verified2 | `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~LongRunningToolCallStoreTests|FullyQualifiedName~LongRunningProgressContractTests|FullyQualifiedName~McpToolResultsTests|FullyQualifiedName~McpFormattingTests|FullyQualifiedName~McpInputSchemaTests|FullyQualifiedName~DependencyGraphCacheTests|FullyQualifiedName~DependencyGraphCacheRetentionTests|FullyQualifiedName~DependencyGraphCacheCancellationTests|FullyQualifiedName~DependencyGraphOutgoingCollectorTests' --logger 'console;verbosity=normal'` | FAIL 1; 113 PASS/1 FAIL, 12.5793 s; legacy idle-TTL fixture used new default poll wait longer than its artificial TTL |
| verified2 | `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~LongRunningNavigationProgressContractTests|FullyQualifiedName~LongRunningTransportComponentTests|FullyQualifiedName~TrafficCaptureTransportIntegrationTests|FullyQualifiedName~SourceDependencyGraphOutgoingContractTests|FullyQualifiedName~SourceDependencyGraphCacheContractTests|FullyQualifiedName~SourceRelationshipToolsContractTests.SourceRelationshipHandlersReturnNavigableResultsErrorsAndBoundedProjections|FullyQualifiedName~AssemblyToolsContractTests.AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes' --logger 'console;verbosity=normal'` | FAIL 1; 13 PASS/1 FAIL, 1.0694 min; new schema assertion incorrectly treated nullable SDK type array as a string |
| workspace-extended3 | `pwsh -File ./scripts/test-integration.ps1 -IncludeExtended -Filter 'FullyQualifiedName~WorkspaceLoadingIntegrationTests.MSBuildSolutionLoader_CustomTargetsAndScratchCleanupPreserveWorkspaceSnapshot|FullyQualifiedName~WorkspaceLoadingIntegrationTests.MSBuildSolutionLoader_ColdSolutionsWithSameNamedProjectsHaveIsolatedSnapshots' --logger 'console;verbosity=normal'` | PASS 0; 2 tests, 5.7748 s |
| final4 | `pwsh -File ./scripts/build.ps1` | PASS 0; 2.25 s; zero warnings/errors after fixture-only corrections |
| final4 | `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~LongRunningToolCallStoreTests.ValidPollResetsRunningIdleDeadline' --logger 'console;verbosity=normal'` | PASS 0; 1 test, 2.6298 s |
| final4 | `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~LongRunningNavigationProgressContractTests.DependencyGraph_SdkSchemaKeepsPollingOpaqueAndInternalWindowsOutOfArguments' --logger 'console;verbosity=normal'` | PASS 0; 1 test, 1.5696 s |
| audit-baseline5 | `pwsh -File ./scripts/build.ps1` | PASS 0; 1.83 s; zero warnings/errors; A01 test added, cause unchanged |
| audit-baseline5 | `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~LongRunningNavigationProgressContractTests.DependencyGraph_InitialMaterializationRemainsLoadingUntilAnalysisStarts' --logger 'console;verbosity=normal'` | FAIL 1; 1 test, 2.5831 s; expected loading, actual refreshing |
| audit-fixed6 | `pwsh -File ./scripts/build.ps1` | PASS 0; 1.50 s; zero warnings/errors after minimal initial LoadTask await |
| audit-fixed6 | `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~LongRunningNavigationProgressContractTests' --logger 'console;verbosity=normal'` | PASS 0; 4 tests, 12.4568 s |

These results cover all 114 originally selected Fast and 14 originally selected Integration cases plus the added A01 regression, using the original successful cases and targeted corrections. They are not described as a single all-green broad rerun; R08 runs the complete eligible routine selection once.

The adapter correction validates distinct-need overflow before insertion inside its existing lock; duplicate identity notifications remain harmless. The legacy idle test explicitly sets a 15 ms poll override while retaining 800 ms inactivity, both 500 ms waits, valid-poll reset and eventual expiry assertions; a separate new contract verifies production's independent one-second poll default. The SDK schema test asserts the actual ordered `string`/`null` type array while retaining numeric-token rejection. No production acceptance/assertion was weakened.

Independent actual-code audit by separate gpt-6.1-sol/medium covers the complete R07 requirement matrix: one job/query/cursor/replay, fixed collision-checked token issuance, exact first/poll budget recovery, independent windows, phase/elapsed monotonicity, new/shared/cached/failed document needs and totals, existing cancellation/capacity/expiry/shutdown and Core/host boundaries, consumer wiring, actual routes, SDK component, docs and rule 08. Its only confirmed finding A01 was initial materialization incorrectly reported as refreshing. Root's actual failing baseline above precedes the minimal existing LoadTask await. The independent correction-only delta audit closes A01: caller cancellation releases the lease/owned wait without cancelling shared resident load; existing failure/retry/lifetime behavior remains, GetCurrentSnapshotAsync occurs exactly once, no new state/callback/framework. No remaining confirmed defect or required automated gate is open.

### Orchestrator acceptance and structural checkpoint

Root reviews each item: R07.1 implemented behavior with independently executable exact budget/progress/lifecycle contracts; R07.2 required focused build/Fast/actual-handler/SDK/selected Extended coverage passes after corrections; R07.3 live-client/expanded historical EOF evidence is explicitly deferred as F05 under the later user-approved execution contract, never a new-build PASS, historicalCause=undetermined; R07.4 source commit, image/SDK/client provenance and measured raw artifact links are in [evidence/R07.md](../evidence/R07.md), docs and rule 08 reflect actual implementation. F05 does not hide a confirmed bug.

Root source/diff review confirms the existing refresh, cache subscription and lifetime owners remain authoritative; source initial loading now uses the existing Task boundary. Required immutable request progress is centralized in one narrow host adapter with typed Core events and no MCP/Core dependency inversion. Required exact-identity dedup state, monotonic timestamp/phase state and nullable fixed total cannot be removed without losing specified behavior. No general progress, scheduler, generator or provenance layer was added.

RelationshipTools remains approximately 1500 nonempty lines, principally route resolution/recovery and final formatting. Cohesive retained facts, frontier traversal and operation progress are now separately owned. Existing large handler contract classes still combine scenario fixtures. New outgoing/cache/progress contract classes are independently filterable. Blanket route/registration rewrites, arbitrary line-count splitting, global fixture/parser/test-framework changes and E2E rewrites remain outside this task; see [findings structural scope](../findings.md#structural-scope).
