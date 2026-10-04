# Shared roadmap execution rules

[Index](../roadmap.md). Read this contract together with the selected point file and its linked specifications.

## Execution contract

The orchestrator executes R01 through R08 in order, one active implementation point at a time. Every point depends on the preceding point's verified commit. Assign its linked contract, required outcome, acceptance and verification together. Do not delegate only a code fragment without its consumers, wiring and evidence obligations.

### Task-local agent roles

The user's 2026-10-04 override during R05 requires `gpt-6.1-sol` with reasoning `medium` for all subsequent implementation, test implementation and fix subagents throughout this task. Configure both values explicitly when starting those agents. This supersedes this task's earlier Luna/high implementation assignment; historical executed evidence keeps its actual agent attribution. It applies only to `agentic-navigation-evolution` and does not change repository or personal subagent defaults.

Independent auditors also use explicitly configured `gpt-6.1-sol` / `medium`, remain read-only and must be separate from the implementation/fix worker. The orchestrator alone owns commits, checklist changes and executed evidence. Continue the active work without pausing for the role change; no push or deployment is authorized.

Technical behavior is defined in [01](../01-symbol-identity-and-recovery.md), [02](../02-dependency-graph-analysis.md), [03](../03-long-running-operations-and-transport.md) and [04](../04-snapshot-refresh-and-analysis-cache.md). Their specified decisions replace the earlier open questions and proposals. Internal class/file placement may follow the existing owners; that freedom does not reopen the public contract.

### Approved delivery priority and deferred evidence

On 2026-10-04 the user explicitly approved the generator creator contract and prioritized completing the entire implementation over repeated testing/evidence loops. Additional evidence may be deferred to [findings.md](../findings.md) for later review. This steering supersedes earlier point-level completion gates for explicitly deferred measurements, expanded test matrices and environment-dependent connected-client evidence; it does not change observable product behavior or permit claiming an unrun check passed.

For each point, finish production wiring/consumers/docs and focused behavioral tests, execute the official build and the narrow affected eligible tests, and obtain one independent actual-code audit. Fix confirmed functional defects and rerun their affected checks. Re-audit the corrections and changed areas rather than restarting the entire audit without new evidence. Consolidate broader routine coverage at R08; do not repeat successful gates without a relevant change or unresolved concrete failure. Repository stall handling and E2E exclusion still apply.

Record deferred items with a stable ID, exact missing evidence, reason, follow-up command/acceptance and owner in findings.md, and link their IDs from point evidence. Distinguish confirmed correctness defects from unexecuted scenarios and observations. An unresolved confirmed functional defect remains a blocker; a user-approved evidence deferral does not. Point checklist/evidence obligations may be completed with such explicit deferrals once implementation, focused gates and independent review are complete. Never mark the deferred check itself PASS. Retain all exact executed commands and their outcomes. R08 reports every still-open deferred item.

The user's supplementary R03 guardrail requires the smallest concrete implementation preserving all binding acceptance. Reuse existing refresh/fingerprint/lifetime owners and .NET primitives; introduce a helper only for a concrete requirement or evidenced defect. Generator admission is limited to actually supported creators, with concrete diagnostics for unknown/unprovable inputs. Do not create general cache, provenance, sandbox, hooking or audit frameworks or speculative special-case support. Explain any substantial extension by its forcing requirement and why a smaller change in the existing owner cannot satisfy it. Before completion, remove unnecessary new layers, state and helper types; record the review in point evidence. Escalate a concrete disproportionate contract conflict instead of silently broadening architecture or weakening acceptance.

Before each point, inspect current HEAD/status, the linked specifications, affected definitions/callers/runtime wiring, relevant tests and repository rules. Recheck sources because earlier file links may be renamed or removed. Preserve unrelated work and stop on staged-file conflicts. No automatic push, deployment or optional agent-workflow step is included.

Every completed point must include:

- Production code, consumers, registration/configuration changes and removal of superseded paths within that point's scope.
- Requirement/regression tests, relevant current-state docs and affected agent navigation instructions in the same verified commit.
- The commit ID, executed checks and their outcomes in the current point file's Execution evidence section. Do not mark success from a plan, source inspection or unrun tests.
- Explicit remaining environmental limitations, if any; a mandatory unavailable check leaves the point blocked rather than silently complete.

A point may use several coherent commits when needed, but its successor starts only after all point-level acceptance has passed. R01 is the only intentionally internal preparatory point: the public reference switch and full old-handle removal happen together in R02.

## Implementation structure and checkpoint review

User steering during R02 requires the remaining points to centralize shared semantics and extract independently owned responsibilities where appropriate. New behavioral contracts must have narrowly executable tests; preservation tests must retain meaningful independent assertions rather than mirror implementation details.

At the next verified checkpoint, the orchestrator records a concrete assessment of remaining structure problems in `RelationshipTools` and the large handler contract test classes in that point's evidence. Separate required roadmap integration/semantic extraction from broader refactoring that remains outside this task. This review does not authorize a blanket rewrite, acceptance changes, or delaying an otherwise verified point for unrelated cleanup. Subsequent points follow the same ownership and verification rules.

If new evidence contradicts a specified requirement or exposes an architectural conflict, record the exact conflict and request a decision before dependent implementation. Routine implementation choices within the contract require no additional approval. A test stall follows the repository's five-minute rule: open a named diagnosis/correction subpoint under the current point before further feature work.

## Completion and evidence ownership

The eight point checkboxes in the [index](../roadmap.md#progress-index) are the authoritative completion state. Read their current state and each point's execution evidence; implementation work alone is not completion. Record Pending, In progress or Blocked and its concrete next action in the current point file's Execution evidence section while the point remains unchecked. Complete means its top-level checkbox is checked.

Only the orchestrator changes checkbox states, including point-specific items; workers report evidence without ticking boxes. The orchestrator explicitly reviews each checklist item in that point's file, records executed evidence and verified implementation commit(s), then changes that point's top-level `[ ]` to `[x]`. A worker's completion message, code changes alone, tests merely defined, or a commit with missing checks never authorizes a tick. The next point cannot start until that tick is recorded. Do not pre-check future steps or tick several points from one general success statement.

Implementation acceptance and each point-specific checklist item must actually pass before its box is ticked. A blocked or unrun item remains unchecked. If later changes invalidate a completed item, reopen that item and the affected top-level point, record why, and reverify all affected successor evidence before closing them again. Record implementation commit IDs already present in Git; a following documentation commit can persist the checklist/evidence update without needing to know its own future hash.

Keep implementation commits, exact commands/results, measurements/artifacts, blockers and next actions in the corresponding point file's Execution evidence section. The index owns only the eight completion checkboxes and document-maintenance history; it does not duplicate per-point evidence. Keep all technical requirements in their owning specification. Paths in verification commands are relative to the repository root, even when the command is documented in this subdirectory.

## Verification commands and evidence policy

Repository [.agents/rules/04](../../../.agents/rules/04-verification.mdc) owns all mandatory gates and stall handling; [.agents/rules/05](../../../.agents/rules/05-git.mdc) owns automatic commits and staging. Commands:

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test-fast.ps1 -Filter '<affected eligible tests>'
pwsh -File ./scripts/test-integration.ps1 -Filter '<affected routine contracts>'
pwsh -File ./scripts/test-integration.ps1 -IncludeExtended -Filter '<relevant extended tests>'
pwsh -File ./scripts/test.ps1
```

Choose actual existing/new fully-qualified test selections after inspecting their categories; placeholders are not executable final evidence. All official scripts exclude E2EIntegration even with IncludeExtended. Keep transport-free requirements in the eligible selection. Capture actual executed commands, exit outcomes and reports. Preserve relevant `temp/*.log`/TRX evidence before a later script overwrites it. Do not claim read-but-unrun tests passed.

Documentation-only consolidation requires reviewed diffs, valid local links and `git diff --check`, not a product build.
