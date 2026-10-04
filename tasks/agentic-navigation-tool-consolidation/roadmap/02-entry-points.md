# M2 — Consolidated navigation entry points

## Intent and scope

Consolidate overlapping entry points while preserving useful detail. Implements P2 and [concept 5.2–5.4](../concept.md#52-symbol-context). Depends on M1; ends with thirteen public tools, before the extension merge.

## Executable points

- [ ] **M2.1 — Preserve class structure through context members.** Reuse the existing scanner for complete member metadata, filters, sort/scope behavior, and paging; preserve other sections' independent scope/status. Migrate substantive tests, update consumers/docs, then remove `get_class_structure` and dead wrappers. Acceptance: full requested members remain reachable, type-only validation is correct, and unselected sections do not run.
- [ ] **M2.2 — Merge source scope and namespace browsing.** Introduce explicit `browse_target` views using existing scanners and view-specific validation/cursors. Replace registrations and consumer hints/docs. Acceptance: each view preserves its former inventory behavior and never invokes the other view's scanner; loaded scope/framework/generated distinctions remain truthful.
- [ ] **M2.3 — Merge hierarchy and implementation entry points.** Introduce explicit `get_type_relations` modes with the current source/assembly root support, mapping/override behavior, filters, and paging. Replace old registrations and consumers/docs. Acceptance: unsupported roots/mode arguments fail clearly; only the requested scanner runs. New source metadata selectors are implemented in M3, not guessed here.
- [ ] **M2.R — Independent Sol review, fixes, and acceptance.** Check all replacements and migrated regressions, SDK schemas, defaults, section/view/mode isolation, and thirteen-tool catalog. Close only after required fixes/checks pass.

## Verification and non-goals

Reuse class-structure, index-scope, namespace-tree, hierarchy, implementation and MCP context/catalog tests identified in M0. Check representative normal, filtered, paged, and invalid-view cases; retain established ownership/generated regressions without a new combination matrix. Official build and affected tests per code point; relevant selected extended tests per rules. No generic dispatcher/provider registry or universal graph engine; independent batched body/skeleton tools stay.

Existing starting points: [SourceToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceToolsContractTests.cs) for section/cursor/snapshot isolation and inventory coverage, [ClassStructureScannerTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/ClassStructureScannerTests.cs), [TypeHierarchyTests](../../../tests/AiNetCodeNavigator.FastTests/Hierarchy/TypeHierarchyTests.cs), and the [FileStructure tests](../../../tests/AiNetCodeNavigator.FastTests/FileStructure/).

## Completion evidence

Not started. Record commits, former-to-new regression coverage, commands/results, inspected output, and review/fix disposition here.
