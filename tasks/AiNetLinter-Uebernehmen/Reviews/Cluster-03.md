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

### Independent audit 1/3 of point 3.1

- Reviewed commit: `3afd5c80e468510428c49c1f869d0d669917b9a3` (clean working tree before review).
- Reviewer: `gpt-6-sol`, medium reasoning effort. AiNetLinter's `AnalysisSymbolIdentity` was inspected read-only through its MCP `find_symbol` and `get_symbol_body` tools. No reference files were changed. This audit did not run a build or tests; the gate results above belong to the implementation slice, not this audit.
- **P1 — Equal project paths collapse distinct target-framework projects.** `AnalysisSymbolIdentity.GetStableProjectMarker` hashes only `Project.FilePath` (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:169-183`), then `FormatHandoff` appends that marker to the DocumentationCommentId (`:32-36`, `:61-64`). Two Roslyn projects loaded from the same multi-target `.csproj`, with different target frameworks and the same declaration ID, therefore produce identical internal source identities within the same solution snapshot. The existing different-project test uses separate project directories (`tests/AiNetCodeNavigator.FastTests/Symbols/AnalysisSymbolIdentityTests.cs:39-69`) and does not exercise this collision. AiNetLinter uses the same path-only marker; this is a reference limitation that conflicts with this product's requirement to avoid resolving an ID to the wrong project/target. **Reproduction/acceptance:** Build a solution containing two Roslyn projects with the same absolute `.csproj` path but distinct target frameworks and a shared declaration; demonstrate unequal canonical identities and successful resolution to the intended target after the fix. Include a stable marker across equivalent reloads and avoid ephemeral `ProjectId` values.
- **P2 — Equivalent Windows target paths produce different handoff target tokens.** `Matches` explicitly treats case variants of an absolute path as equal on Windows (`AnalysisSymbolIdentity.cs:142-150`), but `Format` passes the original `CanonicalPath` (`:20-29`) to `SymbolHandoffToken.TryCreateTarget`, which hashes `Path.GetFullPath` without normalizing Windows case (`src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffToken.cs:15-39`). For the same content hash and symbol, `C:\\Repo\\App.slnx` and `c:\\repo\\app.slnx` thus match as identities while yielding different `i:` target tokens. `AnalysisTargetResolver` also preserves caller path case (`src/AiNetCodeNavigator.Core/Workspace/AnalysisTargetResolver.cs:122-151`). **Reproduction/acceptance:** On Windows, format the same symbol with case-variant absolute target paths and assert identical target token or a demonstrably equivalent lookup; keep path comparison and token construction consistent. Cover source and assembly targets.
- **Boundary:** `CreateCanonicalSymbolIdentifier` creates an `L:` file/line/column key for local functions (`AnalysisSymbolIdentity.cs:97-140`), while `FormatHandoff` intentionally rejects them (`:88-91`). This helper has no production caller yet; connecting file/line identifiers to the public handoff contract is part of point 3.3. Its current isolated test is not evidence of a local-function roundtrip.
- Point 3.1 audit checkbox remains open pending fixes and a follow-up audit. Point 3.2 and 3.3 gates are outside this review.
