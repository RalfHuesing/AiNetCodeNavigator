# Cluster 4 Review Log

## Point 4.1 implementation

- Base commit: `d821f8bc89e60c3bd0b1d75e4ac7eddf60a9752a`; the working tree was clean before this slice.
- Compared `FindSymbolScanner` and its FastTests in AiNetCodeNavigator with AiNetLinter's `FindSymbolScanner`, `McpScopeClassifier`, and classifier tests through read-only AiNetLinter MCP navigation. Name and wildcard matching and `SymbolKind` filtering were present. AiNetCodeNavigator filtered scope using `TestDetector.IsTestSymbol`, while the reference classifies each source location from its project and document.
- **P2 implementation finding resolved — project scope was inferred from symbol/file names.** A plain `SharedType` declaration in a project named `App.Tests`, with a non-test path and filename, appeared in both `production` and `tests` results. The new regression initially failed with two production entries. The scanner now classifies each source location using its Roslyn project and document path, then applies `all`, `production`, or `tests` to those locations. The regression verifies separate same-named declarations in production and test projects, plus inclusion in `all`.
- Existing FastTests cover exact-name and wildcard matching and class/method kind filtering. No changes were needed for those filters.
- Added current-state documentation at `docs/navigation/find-symbol.md` and linked it from `docs/README.md`.
- Point audit remains open as requested. This implementation record is not an independent audit; the checkbox in `Clusters/Cluster-04.md` remains `[ ]`.

### Verification

| Gate | Result |
|---|---|
| Regression before fix | Failed as expected: production scope returned both projects |
| Focused regression after fix | Passed, 1/1 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 268/268 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 280/280 across both test projects |
| `git diff --check` | Passed before commit |

## Point 4.1 audit finding fixes

- Base commit: `71c9e5732eac76b4af111887739e163ac3648971`; working tree was clean before this slice.
- Read-only AiNetLinter MCP inspection covered `SymbolKindClassifier`, `FindSymbolScanner`, `McpScopeClassifier`, `GeneratedCodeDetector`, and the scope classifier tests. The reference separates delegates and record classes/structs from plain classes/structs and filters generated source per location, with generated inclusion disabled by default.
- **Kind filter gaps fixed.** The previous `struct` filter also returned record structs, and the request vocabulary lacked `delegate`, `record class`, and `record struct`. Added these filters, made plain `struct` exclude record structs, and made the entry kind identify record classes and record structs separately. A regression failed on the original implementation because it returned both a plain struct and a record struct for the `struct` query; new coverage checks delegate and all record query forms.
- **Generated source contract added.** `FindSymbolScanRequest.IncludeGenerated` defaults to `false`. The scanner filters source locations by project scope and generated status. It recognizes `obj/`, `.g.cs`, `.g.i.cs`, `.generated.cs`, `.designer.cs`, auto-generated headers within the first five lines, and `GeneratedCodeAttribute`. `IncludeGenerated: true` enables those locations under `all`, `production`, or `tests`. A mixed partial declaration retains only its editable location by default and both locations when generated code is requested. Tests exercise path, header, attribute, production/test scopes, and mixed declarations.
- Updated the current-state Find Symbol page and Cluster 4 implementation checklist. The independent point audit checkbox remains `[ ]`; this fix record is not the independent audit.

### Verification

| Gate | Result |
|---|---|
| Regression before fix: plain `struct` filter | Failed as expected: also returned a record struct |
| Focused FindSymbolScanner FastTests after fix | Passed, 11/11 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 271/271 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 283/283 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 4.1

- Reviewed commit: `bc1da65c893eb3686b7205e2660950bd016a1c9a` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Read-only comparison: AiNetLinter `FindSymbolScanner`, `SymbolKindClassifier`, `McpScopeClassifier`, and their relevant FastTests. The project/document scope fix and the existing exact name, wildcard, class/method, multi-project, and truncation tests support the checked implementation items. This review did not run a build or tests; the implementation gates above belong to the earlier slice.

### Open findings

1. **P2 — The kind filter does not cover the reference's public vocabulary.** `FindSymbolModels.cs:17-29` has no `delegate`, `record class`, or `record struct` values. `FindSymbolScanner.cs:222-236` also admits record structs for plain `struct`, while the reference `SymbolKindClassifier.MatchesTypeKind` excludes records from plain `class`/`struct` and recognizes the specific record kinds and delegates. The local kind test (`FindSymbolScannerTests.cs:262-275`) covers only class versus method. **Acceptance:** expose and correctly filter all reference kind values, preserve distinct record class/struct results, and test positive and mismatched cases, including a record struct excluded by plain `struct`.
2. **P2 — Generated declarations are returned by default with no opt-in control.** `FindSymbolScanRequest` (`FindSymbolModels.cs:49-56`) has no `IncludeGenerated` option, and `FindSymbolScanner.CollectVisibleLocations` (`FindSymbolScanner.cs:158-185`) admits every source location that passes the project scope. The reference `TryCreateVisibleLocationAsync` excludes `McpSourceKind.Generated` unless `IncludeGenerated` is true. The local tests have no generated-file or generated-header case. **Acceptance:** classify generated locations using the reference conventions, exclude them by default for `all`, `production`, and `tests`, allow explicit inclusion, and cover both generated path/header and a declaration with generated and editable locations.

The point 4.1 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.

## Independent audit 2/3 of point 4.1

- Reviewed commit: `ce047c598178beb5c0824faa668523fe88af6bae` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Read-only comparison: AiNetLinter `SymbolKindClassifier.MatchesTypeKind`, `FindSymbolScanner.TryCreateVisibleLocationAsync`, `McpScopeClassifier.ClassifySourceKindAsync`, and `GeneratedCodeDetector.IsGenerated`. The second implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 kind vocabulary — closed.** `FindSymbolModels.cs` now includes delegate and both specific record forms. `FindSymbolScanner.FilterByKind` distinguishes plain class/struct from record class/struct, while generic record includes both. `DescribeKind` returns the specific record form. New FastTests check delegate, generic and specific record results, mismatches, and record struct exclusion from plain struct. The plain class branch also explicitly excludes records. No kind-filter regression was found in the reviewed paths.
2. **P2 generated-source filtering — closed.** `FindSymbolScanRequest.IncludeGenerated` defaults to `false`. `CollectVisibleLocationsAsync` filters each source location after project/document scope classification. `IsGeneratedDocumentAsync` matches the reference's path suffixes, `obj/` segment, first-five-line header, and declared-symbol attribute checks. New FastTests cover generated path variants, header, attribute, explicit inclusion in production and test scopes, and an editable/generated partial declaration. The existing scope and multi-project tests remain present.

No open finding remains for point 4.1 on this reviewed Core implementation. The audit checkbox is complete after two audits. The end-to-end MCP contract remains subject to Cluster 11 verification.
