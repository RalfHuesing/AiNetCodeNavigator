# R04 — Collect each document batch once

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R03](R03-snapshot-identity.md). Next: [R05](R05-dependency-cache.md).

Contract: [02 — separation, coverage and broad analysis](../02-dependency-graph-analysis.md).

Separate immutable semantic collection from page/traversal projection in the existing Core dependency owner. Collect a selected document batch once, derive all relationship pages from it and reuse its compilation acquisitions. Update source handler document-window draining and applicable assembly scanner callers to the new separated API.

Retain broad incoming/both behavior and the public type-oriented dependency semantics. Use exact deterministic ownership/edge merge and explicit collection errors/coverage. This point does not yet depend on warm cache hits or targeted outgoing scheduling.

Acceptance: multiple relationship pages produce no repeated document scans; more-than-1,000-document coverage and late roots are correct; scope/generated filtering, exact type ownership, limits, totals/errors and cancellation are preserved.

Verification: first reproduce repeated collection with a deterministic failing counter assertion against the old behavior. Then official build, DependencyGraphScanner/traversal FastTests and source/assembly dependency handler contracts plus affected targeted extended navigation tests. Capture the controlled baseline and R04 measurements defined in specification 02. Do not use the audit EOF as proof of a scanner cause.

Completion checklist:

- [ ] R04.1 — The old repeated-collection regression was reproduced and its failing evidence preserved before correction.
- [ ] R04.2 — Collection/projection separation implements specification 02; all point-level coverage/ordering/error/cancellation acceptance passed.
- [ ] R04.3 — Required build/test selections and controlled baseline/R04 measurements completed; affected current-state docs are updated.
- [ ] R04.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R04.1–R04.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Pending |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Implementation has not started |
