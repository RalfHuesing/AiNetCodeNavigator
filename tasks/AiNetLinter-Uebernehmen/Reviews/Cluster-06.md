# Cluster 6 Review Log

## Point 6.1 implementation record

- Starting commit: `4068706a60cd00c9d13137384a572db196bb10a6`, clean working tree.
- Reference review used the AiNetLinter read-only MCP index and source for `SolutionFileWalker`, `GetFileTreeScanner`, `FileTreeFilter`, `FileTreePathResolver`, `FileTreeAccumulator`, `GetFileTreeInputValidator`, the filesystem walk options/exclusions, and `GetFileTreeScannerTests`. `SolutionFileWalker` reads Roslyn project documents for related project-scoped file scans; the `get_file_tree` implementation itself uses the physical filesystem walker and shared exclusion rules. The Core implementation follows the latter contract and does not read source contents unless line counts are explicitly requested.
- Before-fix regressions were run with `pwsh -File ./scripts/test-fast.ps1 -Filter FullyQualifiedName~GetFileTreeScannerTests`. Three failed as expected: `RelativeRoot="../sibling"` exposed `secret.cs` outside the root; `MaxDepth=0` did not mark a remaining child directory as truncated; and `view="summary"` returned file entries rather than a bounded, recursively aggregated directory summary. The byte-for-byte read-only check passed before and after the fix.
- `GetFileTreeScanner` now confines relative roots, rejects invalid view/sort/glob/bounds, skips excluded and reparse-point directories, reports cancellation/depth/access warnings, applies the 32-depth and 2,000-result hard caps, sorts output deterministically, supports extension and relative glob/exclusion filters, and exposes truncation reasons plus one safe next action. Summary view returns directory aggregates and no file entries. `MaxResults` bounds returned items; the API has no retained cursor or offset pagination, matching the AiNetLinter scanner's discovery contract.
- Added scanner contract tests for root escape, missing roots, recursive summary roll-up, result truncation, depth/cancellation status, deterministic ordering, filter composition, hard caps, and source-file immutability. Added current-state documentation at `docs/navigation/get-file-tree.md` and linked it from `docs/README.md`.
- No independent audit has been performed yet. The 6.1 audit checkbox in `Clusters/Cluster-06.md` remains open for the independent auditor; 6.2 and 6.3 were not touched.

## Point 6.1 independent audit 1 of 3

- Reviewed commit: `429de13101b38f40a9adcfe9e510bc1be216d838` (`gpt-6-sol`, medium). The working tree was clean before this documentation edit. Inspection covered the Core scanner, filter, result model, all 12 FastTests, current-state documentation, and AiNetLinter's read-only `SolutionFileWalker`, `GetFileTreeScanner`, and `FileTreePathResolver`. AiNetLinter uses `SolutionFileWalker` for a separate Roslyn project-document scan; its `get_file_tree` contract walks the physical filesystem, as this Core scanner does. No product build or tests were run during this audit; the implementation record above reports the earlier implementer's gates.
- **P1 — Ancestor link escapes the requested root.** `src/AiNetCodeNavigator.Core/FileStructure/GetFileTreeScanner.cs:43-61` checks the normalized path lexically and tests only `targetDir` for a reparse point. With `RootDirectory=<root>`, `<root>/link` pointing outside, and `RelativeRoot="link/subdir"`, the final `subdir` is not itself a reparse point, so lines 89-103 enumerate outside files. The current test at `tests/AiNetCodeNavigator.FastTests/FileStructure/GetFileTreeScannerTests.cs:95-107` covers only `..`. Acceptance: reject or safely confine paths traversing a link ancestor, with a deterministic regression test, while preserving read-only behavior.
- **P2 — Relative analysis roots are silently accepted.** `GetFileTreeScanner.cs:31-35` calls `Path.GetFullPath` before `Path.IsPathFullyQualified`, making the latter check true for a relative input. The error at lines 22-24 promises an absolute root; AiNetLinter's `FileTreePathResolver.cs:26-29` explicitly rejects non-rooted analysis roots. Acceptance: reject relative `RootDirectory` before full-path normalization and test the recoverable error result.
- **P2 — Result budget and truncation disagree with the selected view.** `GetFileTreeScanner.cs:169-185,199` takes `MaxResults` files and separately takes `MaxResults` summaries. A files/tree response can therefore expose up to twice the requested item count. Also, one matching file in a child directory with `View="files", MaxResults=1` produces one file entry but two summary entries (root and child), so `maxResults` is reported despite the file view being complete. `FormatOutput` at lines 351-386 renders the active view while `Summaries` are still returned. Acceptance: enforce one coherent bound on returned/displayed items and mark `maxResults` only when the active view omits entries; add tests for both over-budget and false-truncation cases.
- The point's audit checkbox remains open because these findings are unimplemented. Three follow-up work items were added under 6.1; no status is claimed for 6.2 or 6.3.

### Verification

| Gate | Result |
|---|---|
| Before-fix focused repros | 3 failures as expected; read-only check passed |
| Focused `GetFileTreeScannerTests` after fix | Passed, 12/12 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 393/393 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 405/405 across both test projects |

## Point 6.1 audit 1 remediation

- Scope: remediate all three findings from the independent audit at commit `2258e75833c0ba4be1aa3ab304c7025351cb63e5`. The independent 6.1 audit checkbox remains open pending follow-up audit.
- Before-fix repros: the relative-root test accepted `"."` and returned a successful scan; the files-view contract exposed hidden summary entries; the tree-view contract returned a file and a directory summary for `MaxResults=1`. Those assertions failed as expected. For the reparse-point finding, code inspection confirmed that only the final target was checked. Creating a real symbolic-link fixture failed in this Windows environment because the process lacks the required privilege, so the regression test exercises the exact production path-walk helper with a deterministic predicate that marks an intermediate `link` component as a reparse point.
- `RootDirectory` is now checked with `Path.IsPathFullyQualified` before `GetFullPath`. The root and each ancestor from `RelativeRoot` up to the analysis root are checked for reparse points before traversal.
- Result projection now follows the active view. Files view returns only bounded file entries; summary view returns only bounded directory aggregates; tree view uses one combined budget for root files and directory entries. `maxResults` is reported only when the selected view omits an item.
- Added FastTests for the three findings. Current-state documentation now describes absolute-root validation, reparse-ancestor confinement, and the single selected-view result budget. The three remediation checklist items are complete; the independent audit checkbox remains open.

### Remediation verification

| Gate | Result |
|---|---|
| Before-fix relative-root and result-budget repros | Failed as expected; the reparse-point finding is covered by source evidence and a deterministic ancestor-walk test because the OS denied symbolic-link creation |
| Focused `GetFileTreeScannerTests` after fix | Passed, 16/16 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 397/397 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 409/409 across both test projects |

## Point 6.1 independent audit 2 of 3

- Reviewed commit: `68dd255a4ad4a6ebb54f6235389c96b480789ee8` (`gpt-6-sol`, medium). The working tree was clean before this documentation edit. This follow-up inspected the remediation diff, current scanner and regression tests, and the reported gate results above. No build or product test was run by the auditor.
- **P1 ancestor reparse-point confinement — accepted.** `GetFileTreeScanner.cs:59-66` invokes `FindReparsePointAncestor` before enumeration. The helper at lines 258-280 checks the final target and then each parent through the analysis root, so a `RelativeRoot` such as `link/subdir` reaches the intermediate `link` check. `GetFileTreeScannerTests.cs:108-128` deterministically tests that walk with a predicate identifying the intermediate component. The test does not exercise an actual filesystem symbolic link or junction because the implementation environment denied link creation; this is a documented platform test limit, not evidence of a remaining implementation gap. Production calls the same helper with `IsReparsePoint`.
- **P2 absolute-root validation — accepted.** `GetFileTreeScanner.cs:31-36` now checks the original `RootDirectory` with `Path.IsPathFullyQualified` before normalization; `GetFileTreeScannerTests.cs:129-137` covers a relative `"."` root and its recoverable error.
- **P2 selected-view result budget — accepted.** `GetFileTreeScanner.cs:174-207` projects only entries for the selected view. Files and summary views use their respective counts for truncation; tree view builds one sorted item list and applies a single `MaxResults` limit before splitting the returned files and directories. `GetFileTreeScannerTests.cs:138-174` covers both the prior false truncation and the combined tree budget. The summary limit also remains covered by the existing recursive summary test.
- All three audit-1 findings meet their acceptance conditions. The 6.1 audit checkbox is marked complete. No new finding was identified within this targeted follow-up; 6.2 and 6.3 were outside its scope.

## Point 6.2 scanner implementation

- Starting commit: `3535d2ca0db9883a34dfb42696031c85d6cbab60`, clean working tree. Scope is only point 6.2; the independent audit checkbox remains open, and 6.3 was not changed.
- The read-only AiNetLinter comparison covered `GetNamespaceTreeScanner`, `RenderNamespaceTree`, `CollectNamespaceTreeNodes`, `GetNamespaceTreeTool`, and the scanner FastTests. AiNetLinter limits namespace traversal by depth, defaults `maxResults` to 50 with a hard cap of 200, applies the active result limit to the namespace projection, returns truncation guidance, and propagates cancellation while converting other scan exceptions to a recoverable compilation error. Its project-scoped scan derives allowed syntax trees from project documents and does not provide offset/cursor pagination.
- Before-fix reproductions: the focused FastTests had four expected failures out of five. The scanner counted 2 namespace entries where the output hierarchy contained 3 (the parent was omitted from the total); a 41-segment namespace produced depth 80; an unknown project name returned a success-shaped empty payload; and a large result had 205 nodes while the total omitted its synthesized root. The hierarchy fixture also covers block-scoped and file-scoped declarations, partial types, and aggregation across projects.
- `NamespaceTreeScanner` now limits depth to 32 and defaults the result budget to 50 with a 200-node cap. One result budget covers all namespace nodes, including ancestors; the structured tree and formatted output share the same projection. Namespace totals include synthesized parents within the effective depth. Source types are restricted to the target project's document syntax trees; partial types count once per project, while matching namespace names aggregate across solution projects. A namespace containing source types below the depth cap adds its visible prefix and reports `maxDepth`. Unknown project names and scan exceptions return an error payload; cancellation is rethrown. No offset/cursor pagination was added, matching the reference tool's continuation model.
- Added FastTests for declared namespace hierarchy, file-scoped and nested namespaces, partial-type de-duplication, multi-project aggregation, result and depth truncation, bound clamping, unknown project errors, cancellation, and unchanged document text. Added current-state documentation at `docs/navigation/get-namespace-tree.md` and linked it from `docs/README.md`.
- No independent audit has been performed for 6.2. Its checklist item remains open. Point 6.3 was not touched.

### Point 6.2 verification

| Gate | Result |
|---|---|
| Before-fix focused `NamespaceTreeScannerTests` | 4 expected failures, 1 passed |
| Focused `NamespaceTreeScannerTests` after fix | Passed, 7/7 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 403/403 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 415/415 across both test projects |
