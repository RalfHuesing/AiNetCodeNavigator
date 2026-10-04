# M3 — Discovery and metadata ownership

## Intent and scope

Improve declaration discovery and source-to-metadata transitions with exact ownership. Implements P3 and [concept 5.4–5.5](../concept.md#54-type-relationships-and-metadata-contracts). Depends on M2; ends with twelve public tools.

## Executable points

- [ ] **M3.1 — Integrate extension declarations and discovery filters.** Extend `find_symbol` for source/assembly extensions, project/namespace/signature filters, and declared receiver matching. Preserve existing discovery/batching and old extension-search capability. Migrate regressions and consumers/docs; remove `find_assembly_extensions` after replacement checks pass. Acceptance: filtered results retain actual extension status, correct owners and usable references; ineffective/invalid argument combinations fail; no expression-applicability promise.
- [ ] **M3.2 — Resolve metadata contracts in source type relationships.** Use existing loaded compilation/reference facts for qualified metadata types and exact member IDs, including explicit validated metadata-owner selection. Preserve source-first resolution and ambiguity semantics. Acceptance: BCL/interface-member implementations and overrides bind semantically; duplicate owners do not get guessed; returned source/assembly references support exact owner-qualified follow-ups; arbitrary DLL paths are not loaded by this selector.
- [ ] **M3.R — Independent Sol review, fixes, and acceptance.** Review filter comparison behavior, source-first/metadata fallback, overload/owner mapping, assembly closure limitations, advertised follow-ups, and twelve-tool catalog. Close after required fixes/checks pass.

## Verification and non-goals

Reuse discovery/extension, stable-reference/owner, and relationship fixtures/contracts. Add small metadata member/competing-owner cases where existing samples lack the required behavior; check W5 and W7 follow-ups with known results. Run official build, narrow affected tests, and required relevant extended ownership/relationship coverage. No new metadata index, arbitrary reference closure, expression completion, dynamic dispatch simulation, or owner-reference grammar change.

Existing starting points: [AssemblyNavigationScannerTests](../../../tests/AiNetCodeNavigator.FastTests/Assemblies/AssemblyNavigationScannerTests.cs), [AssemblyToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/AssemblyToolsContractTests.cs), [FindReferencesResolverTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/FindReferencesResolverTests.cs), and [SourceRelationshipToolsContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceRelationshipToolsContractTests.cs). Inspect their current coverage before adding the genuinely new metadata-selector cases.

## Completion evidence

Not started. Record commits, current test mapping/commands/results, owner/metadata and extension follow-up artifacts, and review/fix disposition here.
