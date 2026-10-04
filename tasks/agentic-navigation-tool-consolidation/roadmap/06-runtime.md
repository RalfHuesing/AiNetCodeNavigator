# M6 — Runtime findings and capacity recovery

## Intent and scope

Remove demonstrated waste and make bounded concurrency recoverable. Implements P6 and [concept sections 6 and 8.2](../concept.md#6-performance-compactness-and-concurrency). Depends on M5, except earlier blocking corrections already owned by M0 or the affected milestone.

## Executable points

- [ ] **M6.1 — Correct reproduced runtime causes.** Review M0 findings and any measured implementation regressions against current code. Identify load/freshness/identity/traversal/formatting cost, then apply the smallest justified correction in its existing owner. Acceptance: each concrete finding is corrected and checked, closed as non-reproducing with evidence, or left explicitly unresolved; no speculative cache or speedup claim. Record comparable affected measurements and functional/root-cause evidence under section 8.2.
- [ ] **M6.2 — Preserve admission, cancellation, and recovery.** Implement any remaining required saturation envelope/delay changes and verify admitted/rejected work, temporary reservations, polling cancellation, capacity release and retry. Preserve budgets/leases/snapshot freshness. Acceptance: the focused five-requests/four-slots case recovers without leaked work/tokens, queue, or automatic server retry; existing lifetime/freshness regressions remain intact. Reuse previously completed corrections and tests instead of repeating them.
- [ ] **M6.R — Independent Sol review, fixes, and acceptance.** Review demonstrated benefit, simple ownership, existing caching reuse, freshness/lifetime safety, and saturation recovery evidence. Close after required fixes/checks pass; the absence of a reproduced hotspot is not a reason to invent one.

## Verification and non-goals

Official build and narrow affected tests per production correction, selected extended shared-host/workspace/response tests when affected. Preserve same-timestamp/source-generator/replaced-binary regressions; their rarity is not permission to delete them. Measure only corrected workflows, normally three warm runs per build, separating cold load. No whole-catalog repeated benchmark, soak test, arbitrary limit increase, new storage/index/scheduler framework, or blind rerun after inconclusive timing.

Existing starting points: [Workspace freshness tests](../../../tests/AiNetCodeNavigator.FastTests/Workspace/), [StableSymbolReferenceTests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/StableSymbolReferenceTests.cs), [LongRunningToolCallStoreTests](../../../tests/AiNetCodeNavigator.FastTests/Mcp/LongRunningToolCallStoreTests.cs), [StableReferenceResolutionContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/StableReferenceResolutionContractTests.cs), and [AssemblyCallTreeOwnerContractTests](../../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/AssemblyCallTreeOwnerContractTests.cs). Select the coverage actually affected by each correction; this list does not mandate running all classes per point.

## Completion evidence

Not started. Record finding IDs and disposition, owners/corrections/commits, samples/measurements with build identity, commands/results, and review/fix disposition here. Link earlier blocking fixes rather than duplicating work.
