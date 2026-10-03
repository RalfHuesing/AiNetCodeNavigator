# R05 — Bounded reuse of immutable dependency facts

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R04](R04-single-collection.md). Next: [R06](R06-targeted-outgoing.md).

Contract: [04 — bucket identity, coverage, concurrency and retention](../04-snapshot-refresh-and-analysis-cache.md), with [02 — reuse](../02-dependency-graph-analysis.md).

Implement one runtime-owned Core dependency cache with specification 04's exact bucket key, per-document subscriptions, deterministic accounting/eviction and scalar facts. Reuse successful per-document facts, atomically track partial/full coverage and single-flight overlapping collection jobs. New projections share facts; new snapshots/scopes/generated settings do not.

Acceptance: warm requests whose required successful facts remain resident collect no new documents, concurrent overlaps do not duplicate work, partial facts cannot prove missing incoming edges, failure/cancellation permits retry, changed context prevents reuse, and oversized/evicted data recomputes correctly. One subscriber's cancellation cannot cancel another's job.

Verification: official build; cache/collector concurrency and time-injected lifecycle FastTests; source graph handler contracts covering root/direction/depth changes and refresh; relevant narrowly filtered extended tests. Record cold/warm collection counts and timings. Include admission accounting, TTL/LRU, source-owner eviction and runtime disposal evidence.

Completion checklist:

- [ ] R05.1 — Cache identity, partial/full coverage, single-flight subscribers and retention limits implement specification 04.
- [ ] R05.2 — Warm reuse, invalidation, concurrency/cancellation, error retry, accounting/TTL/LRU and disposal acceptance passed.
- [ ] R05.3 — Required build/test selections and cold/warm measurements completed; affected current-state docs are updated.
- [ ] R05.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R05.1–R05.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Pending |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Implementation has not started |
