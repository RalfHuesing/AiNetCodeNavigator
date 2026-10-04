# R08 — Final gates and completed handoff

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R07](R07-polling-and-transport.md). Next: Completed handoff.

Review the final implementation against the four contracts and the R02 migration inventory. Verify no mandatory point is blocked, no temporary old/new public paths remain and current-state docs/agent rules match implemented behavior.

Run the official build if changes since the last successful build require it, and the complete eligible routine solution gate once at completion. Finish any required narrowly selected extended checks not already covered by a valid earlier run. Do not automatically run the complete extended suite, routine-excluded E2E suite or a separate broad benchmark suite.

Review measured cold/warm/targeted evidence and client evidence without turning fixture timings into universal claims. Check task links after source removals/renames. Record final commit(s), checks, known factual limits and implemented contract coverage. A reproducible unresolved defect or unavailable mandatory client gate prevents completion unless that additional client evidence has been expressly deferred under the user-approved execution policy; a documented undetermined historical EOF cause alone does not, under specification 03's defined evidence outcome.

This point consolidates required completion evidence; it does not add a separate testing feature or invoke another workflow. Leave deployment/push to a later explicit request.

Completion checklist:

- [x] R08.1 — Final contract/inventory/doc review passed; R01–R07 remain checked and have no unresolved mandatory blockers.
- [x] R08.2 — The required final eligible routine gate and any outstanding required build/targeted extended checks passed.
- [x] R08.3 — Measurements, client evidence and factual limits were reviewed; task links remain valid and no deployment/push was performed.
- [x] R08.4 — Final implementation commit(s), exact completed checks and handoff evidence are recorded; the orchestrator reviewed R08.1–R08.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Complete; verified source corrections and final 780-case routine PASS, independent audits/deltas closed |
| Implementation commit(s) | `a750be14222dc8c76521c109b0943de45f06fc40`; `f63bd49f6cd93887d9d56789386338146bd0155c` |
| Executed verification | Final complete eligible routine PASS: 680 Fast + 100 Integration, zero failures/skips; official builds and affected corrections PASS, exact commands below |
| Measurements / artifacts | Actual gate logs/TRX under temp/roadmap-evidence/R08; valid file/heading links; F01–F05 remain deferred, SDK proof and structural limits reviewed |
| Blocker / next action | No confirmed functional or mandatory automated blocker; F01–F05 explicitly deferred additional evidence, no push/deployment |

### Final candidate review and routine gate in progress

Root starts R08 from clean R07 completion HEAD `eff1a1dc1f21af59fb4feeb51f56864a9bea4563` and product `a3efc4cba3b5af0c33f3bc6d24941d207f3f44a6`, with R01–R07 details/index checked. Existing explicitly configured gpt-6.1-sol/medium main implementation worker and separate read-only Sol/medium auditor inspect all four contracts, R02 full migration inventory and actual integrated owners/consumers/docs. Product/tests stay frozen throughout Root's final official routine gate.

`pwsh -File ./scripts/test.ps1 --logger 'console;verbosity=normal' --logger 'trx;LogFilePrefix=R08'` is the once-at-completion complete eligible routine selection, excluding ExtendedIntegration/E2EIntegration under official rules. Fast stage finishes 680 tests / 679 PASS / 1 FAIL / zero skips, 27.3355 seconds. Integration is still in progress; no final success is claimed. Root monitors observable per-test progress within one-minute intervals.

#### R08-D01 — required-input validation regression

Actual existing regression `TestDetectorTests.TestRecommendationBuilderRejectsNullRequiredInputs` fails at line 86: expected ArgumentNullException, actual NullReferenceException. Stack: new SourceReferenceFormattingContext.CreateAsync:41 via CreateFormatterAsync:32, public TestRecommendationBuilder.BuildAsync:38. Test asserts both required symbol and Solution rejection. Root preserves TRX `TestResults/R08_net10.0_20261004131508.trx` and temp/test.log before another gate.

Main Sol read-only diagnosis confirms the public overload builds a formatter before its existing private-core guards. Minimal proposed cause correction adds the same ordered ArgumentNullException.ThrowIfNull(targetSymbol)/ThrowIfNull(solution) at public entry before formatter acquisition, preserving private guards for internal callers and both existing test assertions. No new layer/helper or assertion change is needed. Root holds edits until this full Integration run freezes; then official build/affected TestDetector tests and independent correction-only delta are mandatory. This actual functional defect supersedes both initial source-reviewed no-finding reports and is not deferred.

### Complete routine baseline outcome / finite correction slice

The complete baseline ends FAIL exit 1: Fast 680 / 679 PASS / 1 FAIL, 27.3355 s; Integration 99 / 95 PASS / 4 FAIL, 5.2296 min; zero skipped in both. Individual tests continue completing throughout; no individual or stage stalls for five minutes. Logs/TRX are preserved at `temp/roadmap-evidence/R08/final1/{test.log,FastTests.trx,IntegrationTests.trx}`. This is actual failing-regression evidence, never a final PASS.

- D01: required-input public TestRecommendationBuilder guard before formatter acquisition; existing null assertion unchanged.
- D02: bounded SDK component fixture wait uses a relative timer and actually observes 999 ms. Root reopens R07.2/index until its absolute monotonic deadline correction, targeted PASS and delta audit; see R07's reopened evidence.
- D03: SourceToolsContractTests.GetContextSelectsSectionsSharesIdentityAndContinuesOnlyTheCursorSection misses direct-use OtherBehavior; GetContextTestsPagesAllCandidatesAndKeepsExpansionLimitPartialOnFinalPage reports 0 instead of 257. Fixture calls and caller-scope separation are valid. Exact retargeted/source symbol seam after fresh immutable metadata capture is under diagnosis; preserve exact-owner/ambiguity guards and candidate cap/paging assertions.
- D04: RelationshipToolsContractTests.GetImpact_OriginalSdkContractRequiresSymbolAndRoutesSourceAndAssemblySymbols helper expects a completed page immediately after fresh-call running-admission budget recovery; actual completed projection separately requires 129 tokens. Main must distinguish the specified control-admission and completed-page budget phases while preserving exact same-unit recovery, complete reconstructed long-caller evidence and query/token invariants, or report an actual product conflict. No assertion weakening is authorized.

Existing task-specific explicitly configured Sol/medium Main owns the minimal product/candidate/budget-helper fixes; the separate sole-file Sol/medium SDK worker owns only its absolute deadline correction. An attempted extra test-worker follow-up is rejected by the agent thread limit; no implementation is performed by Root, and ownership is consolidated in the existing Main worker. Product/test edits begin only after the full gate freezes. Root serially verifies affected changes, obtains independent correction-only audit and then repeats the complete routine selection because actual changes/failures justify it. No new broad audit or additional performance proof loop is started.

Root file-link inventory reads 64 current/task/rule sources: 280 local link targets and 29 heading links, all valid; diagnostics `final1/document-{file,anchor}-links.json`. Legacy-prefix search yields 64 lines: five codec rejection/drive-exception code/comments, 56 test rejection/drive literals (including one unrelated numeric interpolation false positive), three docs/rule rejection guidance. No active Base62/registry/counter/old handle-error machinery matches; `rg` no-match exit 1 is a negative inventory result, not a failed product gate. `final1/legacy-prefix-inventory.txt` retains all lines.

### Executed cause proof and affected correction gates

All worker-owned product/tests are frozen before each official Root gate; no concurrent build/test. No assertion/category/analyzer/timeout was weakened.

| Artifact directory under temp/roadmap-evidence/R08 | Exact command | Result |
| --- | --- | --- |
| diagnostic2 | `pwsh -File ./scripts/build.ps1` | PASS 0, zero warnings/errors, 5.54 s |
| diagnostic2 | `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceToolsContractTests.TestCandidates_FreshCapturedProjectReferencesMapBoundMethodsToExactSourceDefinitions' --logger 'console;verbosity=normal'` | FAIL 1, 1 case, 6.5078 s; both distinct-retargeted binding and exact Roslyn source mapping assertions pass, final expected OtherBehavior/TargetTests candidate list fails |
| fixed3 | `pwsh -File ./scripts/build.ps1` | PASS 0, zero warnings/errors, 5.51 s |
| fixed3 | `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~TestDetectorTests' --logger 'console;verbosity=normal'` | PASS 0, 28 tests, zero skipped, 5.9351 s |
| fixed3 | `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceToolsContractTests.TestCandidates_FreshCapturedProjectReferencesMapBoundMethodsToExactSourceDefinitions|FullyQualifiedName~SourceToolsContractTests.GetContextSelectsSectionsSharesIdentityAndContinuesOnlyTheCursorSection|FullyQualifiedName~SourceToolsContractTests.GetContextTestsPagesAllCandidatesAndKeepsExpansionLimitPartialOnFinalPage|FullyQualifiedName~RelationshipToolsContractTests.GetImpact_OriginalSdkContractRequiresSymbolAndRoutesSourceAndAssemblySymbols|FullyQualifiedName~LongRunningTransportComponentTests' --logger 'console;verbosity=normal'` | PASS 0, 5 tests, zero skipped, 1.1780 min |

D01 public guards reject invalid inputs before compilation/formatter work. D03 cause-level change normalizes successful unambiguous bound symbols through Roslyn FindSourceDefinitionAsync in the exact current Solution, then retains exact original-definition SymbolEqualityComparer matching. Existing error receiver, syntax-location, ambiguity, duplicate-project, caller scope, candidate cap, complete paging and evidence assertions remain. No display-name/doc-ID fallback, new cache or symbol registry is introduced. The original 257-candidate page test finishes in 44 seconds, a bounded observed duration rather than a latency promise; it retains complete expected candidates, all pages and final partial-limit state.

D04 separates fresh/poll running-control admission minima from completed projection-unit minima. A retry cannot repeat the same control failure or same projection-unit failure; current exact budgets and token survive running polls. Complete expected result text, long-caller reference occurrence and body reconstruction remain asserted. D02 uses two local absolute monotonic deadline waits; strict >=1000 interval assertions and original frame/server windows remain.

Fresh successful SDK artifact `temp/agentic-navigation-evolution/R07/20261004T112543495Z-115ed35b81cb4c3e92294a745e860803/`: completed=true, executionCount=1; server version `1.0.2+eff1a1dc1f21af59fb4feeb51f56864a9bea4563` plus precisely this frozen correction slice, same SDK/frame-writer identity. Actual running receive→next send intervals 1013/1013/1000 ms, poll response durations 1002/1016 ms, final 8 ms, replay 19 ms. Raw frames/traffic remain ignored; new-build connected-client evidence is still F05, not this fixture.

### Independent correction audit and corrected final gate

Separate read-only gpt-6.1-sol/medium actual-diff/source delta audit PASS closes D01–D04 after the affected build/28 Fast/5 Integration PASS. No new concrete defect or assertion weakening is found. Exact current Solution source mapping, ambiguity/error/receiver/current-document boundaries, no persistent symbols, both measured SDK deadlines/cancellation, control-versus-projection budget phase recovery and complete paging/reference assertions were independently reviewed. Unchanged integrated owners retain the earlier complete point audits; no new blanket audit matrix is started.

Verified correction commit: `a750be14222dc8c76521c109b0943de45f06fc40`. R07.2/index is explicitly rechecked in completion commit `2b3685bf18b8e84fcdd27fee2b7b166c84ef2145` after actual SDK PASS and delta. Root now executes the same complete routine command again against this committed corrected source, because the previous full FAIL and cause changes require final integration coverage. Its result remains pending until exit/log/TRX establish it. No more product changes are planned while this gate runs.

Final task specification status/entry-point summaries are reconciled to verified R03–R07 owners; they do not alter technical requirements. F01–F05 and current structural boundaries remain factual handoff limits. R01–R07 implementation and detail/index completion are reviewed; R08 alone remains open pending its final full gate.

### R08-D05 — actual shutdown cancellation-dispatch race

The corrected full routine Fast stage fails a previously passing cache-lifetime case: 680 / 679 PASS / 1 FAIL, zero skipped, 22.4866 s. DisposeAsync_AwaitsBlockedCollectionWithoutHoldingCacheLock expects OperationCanceledException at line 201 but receives no exception. Integration continues with per-test progress; no edits occur during the gate. Root reopens R05.1/R05.2/index and assigns existing Sol/medium Main the correction.

Read-only cause: cache shutdown marks batches Retired immediately, while CancelAsync-linked request callbacks are dispatched asynchronously. Retirement completion can win that propagation gap and ordinary partial results can pass the existing request-token-only success boundary. Minimum owner-local fix captures existing shutdown.Token before linking and checks that captured token at final result publication in addition to requestToken. The captured CancellationToken struct avoids reading a disposed CTS later. No generic cancellation/ownership layer, test delay, timeout increase or weakened success/cancellation assertion is authorized. Per-target retirement diagnostics stay distinct from whole-cache shutdown. Existing actual failing regression is retained; final affected checks/delta/full successor gate remain mandatory.

### Shutdown correction closure and final successor gate

Second complete routine command (same exact command as baseline) ends Integration PASS but overall exit 1: Fast 680 / 679 PASS / 1 FAIL / zero skipped, 22.4866 s; Integration 100 / 100 PASS / zero skipped, 6.1287 min. Only D05 remains in that run. Artifact `temp/roadmap-evidence/R08/full-corrected4/{test.log,FastTests.trx,IntegrationTests.trx}`. This is not called a full PASS; suites exceed five minutes with continuing individual progress, no individual/stage stall.

D05 correction commit `f63bd49f6cd93887d9d56789386338146bd0155c` has exactly three owner-local changed lines: capture existing shutdown token, link that exact token, check it before final result plus existing request-token check. `pwsh -File ./scripts/build.ps1`: PASS0/zero warnings/errors, 4.75 s. `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~DependencyGraphCacheTests|FullyQualifiedName~DependencyGraphCacheRetentionTests|FullyQualifiedName~DependencyGraphCacheCancellationTests|FullyQualifiedName~LongRunningProgressContractTests' --logger 'console;verbosity=normal'`: PASS0, 23 tests / zero skipped, 2.7195 s. Logs/TRX `temp/roadmap-evidence/R08/shutdown-fixed5/`.

Separate actual-diff Sol/medium delta PASS closes D05: direct captured token prevents callback-dispatch success, stays safe after source disposal, throw remains within Unsubscribe finally; target retirement/retry/quiescing/locks and all test assertions remain unchanged. Root rechecks R05.1/R05.2/index in `54989b88b54a9de2ee42fab4f3f56050015a9ff9`. Third complete routine command against this committed correction is now running; it consolidates all affected successor evidence and remains pending until actual success. Repeats are driven only by actual full-gate failures and cause corrections; no arbitrary proof matrix or performance repetition occurs.

Root inventories the only eligible ExtendedIntegration cases, both WorkspaceLoadingIntegrationTests methods already passed in R07 (5.7748 s). Neither calls TestRecommendationBuilder nor DependencyGraphCache; subsequent corrections change test-candidate matching, fixture wait/budget helpers and cache result cancellation, not those loader/scratch responsibilities. No outstanding affected Extended case is identified; complete E2E/Extended suites are not implicitly claimed passed.

### Final structural and factual-limit review

Root's final source inventory records nonempty lines as observations, not split targets: RelationshipTools 1500, SourceToolsContractTests 1292, AssemblyToolsContractTests 2236, SourceRelationshipToolsContractTests 725. RelationshipTools still mixes route-specific validation/resolution/recovery with response presentation; legacy contract classes still combine route scenarios and fixture builders. Required common collection/projection, exact identity/reference semantics, bounded facts/subscriptions, outgoing frontier scheduling and immutable request progress now have cohesive owners. New reference, snapshot, collection/cache/outgoing, progress/handler/SDK contracts and the retargeted binding regression are separately executable through official filters. Blanket handler/registration rewrites, arbitrary line-count-driven splitting, global fixture/parser/test-framework reorganization and E2E rewrites remain explicitly outside this task; size alone introduces no completion blocker.

Root removes no required state merely for size: weak exact-Solution memo/tickets, successful per-document coverage/subscribers/retention, BFS admitted distances, measured immutable progress identity/phase/time and local shutdown token each satisfy a specified requirement or actual evidenced defect. No general generator sandbox, provenance platform, scheduler, audit/hook/cache/progress framework or public configuration knob was introduced.

Deferred items reviewed: F01 cross-platform capture/path-replacement stress; F02 stronger shared-host loaded/captured image evidence; F03 expanded five-run timing matrices; F04 extra simultaneous registry-retirement/cache-inflight composition; F05 new-build connected-client and expanded original/generated-target EOF capture. All remain unexecuted additional evidence with explicit reason/follow-up/owner in findings.md; none is a PASS or deferred confirmed bug. HistoricalCause remains undetermined. Work-count regressions prove one collection per needed document, warm resident facts produce zero new collections and depth-1 outgoing scans exact roots rather than a full fixture; they do not prove a universal latency gain. No external deployed DLL/client was replaced, no push/deployment occurred.

Final current/task/rule documentation inventory: 64 files, 287 local link targets, 30 heading links; zero missing targets/anchors. Diagnostics `temp/roadmap-evidence/R08/final6/document-links.json`. The final ordinary gate is still running; all 680 Fast cases now PASS (22.7748 s) and integration progress continues without an observed failure.

### Completed final gate and orchestrator acceptance

Final `pwsh -File ./scripts/test.ps1 --logger 'console;verbosity=normal' --logger 'trx;LogFilePrefix=R08'`: **PASS, exit 0** on committed corrected source at `54989b88b54a9de2ee42fab4f3f56050015a9ff9` (product corrections `a750be14222dc8c76521c109b0943de45f06fc40` and `f63bd49f6cd93887d9d56789386338146bd0155c`). Fast: 680/680 PASS, 22.7748 s. Integration: 100/100 PASS, 5.9690 min. Root checks actual TRX counters: zero failed/error/timeout/aborted/notExecuted in both reports. No individual/stage stall occurs; progress continues through the suite. Complete Extended/E2E suites are intentionally excluded by the official routine contract, not claimed passed. Required affected Extended coverage remains the actual R07 two-case PASS described above.

Artifacts `temp/roadmap-evidence/R08/final6/{test.log,FastTests.trx,IntegrationTests.trx,document-links.json}`; original final TRX names `R08_net10.0_20261004133854.trx` and `R08_net10.0_20261004134454.trx`. All ignored raw artifacts are local/nonportable. Final tested Debug AiNetCodeNavigator.dll SHA-256 `1B9C183E5AB0B9562F2E9EC2C659139AAC30BCC73104E48B30B495921487A5F7`. The last successful official build is 4.75 s / zero warnings/errors; only task documentation follows, so another product build is not required.

Final SDK fixture run `temp/agentic-navigation-evolution/R07/20261004T114448851Z-96147e75c6524534aaa1aab957d3f140/`: server `1.0.2+54989b88b54a9de2ee42fab4f3f56050015a9ff9`, SDK `2.2.0.0`, explicit bounded frame-writer client `1.0`, completed=true/executionCount=1. Running receive→next-poll waits are 1010/1001/1028 ms; server running poll durations 1002/1018 ms; final 3 ms, replay 16 ms. Same token/query, monotonic phases and final replay remain asserted. This is current-build SDK component evidence, not a connected product client; F05 remains unrun.

Root independently reviews R08.1: all four implemented contracts, completed R02 migration inventory, actual consumer/registration/lifetime seams, current docs/rule08 and smallness assessment; separate Main review/full independent bounded source audit plus cause-specific D01–D05 deltas are complete. Existing independently audited R01–R07 acceptance remains checked, with R05/R07 explicitly reopened and corrected where full gates contradicted earlier evidence. No confirmed defect remains.

Root reviews R08.2: final complete eligible routine PASS above, latest official build PASS and all affected correction checks PASS; no outstanding relevant Extended gate. Earlier failing runs are retained as failing evidence, not relabeled successful. Root reviews R08.3: actual work-count/identity/SDK measurements, image/client identity limits, F01–F05 disposition and valid local links/anchors; no push/deployment. Root reviews R08.4: exact source commits, checks, audit/fix history, raw artifact locations, final structural exclusions and factual limits are recorded here and in findings.md. Root deliberately checks all four detail items and the R08 index only after these completed results.

Completed handoff: all R01–R08 implementation points are finished under the user-approved execution policy. Future review may execute F01–F05 in separate work; no additional current implementation, generic framework/refactor, external DLL deployment/client replacement or automatic publishing is pending. A later new-build connected-client exercise requires separate deployment/reconnect authorization; historical EOF remains undetermined.
