# R02-D01 — Audit regression host abort and fixture teardown

[Parent R02](R02-public-reference-migration.md) · [Execution rules](execution.md)

Status: Complete. Root-reviewed finite failing baseline and fixture correction are recorded below; R02 behavioral findings remain open separately.

## Reproduction and evidence

After the official build passed, the root executed:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests' --logger 'console;verbosity=normal'
```

The run started on 2026-10-04 at approximately 00:01:11 Europe/Berlin. VSTest reported a crashed testhost at 00:03:16; the TRX has zero completed/executed results. No passing or failing assertion is established by this aborted run. The root stopped only the remaining verified owned run process tree (original root PID 123380, matching command and creation time), then confirmed session exit 1. The host aborted before the five-minute stall threshold; this is an abnormal verification-stage diagnosis, not an invented five-minute stall or an acceptance failure proof.

Artifacts: `temp/roadmap-evidence/R02/audit-regression-run1/{build.log,test-integration.log,IntegrationTests.trx,owned-process-tree.json}`. The saved TRX records the abort, not a complete test selection.

## Diagnosis scope and assignment

Luna/high owns the narrow new regression fixtures and the inert fault seam only. Determine the smallest affected fact through official root-run filters. Inspect fixture cancellation, the separation between request-waiter cancellation and operation lifetime, fault-injected scope retention and teardown. `AssemblyAnalysisSession.DisposeAsync` waits for leases to drain; a deliberately leaked baseline lease must be observable without making the test's own cleanup hang. This source observation is a hypothesis, not proof of the testhost crash cause.

Before a behavior fix, retain fault-injected scopes in the fixture and explicitly release them in a test `finally` after capturing/asserting the active-access invariant, if necessary to allow the original faulty baseline to terminate. Keep the production branch faulty for its actual regression proof. Control a real operation cancellation or a deterministic cancelled resolver task; do not assume cancelling the public request waiter cancels the job. Avoid infinite fixture waits. No larger timeout, skips, category changes, broad rerun, weakened assertion, or unrelated roadmap work is authorized.

## Acceptance and verification

- [x] D01.1 — Identify the affected fact/stage with actual narrow execution and finite teardown evidence; distinguish observed abort from inferred causes.
- [x] D01.2 — Correct fixture cancellation/cleanup in its owning test responsibility, retaining actual before-fix lease/Context/Skeleton invariants.
- [x] D01.3 — Official build and complete narrow regression-class execution terminate normally and establish the real expected failing baseline; preserve logs/TRX.

The root alone records results and checks these items. If the abort remains unexplained or required narrow verification cannot terminate, retain D01 and R02 open and record the concrete blocker. Do not claim that the original crashed selection demonstrated A01/A02/A03.

## Execution evidence

Initial abnormal run and owned-process cleanup recorded above. Next action: Luna read-only fixture diagnosis and smallest-fact selection, then root official narrow execution before any behavior correction.

### Narrow source isolation

`pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests.SourceSkeletonSemanticReferenceFailuresRetainPerItemRecoveryActions' --logger 'console;verbosity=normal'`: FAIL, exit 1, one completed failing test, approximately 1.876 seconds. The Source Skeleton exact-resolution branch reaches the missing per-item failure-format assertion; fixture and runtime cleanup terminate normally. Saved log/TRX: `temp/roadmap-evidence/R02/diagnosis-source-run1/`.

This excludes the Source-only fact from the observed indefinite-cleanup hypothesis. Luna's read-only diagnosis confirms that the original cancellation fixture assumed request-waiter cancellation stopped the job, which is not the current operation contract. Faulted scopes were also not retained for cleanup, while Session teardown explicitly waits for leases. These are confirmed fixture faults; they are not a proven Windows/testhost crash explanation. Authorized tests-only correction: capture the acquired fault scope, snapshot the active-count invariant, release that scope in fixture `finally`, use a deterministic cancelled task for job-path cancellation, then verify fresh navigation and the pre-cleanup count. Retain every original assertion; check missing Next action before shared status text so the next actual baseline directly establishes the required recovery failure. No production behavior fix yet.

### Exact orphan attribution and cancellation-fact diagnosis

The next official build failed (exit 1, 10 MSB3026 retry warnings, MSB3027/MSB3021 errors, approximately 14 seconds) because apphost PID 101336 still held the IntegrationTests executable. Saved log: `temp/roadmap-evidence/R02/diagnosis-build2/build.log`. Root's initial ancestry-only cleanup missed this orphan after its intermediate parents exited; no success is claimed for that build.

The process's executable and creation time match the original aborted baseline. Its xUnit response file names exactly `StableReferenceResolutionContractTests.AssemblySymbolResolutionCancellationAfterScopeAcquisitionReleasesLease`; the root decoded only the executing-case attribution and saved `orphan-attribution.json` alongside `orphan-host.json`. This establishes the active original fact/stage, rather than guessing from class order. Root then stopped only that positively attributed orphan. The original test's waiter-cancellation assumption and uncollected scope make its execution/teardown nonfinite; both fixture causes are corrected by the new cancelled-task plus captured-scope/finally design. The adapter's crash report is retained verbatim; no unrelated OS crash cause is inferred.

No further gate may run until verifying that the attributed orphan has ended. Next: corrected fixture official build and five-case baseline, with finite completion and actual invariant failures required.

### Corrected fixture build and finite baseline

- After confirming the attributed orphan ended, `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 1.79 seconds.
- The exact five-fact command above now completes normally: FAIL, exit 1, 5 completed failing tests, approximately 3.176 seconds. No host abort, stalled teardown or remaining IntegrationTests apphost. Saved build/log/TRX: `temp/roadmap-evidence/R02/diagnosis-regression-run2/`.
- Actual A01 regression proof: both Source and Assembly semantic owner-resolution failures omit `Next action`; the failing output includes the complete failed item. Actual A03 proof: cancelled-task and exception cases each capture active-access count 1 rather than 0 before fixture cleanup, then complete cleanup/recovery finitely.
- A02 fixture fails earlier on explicit `includeReferences:false` with only `body`: the existing argument-presence contract requires `callers` even for explicitly supplied false. This is not a Context reference failure proof. Luna is assigned tests-only selection correction to `[body, callers]` for direct false/closure true comparison; production validation remains unchanged. Required singleton A02 actual baseline and D01 closure remain pending.

### D01 closure — root-reviewed finite actual baseline

- Corrected fixture build: `pwsh -File ./scripts/build.ps1` PASS, exit 0, 0 warnings/errors, approximately 2.91 seconds.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests.AssemblyClosureContextDeclarationRetainsExactOwnerReferenceForBodyFollowup' --logger 'console;verbosity=normal'`: completed FAIL, one test, approximately 2.723 seconds. The direct false/reference comparison succeeds; the closure true Target omits `handoffId` and its property read fails. This is actual A02 pre-fix evidence, not argument rejection. Saved build/log/TRX: `temp/roadmap-evidence/R02/diagnosis-context-run3/`.
- Final prepared-class baseline: `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests' --logger 'console;verbosity=normal'`: completed FAIL, exit 1, 5 failing / 0 passing / 0 skipped, approximately 3.314 seconds. All five fail on the required actual A01/A02/A03 invariants; normal raw missing-ID and raw/stable success/count subcases in the lifetime facts pass before their captured leak assertions. Saved build/log/TRX: `temp/roadmap-evidence/R02/audit-regression-run4/`.
- The root rechecked no IntegrationTests apphost remains. D01.1 is satisfied by exact executing-cancellation-case/orphan attribution and narrow isolation; D01.2 by controlled cancelled-task semantics plus scope capture/finally cleanup retaining pre-cleanup counts; D01.3 by analyzer-clean build and finite whole-class failing baseline. The adapter abort text remains historical; no unobserved OS failure is claimed. No larger timeouts, skips, category changes, broad blind retry or production correction was used to close diagnosis.
- D01 is Complete. R02 remains open pending behavior fixes, required affected gates and separate Sol re-audit.
