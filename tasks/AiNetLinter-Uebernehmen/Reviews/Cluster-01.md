# Cluster 1 Implementation Review

## Point 1.1: TestKit Foundation

- Implementation base: `cb7c555611d95adc91f0bc776e609c715ee9176c` (working tree was clean).
- Implementer comparison: AiNetLinter's `RoslynTestSolutionFactory` and `RoslynTestSolutionFactoryTests` were inspected read-only through the AiNetLinter navigation MCP. Its factory establishes the matching baseline: cached BCL metadata references, multi-project `AdhocWorkspace` solutions, project references, nullable and preprocessor options, virtual paths, and disposal through a solution handle.
- Existing AiNetCodeNavigator support already supplied the builder, fixtures, and basic assertions. Its tests only checked a one-project solution, one resolved type, two fixture types, and handoff ID examples; they did not prove the broader advertised behavior.
- Changes: reject duplicate project names, malformed project/document inputs, duplicate project references, and unresolved project references before creating a workspace; dispose the workspace if solution construction fails. Expand TestKit tests to cover syntax trees, valid compilations, cross-project symbol resolution, nullable and preprocessor options, metadata-reference reuse, virtual paths without filesystem writes, fixture semantics, and assertion helper contracts.
- Current-state documentation updated: [development/build-and-tests.md](../../../docs/development/build-and-tests.md).

### Verification

All official gates were executed:

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| Focused TestKit tests (`dotnet test ... --filter FullyQualifiedName~TestKitInfrastructureTests`) | Passed, 16/16 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 4/4 |
| `pwsh -File ./scripts/test.ps1` | Passed, 191/191 across both test projects |
| `pwsh -File ./scripts/test-fast.ps1` | Passed on the final run, 187/187 |
| `git diff --check` | Passed |

Two earlier standalone FastTests runs failed in the unrelated `HandoffHandleRegistryTests.ConcurrentRequests_AreThreadSafeAndDeduplicated` test (`19` unique IDs observed, `20` expected). The full suite and a subsequent standalone FastTests run both passed. This transient failure is recorded for the point auditor to assess.

### Independent audit

- Audit count: 1 of 3. Reviewed commit `ad26ff0709e39aae36f9a881d11932b45eeb0bbe` with `gpt-6-sol` (medium). The working tree was clean before this documentation edit.
- Scope: point 1.1, its fast tests and public TestKit contracts, the concept and code map, and AiNetLinter's `RoslynTestSolutionFactory` plus its platform contract tests (read-only). The reference factory also leaves the returned immutable solution separate from `Workspace.CurrentSolution`; this is a behavior to correct in the adapted TestKit, not a reason to accept it.
- **P2 — Handoff assertion accepts invalid product handles.** `tests/AiNetCodeNavigator.TestKit/Assertions/NavigationAssertions.cs:17` permits `_` and `-`; `tests/AiNetCodeNavigator.FastTests/TestKit/TestKitInfrastructureTests.cs:181-182` explicitly marks both forms valid. `src/AiNetCodeNavigator.Core/Symbols/HandoffCounterAlphabet.cs:12-31` permits only ASCII letters and digits, and `IsValidHandle` uses that alphabet. Therefore `AssertValidHandoffId("h:foo_bar")` succeeds for a handle the product rejects. Align the assertion with the product validator and test both accepted and rejected boundary cases.
- **P2 — Exposed workspace does not contain the returned projects.** `tests/AiNetCodeNavigator.TestKit/Builders/TestWorkspaceBuilder.cs:93-125` starts with an `AdhocWorkspace`, then calls immutable `Solution.AddProject` and `Solution.AddProjectReference` without applying that solution to the workspace. The handle exposes both values at lines 29-31, but its `Workspace.CurrentSolution` remains empty (or contains only the empty virtual solution), while `Solution` contains the projects. The current tests at `tests/AiNetCodeNavigator.FastTests/TestKit/TestKitInfrastructureTests.cs:26-29` and `:51-53` inspect only `handle.Solution`, so this divergence is uncovered. Make both exposed views consistent and add a regression check with projects, documents and references.
- **P3 — Error-path assertions are narrower than the new validation.** `tests/AiNetCodeNavigator.TestKit/Builders/TestWorkspaceBuilder.cs:134-186` adds rejection for null specs, malformed document names/content, null metadata references, duplicate and unknown project references. `tests/AiNetCodeNavigator.FastTests/TestKit/TestKitInfrastructureTests.cs:107-125` checks only duplicate project names and an unknown reference. Add focused tests for the other public input failure cases, including the fluent entry points, to substantiate the claimed validation.
- Acceptance: fix both P2 findings and add the stated failure-path tests; rerun the affected official gates before marking the point's audit checkbox complete. No product code was changed by this audit.
- Gate evidence: the implementation review above records build, integration, full suite and final fast suite as passed on the implementation turn, with two earlier transient failures in `HandoffHandleRegistryTests.ConcurrentRequests_AreThreadSafeAndDeduplicated`. This auditor did not run a build or tests and does not independently certify those results. The transient failure is outside point 1.1 and remains a separate observation for the orchestrator.
- The Cluster 1 checklist's audit checkbox remains open while these findings are pending.
- No 1.2 or 1.3 implementation work was included in this slice.
