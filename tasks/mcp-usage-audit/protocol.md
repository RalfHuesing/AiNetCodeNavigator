# Execution and evaluation protocol

## 1. Preflight and freeze

Read applicable rules and current code before making implementation claims. Record Navigator commit, dirty state, runtime/SDK, tool schema/catalog identity, agent model and reasoning, runner version, target revision and dirty state, framework/load limitations, host settings, budgets, and cache/process state in local run metadata. Do not hash or inventory entire external repositories unnecessarily; record relevant input identity and material drift.

Verify that registry and raw-output directories are ignored and untracked (`git check-ignore` and `git ls-files`). Resolve aliases without echoing paths into tracked reports. Inspect target existence and build prerequisites. Preserve external changes. Missing workloads, references, or indexed projects are evidence of incomplete coverage, not an empty result.

Run the permanent `ExploreAllTools` calibration through `scripts/explore.ps1`, and inspect its actual outputs. Re-check current discovery and schemas rather than assuming the initial 12-tool catalog stays fixed. The runner currently has no target-path CLI parameter: temporary scenarios must read the local registry and local query data, then pass the resolved `targetPath` to `CallAsync`. `context.RepositorySolution` selects only the Navigator solution. Do not invent unsupported script flags.

## 2. Question design before implementation

Create at least six applicable questions per source target: four baseline/replay questions and two untouched holdouts. Across the campaign cover the families below, plus at least three managed-assembly questions. Assign neutral IDs Q001 onward. Choose real examples during local discovery; keep actual symbols, paths, query text, and expected answers local. Record exclusions with reasons instead of forcing nonsensical questions.

| Family | Expected evidence |
| --- | --- |
| Unknown entry point | Narrow scope, locate a declaration, obtain body and ownership |
| Contract to implementation | Interface/base member, concrete implementation/override, ambiguity resolved |
| Call chain | Incoming and outgoing sites, bounded traversal, explicit static/runtime distinction |
| Hypothetical change impact | References/dependencies and relevant static test candidates; no external edit |
| Cross-project boundary | Project/type origin and source-to-metadata handoff |
| Output recovery | Polling, outer pages, domain pages, body windows, limits, invalid or stale references |
| Assembly navigation | API discovery, decompiled implementation, search/origin and owner-preserving follow-up |

Use neutral fixtures for deterministic edge cases not naturally present. Never present fixture evidence as large-repository evidence. Include overloads/ambiguous names and a negative or unsupported question. A justified "cannot determine" is better than a false exhaustive answer.

A reviewer establishes bounded ground truth from relevant source ranges and wiring, independently of the answer being evaluated. Static edges do not establish runtime dispatch. Store supporting locations and uncertainty locally. The explorer receives only the question and permitted starting knowledge, not the answer. Seal holdout definitions locally before implementation; implementers do not read their answers. If exposed, replace the affected holdout and record why.

## 3. Explore and inspect

Use the existing exploration runner for real handler output. Keep scenario code generic; load all external identifiers from ignored local JSON. Store a replay recipe and a copy of temporary generic scenario source in the ignored run directory before deleting the scenario from `Scenarios/`. Keep only the permanent baseline scenario after the experiment, as required by the exploration guide. Do not add manual exploration to CI or routine test selections.

Work adaptively: inspect a response, choose the next call, and record why. A pre-scripted happy-path replay alone does not evaluate tool discoverability or agent decision-making. A fresh explorer should attempt a question using tool descriptions and normal navigation rules before receiving task-specific hints. Record schema/tool-selection confusion separately from engine failures.

Inspect every saved `response.txt`, relevant raw attempt, and payload. Track exact references, owner targets, snapshot identity, omissions, and section status. Respect reported polling delays; complete outer pages before domain cursors/body windows. The runner follows polls and outer pages automatically, but domain pages and semantic recovery require explicit follow-ups. Count the automatic attempts too. Successful process exit is not proof of a correct, complete, or useful answer.

Direct source reads or text search may establish ground truth or supply a justified fallback. Record fallback use and its cost; do not count a fallback-derived answer as MCP-only success. Stop a question after 20 logical calls or 10 minutes, whichever comes first, preserve evidence, and classify the obstacle. This is an audit bound, not a product limit. Any extension requires a recorded reason and finite new bound. Repository stall rules take precedence.

The campaign uses transport-free exploration exclusively. It exercises production schemas, argument validation, binder, handlers, and formatting. JSON-RPC/stdio transport tests, live MCP-client sessions, handshakes, traffic capture, and host request-filter wrapper verification are outside this campaign. Do not build or run a transport harness. Record transport-only observations as out of scope; they do not block campaign acceptance. Do not infer transport behavior from runner success. The product's JSON-RPC stdout invariant remains unchanged.

## 4. Measurements and judgments

Use `templates/run.json` and `templates/question.md`. Missing measurements are `null` with a reason, never zero. Record per-question results and distributions; small samples are descriptive, not statistical proof.

- Correctness: verified / partly verified / incorrect / unsupported / blocked, with independent evidence and false-completeness claims called out.
- Cost: logical navigation calls, all physical attempts (initial/poll/outer/domain), retries, failed calls, fallback reads, time to a supported answer, and output volume across all attempts.
- Output volume: sum UTF-8 bytes of text blocks from raw attempt responses, not `response.txt` separators or only the last `response.json`. Runner serialization is not wire bytes. Bytes are sufficient by default; count tokens offline only if an available tokenizer adds decision-relevant evidence, and record its identity. These are not billed model-context measurements. Include schema size separately when evaluating catalog changes.
- Timing: distinguish whole scenario process/build time, target loading, and tool/answer latency. Existing runner artifacts do not establish precise per-call timings; instrument locally or leave unavailable. Separate first-load from warm runs and record process reuse. Never label a new process as a fully cold OS cache.
- Agent usability, each scored 0/1/2: tool selection, argument clarity, result readability, owner/reference handoff, completeness visibility, and actionable recovery. Anchors: 0 = misleading/unusable, 1 = succeeds with avoidable interpretation or hints, 2 = clear next step without special knowledge. Attach one concrete observation per score; no aggregate score should hide correctness failures.

Freeze query, starting knowledge, target snapshot, budgets, agent settings, and measurement method for paired replay. If tool contracts change, preserve question intent and disclose request differences. Run each comparison once by default; use three comparable repetitions only when a timing claim is material to accepting a performance change, and report median/range. Compare first-load and warm samples separately. Re-evaluate a correctness or cost regression before accepting savings elsewhere. Fresh holdouts measure transfer; replays measure known-case improvement.

## 5. Findings and implementation

Use `templates/finding.md` locally. Classify cause as environment/loading, navigation engine, schema/tool selection, output, recovery, performance, or agent misuse. Prioritize false answers, unsafe boundaries, and lost coverage before cosmetic cost reduction. A candidate needs evidence, expected benefit, alternatives (including no new tool), acceptance criteria, affected consumers, and verification scope. Seek recurrence on another target; a single severe correctness bug may justify action with a neutral reproduction.

Choose a small coherent implementation slice after baseline review, not after every inconvenient call. Add executable roadmap children before assigning work. Verify a failing regression before fixing a deterministic defect. Update owning code, schemas, consumers, tests, docs, and usage rules together. Do not preserve obsolete tools by default or remove them without checking callers. Keep a sanitized tool decision in `results.md`.

## 6. Verification, privacy, and completion

### Task-specific test scope

The user's instruction for this campaign is to omit JSON-RPC testing and run only necessary tests because broad selections can take excessive time. This explicitly replaces the blanket full routine solution gate at task completion in `.agents/rules/04-verification.mdc` for this task only. The general rule file remains unchanged. Official scripts, meaningful behavior/regression checks, honest reporting, and stall handling still apply.

- Documentation-only changes: review the diff and run `git diff --check`; no build or tests.
- Code slices: run the official build and select the smallest set that checks the changed behavior and affected contracts. Use `scripts/test-fast.ps1 -Filter '<actual test filter>'` and/or `scripts/test-integration.ps1 -Filter '<actual test filter>'` according to the affected layer; do not run both automatically. Determine actual test names before execution. Verify deterministic fixes with a failing regression followed by the corrected test.
- For every selected group, record the specific risk it covers, the exact filter, and expected duration. Start with a single test or class when duration is unknown. Aim for selections of at most five minutes; split broader groups by risk. A necessary longer selection needs a documented reason and finite time budget before running, not a blanket timeout increase or a new user approval step.
- Extended integration: run only a narrowly filtered case when changed shared/source/assembly behavior has a concrete risk not covered by the selected faster tests. Record the gap it closes. Do not run an entire extended, performance, transport, or E2E suite.
- Reuse passed results while the tested code, dependencies, and relevant configuration remain unchanged. Repeat only affected checks after edits, failures, or new evidence. Do not duplicate selected fast/integration tests through `test.ps1`.
- Final acceptance: review the selected checks across all slices and run only remaining coverage gaps or checks invalidated by later edits. No automatic unfiltered `test.ps1`, `test-fast.ps1`, or `test-integration.ps1`, including at campaign completion. Record intentionally unrun suites as scope exclusions, not successful gates.
- Static export: retain one smoke check on the final candidate as the explicit safeguard for the user's export invariant. A matching successful check after the last exporter/shared-dependency change can satisfy it; no mandatory baseline smoke or redundant rerun. Use `pwsh -File ./scripts/export-assembly-smoke.ps1 -SkipBuild` only after a matching successful official build with current binaries and configuration; otherwise build first. Add targeted export regression tests only for affected contracts. Inspect `last-run.log`, catalog, and per-assembly manifests; a zero exit code can still contain partial exports. Use only the smoke script's dedicated disposable dump.

Monitor active verification at least once per minute. Five minutes without concrete progress triggers preservation of logs/TRX, termination only of the owned run, and a dedicated diagnosis/correction roadmap item before any unrelated implementation. Do not increase timeouts, weaken tests, or silently skip the gate. Follow the repository's bounded-slow-test exception only with evidence.

Before each commit, inspect the complete staged content and filenames, generated fixtures, and commit message for external identifiers. Derive sensitive names/paths locally from registry and findings; perform case-insensitive literal checks without printing matching sensitive text, then manually review semantic leaks (renamed snippets can still reveal products). A name scan alone is insufficient. Keep raw output, ground truth, precise examples, and local handoffs ignored. Publish only independently written neutral reproductions and alias-based summaries.

Preserve unrelated changes, stage explicit verified paths only, stop on staged-file conflicts, run `git diff --check` and inspect the staged diff, then make a Conventional Commit without pushing. Documentation-only preparation needs diff review and whitespace checks, not production tests. Update `docs/` only for implemented behavior; plans stay here.
