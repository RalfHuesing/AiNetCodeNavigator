# Resume the Navigator roadmap

Continue `tasks/AiNetLinter-Uebernehmen/Konzept.md` in repository
`C:\Daten\Entwicklung\Ralf\AiNetCodeNavigator` according to its orchestration rules.
The user explicitly authorizes continuing implementation, independent audits,
required verification, documentation, and automatic commits through the remaining
roadmap. Communicate with the user in German. Continue autonomously; answer status
questions briefly and then resume work. Do not stop for routine confirmation.

## Consume and delete this prompt

Read this entire file and retain its operational instructions in your context.
Read the required repository context below and inspect Git before making changes.
Then **delete this exact file, `tasks/AiNetLinter-Uebernehmen/Resume.md`, when
executing this resume prompt**. The user explicitly requested this deletion; do
not ask for permission again. Delete only this file, preserve unrelated changes,
review the deletion with `git diff --check`, stage only this exact path, and make
the required documentation-only commit before starting another writing agent.
Do not recreate the prompt as permanent documentation.

## Required initial reads and verification

- `AGENTS.md` and all required `.agents/rules/01-*.mdc` through `08-*.mdc`.
- `tasks/AiNetLinter-Uebernehmen/Konzept.md`, `CodeMap-AiNetLinter.md`, and
  `Review-2026-09-30.md`.
- All cluster checklists under `tasks/AiNetLinter-Uebernehmen/Clusters/`, all
  available `Reviews/Cluster-XX.md`, and `Findings.md`.
- `README.md`, `docs/README.md`, and the relevant current-state documentation.
- Current branch, HEAD, working tree, staged paths, ancestry, and the actual
  relation to `origin/main`. Verify implementation claims against code and tests;
  checked boxes alone are not acceptance evidence.

The optional `.agents/agent-workflow/` has changed through separate work. Read
applicable repository rules, but do not invoke an optional workflow step unless
the user requests it. This task already invokes the orchestration in Konzept.md.

## Fixed handoff and evidence provenance

- Last Navigator product slice:
  `20886fc801890c685e34af88bc16db44791ce447`
  (`feat(navigation): register initial MCP tools`).
- HEAD immediately before creating this resume file:
  `cf99c98d21312859a74d9f3a4ccdf705ecff0c0d` (`docs: infos`), branch `main`.
  Working tree and index were clean. This prompt's documentation commit is a
  descendant; determine its actual hash from Git rather than expecting the
  pre-prompt HEAD to remain current.
- At that snapshot `origin/main` was
  `29aeb138aad8c12a030b1834db66e0e350b88c89`; main was 5 commits ahead and 0
  behind before the resume-file commit. The remote-tracking ref changed during
  the previous session, so do not reuse the original user's 14-ahead figure.
- The original handoff `4a563288ae41c3947ea4e92044d2824498cd3caa` was the
  requested fixed 8.2 audit base. Those audits are now complete; do not restart
  them or restore that old checkout.
- All implementing/auditing agent turns and their launched build/test/stdio
  processes ended before this prompt was written. No writing agent needs to be
  resumed from the prior chat. Create the required agents in the new chat.
- Separate external work added Lab, optional workflow, and information documents.
  In particular, preserve `860a729`, `ba95603`, `d031149`, and `cf99c98`, and all
  prior Lab commits. These were not authored by the Navigator implementer.
  Inspect new changes before editing and never stage, revert, or delete unrelated
  work. Concurrent external work is not evidence that our team has two writers.
- After the clean pre-prompt snapshot, new **unrelated uncommitted work** appeared:
  `.gitignore`, `docs/development/build-and-tests.md`, `ainetreview.json`, and
  `tests/AiNetCodeNavigator.FastTests/Reporting/RepositoryAuditReportTests.cs`.
  These paths are not owned by this roadmap's current slice or the resume-file
  commit. Preserve them; their current status can change independently. The
  previously green gates describe the committed Navigator slice, not these later
  external test changes. Do not claim a globally clean working tree while they
  remain uncommitted. Record the actual new-chat baseline and avoid concurrent
  writes with any active external writer; do not discard their work to clean up.

The implementer ran these official gates for the product slice; the orchestrator
read the actual final logs. These are **implementation evidence, not an
independent 9.2 audit**:

| Gate | Final result |
| --- | --- |
| `pwsh -File ./scripts/build.ps1` | 0 warnings, 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | 545/545, zero skipped |
| `pwsh -File ./scripts/test-integration.ps1` | 16/16, zero skipped |
| `pwsh -File ./scripts/test.ps1` | 545 fast + 16 integration = 561, zero skipped |
| Scoped staged `git diff --check` | Passed |

Static logs are `temp/build.log`, `temp/test-fast.log`,
`temp/test-integration.log`, and `temp/test.log`. Later runs overwrite them.
Documentation-only creation of this prompt needs diff review and diff checking,
not a repeated product gate. The review records earlier red runs and their fixes.

## Accepted points and limits that must survive the handoff

| Point or cluster | State |
| --- | --- |
| Cluster 2, point 2.3 | Open documented MSBuild wildcard/unresolved-import candidate-path boundary; three point audits exhausted |
| Point 3.3 | Assembly consumer boundary implemented in Cluster 7 and status reconciled; three point audits exhausted |
| Cluster 7 | Closed after integration review 2, following fix round 1 |
| Points 7.2 and 7.3 | Closed after their third and final point audits |
| Point 8.1 | Closed after audit 2/3 |
| Points 8.2, 8.3, 8.4 | Closed after audit 3/3 each |
| Point 8.5 | Open, 0/3 audits; depends on complete 9.2 public registrations |
| Cluster 8 | Open; initial integration review 1 covered internal scope, fix rounds 0; public integration deferred |
| Point 9.1 | Closed after independent audit 2/3 |
| Point 9.2 | Partial implementation only; open, 0/3 independent point audits |
| Points 9.3 and 9.4 | Open; no point audits yet |
| Clusters 10 and 11 | Open; no point audits yet |

Other completed point and integration counts are authoritative in the existing
reviews; do not start them over. **No fourth point audit of 2.3, 3.3, 7.2, 7.3,
8.2, 8.3, or 8.4.** Review new public consumer integration under its actual new
roadmap point, without renaming an exhausted old audit to evade the limit.

Accepted remediation commits for reference:

- 8.2: `b7895eef60e834443f0e094989a8f7c7924e09a9`.
- 8.3: `7db5c08a0e408df9fca2a921ea1191af86f8ba3f`.
- 8.4: `2da79dfaaefddd3002f32dbe449707ad3f3fc24c`.
- 9.1: `3cd6919e9c2c8f2c543082cd93fb662f2e406ae5`, accepted by the independent
  audit documented in `29aeb138aad8c12a030b1834db66e0e350b88c89`.

The answered product question is settled: `reload_config` reads the Navigator's
own JSON host configuration, validates it, and atomically applies supported
settings. The implemented runtime setting is `minimumLogLevel` only. Capacities,
TTLs, budgets, transport, and log paths remain startup settings. Unchanged reloads
retain their version; budget-rejected reloads preserve active state. No question
about Linter rule reload remains pending.

## Current 9.2 slice and concrete remaining work

The real stdio host currently registers **9 tools**, not the final 22:

- Navigation: `find_symbol`, `get_symbol_body`, `get_file_skeleton`,
  `get_class_structure`, `get_file_tree`, `get_namespace_tree`, `get_index_scope`.
- Maintenance: `get_server_health`, `reload_config`.

The current real-process tests exercise initialize, the exact nine-tool catalog,
source find -> opaque `h:...` -> body, source skeleton/class/file/index/namespace
queries, managed-DLL find -> assembly handoff -> body, protocol-only stdout, and
EOF shutdown. This does not establish all assembly structure consumers, reference
closure, defaults, filters, budgets, or lifecycle scenarios.

The cold-source test prepares and restores a fresh `.slnx`/`.csproj` fixture before
the baseline, compares the complete target file/directory tree before its first
public navigation call and afterward, includes `obj` and `bin`, and checks host
scratch cleanup after EOF. A red run found design-time MSBuild outputs in the
target despite `DisableRarCache=true`. The fix now redirects intermediate/output
state through `CustomBeforeMicrosoftCommonTargets` to per-process/per-workspace
temporary paths with project-directory identity, keeping original restore assets.
Host logs are outside the analyzed fixture. The broader repository snapshot
excludes `.git` and the four explicitly named active gate logs; integration tests
were serialized to avoid their own temporary-fixture races. No dedicated
two-solution, same-project-name isolation regression has yet been performed.

Core tests also cover namespace prefix/kind/include-types filtering and domain
truncation surviving continuation and replay with no structured partial result.
These are not substitutes for the public per-tool contract matrix.

Next implement the **13 remaining navigation registrations**, preserving the
AiNetLinter navigation contracts rather than merely registering names:

- Relationships: `get_call_tree`, `find_references`, `get_type_hierarchy`,
  `find_implementations`, `get_impact`, `dependency_graph`, `resolve_type_origin`.
- Assembly: `get_assembly_context`, `inspect_assembly`, `search_assembly`,
  `find_assembly_extensions`.
- Context: `get_feature_context`, `get_test_context`.

Also complete the existing seven tools' parameter/filter parity and public
contracts. Concrete open boundaries recorded in the 9.2 review include:

- Very small positive token budgets can fail in navigation error formatting
  before routing. Verify sanitized SDK `InvalidParams` when the mandatory error
  envelope cannot fit, and executable exact byte/token recovery when it can.
  Maintenance already has independent evidence; navigation does not.
- Review larger body/find rendering for escaped monolithic text and line-safe
  pagination. Prefer existing useful text formatters over duplicating full text
  inside a large serialized JSON string.
- Preserve domain completeness on every page, final page, replay, and budget
  variant; do not claim complete success for bounded/truncated scans. Omit
  structured content for partial results. Verify the complete-result structured
  policy explicitly through the actual tool path.
- Complete reference-closure and owner-specific assembly handoffs, including
  Root -> B -> C, missing references, and stale reference hash/generation.
  Each reference handoff must bind its own canonical path/content hash/generation;
  never issue source handoffs for virtual assembly documents. Advertise only
  actually supported and roundtrip-tested assembly follow-ups.
- Keep source loading/retry errors intact, stable-deduplicate patterns before
  mapping results, reject malformed/stale/cross-target tokens, and propagate body
  failures honestly rather than enclosing errors in complete success.
- `get_impact` needs source Git/change-context behavior in addition to existing
  symbol impact. Use safe `ProcessStartInfo.ArgumentList`, verify refs, handle
  worktree `.git` files, avoid optional locks/external diff/textconv, and await
  cancelled child processes. Do not copy unsafe reference command strings.
- `dependency_graph` needs exact project/symbol identity; `resolve_type_origin`
  needs source mode; `get_assembly_context` needs symbol-composed options.
- Public namespace selection uses `project`, not `projectName`; source with no
  filters needs the reference project overview. Check public limits and filters
  against the actual reference registrations, not the narrower current Core API.

Reference implementations remain read-only under
`C:\Daten\Entwicklung\Ralf\AiNetLinter`. Relevant registration files are under
`src/AiNetLinter/Mcp/Registration/`; consult their symbol/body/file-structure/graph/
assembly contracts and tests. Do not copy linting, metrics, diagnostics, or write
tools. `.sln`/`.slnx` source targets and managed `.dll`/`.exe` targets must keep
correct target routing and identity throughout.

## Continue in this order

1. Finish 9.2's complete twenty-tool implementation in tested, committed vertical
   slices. The current seven-tool slice is not a completed numbered point.
2. After the full numbered point, commission **independent 9.2 audit 1/3** on a
   fixed product commit with `gpt-6.1-sol`, reasoning effort `medium`. Fix findings
   and re-audit only as needed, with at most three point audits total.
3. Return to 8.5 for actual per-tool success/error/loading/cancellation/budget/
   structured/domain-truncation/pagination/input verification, then its audits.
   This documented dependency exception does not consume a point audit.
4. Complete Cluster 8's deferred public integration review and any allowed fix
   rounds; do not re-audit exhausted internal points.
5. Continue 9.3, 9.4, and Cluster 9 integration in roadmap order.
6. Continue every numbered point and audit of Cluster 10, then its integration.
   The English migration explicitly includes existing tasks, concept/checklists/
   reviews, optional workflow documents, comments, messages, tests, scripts,
   configuration and authored filenames. Rename German paths and update links.
   Preserve proper names such as copyright owner `Ralf Hüsing` and maintain
   meaningful Unicode test coverage. Do not invoke an optional workflow merely
   because its files are being translated.
7. Continue Cluster 11's real source/assembly stdio acceptance, tool catalog/client
   setup documentation, final official gates, 22-tool matrix, point audits, and
   integration review. A catalog/schema test alone is insufficient acceptance.

## Orchestration and verification constraints

- Orchestrator: `gpt-6.1-sol`, as Konzept.md specifies.
- Implementation agent: `gpt-6-luna`, reasoning effort `high`.
- Independent point/integration auditor: `gpt-6.1-sol`, effort `medium`.
  Use explicit model/effort with a fresh or limited-history fork where required
  by the agent tool. Do not silently inherit a different model.
- One exclusive writing phase across the entire team: code/docs/tests, restores,
  builds/test runs, generated files/static logs, staging, and commits all count.
  The orchestrator also waits until the previous writer's turn and all launched
  processes end. Read-only reference research may run concurrently. Fixed-commit
  mutable-repository audits start after the writing phase ends.
- An auditor may use permitted `gpt-6-luna`/high read-only research subagents for
  independent Source/Assembly comparisons; they do not build, test, write, commit,
  or spawn further agents. The auditor owns conclusions and the audit count.
- Max three point audits in total. Initial cluster review plus at most three
  integration fix rounds; preserve existing counts. Record fixed commit, model,
  findings, fixes, actual own gates, and remaining risks in the cluster review.
- Use proactive read-only AiNetLinter MCP semantic navigation before broad C#
  text exploration, and `rg` for appropriate non-C# or unavailable-index fallbacks.
- Reproduce defects with a meaningful failing test before fixing. Never weaken
  relevant assertions or omit workspace `obj`/`bin` to conceal navigation writes.
- Execute the official PowerShell gates, inspect their static logs, and distinguish
  implementer evidence from the independent auditor's own checks.
- Update affected current-state docs with verified implementation; keep plans in
  tasks. Automatically commit verified slices with English Conventional Commits
  and explicit paths. Never `git add .`, `git add -A`, or `git commit -a`; inspect
  staging conflicts. No push, amend, reset, or history rewriting is authorized.
- External AiNetLinter and AiNetReview remain strictly read-only. Core must not
  depend on MCP/transport; stdout is exclusively the MCP JSON-RPC stream.

## Optional retained research for 11.2

Official client documentation was checked on 2026-09-30. Recheck current sources
when writing setup docs. No actual client UI connection has been accepted yet.

- [Claude Desktop local-server guide](https://modelcontextprotocol.io/docs/2026-07-28/develop/connect-local-servers):
  Windows configuration `%APPDATA%\Claude\claude_desktop_config.json`,
  `mcpServers` with `command`/`args`/`env`, then completely restart the client.
- [Cursor MCP documentation](https://prod.cursor.com/docs/mcp): project
  `.cursor/mcp.json` or global `~/.cursor/mcp.json`; stdio process configuration.
- [Antigravity MCP documentation](https://antigravity.google/docs/mcp): current
  global path `~/.gemini/config/mcp_config.json` or workspace
  `.agents/mcp_config.json`. Do not reuse the older Antigravity path without
  verification; use `command`/`args`/`env`/`cwd` for stdio.

Use the Navigator executable or `dotnet` with an absolute built DLL path, not
`dotnet run` that may emit build output on stdout. Source MSBuild loading needs
the SDK. Do not infer a Navigator Node requirement from a guide's Node example.

**Do not report complete product delivery until the real MCP-Stdio contracts and
Cluster 11 have been accepted.** Report remaining point 2.3 debt explicitly even
after all otherwise achievable work, and preserve honest limits for any remaining
unimplemented or unverified scenario.
