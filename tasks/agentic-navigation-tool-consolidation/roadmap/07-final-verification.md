# M7 — Final agent-flow verification and release readiness

## Intent and scope

Verify the implemented concept as a usable MCP server and close the task with evidence. Implements P7 and [concept 8.2–8.4](../concept.md#82-proportionate-improvement-checks). Depends on M6. Reuse earlier passed checks/artifacts when still applicable; no second full development pass.

## Executable points

- [ ] **M7.1 — Verify final contract and remove remaining migration leftovers.** Check exactly twelve tool names, SDK schemas, defaults/validation, consumers, advertised follow-ups/recovery, current-state docs/README and agent navigation rule against implemented behavior. Remove any remaining superseded paths after checking consumers. Acceptance: no result recommends a removed name and no duplicated old route remains; all required capabilities are represented by working replacements. Any code correction receives its affected official checks.
- [ ] **M7.2 — Run required final checks and inspect Exploration flows.** Run official build and the complete eligible routine solution tests once on the final implementation, plus still-required narrow extended coverage not already verified. Complete/read the existing Exploration scenarios for all twelve tools and distinct modes/views/sections using W1-W8 representative source/assembly flows. Inspect saved initial requests, responses, polls/pages, domain cursors/body windows and exact owner/reference follow-ups. Acceptance: correct useful content and known error/recovery results, not merely exit code zero; no unresolved required content/ownership/follow-up failure. Keep output artifacts under existing ignored directories.
- [ ] **M7.3 — Verify real MCP transport and practical gains.** Run a small actual MCP smoke against an identified final server build using an existing client facility or a minimal script. Exercise initialize/list/call and at least one actual operation poll, one continuation page, and one owner-qualified reference follow-up through JSON-RPC. Select representative production calls and existing supported budgets/configuration to trigger those transitions with modest samples; no enormous or artificially stalled workload. Record the distinction from Exploration's transport-free binder flow. Review existing W3/W4 output-size and corrected-runtime evidence and explain concrete gains from the inspected agent flows. Acceptance: contract and all named transitions are verified through actual transport and claimed gains have evidence; a missing transition remains an open check. No model/LSP benchmark campaign or new transport harness. Official test scripts' E2E exclusion remains intact; this explicit smoke is a separate manual check, not an attempt to bypass that exclusion.
- [ ] **M7.R — Independent Sol final review, focused fixes, and task acceptance.** Check completion against concept and roadmap, preserved substantive regressions, final outputs, required gates, and remaining findings. Correct concrete blockers, rerun affected/invalidated checks, and have the reviewer verify the fixes. Acceptance: all required behavior/checks pass, no material observed regression or unresolved required finding remains, and index/milestone status reflects evidence. Nonblocking polish does not prolong the task.

## Verification and non-goals

Follow repository script/category/stall rules and the orchestrator stopping policy. Exploration does not replace tests or JSON-RPC smoke. A renamed tool alone or a smaller truncated response is not success. Do not rerun an unchanged successful suite for each review round or test every argument combination. If the runner lacks a required scenario, complete it in that runner with coordinated ownership; do not create a parallel framework.

## Completion evidence

Not started. Fill this compact record during execution:

- Final implementation/build identity and relevant commits:
- Official commands, filters, outcomes, and log/result paths:
- Exploration scenario names, targets, artifact paths, inspected pages/windows/follow-ups, expected error cases:
- Actual transport smoke command/client, build and result:
- Output reduction and runtime finding evidence (link M0/M4/M6):
- Independent reviewer, actionable findings, fixes/rechecks, nonblocking suggestions:
- Unrun checks or unresolved requirements (none is recorded only when verified):
- Final acceptance and roadmap closure:
