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
