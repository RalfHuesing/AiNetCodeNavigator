# Shared roadmap execution rules

[Index](../roadmap.md). Read this contract together with the selected point file and its linked specifications.

## Execution contract

The orchestrator executes R01 through R08 in order, one active implementation point at a time. Every point depends on the preceding point's verified commit. Assign its linked contract, required outcome, acceptance and verification together. Do not delegate only a code fragment without its consumers, wiring and evidence obligations.

Technical behavior is defined in [01](../01-symbol-identity-and-recovery.md), [02](../02-dependency-graph-analysis.md), [03](../03-long-running-operations-and-transport.md) and [04](../04-snapshot-refresh-and-analysis-cache.md). Their specified decisions replace the earlier open questions and proposals. Internal class/file placement may follow the existing owners; that freedom does not reopen the public contract.

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
