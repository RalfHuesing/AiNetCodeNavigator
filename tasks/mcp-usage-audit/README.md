# MCP usage audit and navigation improvement

Status: prepared; no audit or product implementation has started.

This is a task-specific orchestration package, not an invocation of the optional `.agents/agent-workflow/` steps. Read [the charter](charter.md), [protocol](protocol.md), and [roadmap](roadmap.md) before execution. Repository rules remain binding.

## Start and resume

Give the executing agent this prompt:

> Execute tasks/mcp-usage-audit/prompts/orchestrator.md. Use tasks/mcp-usage-audit as the task directory. Perform the usage audits and evidence-driven improvements within its charter. Resolve repository aliases through the ignored local registry. Resume from the first unfinished roadmap item, and preserve completed evidence.

This prompt starts execution only when the user sends it or otherwise explicitly starts the campaign. Preparing this package does not start the campaign.

## Contents

| File | Purpose |
| --- | --- |
| [charter.md](charter.md) | Objective, authority, boundaries, and completion criteria |
| [protocol.md](protocol.md) | Reproducible exploration, measurements, privacy, and verification |
| [roadmap.md](roadmap.md) | Ordered execution and handoff state |
| [results.md](results.md) | Sanitized campaign results and decision ledger |
| [prompts/](prompts/orchestrator.md) | Orchestrator, explorer, implementer, and reviewer assignments |
| [templates/](templates/run.json) | Local run metadata, findings, and per-question evidence |

## Data locations

- `temp/external-repos/audit-targets.local.json`: local alias-to-target registry, populated from the user's local inventory. Do not print it or copy it into Git.
- `temp/external-repos/mcp-usage-audit/<run-id>/`: local question definitions, ground truth, findings, run metadata, replay recipes, and agent handoffs.
- `temp/exploration/<scenario>/<UTC-run>/`: runner-generated raw requests, responses, attempts, and errors. Reference these from local run metadata; preserve evidence before cleanup or reruns.
- `tasks/mcp-usage-audit/results.md`: manually sanitized summaries only. Templates are blank forms, not evidence of completed work.

The registry is machine-local and intentionally absent from Git. If missing, reconstruct it from `temp/external-repos/external-repos.md`; if both are missing, ask for the local inventory. Never guess paths. Never force-add ignored artifacts. The local registry also records unverified user-supplied framework and size hints; preflight must verify them.

## Current references

- [Manual exploration](../../docs/development/mcp-exploration.md) and its source under `tools/AiNetCodeNavigator.Exploration/`.
- [Tool contracts](../../docs/tools/README.md), [response budgets](../../docs/mcp-response-budgets.md), and [continuations](../../docs/mcp-long-running-calls.md).
- [Traffic capture](../../docs/mcp-traffic-capture.md): separate live-transport measurements, when needed.
- [Assembly export](../../docs/assembly-export.md) and [verification gates](../../docs/development/build-and-tests.md).

These links describe current behavior. This task describes future work and acceptance criteria; it is not proof of implementation.
