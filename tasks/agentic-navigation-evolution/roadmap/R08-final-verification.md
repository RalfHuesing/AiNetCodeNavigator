# R08 — Final gates and completed handoff

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R07](R07-polling-and-transport.md). Next: Completed handoff.

Review the final implementation against the four contracts and the R02 migration inventory. Verify no mandatory point is blocked, no temporary old/new public paths remain and current-state docs/agent rules match implemented behavior.

Run the official build if changes since the last successful build require it, and the complete eligible routine solution gate once at completion. Finish any required narrowly selected extended checks not already covered by a valid earlier run. Do not automatically run the complete extended suite, routine-excluded E2E suite or a separate broad benchmark suite.

Review measured cold/warm/targeted evidence and client evidence without turning fixture timings into universal claims. Check task links after source removals/renames. Record final commit(s), checks, known factual limits and implemented contract coverage. A reproducible unresolved defect or unavailable mandatory client gate prevents completion unless that additional client evidence has been expressly deferred under the user-approved execution policy; a documented undetermined historical EOF cause alone does not, under specification 03's defined evidence outcome.

This point consolidates required completion evidence; it does not add a separate testing feature or invoke another workflow. Leave deployment/push to a later explicit request.

Completion checklist:

- [ ] R08.1 — Final contract/inventory/doc review passed; R01–R07 remain checked and have no unresolved mandatory blockers.
- [ ] R08.2 — The required final eligible routine gate and any outstanding required build/targeted extended checks passed.
- [ ] R08.3 — Measurements, client evidence and factual limits were reviewed; task links remain valid and no deployment/push was performed.
- [ ] R08.4 — Final implementation commit(s), exact completed checks and handoff evidence are recorded; the orchestrator reviewed R08.1–R08.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Pending |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Implementation has not started |
