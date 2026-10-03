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
| Working state | Pending |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Implementation has not started |
