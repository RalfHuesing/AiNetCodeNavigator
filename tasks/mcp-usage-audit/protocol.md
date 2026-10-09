# Exploration and improvement protocol

## Prepare

Read the current repository rules, [charter](charter.md), [roadmap](roadmap.md), Explorer guide, and affected tool contracts. Resolve R01–R04 paths through the ignored `temp/external-repos/audit-targets.local.json`; never copy its values to tracked text. Check `git status`, target revisions and dirty state, relevant build configuration, reference availability, tool catalog, Explorer build identity, runtime, and ignored evidence paths. Keep original targets unchanged. Missing references or incomplete loading are coverage limits.

## Explore as an agent

Use only `pwsh -File ./scripts/explore.ps1 -Scenario <name>` with temporary generic scenarios in `tools/AiNetCodeNavigator.Exploration/Scenarios/`. The scenario reads local target/query data from ignored files at runtime; no external identifiers in source. Inspect each saved `response.txt`, raw attempts, and payload before choosing the next call. Follow reported polling, outer pages, domain cursors, and body windows. Preserve returned references and owners. Remove temporary scenarios after use; retain a copy and replay recipe in ignored evidence.

Scouts receive a target alias, permitted starting knowledge, a bounded task family, and an ignored evidence directory, but no answer or prior solution trace. They may formulate realistic questions from local discovery. Prefer questions exposing owner handoff, ambiguous symbols, partial scope, relationships, recovery, or large output. Keep each question to 20 logical calls or ten minutes unless a finite extension is recorded. Source reads or text search may establish independent ground truth or a disclosed fallback; they are not counted as Explorer success. No live MCP client, direct JSON/handler/binder invocation, or alternative simulator is allowed.

Record for each question: Navigator commit and binary hash, target snapshot and load limits, Explorer process/cache state, exact question and starting knowledge, tool choices, logical calls, every physical attempt, errors, fallback reads, raw visible UTF-8 text bytes, supported answer, omissions, and uncertainty. These bytes are neither wire bytes nor billed model context. Timing claims require isolated comparable measurements; otherwise leave latency unavailable.

## Decide and improve

An independent reviewer checks bounded ground truth and the scout's answer against actual output, including false completeness and unnecessary recovery. Classify each observation as environment/loading, engine, tool selection/schema, output, recovery, or agent misuse. Prioritize incorrect or misleading answers, failed handoffs, and lost coverage. A single serious deterministic defect can qualify with a neutral reproduction; cost-only ideas need recurrence or a clear consumer-safe benefit.

Before implementation, freeze the selected question, input snapshot, budget, expected benefit, affected consumers, and a fresh transfer task. Add a small roadmap item with owner and acceptance. Reproduce a deterministic defect with a failing neutral test when feasible. Change the smallest owning component; update contracts, tests, current-state docs, and usage rules together. Do not read sealed transfer answers. After review, allow at most two focused correction attempts per candidate; then reject or report the blocker rather than silently widening scope.

Replay using the freshly built Explorer against the fixed input. Preserve both versions' raw evidence. Re-evaluate correctness before accepting lower call/byte counts. A fresh agent task tests transfer, but cannot retroactively become a preimplementation holdout. Do not mix snapshots, changed binaries, or source windows from different revisions.

## Tests, privacy, and completion

For documentation-only changes, inspect the diff and run `git diff --check`. For each code slice, use `pwsh -File ./scripts/build.ps1` and the smallest necessary named selection of `scripts/test-fast.ps1 -Filter ...` and/or `scripts/test-integration.ps1 -Filter ...`. Record the risk, filter, expected duration, and actual result. Do not run unfiltered full, extended, performance, transport, JSON-RPC, or client suites as an automatic gate. This task-specific policy continues the user's narrow-check instruction and overrides the blanket completion-suite rule for this campaign only. A needed extended case must be narrow and justified.

Monitor verification progress at least once per minute. Apply the repository's five-minute stall procedure, preserving logs and stopping only owned processes. Allow at most two focused diagnosis attempts before reporting an unresolved required blocker. Reuse still-valid checks after unchanged code. After the last product change and matching official build, run `scripts/export-assembly-smoke.ps1 -SkipBuild` and inspect `last-run.log`, the catalog, and per-assembly manifests for actual completeness.

Before every local Conventional Commit, inspect exact staged paths and diff, scan for external identifiers without printing them, and review semantic privacy. Stage only owned paths. Never push. The orchestrator updates [results.md](results.md) with sanitized findings, before/after evidence, rejected/deferred ideas, and remaining limits; ignored `temp/external-repos/mcp-usage-audit/round2/` holds raw material.
