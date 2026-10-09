# Sanitized results and decisions

Status: first round active. Preflight and permanent calibration are complete; question baseline, findings, and product changes are pending.

## Run index

| Run ID | Revision | Alias | Question IDs | Mode / cache state | Outcome | Local evidence key |
| --- | --- | --- | --- | --- | --- | --- |
| A-001 | `ece3fecc6fc76f279f7cc42ec7c00ac35820af74` | R01–R04 | — | Static target preflight; one new calibration process with resident reuse | Four source targets exist. R02 calibration invoked all 12 tools. R03 has missing local references, so complete semantic coverage is unproven. Runtime source loading of external targets is pending. | `round1/preflight-orchestrator.json`, `round1/preflight-legacy/summary.md`, `round1/preflight-modern/sanitized-summary.json`, `round1/calibration.json`, `round1/calibration-observations.json` |
| B-001 | `6be1ff019aa0129ae5c704d4edb0e162aff0946d` | R01–R04 and one managed assembly | Q001–Q027 | Independent bounded source and metadata review; no navigation answers exposed to explorers | Four replay and two sealed holdout source questions per alias; three assembly questions. Question text, expected answers, starting knowledge, evidence ranges, limits, and separate answer-free handoffs are stored locally. Missing R03 references remain an explicit ground-truth limit. | `round1/measurement-plan.json`, `round1/ground-truth-legacy/validation.json`, `round1/ground-truth-modern/sanitized-status.json`, `round1/ground-truth-self/validation.json` |

Preflight verified the ignored registry and raw-evidence boundaries, clean target working trees, target revisions, installed SDK/runtime, and relevant design-time build configuration. R01 has 15 projects targeting modern .NET variants; R03 has three classic .NET Framework projects; R04 has three modern .NET projects. These are project metadata observations, not proof of successful indexing. R03 has 56 unavailable declared reference files. The Navigator's own R02 load reported seven projects and 305 C# documents with no known framework coverage gap. The calibration's assembly result and several bounded tool results reported partial coverage or explicit continuations; process success is not counted as a complete answer. The runner exercised production schemas, validation, binder, handlers, and formatting without transport. No external build, restore, clean, source edit, or transport check ran.

Baseline and replay measurements use new `tools/AiNetCodeNavigator.Exploration` processes and record code revision, build and binary identity, and response snapshots. The already running MCP server is not a measurement source. Answers from different code builds or snapshots are not combined. Holdout answers are withheld from baseline explorers and implementers.

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
