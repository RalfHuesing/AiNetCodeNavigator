# Orchestrator prompt: implement agentic navigation consolidation

## Assignment

You orchestrate implementation of this task when the user starts execution. Read repository `AGENTS.md` and required rules, [concept.md](concept.md), [roadmap.md](roadmap.md), and the active milestone. The product goal is a substantially more useful MCP server, not maximizing tests, documents, agent count, or abstractions. Preparation alone does not authorize starting production work in the preparation turn.

Implement all required concept behavior. Git-based navigation remains excluded. Refactoring this server's affected implementation is authorized within the concept's scope: reuse existing owners, simplify code, remove superseded paths, and preserve boundary/lifetime/error semantics. Do not turn internal refactoring into a product feature or an unrelated architectural rewrite.

## Roles and model routing

| Role | Model and reasoning | Responsibility |
|---|---|---|
| Implementation and substantive fixes | `gpt-6.1-sol`, `medium` | Implement one bounded point including affected consumers, tests, documentation, and commit. |
| Independent review | `gpt-6.1-sol`, `medium` | Read code, concept, relevant tests/results, and actual output; identify concrete acceptance/correctness/simplicity findings. |
| Small, low-risk supporting work | `gpt-6-luna`, `high` | Inventory callers/tests, check links or names, or make mechanical documentation changes with explicit source facts and acceptance. |

These identifiers implement the user's Sol 6.1/medium and inexpensive Luna preference. Explicit user changes override them. For model overrides, use a limited-history or fresh-context subagent as required by the available delegation API; give it the task paths and a self-contained assignment. If the requested model is unavailable, report that concrete constraint before substituting a different implementation/review model.

Luna is appropriate when the result is narrow, easy to verify, and has little semantic ambiguity. Do not assign metadata resolution, source identities/freshness, traversal semantics, concurrency/lifetimes, or final correctness review to Luna. Do not break a cohesive Sol point into many tiny Luna jobs merely to reduce nominal model cost. The orchestrator checks Luna results; a production edit still receives the milestone's Sol review and required checks.

The orchestrator owns sequencing, assignments, status, and acceptance. Delegate production changes. It may maintain these task documents and inspect code/results. Run one writer at a time in the shared checkout. Read-only supporting agents may overlap only when their input is stable and they do not interfere with verification; serialize scripts that overwrite shared logs or compete for measured runtime capacity. Do not create separate user-owned chats for subtasks.

## Execution and resumption

1. Record `HEAD` and working-tree status; preserve unrelated changes and coordinate any active shared-file owner, including the Exploration runner. Inspect current sources because the concept's original baseline is historical.
2. Select the first open executable child point. A point includes analysis, implementation, affected checks, current-state documentation, and an explicit-path commit. Split an oversized point in the same milestone first; do not invent new product scope.
3. Dispatch an implementation agent with the point, concept sections, ownership boundaries, relevant consumers, required observable behavior, verification purpose, and stopping rule. The agent determines actual test filters from current sources, rather than guessing a class name. It must report what changed, commands and outcomes, commit, and remaining findings. Mark only verified implementation checkboxes; leave independent review and parent acceptance open.
4. Inspect the returned change and evidence. Correct incomplete scope or an unsupported success claim before advancing. Do not rerun passing tests just to reproduce the agent's console output when its identifiable result is available.
5. After the milestone's implementation points, dispatch a separate Sol reviewer. The reviewer does not write production code. Check concept fidelity, retained defect-preventing behavior, public schemas and consumers, ownership/recovery, actual usability, and avoidable complexity. Use saved Exploration outputs where useful. No automatic whole-suite rerun is part of review.
6. Resolve actionable findings through a bounded fix assignment, normally to Sol. Reviewer follow-up checks the changed paths and original findings. Plan at most two focused fix/review rounds for the milestone. If concrete blockers remain, record them as an executable correction point and diagnose/re-scope that work before continuing dependent milestones; the round limit never means accepting broken behavior. Do not evade it by resetting the same finding's round counter. A new bounded plan needs evidence explaining why the earlier approach failed. Ask the user only for a genuinely missing decision or scope conflict, not routine implementation choices.
7. Close review and parent acceptance when required fixes/checks pass. Record concise evidence in the milestone, commit task-state updates, and continue. At interruption/context loss, resume from the first open point and its recorded evidence; completed work is not restarted.

Use progress updates to explain findings, decisions, and remaining work. Do not ask for repeated permission to perform work already authorized. Do not push, amend, rewrite history, or commit unrelated files.

## Tests have a purpose and an end

Preserve the original intent of existing tests for real functional bugs. A removed tool name requires migrating its regression to the replacement path, not deleting the assertion. Remove obsolete registration/implementation-specific expectations only when their contractual purpose has been replaced and coverage is accounted for.

For a new contract or concrete bug, add the smallest useful deterministic check at the appropriate existing level. A few normal and boundary examples can prove paging, helper depth/cycles, or capacity recovery. No parameter Cartesian product, huge synthetic stress fixture, repeated benchmark campaign, or new framework is required. State which behavior a new test protects; if that cannot be stated, do not add it.

Use official scripts: build plus narrow affected FastTests/IntegrationTests for each production slice, selected ExtendedIntegration only when repository rules require affected coverage, and the complete eligible routine solution selection once at final completion. Use current script filters. Do not disable tests, weaken assertions, or change categories to get a green result. Documentation-only work requires review and `git diff --check`, not a build.

After checks pass, stop testing that slice. Repeat or broaden only for a relevant edit, an observed failure, a concrete unresolved concern, or the final required gate. If a follow-up changes production code after the final gate, repeat the affected checks and any final gate invalidated by that change; do not replay all Exploration or timing series for unrelated files.

For suspected stalls, follow the repository verification rule: monitor at most one-minute intervals, preserve evidence at five minutes without concrete progress, stop only owned processes, and insert/assign a focused diagnosis/correction point before dependent work. Never hide a stall by longer timeouts or speculative repeated broad runs.

Measure actual runtime findings using concept section 8.2. Percentage objectives do not justify complexity or repeated test loops. If measurements are inconclusive, state that and use functional/root-cause evidence without claiming a speedup. No genuine blocker or material regression may be marked complete.

## Review finding format and acceptance

An actionable finding identifies a file/location or saved response, the violated requirement or observed consequence, and a finite way to verify correction. Separate required fixes from nonblocking suggestions. A preference, hypothetical unsupported case, or demand for more abstraction/testing is not a blocker by itself. Simpler code is preferred when it meets the same present contracts.

Keep the finding and its resolution in the milestone's completion evidence. No mandatory standalone audit file or review of every unchanged file is needed. Fix all correctness and missing-required-behavior findings; do not silently promote subjective suggestions into scope.

## Final acceptance

M7 must inspect the real production input/output flow through the existing Exploration runner, including required polling/pages and exact owner/reference follow-ups. Exit code zero alone does not prove useful or complete content. Keep its transport-free boundary explicit and run the separate real MCP transport smoke. Record identifiable build, commands/results, scenarios/artifacts, review disposition, and unrun checks in M7.

Finish with the implemented outcome, practical gains, verification limits, remaining blockers if any, and relevant commits. Completion requires the concept and roadmap acceptance, not simply closing checkboxes.
