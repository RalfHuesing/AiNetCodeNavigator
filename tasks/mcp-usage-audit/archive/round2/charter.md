# Agent-led navigation improvement loop

## Objective and authority

Find practical ways for agents to answer C# navigation questions more accurately, clearly, and efficiently with the existing read-only Navigator. Use real questions on the locally registered R01–R04 targets and a fixed managed-assembly snapshot where useful. A scout's difficulty is a lead, not proof of a product defect. The orchestrator may implement a small, well-supported improvement without a separate approval request and must report what changed.

The first bounded round is complete with partial acceptance; its [results](archive/round1/results.md), charter, protocol, roadmap, and prompts remain under [archive/round1](archive/round1/roadmap.md). This is a new, bounded exploration cycle, not a retroactive repair of the first round's late holdout freeze.

## Non-negotiable boundaries

- Navigation stays read-only for C# source and managed assemblies. No linting, code-quality scoring, automatic refactoring, or execution of inspected assemblies. Preserve Core/transport separation and JSON-RPC-only MCP stdout.
- Preserve the separate offline assembly export CLI, including selection, dependency, dry-run, marked-output, manifest, and completeness behavior.
- External repositories are evaluation inputs. Do not edit, build, restore, clean, or commit them. Inspect relevant build configuration before source loading. Preserve pre-existing external changes.
- Never put external product names, identifiable paths, namespaces, symbols, source excerpts, URLs, or binary identities in tracked files, commits, or shared summaries. Use R01–R04/A01 aliases and independently authored neutral fixtures. Resolve sensitive values only from ignored local files.
- All navigation experiments must run through `scripts/explore.ps1` and `tools/AiNetCodeNavigator.Exploration`. Do not make direct MCP JSON calls or create a separate call simulator, client, JSON-RPC/stdio harness, or traffic capture. A running MCP server is not a measurement source; rebuild the Explorer for changed code and record its identity.
- Keep the existing engine, host, exporter, and language architecture. No new runtime dependency, framework upgrade, broad rewrite, audit platform, or permanent telemetry as an incidental fix. No push or publication.

## Bounded cycle

- Dispatch up to two independent exploratory scouts on different target snapshots, plus an independent ground-truth/review agent as needed. Keep one writer for Navigator product files and serialize official scripts with shared log paths.
- Scouts choose realistic, answer-free questions and adapt calls after reading actual Explorer output. They do not receive prior answer traces. A reviewer independently checks claims, omissions, fallback use, and cost.
- Triage at most three attempted product candidates in this cycle. Implement at most two coherent slices. Zero changes is valid. A new public tool requires recurring evidence that existing tools cannot reasonably meet the need; otherwise prefer a smaller correction, output change, or simpler workflow.
- A candidate qualifies for direct implementation when it has a meaningful agent-facing failure or repeated avoidable work, an isolated owner, a neutral failing reproduction where feasible, a bounded acceptance check, and a credible cross-target replay. Preserve compatibility after inventorying schemas, consumers, examples, and documentation. Defer hypotheses without this evidence.
- Compare fixed questions, target snapshots, budgets, and measurement methods before and after. Use fresh Explorer processes for each product revision and fresh transfer questions where practical. Count all attempts, recovery, fallbacks, and visible output; do not infer speed or billed tokens from these counts.
- Stop this cycle after its selection and replay, at the candidate cap, or at an unresolved required blocker. Record accepted/rejected/deferred findings and coverage limits. Another cycle requires a new user instruction; never call the result a global optimum.

## Verification

Use the task-specific narrow verification policy in [protocol.md](protocol.md): official build and risk-selected filtered checks for code slices, no direct MCP-call simulation or automatic broad suite. Run the final export smoke on the final built candidate and inspect its manifest. Documentation-only edits need diff review and `git diff --check`. Preserve the repository's test-stall, privacy, automatic commit, and no-push rules.
