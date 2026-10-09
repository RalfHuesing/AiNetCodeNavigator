# Charter

## Objective and authorized scope

Improve the usefulness, correctness, efficiency, and agent usability of read-only C# navigation through real usage on the supplied local repositories, including a large solution. Audit the use of the MCP server, not the quality of external product code.

Once execution is started, the agent may redesign, consolidate, remove, or extend navigation tools and their outputs. Tool count is not a success metric. Changes require observed evidence, a neutral reproduction where feasible, and cross-target validation. A useful tool may remain unchanged. Do not invent a backlog merely to demonstrate activity.

Use transport-free exploration only. JSON-RPC/stdio/client testing and traffic capture are excluded. Apply the user's task-specific minimal test policy in [the protocol](protocol.md#task-specific-test-scope): necessary filtered behavior checks and export protection, with no automatic full-suite completion gate. This overrides the general blanket completion-test requirement for this campaign, not the product invariants.

External repositories are read-only evaluation inputs, not binding product specifications. Inspect only the provided targets and relevant dependencies. Do not edit, commit, clean, restore packages into, or build external repositories as an implicit audit step. Source loading performs MSBuild design-time evaluation and is not a sandbox: inspect relevant build configuration before loading; if it requires unapproved writes or execution, record a blocker and seek a specific decision. Record pre-existing external working-tree state and inspect it again after use without reverting user work.

## Invariants

- MCP remains read-only C# source and managed assembly navigation. No linting, diagnostics product, code-quality scoring, automatic refactoring, or runtime execution of inspected assemblies.
- The separate static assembly decompilation/export CLI remains functional, including named selection arguments, dependency behavior, dry-run, marked-output ownership, completeness reporting, and navigation artifacts. Its explicitly selected disposable output is the only export write target.
- MCP stdout remains JSON-RPC only. Preserve architectural boundaries between Core and transport.
- No external repository product names, identifiable paths, namespaces, symbols, source snippets, URLs, or binary identities in tracked code, task reports, commits, issue/PR text, or other GitHub content. Use aliases and independently authored neutral fixtures. This applies to temporary scenario source too: resolve sensitive query values from ignored files at runtime.
- Do not push or publish. Commit verified local slices according to repository rules.

Tool contract changes are allowed; the two product invariants above are not negotiable. Before changing a tool, inventory in-repository consumers, schemas, documentation, usage rules, and client-facing examples. Record the compatibility decision and update them coherently. Ask only if a material consumer constraint or architectural decision cannot be resolved from the authorized scope and current sources.

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
- Highest-priority correctness and safety findings are resolved and independently checked. Lower-priority findings are accepted, rejected with reasons, or explicitly deferred in the decision ledger.
- Accepted changes preserve or improve independently verified answers; paired measurements show the intended benefit without concealing regressions. No fabricated percentage target or speed claim.
- Replay and fresh holdout tasks confirm usefulness across targets. Failures and unsupported questions remain visible.
- Necessary risk-selected behavior checks and the final export safeguard pass; documentation and navigation instructions match the final tools; temporary scenarios are removed. Excluded transport checks and full suites are not completion blockers and must not be reported as passed.
- A sanitized final report gives evidence, limitations, tool decisions, remaining work, and measured before/after results. Do not declare completion while a required gate is blocked.
