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
