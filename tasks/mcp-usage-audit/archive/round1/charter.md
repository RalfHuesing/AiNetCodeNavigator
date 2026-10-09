# Charter

## Objective and authorized scope

Improve the usefulness, correctness, efficiency, and agent usability of read-only C# navigation through real usage on the supplied local repositories, including a large solution. Audit the use of the MCP server, not the quality of external product code.

Once execution is started, the agent may redesign, consolidate, remove, or extend navigation tools and their outputs within the bounded first round below. Tool count is not a success metric. Changes require observed evidence, a neutral reproduction where feasible, and cross-target validation. A useful tool may remain unchanged. Do not invent a backlog merely to demonstrate activity.

Use transport-free exploration only. JSON-RPC/stdio/client testing and traffic capture are excluded. Apply the user's task-specific minimal test policy in [the protocol](protocol.md#task-specific-test-scope): necessary filtered behavior checks and export protection, with no automatic full-suite completion gate. This overrides the general blanket completion-test requirement for this campaign, not the product invariants.

External repositories are read-only evaluation inputs, not binding product specifications. Inspect only the provided targets and relevant dependencies. Do not edit, commit, clean, restore packages into, or build external repositories as an implicit audit step. Source loading performs MSBuild design-time evaluation and is not a sandbox: inspect relevant build configuration before loading; if it requires unapproved writes or execution, record a blocker and seek a specific decision. Record pre-existing external working-tree state and inspect it again after use without reverting user work.

## Invariants

- MCP remains read-only C# source and managed assembly navigation. No linting, diagnostics product, code-quality scoring, automatic refactoring, or runtime execution of inspected assemblies.
- The separate static assembly decompilation/export CLI remains functional, including named selection arguments, dependency behavior, dry-run, marked-output ownership, completeness reporting, and navigation artifacts. Its explicitly selected disposable output is the only export write target.
- MCP stdout remains JSON-RPC only. Preserve architectural boundaries between Core and transport.
- No external repository product names, identifiable paths, namespaces, symbols, source snippets, URLs, or binary identities in tracked code, task reports, commits, issue/PR text, or other GitHub content. Use aliases and independently authored neutral fixtures. This applies to temporary scenario source too: resolve sensitive query values from ignored files at runtime.
- Do not push or publish. Commit verified local slices according to repository rules.

Tool contract changes are allowed; the two product invariants above are not negotiable. Before changing a tool, inventory in-repository consumers, schemas, documentation, usage rules, and client-facing examples. Record the compatibility decision and update them coherently. Ask only if a material consumer constraint or architectural decision cannot be resolved from the authorized scope and current sources.

## Bounded first round

- Execute one baseline, one selected improvement round, and one final replay/holdout assessment. Select at most three improvement candidates at D. Count attempted candidates, including rejected ones; do not refill the queue or disguise unrelated changes as one candidate. Necessary tests, documentation, and corrections belong to their candidate. Zero product changes is a valid outcome if evidence warrants none.
- Prefer correcting existing behavior, improving descriptions/defaults/output, or simplifying existing tools. Add at most one new public tool in this round, and only when existing tools cannot reasonably answer a recurring navigation need. Explain why extending or composing current tools is insufficient. A neutral reproduction can justify a single-case correctness fix; speculative generality cannot justify a new subsystem.
- Keep the existing C#/.NET, Roslyn, decompiler, MCP-host, and offline-export architecture. No replacement engine, new language support, database/vector store, embeddings/RAG, hosted service, network API, UI, background daemon/watcher, plugin framework, or runtime LLM integration. No solution-wide rewrite or unrelated cleanup. Small necessary refactoring within the owning components remains allowed.
- Do not add a new runtime dependency or upgrade frameworks/packages as an incidental improvement. If indispensable, describe the concrete need and tradeoff as a separate proposal for the user; do not implement it in this round. Use existing libraries and infrastructure first.
- Keep audit machinery temporary and small: existing runner, local JSON/Markdown evidence, and short helpers. No benchmark platform, generic orchestration framework, exhaustive repo map, or permanent telemetry system. Do not expand the exporter with new features; preserve it and correct only regressions caused by this work.
- Freeze the selected scope at D. Newly discovered independent ideas and pre-existing defects go to the decision ledger for a possible later round. Regressions introduced by this round must be corrected or the agent's own candidate changes selectively undone, preserving unrelated work. Reverting a rejected candidate means a normal explicit corrective change/commit, never a hard reset or rewritten history.
- Allow at most two focused correction attempts per candidate after its first review/replay. For a stalled required check, allow at most two focused diagnosis attempts with explicit time bounds under the existing stall rule. If still unresolved, stop dependent work and report the blocker or reject the candidate safely. A model escalation does not reset these limits. Do not expand into a test-infrastructure repair project.
- At the round boundary report accepted/rejected candidates, deferred findings, remaining limitations, and verification. Further rounds or the excluded architecture work require a new user instruction. An unresolved serious pre-existing defect must be highlighted and prevents an unqualified readiness claim; it does not authorize unlimited implementation. A bounded round may end with a clearly reported partial result.

## Evaluation targets

| Alias | Intended role (verify during preflight) |
| --- | --- |
| R01 | Large external source solution; user-reported approximately 180k LOC |
| R02 | Navigator's own source solution; neutral reproducible calibration |
| R03 | External legacy .NET Framework solution; user-reported net48 |
| R04 | External modern .NET solution; user-reported net10 |

Do not count inaccessible or partially loaded targets as successful coverage. Framework hints are not verified project metadata. Select a managed assembly through local discovery or a neutral fixture; the inventory does not yet provide a verified external assembly target.

## Completion

- Baseline evidence covers all four targets or explicitly identifies unresolved blockers; a blocked target keeps full campaign acceptance open.
- Every current tool has at least one inspected baseline invocation with its supported target kind; representative multi-step questions cover source and assembly navigation, scale, recovery, and partial results.
- Selected correctness and safety findings are resolved and independently checked. All remaining findings, including serious pre-existing issues outside the selected scope, are explicitly reported with impact and disposition. Newly introduced regressions cannot be deferred as successful acceptance.
- Accepted changes preserve or improve independently verified answers; paired measurements show the intended benefit without concealing regressions. No fabricated percentage target or speed claim.
- Replay and fresh holdout tasks confirm usefulness across targets. Failures and unsupported questions remain visible.
- Necessary risk-selected behavior checks and the final export safeguard pass; documentation and navigation instructions match the final tools; temporary scenarios are removed. Excluded transport checks and full suites are not completion blockers and must not be reported as passed.
- A sanitized final report gives evidence, limitations, tool decisions, remaining work, and measured before/after results. Do not declare completion while a required gate is blocked.
