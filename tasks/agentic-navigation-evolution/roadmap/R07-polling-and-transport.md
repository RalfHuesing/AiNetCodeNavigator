# R07 — Short polls, progress and transport diagnosis

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R06](R06-targeted-outgoing.md). Next: [R08](R08-final-verification.md).

Contract: [03 — all sections](../03-long-running-operations-and-transport.md).

Separate the 15-second first wait from one-second polls. Add immutable measured progress and atomic running controls, preserving tokens/query/outer/domain paging. Wire phase/counter events through existing loading, refresh, identity, dependency collection and formatting boundaries; unrelated analyses report phases without fabricated counters.

Preflight first-call control budgets before admitting work, and implement executable poll-budget recovery that retains its admitted operation/token. Preserve cancellation, inactivity, capacity, shutdown and replay behavior with shared R05 subscriptions.

Acceptance: one job across polls; correct windows; monotonic progress; agent-visible wait/action; no busy loop in the client verification; tight-budget recovery does not lose the job; final success/error and continuations remain usable. The bounded SDK fixture transport component and actual connected-agent round-trip are both recorded.

Verification: official build; LongRunningToolCallStore/control formatter/schema FastTests; transport-free actual Navigator route contracts and the explicitly classified bounded SDK fixture transport component; affected narrowly selected extended host/response tests. Perform the specification's actual-client verification and EOF evidence capture without treating routine-excluded E2E tests as automatically passed. Diagnose/fix any reproducible cause; if historical reproduction is impossible, record the expressly allowed undetermined outcome and successful controlled evidence.

Completion checklist:

- [ ] R07.1 — Response windows, progress, token issuance, budget preflight/recovery and lifecycle implement specification 03.
- [ ] R07.2 — Required build/component/handler/transport selections passed; the bounded SDK fixture transport component completed.
- [ ] R07.3 — The actual connected-agent round-trip and EOF diagnosis outcome satisfy specification 03; no mandatory client check is unavailable.
- [ ] R07.4 — Implementation commit(s), client/build identity, linked evidence/R07.md summary, executed evidence and current-state docs are recorded; the orchestrator reviewed R07.1–R07.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Pending |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Implementation has not started |
