# Cluster 6 Review Log

## Point 6.3 implementation record

- Starting commit: `a16692571c79b195f2b756f77fac9c31a1f782e9`, clean working tree. Scope is only point 6.3; its independent audit checkbox remains open.
- Read-only AiNetLinter comparison covered `GetIndexScopeScanner`, its tool contract tests, and output rendering. AiNetLinter's tool includes physical non-C# project assets and generated/build categories as well as Roslyn document counts, and reports compile-error documents in the inventory. The Core scanner here reports the loaded Roslyn `Project.Documents` inventory only. It does not read document text, touch the filesystem, or modify the workspace, and does not compile documents. The reference contract has no result bounds or cursor pagination.
- Before-fix focused repros: 4 of 6 `IndexScopeScannerTests` failed. The scanner returned all 205 projects and all 70 file types without limits, emitted German report labels, and dereferenced a null solution instead of throwing `ArgumentNullException`. Cancellation and the original inventory test passed.
- `IndexScopeScanner` now supports solution and case-insensitive project scope. It counts the full selected scope before projecting at most 100 projects and 64 file types by default (hard caps 500 and 128). Complete totals remain available alongside shown counts, truncation reasons, effective/requested bounds, and a next action. Unknown projects return a recoverable error; cancellation propagates. Reports and scanner-authored guidance are English. C# counts include only `.cs` Roslyn document entries from C# projects; malformed source remains counted because the scan does not compile. No cursor or offset pagination is provided.
- Added FastTests for project/file-type bounds and deterministic projection, scope selection, unknown-project errors, bound clamping, compile-error inventory, English output, source immutability, null input, and cancellation. Added current-state documentation at `docs/navigation/get-index-scope.md` and linked it from `docs/README.md`. The 6.3 implementation subtasks are checked; the independent audit checkbox remains open.

### Point 6.3 verification

| Gate | Result |
|---|---|
| Before-fix focused `IndexScopeScannerTests` | 4 expected failures, 2 passed |
| Focused `IndexScopeScannerTests` after fix | Passed, 8/8 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 412/412 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 424/424 across both test projects |

## Point 6.3 independent audit 1 of 3

- Reviewed commit: `0a5a18c863cd0aef0e4130af4ad699c9cef5935b` (`gpt-6-sol`, medium), with a clean working tree before this documentation edit. Inspected `IndexScopeScanner`, models, eight FastTests, current-state documentation, and AiNetLinter's read-only `GetIndexScopeScanner`. The solution/project document and extension counts are taken before the separate project and file-type presentation limits, unknown project and cancellation paths are explicit, scanner-authored text is English, and the scanner only reads Roslyn metadata. The deliberate Roslyn-document scope is documented; AiNetLinter's additional physical non-C# asset inventory is outside this Core inventory. No product gates were run by the auditor; the implementation record above reports the earlier gates.
- **P2 — Test and generated document populations are missing.** `src/AiNetCodeNavigator.Core/FileStructure/IndexScopeScanner.cs:53-88,130-151` reports `TestProjectCount`, but never counts documents belonging to those test projects or documents classified as generated. Neither count exists in `IndexScopeModels.cs:19-41` or the formatted report at `IndexScopeScanner.cs:223-256`. AiNetLinter's `GetIndexScopeScanner.cs:57-67,125-128` reports both generated and test-project document counts as part of its index-scope population. For a test project with multiple documents and an indexed `.g.cs` document, the current payload cannot answer either document-level question; `IndexScopeScannerTests.cs:99-118` checks read-only behavior but asserts neither count. Acceptance: add explicit generated-document and test-project-document totals for the selected Roslyn document scope, define generated classification consistently with navigation, keep totals complete when either list is truncated, and test mixed production/test/generated documents plus project filtering.
- The 6.3 audit checkbox remains open pending this finding. No finding is raised for 6.1 or 6.2 in this point audit.

## Point 6.3 audit 1 remediation

- Scope: address the P2 missing generated- and test-document totals from the independent audit at `f1c0ba33932af75022027ca369b09148ab591dd2`. The independent audit checkbox remains open for follow-up.
- Before-fix regression attempt: the new contract tests failed to compile because `IndexScopePayload` had no `GeneratedDocumentCount` or `TestDocumentCount` properties. The reported scanner behavior also exposed no corresponding summary output.
- Added both counts to `IndexScopePayload` and the formatted report. Counts are computed across the complete selected Roslyn project scope before project/file-type presentation limits. `TestDocumentCount` applies the same project-or-document-path rule as symbol navigation. `GeneratedDocumentCount` counts C# documents and uses the same path, header, and `GeneratedCodeAttribute` rules as symbol navigation.
- Extracted the generated-document classifier to `GeneratedDocumentDetector` and made `FindSymbolScanner` and `IndexScopeScanner` share it. Added FastTests covering production and test projects in one solution, `.g.cs` documents, test-path files, top-level totals despite project-list truncation, and case-insensitive project filtering. Updated the current-state documentation. No independent follow-up audit has been performed; the 6.3 audit checkbox remains open.

### Audit 1 remediation verification

| Gate | Result |
|---|---|
| Before-fix regression attempt | Contract tests failed to compile because both count properties were missing |
| Focused `IndexScopeScannerTests` after fix | Passed, 10/10 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 414/414 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 426/426 across both test projects |

## Point 6.3 independent audit 2 of 3

- Reviewed commit: `bbca9a12603a7aea50b1119b726ee7c1792c5fc2` (`gpt-6-sol`, medium), with a clean working tree before this documentation edit. Inspected the remediation diff, shared generated-source classifier, index-scope scanner/models, ten FastTests, and current-state documentation. No product gates were run by this auditor; the remediation record above reports the implementer's gate results.
- **P2 generated and test document populations — accepted.** `IndexScopeScanner.cs:55-100` counts both populations inside the selected project loop before project and extension projection at lines 119-128. The payload and formatted report expose `GeneratedDocumentCount` and `TestDocumentCount` (`IndexScopeModels.cs:42-43`; `IndexScopeScanner.cs:166-167,249-250`). `GeneratedDocumentDetector.cs:15-50` is now called by both this scanner and `FindSymbolScanner`, so their C# generated-source classification shares one implementation. The test-document rule at `IndexScopeScanner.cs:76-80` matches `FindSymbolScanner.MatchesScope` (`TestDetector.IsTestProject` or `IsTestFile`). `IndexScopeScannerTests.cs:148-200` checks mixed production/test/generated documents, complete totals despite `MaxProjects=1`, case-insensitive project filtering, and the Markdown summary. `docs/navigation/get-index-scope.md` states the C# generated-document scope and that totals precede presentation limits.
- The audit-1 finding meets its acceptance conditions, so the 6.3 audit checkbox is complete. No new finding was identified in this targeted follow-up.

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

## Point 6.2 independent audit 1 of 3

- Reviewed commit: `a7857f8db9fbc0c3050b0de6eb38f1d2cb3c475b` (`gpt-6-sol`, medium), with a clean working tree before this documentation edit. Compared `NamespaceTreeScanner`, models, seven FastTests, and current-state documentation with AiNetLinter's read-only `GetNamespaceTreeScanner` and renderer. The scanner correctly combines block-scoped, nested, file-scoped, and partial declarations through Roslyn symbols and project source-tree membership; its unknown-project error, cancellation propagation, and shared result projection are supported by code and tests. AiNetLinter has a different three-level tool surface and uses German text; the Navigator's English-output requirement comes from `Konzept.md`. No product gates were run in this audit; the implementation record above reports the earlier gate results.
- **P2 — Public output is still German.** `src/AiNetCodeNavigator.Core/FileStructure/NamespaceTreeScanner.cs:41,58,77,282-328` emits German errors, `NextAction`, summary labels, truncation guidance, and type counts. The public payload exposes these strings through `Error`, `FormattedText`, and `NextAction`, contrary to the English-output requirement in `Konzept.md`. The FastTests at `tests/AiNetCodeNavigator.FastTests/FileStructure/NamespaceTreeScannerTests.cs:69,91-92,113` currently assert German strings. Acceptance: make every scanner-authored public string English and update tests for success, truncation, and error paths.
- **P2 — `TotalTypes` becomes a partial total under depth truncation.** `NamespaceTreeScanner.cs:139-160` counts types in the current namespace but, at the effective depth, only checks whether descendants contain source types; it does not add those types to `totalTypes`. For `namespace A.B; public class T {}` with `MaxDepth=1`, the visible `A` node is retained and `maxDepth` is reported, while `TotalTypes` and the formatted summary report zero types. `docs/navigation/get-namespace-tree.md` qualifies `TotalNamespaces` as depth-limited but leaves `TotalTypes` unqualified. The existing deep-tree test at `NamespaceTreeScannerTests.cs:118-135` checks only displayed depth and truncation. Acceptance: define whether `TotalTypes` covers all matched source types or only represented levels, implement/report that meaning consistently, and test a type below the depth cap; keep the summary and documentation aligned.
- The 6.2 audit checkbox remains open pending these two fixes. No finding is raised for 6.1 or 6.3 in this point audit.

## Point 6.2 audit 1 remediation

- Scope: remediate the two P2 findings from the independent audit at commit `f7dd16a4d84bdb0bf285b593484d59a7c19d2f39`. The 6.2 audit checkbox remains open for follow-up; 6.3 was not changed.
- Before-fix focused repros: five of nine `NamespaceTreeScannerTests` failed. Assertions for success text, truncation guidance, node type labels, and unknown-project errors found German scanner output where English text was required. For `namespace A.B; public class T {}` with `MaxDepth=1`, the structured tree retained `A` and reported `maxDepth`, but `TotalTypes` and the formatted summary reported zero.
- All scanner-authored product strings are now English: compile and project errors, exception summaries, formatted totals, direct type labels, truncation guidance, and `NextAction`. Output contains no German scanner labels.
- `TotalTypes` now counts all named-namespace source types for the selected target, including types deeper than `MaxDepth` and namespaces hidden by the `MaxResults` projection. `NamespaceNode.TypeCount` continues to count only types declared directly in each returned namespace; therefore an ancestor can have `TypeCount=0` while its descendants contribute to the solution-wide `TotalTypes`. The current-state documentation states this distinction. FastTests cover the depth-1 `A.B` case, assert total types is one while direct `A.TypeCount` is zero, and verify English success, truncation, and error output.
- Follow-up independent audit has not been performed. The 6.2 audit checkbox remains open.

### Audit 1 remediation verification

| Gate | Result |
|---|---|
| Before-fix focused `NamespaceTreeScannerTests` | 5 expected failures, 4 passed |
| Focused `NamespaceTreeScannerTests` after fix | Passed, 9/9 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 405/405 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 417/417 across both test projects |

## Point 6.2 independent audit 2 of 3

- Reviewed commit: `9817c76daf9a65caace7b869d25fa9f57993cac8` (`gpt-6-sol`, medium), with a clean working tree before this documentation edit. Inspected the remediation diff, current scanner and models, the nine namespace FastTests, and current-state documentation. This auditor did not run product gates; the remediation record above reports the implementer's gate results.
- **P2 English public output — accepted.** `NamespaceTreeScanner.cs:41,58,77,282-328` now uses English for scanner-authored errors, `NextAction`, summary, truncation guidance, and direct type labels. `NamespaceTreeScannerTests.cs:69,91-92,113,139-174` verifies success, truncation, and unknown-project error strings. The scanner source has no remaining German string literals. Exception messages and user-provided names may retain their originating language; this is outside the scanner-authored-text finding.
- **P2 depth-independent `TotalTypes` — accepted.** At the depth boundary, `NamespaceTreeScanner.cs:152-177` counts source types in the omitted child hierarchy and adds them to `totalTypes`, while `nsTypeCounts` stores a zero-count visible prefix. The new depth-one test at `NamespaceTreeScannerTests.cs:140-157` establishes `TotalTypes=1`, direct `A.TypeCount=0`, and `maxDepth` for a type in `A.B`. `docs/navigation/get-namespace-tree.md` now states that `TotalTypes` covers named namespaces throughout the selected target while node counts remain direct.
- Both audit-1 findings meet their acceptance conditions; the 6.2 audit checkbox is complete. No new finding was identified in this targeted follow-up. Point 6.3 was outside scope.
