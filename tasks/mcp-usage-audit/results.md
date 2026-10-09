# Sanitized results and decisions

Status: first round active. Baseline usage and independent review are complete; improvement selection and product changes are pending.

## Run index

| Run ID | Revision | Alias | Question IDs | Mode / cache state | Outcome | Local evidence key |
| --- | --- | --- | --- | --- | --- | --- |
| A-001 | `ece3fecc6fc76f279f7cc42ec7c00ac35820af74` | R01–R04 | — | Static target preflight; one new calibration process with resident reuse | Four source targets exist. R02 calibration invoked all 12 tools. R03 has missing local references, so complete semantic coverage is unproven. Runtime source loading of external targets is pending. | `round1/preflight-orchestrator.json`, `round1/preflight-legacy/summary.md`, `round1/preflight-modern/sanitized-summary.json`, `round1/calibration.json`, `round1/calibration-observations.json` |
| B-001 | `6be1ff019aa0129ae5c704d4edb0e162aff0946d` | R01–R04 and one managed assembly | Q001–Q027 | Independent bounded source and metadata review; no navigation answers exposed to explorers | Four replay and two sealed holdout source questions per alias; three assembly questions. Question text, expected answers, starting knowledge, evidence ranges, limits, and separate answer-free handoffs are stored locally. Missing R03 references remain an explicit ground-truth limit. | `round1/measurement-plan.json`, `round1/ground-truth-legacy/validation.json`, `round1/ground-truth-modern/sanitized-status.json`, `round1/ground-truth-self/validation.json` |
| C-001 | `67ee07a1978bbb83cb34a3007cb46384869d51a8` | R01 | Q001–Q004 | Two new runner processes; warm reuse after restart | Four answers independently verified. 20 production calls, 46 physical attempts, 80,914 visible UTF-8 bytes, no source fallback. One separate agent script failure occurred before the binder. | `round1/baseline-large/run.json`, `round1/review-large-baseline/reviewed-metrics.json` |
| C-002 | `67ee07a1978bbb83cb34a3007cb46384869d51a8` | R02 | Q007–Q010 | New runner process with warm reuse | Four answers independently verified. 20 logical calls, 23 physical attempts, 48,675 visible UTF-8 bytes, no source fallback. | `round1/baseline-self/run.json`, `round1/review-self-baseline/` |
| C-003 | `67ee07a1978bbb83cb34a3007cb46384869d51a8` | R04 | Q019–Q022 | New runner process with warm reuse | Four answers independently verified. 18 calls/attempts including a separate scope check, 55,225 visible UTF-8 bytes, no source fallback. Test-candidate presentation has a usability limitation; a scope snapshot differs by design boundary and is not merged with semantic results. | `round1/baseline-modern/run.json`, `round1/review-modern-baseline/sanitized-summary.json` |
| C-004 | `67ee07a1978bbb83cb34a3007cb46384869d51a8` | A01 | Q025–Q027 | Fixed ignored managed-assembly snapshot; new runner process | Repeated after a build changed the original target binary. Fixed snapshot is independently accepted for paired replay: 10 calls/attempts, 20,411 visible UTF-8 bytes, one failed direct member handoff, no source fallback. Q025 verified; Q026–Q027 partly verified through type-level recovery. | `round1/assembly-A01-snapshot/snapshot.json`, `round1/baseline-assembly-fixed/run.json`, `round1/review-assembly-fixed/` |
| C-005 | `67ee07a1978bbb83cb34a3007cb46384869d51a8` | R03 | Q013–Q016 | Original target; new runner process with warm reuse | Q013–Q015 verified; Q016 blocked by missing declared references, with two disclosed source fallbacks. 18 calls, 58 attempts, 51,117 visible UTF-8 bytes. Q015 alone used 44 attempts under a 512-byte budget. | `round1/baseline-legacy/run.json`, `round1/review-legacy-baseline/reviewed-metrics.json` |
| C-006 | `302a368` | R03 | Q013–Q016 | Isolated ignored copy with supplied dependencies and import fences; two new runner processes | All four answers independently verified. 17 calls, 32 attempts, 52,372 visible UTF-8 bytes, two recoverable errors, no source fallback. All 129 declared HintPaths exist in the copy. Loaded framework contexts remain unknown; assembly relationships remain partial. The original repository is unchanged. | `round1/R03-complete/overlay-validation.local.json`, `round1/review-R03-overlay-input/review.md`, `round1/baseline-legacy-isolated/run.local.json`, `round1/review-legacy-isolated/review.md` |

Preflight verified the ignored registry and raw-evidence boundaries, clean target working trees, target revisions, installed SDK/runtime, and relevant design-time build configuration. R01 has 15 projects targeting modern .NET variants; R03 has three classic .NET Framework projects; R04 has three modern .NET projects. These are project metadata observations, not proof of successful indexing. R03 has 56 unavailable declared reference files. The Navigator's own R02 load reported seven projects and 305 C# documents with no known framework coverage gap. The calibration's assembly result and several bounded tool results reported partial coverage or explicit continuations; process success is not counted as a complete answer. The runner exercised production schemas, validation, binder, handlers, and formatting without transport. No external build, restore, clean, source edit, or transport check ran.

Baseline and replay measurements use new `tools/AiNetCodeNavigator.Exploration` processes and record code revision, build and binary identity, and response snapshots. The already running MCP server is not a measurement source. Answers from different code builds or snapshots are not combined. Holdout answers are withheld from baseline explorers and implementers.

The original A01 run used a mutable build output and has a different binary hash from question freeze. It is retained as diagnostic evidence, not used as the paired baseline. The accepted A01 row uses a fixed ignored binary snapshot and discloses the target-path change. All byte counts above sum raw visible text blocks across physical attempts; they are not billed context or wire-byte measurements. Timing is unavailable where it was not isolated. No transport, full-suite, or product verification test has run in the baseline phase.

The first R03 run exposed a missing-reference coverage limit. The user supplied a local dependency source. An ignored copy now has 69 HintPath-only project changes, 129 existing referenced files, and neutral local build/package property boundaries so Navigator's repository settings do not affect the copied projects. The copy is a distinct evaluation snapshot; its results are not merged with references or body windows from the original target. One aborted copy run inherited Navigator settings and is retained only as invalid diagnostic evidence.

Use relative keys below `temp/external-repos/mcp-usage-audit/`, not external paths or symbols.

## Findings and decisions

| Finding ID | Category / priority | Affected aliases | Sanitized observation | Decision and reason | Acceptance / verification | State |
| --- | --- | --- | --- | --- | --- | --- |
| F-R03-02 | Recovery / P2 | R03 | A negative source metadata-origin result does not expose unavailable declared references, so an agent may mistake loaded-reference absence for exhaustive absence. | Evaluate at D with a neutral missing-reference fixture. The answer was not proven false for its loaded scope. | Explicit incompleteness and recovery action if selected. | Confirmed, triage pending |
| CSELF-02 | Navigation handoff / P2 | A01 | Public methods in a partially decompiled assembly lack direct stable references; a returned documentation ID failed body lookup, while a type reference recovered text. | Evaluate at D with a neutral managed fixture and owner/reference contract review. | Direct owner-preserving member handoff if selected. | Confirmed, triage pending |
| CSELF-01 | Output / P3 | R02 | A successful, unambiguous origin result emitted a large searched-assembly inventory and required two outer pages at the frozen budget. | Evaluate cost and consumer compatibility at D. | Preserve ownership/completeness while reducing avoidable text if selected. | Confirmed, triage pending |
| C-R04-001 | Usability / P3 | R04 | Test-candidate pages count classes; one returned class contained methods without method-specific evidence. | Defer unless stronger correctness evidence appears; no false answer was observed. | Report page semantics clearly. | Deferred |
| F-R03-03 | Recovery cost / P3 | R03 | A deliberately minimal 512-byte budget required 44 physical attempts; the prescribed paging path completed. | Defer; the extreme budget explains the cost and recovery worked. | Retain as a cost observation. | Deferred |
| F-R03I-01 | Loading / P2 | R03 | Configured classic frameworks are known, but loaded framework contexts remain unknown even with available reference files. | Defer as an explicit coverage limit; current evidence does not isolate a small navigation fix. | Never claim complete semantic framework coverage. | Deferred |
| F-R03I-04 | Loading / P2 | R03, A01 | Assembly relationship analysis remains partial despite usable API/body results. | Defer broad closure work outside this bounded round. | Keep omissions visible in answers and final report. | Deferred |

## Tool contract decisions

| Decision ID | Tool / behavior | Observed problem | Retain / change / merge / remove / add | Consumer impact | Evidence / commit |
| --- | --- | --- | --- | --- | --- |

## Before and after

| Question / alias | Baseline / candidate revisions | Correctness | Calls / attempts | Visible bytes | Answer latency | Usability observations | Limitations |
| --- | --- | --- | --- | --- | --- | --- | --- |

## Final acceptance

Not evaluated. Record actual checks and dates, export state, holdout results, unresolved blockers, privacy review, external read-only verification, and remaining decisions. Retain failed and rejected experiments in local evidence; do not present only successful comparisons.
