# M1 — References, context uses, and evidence

## Intent and scope

Remove duplicated impact navigation and make static evidence understandable. Implements P1 and [concept 5.1–5.2](../concept.md#51-reference-results-and-call-evidence). Depends on M0.

## Executable points

- [x] **M1.1 — Move useful impact summaries to references.** Add the optional summary over the existing discovered result set, with correct snapshot/partial semantics and stable totals across logical pages. Migrate substantive impact regressions, update consumers/follow-up hints/docs, and remove the old MCP registration and superseded paths after checking consumers. Acceptance: summary matches known sites and owner-qualified files/projects without a second traversal; no independent public impact route remains.
- [ ] **M1.2 — Rename context callers to uses and clarify graph evidence.** Update sections, scope argument, validation, formatting, advertised schemas/examples, and tests together. Preserve supported non-call/member evidence and distinguish graph nodes, sites, and static dispatch. Acceptance: replacement names work, old names are rejected as intended, and method-group/member uses are not presented as proven calls or runtime implementations.
- [ ] **M1.R — Independent Sol review, fixes, and acceptance.** Review contract fidelity, migrated regressions, paging/summary semantics, and useful output. Close only after required fixes and affected verification pass.

## Verification and non-goals

Use current reference/call-tree/context tests and existing source/assembly MCP contracts. Add focused summary-across-pages and evidence tests only where missing. Run official build and narrow affected tests for each code point; use relevant extended coverage when required. Keep all other capabilities and budgets. No risk score, changed-symbol analysis, Git integration, or general graph rewrite.

Existing starting points: [FindReferencesResolverTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/FindReferencesResolverTests.cs), [CrossFeatureRelationshipContractTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/CrossFeatureRelationshipContractTests.cs), and [SourceRelationshipToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceRelationshipToolsContractTests.cs). Re-home substantive old impact paging/equivalence assertions instead of retaining a removed route merely for its tests.

## Completion evidence

### M1.1 — verified implementation

The M1.1 implementation commit adds opt-in `find_references.includeSummary=false` to the existing source, own-assembly and bounded assembly-reference closure paths. Core aggregates the already discovered sorted reference sites before its display limit; the closure aggregates its deduplicated discovered sites before paging. There is no second semantic traversal. Summaries distinguish depth-one/deeper sites and retain sorted owner-qualified project/file identities, including distinct same-named source projects sharing one physical file and distinct DLL owners. Physical source project paths stay nullable; pathless projects have explicit snapshot-local project identities. Summary omissions match the surrounding traversal/closure omissions and exclude display-only paging. Each logical page repeats the same full summary; changing summary selection invalidates the cursor.

Removed public `get_impact`, Core `ImpactAnalyzer`/impact models, the assembly impact closure wrapper and formatter, and stale follow-up advertisements. SDK/host catalog assertions and current README/docs/navigation rule now describe sixteen tools. `get_context.callers` and its arguments remain for M1.2. `ExploreConsolidationBaseline` is unchanged, including W3/W4 queries and saved baseline relevance.

Substantive regression migration:

- `ImpactAnalyzerTests` is now `ReferenceSummaryTests`: cross-project chains and resolvable handoffs, duplicate/shared-path sites, result and node limits, requested/effective depth, cycles/shortest traversal, converging sites and reached-from identities. Summary completeness/omissions and totals before display limits are asserted.
- `FindReferencesResolverTests` retains same-named linked-project and unavailable-handoff assertions, with two owner-qualified project/file identities and deterministic order. `CrossFeatureRelationshipContractTests` retains per-site/evidence/column/provenance equivalence with and without summary plus recursion/graph assertions.
- `SourceRelationshipToolsContractTests.ReferencesSummary_ResultCursorReconstructsAllSitesAcrossPages` and `AssemblyToolsContractTests.AssemblyReferencesSummary_ResultCursorReconstructsAllSitesAcrossPages` retain full site reconstruction and stale/changed-query cursor cases and assert the identical full-query summary on every logical page. The root/bridge/leaf closure test additionally checks full summary repetition and distinct DLL owner identities across pages.
- `RelationshipToolsContractTests` migrates SDK required-symbol/removed-argument, source/assembly routing, default reference-owner selection, caller-to-body handoff, exact budget recovery and outer-page reconstruction assertions. It also verifies optional summary/default schema and absent summary by default. All sixteen-route host/SDK expectations and retained E2E callers compile against references plus summary; E2E flows were not run in this slice.

Actual official verification (all successful final runs):

- `pwsh -File ./scripts/build.ps1`: zero warnings/errors, 5.51 seconds; `temp/m1-evidence/build-final.log`.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~ReferenceSummaryTests|FullyQualifiedName~FindReferencesResolverTests|FullyQualifiedName~CrossFeatureRelationshipContractTests|FullyQualifiedName~McpInputSchemaTests'`: 38 passed; `temp/m1-evidence/fast-final.log`, `temp/m1-evidence/fast-final.trx`.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~RelationshipToolsContractTests|FullyQualifiedName~SourceRelationshipToolsContractTests.FindReferences_|FullyQualifiedName~SourceRelationshipToolsContractTests.ReferencesSummary_|FullyQualifiedName~SourceRelationshipToolsContractTests.RelationshipScopesFilter|FullyQualifiedName~SourceRelationshipToolsContractTests.SourceRelationshipHandlers|FullyQualifiedName~AssemblyReferencesSummary_|FullyQualifiedName~AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSixteenRoutes|FullyQualifiedName~AssemblyCallTree_PreservesRecursive|FullyQualifiedName~AssemblyReferenceClosureHandsOffAcrossRootBridgeAndLeafOwners|FullyQualifiedName~IndexScopeContractTests.GetIndexScope_SdkDefinition'`: 14 passed, 31 seconds; `temp/m1-evidence/integration-final.log`, `temp/m1-evidence/integration-final.trx`.
- `pwsh -File ./scripts/test-integration.ps1 -IncludeExtended -Filter 'FullyQualifiedName~MSBuildSolutionLoader_ColdSolutionsWithSameNamedProjectsHaveIsolatedSnapshots|FullyQualifiedName~MSBuildSolutionLoader_CustomTargetsAndScratchCleanupPreserveWorkspaceSnapshot'`: both selected extended workspace-owner/snapshot/read-only checks passed, 2 seconds; `temp/m1-evidence/extended-final.log`, `temp/m1-evidence/extended-final.trx`. These are the two actual ExtendedIntegration tests in current sources; no entire suite was selected.

An initial narrow integration run had 10 passed/4 migration failures: legacy first-page-only JSON assumptions and a comparison of different selector spellings in response headers. Preserved at `temp/m1-evidence/integration-first.log`/`.trx`. Existing outer-page reconstruction helpers now preserve complete payload/handoff/budget assertions, and selector-equivalence compares complete domain payloads. Source project facts remain internal to reference-site serialization so default reference output is unchanged. No test category, timeout, or navigation assertion was weakened. No verification stall occurred; runs were monitored below one minute. No baseline rerun, complete solution gate, transport smoke or performance series was run here.

Diff reviewed and `git diff --check` passed before the explicit-path commit. M1.2, M1.R and parent M1 remain open; next executable point is M1.2.
