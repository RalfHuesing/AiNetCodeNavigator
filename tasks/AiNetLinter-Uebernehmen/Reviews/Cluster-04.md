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

## Independent audit 1/3 of point 4.1

- Reviewed commit: `bc1da65c893eb3686b7205e2660950bd016a1c9a` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Read-only comparison: AiNetLinter `FindSymbolScanner`, `SymbolKindClassifier`, `McpScopeClassifier`, and their relevant FastTests. The project/document scope fix and the existing exact name, wildcard, class/method, multi-project, and truncation tests support the checked implementation items. This review did not run a build or tests; the implementation gates above belong to the earlier slice.

### Open findings

1. **P2 — The kind filter does not cover the reference's public vocabulary.** `FindSymbolModels.cs:17-29` has no `delegate`, `record class`, or `record struct` values. `FindSymbolScanner.cs:222-236` also admits record structs for plain `struct`, while the reference `SymbolKindClassifier.MatchesTypeKind` excludes records from plain `class`/`struct` and recognizes the specific record kinds and delegates. The local kind test (`FindSymbolScannerTests.cs:262-275`) covers only class versus method. **Acceptance:** expose and correctly filter all reference kind values, preserve distinct record class/struct results, and test positive and mismatched cases, including a record struct excluded by plain `struct`.
2. **P2 — Generated declarations are returned by default with no opt-in control.** `FindSymbolScanRequest` (`FindSymbolModels.cs:49-56`) has no `IncludeGenerated` option, and `FindSymbolScanner.CollectVisibleLocations` (`FindSymbolScanner.cs:158-185`) admits every source location that passes the project scope. The reference `TryCreateVisibleLocationAsync` excludes `McpSourceKind.Generated` unless `IncludeGenerated` is true. The local tests have no generated-file or generated-header case. **Acceptance:** classify generated locations using the reference conventions, exclude them by default for `all`, `production`, and `tests`, allow explicit inclusion, and cover both generated path/header and a declaration with generated and editable locations.

The point 4.1 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.
