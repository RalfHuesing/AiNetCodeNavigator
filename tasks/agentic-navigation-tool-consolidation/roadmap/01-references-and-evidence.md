# M1 — References, context uses, and evidence

## Intent and scope

Remove duplicated impact navigation and make static evidence understandable. Implements P1 and [concept 5.1–5.2](../concept.md#51-reference-results-and-call-evidence). Depends on M0.

## Executable points

- [ ] **M1.1 — Move useful impact summaries to references.** Add the optional summary over the existing discovered result set, with correct snapshot/partial semantics and stable totals across logical pages. Migrate substantive impact regressions, update consumers/follow-up hints/docs, and remove the old MCP registration and superseded paths after checking consumers. Acceptance: summary matches known sites and owner-qualified files/projects without a second traversal; no independent public impact route remains.
- [ ] **M1.2 — Rename context callers to uses and clarify graph evidence.** Update sections, scope argument, validation, formatting, advertised schemas/examples, and tests together. Preserve supported non-call/member evidence and distinguish graph nodes, sites, and static dispatch. Acceptance: replacement names work, old names are rejected as intended, and method-group/member uses are not presented as proven calls or runtime implementations.
- [ ] **M1.R — Independent Sol review, fixes, and acceptance.** Review contract fidelity, migrated regressions, paging/summary semantics, and useful output. Close only after required fixes and affected verification pass.

## Verification and non-goals

Use current reference/call-tree/context tests and existing source/assembly MCP contracts. Add focused summary-across-pages and evidence tests only where missing. Run official build and narrow affected tests for each code point; use relevant extended coverage when required. Keep all other capabilities and budgets. No risk score, changed-symbol analysis, Git integration, or general graph rewrite.

Existing starting points: [FindReferencesResolverTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/FindReferencesResolverTests.cs), [CrossFeatureRelationshipContractTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/CrossFeatureRelationshipContractTests.cs), and [SourceRelationshipToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceRelationshipToolsContractTests.cs). Re-home substantive old impact paging/equivalence assertions instead of retaining a removed route merely for its tests.

## Completion evidence

Not started. Record implementation commits, migrated assertion locations, actual commands/results, representative output or Exploration artifacts, and review/fix disposition here.
