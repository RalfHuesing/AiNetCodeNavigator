# Cluster 3 Implementation Review

## Point 3.1: Symbol Identity

- Implementation base: `a5f2ae277561e79c754524de644ed8f51b133993`; the working tree was clean before this slice.
- Reference review: AiNetLinter's `AnalysisSymbolIdentity` was inspected read-only through its MCP server (`find_symbol`, `get_feature_context`, and `get_symbol_body`). The adapted implementation matched its identity behavior. The reference relies on DocumentationCommentIds for symbol identities, generates project markers from project paths when available, falls back to ephemeral Roslyn `ProjectId` values for pathless projects, and treats an absent canonical target path as a wildcard in `Matches`. The candidate test context also pointed to the handoff-token tests; the relevant envelope test was inspected read-only. No reference code or tests were changed or run.
- Findings and fixes:
  - **P2 — Symbols without a usable DocumentationCommentId had no canonical source key.** Roslyn can provide a DocumentationCommentId for local functions, but this handoff contract intentionally rejects local-function declarations. Added `CreateCanonicalSymbolIdentifier`: accepted declarations use their DocumentationCommentId; local functions and other symbols without one use an absolute, normalized source path and one-based line and column. Ambiguous or pathless source locations return `null`.
  - **P2 — Source identity could depend on transient or incomplete project/target identity.** Source handoffs now require a project marker from a project with an absolute project path in the captured solution. Public formatting helpers that could omit the project marker or use an arbitrary ProjectId are private. `Matches` requires both identities to have absolute target paths, normalizes equivalent paths, and uses the host platform's path case rules before comparing content hashes.
- FastTests cover same DocumentationCommentId in separate projects, foreign-solution symbols, pathless projects, overload DocumentationCommentIds, local-function file/line keys, and strict target-path matching. Existing source and assembly identity tests remain in place.
- Current-state documentation updated: [build-and-tests.md](../../../docs/development/build-and-tests.md).
- Scope boundary: this slice does not change the handoff wire format or connect producers to consumers. Handoff roundtrips, opaque `h:...` behavior, stale handles, and Assembly tool consumers remain point 3.3.

### Verification

All official gates passed after the final implementation:

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 229/229 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 241/241 across both test projects |
| `git diff --check` | Passed before commit |

An initial FastTests run failed in the new local-function identity test because the helper selected Roslyn's local-function DocumentationCommentId. The implementation was corrected to use the promised file/line fallback for local functions; all official gates above were rerun afterward.

A later FastTests rerun transiently failed in the unrelated `HandoffHandleRegistryTests.ConcurrentRequests_AreThreadSafeAndDeduplicated` test (`19` IDs observed, `20` expected), matching the race previously recorded in the Cluster 1 review. The immediate retry passed, as did the full suite.

- The point 3.1 audit checkbox remains open for independent follow-up. No 3.2 or 3.3 implementation work was included.
