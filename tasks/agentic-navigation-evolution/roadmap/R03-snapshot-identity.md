# R03 — Fresh snapshot identity once

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R02](R02-public-reference-migration.md). Next: [R04](R04-single-collection.md).

Contract: [04 — freshness, identity inputs and memoization](../04-snapshot-refresh-and-analysis-cache.md).

Implement runtime-owned weak-key single-flight identity memoization for the exact immutable Solution. Route remaining identity call sites through it after R02's removals. Preserve full evidence fingerprints and get-index-scope's configured-inventory identity domain.

Verify current refresh detects loaded source/project/reference changes. Narrowly correct metadata-reference freshness in the existing fingerprint/reload owner if required by the prescribed replacement regression. Do not introduce watcher-only or timestamp-only freshness.

Acceptance: unchanged and concurrent calls compute identity once; cancelled callers do not poison another waiter; new source/config/reference context recomputes; same-timestamp changes are detected; equivalent reloads retain deterministic content identity; old Solution objects are not retained by completed memo entries.

Verification: official build; affected AnalysisSymbolIdentity, resident workspace/fingerprint and routing FastTests; workspace/index-scope/source handler integration contracts; relevant targeted extended workspace/reference tests. Record hashing versus refresh measurements separately.

Completion checklist:

- [ ] R03.1 — Freshness checks and weak-key single-flight identity memoization implement specification 04 at all remaining call sites.
- [ ] R03.2 — Same-timestamp source/reference changes, options, concurrent/cancelled waits, reload identity and lifetime acceptance passed.
- [ ] R03.3 — Required build/test selections passed; separate refresh/identity measurements and affected current-state docs are recorded.
- [ ] R03.4 — Implementation commit(s) and executed evidence are recorded; the orchestrator reviewed R03.1–R03.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | In progress; started from clean verified R02 HEAD `671496d` on 2026-10-04 |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Coordinate immutable image/provenance inputs, add focused regressions before existing freshness corrections, then implement full R03 and execute official gates/audit |

### Prerequisites and independent ownership

R02 product commit `9c1c3951649a16fbdb1b5a065bd8d2a00a79aa44` and verified evidence commit `671496d` are present; all R02 detail boxes and index checkbox are checked. The working tree and index were clean before R03 assignment. Root reread the full specification 04, specification 02 canonical paths, execution rules, all repository rules, R03 and existing identity call-site inventory. R02's verified structural checkpoint governs this point.

- `/root/r03_identity`: newly explicitly configured `gpt-6-luna`, reasoning `high`; owns strict typed fingerprint encoding, weak-key runtime identity memoization/tickets, all remaining identity consumers, request-owned projection separation, Host runtime/support wiring and focused Symbols/MCP Fast tests. Core Workspace, Integration tests and current-state docs are excluded from its ownership.
- `/root/r02_implementation`: reused with its original explicitly configured `gpt-6-luna`, reasoning `high`; new R03 freshness assignment owns Core Workspace capture/fingerprint/refresh/load integration and focused Workspace Fast tests, with narrowly necessary TestKit capture fixtures. It coordinates immutable image bytes/hash and supported binding provenance with the identity owner. Existing reproducible freshness defects require official failing regressions before correction.
- `/root/r02_tests_docs`: reused with its original explicitly configured `gpt-6-luna`, reasoning `high`; owns focused eligible Integration tests, separate refresh/identity measurement fixtures and current-state documentation/root README/navigation rule updates against actual code. Shared helpers retain meaningful independent assertions; existing large contract classes are not globally reorganized.

Root owns all verification, diagnosis, progress/checkboxes and commits. Workers do not execute competing gates. A separate explicitly configured Sol/medium read-only full audit follows actual completed gates. No R03 completion is implied by these assignments or unrun new tests.
### R03-P01 reference freshness — frozen original baseline

Workers held all production behavior changes; only two focused test fixtures and an unreferenced immutable input record file were added. Root reviewed fixture order before execution: compile/capture the original reference first, then replace it.

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, approximately 3.78 seconds reported build time.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~ResidentSolutionMetadataFreshnessTests.GetCurrentSnapshot_WhenReferencedImageChangesWithSamePathSizeMtimeAndMvid_RebindsCompilation' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failing test / zero skipped, approximately 1.6821 seconds. The original compilation's constant is 17. A valid replacement changes it to 18 while preserving path, size, timestamp and MVID (and verifies changed SHA-256). The next fresh snapshot remains the identical old Solution object, failing `Assert.NotSame`. This proves the prescribed missing reference freshness before correction; the later replacement constant assertion is not claimed executed.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceSnapshotIdentityContractTests' --logger 'console;verbosity=normal'`: FAIL, exit 1, one completed failing test, approximately 3.8436 seconds. Initial origin JSON spans outer pages; the new fixture parses its incomplete first page and fails before the replacement invariant. This is a fixture defect, not additional freshness proof. Luna is assigned tests-only proper outer-page draining with unchanged query/budgets; production correction remains held until the original handler baseline reaches its actual invariant.
- Logs/TRX preserved at `temp/roadmap-evidence/R03/metadata-baseline-run1/`. No stall or orphan observed; all gate sessions completed. No R03 checkbox is checked.

- Fixture paging correction build: `pwsh -File ./scripts/build.ps1` FAIL, exit 1, 6 CS1061 errors / zero warnings, approximately 1.84 seconds reported build time. The shared reader tuple names its reconstructed body Text; the new fixture incorrectly reads Body. Luna is assigned tests-only member corrections. Saved build log: `temp/roadmap-evidence/R03/metadata-baseline-build2/build.log`. No production correction or additional acceptance execution occurred.


### R03-P01 actual eligible handler baseline / implementation release

- The tests-only fix reads every outer response page using the existing shared reader with the original 16,384-byte / 1,024-token budgets and unchanged arguments. Snapshot metadata comes from the first-page header; origin JSON uses the reconstructed full body. Six mistaken tuple member names were corrected without changing helper or production semantics.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, approximately 1.94 seconds reported build time.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceSnapshotIdentityContractTests' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failing test / zero skipped, approximately 3.8674 seconds. Original discovery and `OldApi` metadata-origin presence pass. After replacement at the same path/size/restored timestamp with changed image SHA-256, the fresh handler still reports the identical source snapshot ID (`Assert.NotEqual` fails). Full-body replacement binding and source-reference follow-up assertions remain required for the eventual passing test; they are not claimed executed in this failing baseline.
- Artifacts: `temp/roadmap-evidence/R03/metadata-baseline-run3/{build.log,test-integration.log,IntegrationTests.trx}`. Original Fast baseline independently proves the same-object defect with MVID also preserved. All gates terminate normally; no stall or orphan.
- Root now authorizes Luna's complete production freshness correction and identity service integration against the binding R03 contract. Focused tests retain their failed invariants and will additionally verify replacement `NewApi` binding. Independent audit and all mandatory gates remain open.
