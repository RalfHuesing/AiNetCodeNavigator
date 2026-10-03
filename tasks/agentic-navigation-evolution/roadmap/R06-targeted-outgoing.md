# R06 — Targeted outgoing traversal

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R05](R05-dependency-cache.md). Next: [R07](R07-polling-and-transport.md).

Contract: [02 — outgoing algorithm and acceptance](../02-dependency-graph-analysis.md).

For cold outgoing requests, schedule declaration-document work breadth-first from exact type/file roots using R05's collection facts. Include all eligible partial declarations, keep unrelated document types out of the root/frontier set and avoid terminal-depth expansion. Use full cached collection directly when available. Incoming/both continue using complete selected coverage.

Acceptance: a cold depth-1 query scans exactly required root documents; deeper scans expand only needed frontiers; file roots include every selected type; cycles/partials/linked ownership stay correct; results match broad projection by identity/evidence/order. All actual omissions and limits remain visible.

Verification: official build; targeted scanner/traversal FastTests; source dependency handler integration contracts and relevant selected extended partial/cross-project relationship cases. Execute the controlled cold/warm/broad/targeted measurements from specification 02. Record semantic counts as acceptance evidence and timings as measured observations.

Completion checklist:

- [ ] R06.1 — Root/partial/file selection and bounded breadth-first outgoing scheduling implement specification 02.
- [ ] R06.2 — Targeted/broad equivalence, exact required-document counts, depth/cycle/filter/limit and ownership acceptance passed.
- [ ] R06.3 — Required build/test selections and cold/warm/broad/targeted measurements completed; affected current-state docs are updated.
- [ ] R06.4 — Implementation commit(s) and evidence are recorded; the orchestrator reviewed R06.1–R06.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Pending |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Implementation has not started |
