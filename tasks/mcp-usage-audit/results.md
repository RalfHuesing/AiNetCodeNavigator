# Sanitized results and decisions

Status: first round active. Preflight and permanent calibration are complete; question baseline, findings, and product changes are pending.

## Run index

| Run ID | Revision | Alias | Question IDs | Mode / cache state | Outcome | Local evidence key |
| --- | --- | --- | --- | --- | --- | --- |
| A-001 | `ece3fecc6fc76f279f7cc42ec7c00ac35820af74` | R01–R04 | — | Static target preflight; one new calibration process with resident reuse | Four source targets exist. R02 calibration invoked all 12 tools. R03 has missing local references, so complete semantic coverage is unproven. Runtime source loading of external targets is pending. | `round1/preflight-orchestrator.json`, `round1/preflight-legacy/summary.md`, `round1/preflight-modern/sanitized-summary.json`, `round1/calibration.json`, `round1/calibration-observations.json` |

Preflight verified the ignored registry and raw-evidence boundaries, clean target working trees, target revisions, installed SDK/runtime, and relevant design-time build configuration. R01 has 15 projects targeting modern .NET variants; R03 has three classic .NET Framework projects; R04 has three modern .NET projects. These are project metadata observations, not proof of successful indexing. R03 has 56 unavailable declared reference files. The Navigator's own R02 load reported seven projects and 305 C# documents with no known framework coverage gap. The calibration's assembly result and several bounded tool results reported partial coverage or explicit continuations; process success is not counted as a complete answer. The runner exercised production schemas, validation, binder, handlers, and formatting without transport. No external build, restore, clean, source edit, or transport check ran.

Use relative keys below `temp/external-repos/mcp-usage-audit/`, not external paths or symbols.

## Findings and decisions

| Finding ID | Category / priority | Affected aliases | Sanitized observation | Decision and reason | Acceptance / verification | State |
| --- | --- | --- | --- | --- | --- | --- |

## Tool contract decisions

| Decision ID | Tool / behavior | Observed problem | Retain / change / merge / remove / add | Consumer impact | Evidence / commit |
| --- | --- | --- | --- | --- | --- |

## Before and after

| Question / alias | Baseline / candidate revisions | Correctness | Calls / attempts | Visible bytes | Answer latency | Usability observations | Limitations |
| --- | --- | --- | --- | --- | --- | --- | --- |

## Final acceptance

Not evaluated. Record actual checks and dates, export state, holdout results, unresolved blockers, privacy review, external read-only verification, and remaining decisions. Retain failed and rejected experiments in local evidence; do not present only successful comparisons.
