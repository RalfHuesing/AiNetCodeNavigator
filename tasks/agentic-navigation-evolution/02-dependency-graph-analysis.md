# Dependency graph analysis

## Current discussion priority, 2026-10-03

After the handle-removal and unchanged-output decisions, the next recommended substantive improvement is eliminating repeated semantic collection for internal relationship pages. First establish a focused reproduction and verify one collection per document batch. Discuss broader caching and targeted traversal afterward. This is a recommendation pending user decision, not a completed optimization. Preserve the output presentation selected in [topic 07](07-output-format-and-token-efficiency.md).

## Verified current behavior

[RelationshipTools.ScanSourceDependencyGraphAcrossDocumentsAsync](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs) drains document windows and relationship windows through the Core scanner, then applies the requested traversal with `MergeAndTraverse`. Target selectors are cleared during the collection stage. Consequently, small `depth`, `maxResults`, `filePath` and `direction: outgoing` do not currently bound the initial source collection to the selected neighborhood.

For each relationship offset, the handler calls [DependencyGraphScanner.ScanSolutionAsync](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs) again. That method selects documents and collects semantic type references before projecting the relationship page. The same document batch can therefore be scanned repeatedly when it produces multiple relationship pages. The compilation cache inside collection is scoped to one scanner invocation.

The public graph is type-oriented: selecting a member chooses its containing type. It does not promise a member-specific dependency graph. Existing exact project/type identities, scope/generated filters, partial-declaration handling and completeness semantics need to be retained by any optimization.

## Interpretation of the audit

The large-target request was already `depth: 1` and `maxResults: 20`. It returned running controls before an ambiguous EOF/timeout observation. This establishes a slow/incomplete workflow, not the exact performance bottleneck or transport failure cause. Switching to `outgoing` or a file selector is not a demonstrated collection-cost fix in the inspected implementation.

## Proposed sequence

1. Collect each document batch once and project all needed relationship pages from that collected result.
2. Reuse collected relationships for subsequent queries against the same immutable snapshot and scope.
3. Add a targeted outgoing path using all relevant declaration documents, including partial declarations. Only expand to further declarations when the requested depth requires them.
4. Evaluate a reusable incoming-edge index with explicit coverage and invalidation rules.

Cache design is shared with [topic 04](04-snapshot-refresh-and-analysis-cache.md). Transport diagnosis remains [topic 03](03-long-running-operations-and-transport.md).

## Open decisions and verification

- Establish the cost breakdown before attributing all latency to repeated collection.
- Prove that projection paging does not trigger additional semantic scans for unchanged collected input.
- Compare relationship identities and completeness against the existing broad implementation, including roots after the first 1000 documents.
- Preserve same-named project/type separation, source ownership, scope/generated filtering, traversal bounds, cancellation and recoverable scanner errors.
- Incoming-index coverage must be explicit; an incomplete index cannot imply no incoming dependencies.
- Performance criteria require controlled cold/warm measurements, not only a smaller first output page.
