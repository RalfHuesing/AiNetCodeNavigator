# Dependency collection and traversal

Status: specified for implementation. Execution: [R04–R06](roadmap.md#r04--collect-each-document-batch-once). Cache ownership and bounds: [specification 04](04-snapshot-refresh-and-analysis-cache.md).

## Required separation

Separate semantic collection from graph projection. Collection produces immutable raw type-reference edges, project-reference facts, exact ownership and collection coverage/errors. Projection merges those facts, applies root selection/direction/depth/limits, derives project/namespace/file summaries, orders results and renders the existing payload.

For one collection job, each selected `(ProjectId, DocumentId)` is semantically collected at most once. Linked physical files in different projects are different semantic documents. Compilations are acquired once per participating project within that job. Paging offsets, output budgets and different traversals over collected input must never re-enter semantic collection.

Internal scanner document batches retain the current maximum batch size and deterministic document order. Relationship pages are projections of one batch result, not independent scans. Drain all required document batches before claiming full incoming/both coverage, including roots after the first 1,000 documents. Internal document batching is not a new public result cursor.

Keep public type-oriented semantics: selecting a member selects its containing type. Do not turn this feature into a member call graph. Preserve the existing source type-reference evidence model, source ownership, source/generated filters and unsupported/unresolved evidence behavior. Assembly scanning uses the shared separated collector/projector where applicable; assembly demand-driven traversal and new assembly dependency caches are outside this plan.

## Collection result and coverage

The Core result must distinguish:

- Exact immutable source snapshot/context, scope and generated-inclusion selection.
- Eligible semantic document identities and attempted/successful collection identities.
- Raw edges keyed by exact source/destination type ownership, with existing file/namespace/project evidence.
- Recoverable per-document errors, cancellations and actual uncollected coverage.
- Full selected-document coverage versus selected outgoing-root coverage.

Use the same deterministic duplicate-edge merge for broad and targeted paths. For equal type-edge keys, select representative file evidence by the common collector's deterministic document ordering, independent of task completion order. Do not merge same-named declarations from different projects. Existing graph type keys identify relationship evidence and are not accepted as generated navigation references; visible nodes receive their navigable references according to specification 01.

Recoverable collection errors remain visible in omissions and existing error payloads. Cancellation is cancellation, not a successful partial graph. A failed attempt must not mark a document successfully collected.

## Broad incoming and both analysis

For `incoming` or `both`, obtain full raw selected-document coverage, reusing eligible completed collection data. Traverse after that collection. A partial targeted bucket cannot establish absence of incoming edges.

All required scope/generated filters, document-limit signals, errors, depth/node clamps, hidden relationships and continuation-input completeness remain truthful. Output/result-page truncation is separate from semantic coverage. Existing `maxResults` limits a projection; increasing a response budget does not collect more documents.

There is no new dedicated incoming index. Any derived adjacency lookup is a projection helper within the existing immutable collection, rebuilt from retained facts as needed.

## Targeted outgoing analysis

When a full completed collection is available, traverse it directly. Otherwise:

1. Resolve the exact root type, or all named type declarations in the selected file including nested types/delegates. A file linked into several projects retains its current explicit ambiguity behavior; use a uniquely owned symbol instead.
2. Include all declaration documents for each root's original type, including partial declarations outside the selected file. Apply source scope and generated filters before collection.
3. Collect each required semantic document once, retaining its full raw document facts. For traversal, select only edges whose source type is in the current frontier; unrelated types sharing the document do not become traversal roots.
4. Use breadth-first expansion of exact source type identities. Collect a destination's declaration documents only when its distance is less than the effective requested depth and its outgoing edges are needed. Depth 1 collects roots' documents only. Cycles and repeated partial/linked documents do not trigger repeated collection within an owner.
5. For each expansion, use the same edge ordering, scope semantics and effective node/depth limits as broad traversal. Respect existing limits before scheduling unnecessary next-frontier document work.
6. Derive the existing project/namespace/file summaries from the selected traversed edges and existing project-reference semantics, exactly as the broad projection for that query does.

Compilation may still require project/reference-wide work; the guarantee concerns dependency document scans, not zero background compilation. Metadata/external types retain existing evidence rules and are not silently added as source roots.

A successfully expanded type whose every eligible declaration document was collected can establish its outgoing edges at that requested traversal position. This never means full-solution incoming coverage. Eligible-document totals and scanned-document counters report actual coverage, not an invented whole-solution scan. Do not report a document-limit omission merely because unrelated documents were intentionally unnecessary; report real omitted required work, errors and existing traversal limits.

If a required declaration's owner/documents cannot be proven, preserve an explicit analysis omission and actionable recovery. Do not silently suppress evidence or switch to display-name selection.

## Reuse

All reuse follows specification 04. Broad queries can fill a partially collected bucket by collecting missing documents. Targeted queries can reuse successfully collected document facts from earlier queries in that same bucket. A different root, direction, depth or `maxResults` uses a new projection, not a new cache identity.

A completed full graph can serve different outgoing/incoming/both projections. Failed documents may be retried in a later fresh request; no failure is cached as proof of no dependencies. No collection survives into a changed source snapshot.

## Acceptance and performance evidence

Use a deterministic multi-project fixture with more than 1,000 documents, several relationship pages in one document batch, roots near the end, partial types, linked files, duplicate names, cycles, scope/generated exclusions, project references and recoverable collection failures.

Compare broad and targeted results at multiple depths by exact type/project identity, representative file evidence, ordering, totals and semantic omissions. Physical scanned-document counts legitimately differ. Verify unrelated declarations in a root document do not expand the root set and partial declarations contribute all eligible edges.

Instrument semantic collection at its owner in tests. Required performance outcomes are structural:

- R04: one semantic document collection per selected document per job regardless of internal relationship pages.
- R05: a warm unchanged request whose required successful facts remain resident performs zero additional semantic collections; concurrent identical collection needs do not duplicate work. Expiry, eviction or oversized non-admitted facts may require fresh work under specification 04.
- R06: a cold depth-1 outgoing query collects exactly its required eligible root declaration documents, fewer than a full scan in the fixture, with identical dependency evidence.

Record refresh, identity, compilation, semantic collection, projection and end-to-end durations separately. Use one untimed setup plus five measured runs for each cold/warm/broad/targeted scenario on the same build/machine/fixture; cold means dependency cache empty, warm means the prior collection remains resident. Report median, range, document/edge counts and cache state. Wall-clock numbers are evidence, not an invented universal latency SLA. No correctness limit may be weakened to improve the measurements.

## Inspected entry points

[DependencyGraphScanner](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs) currently collects before producing each page. [RelationshipTools](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs) drains those pages and document batches, then merges/traverses. [Models](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphModels.cs) and [traversal](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphTraversal.cs) own the current payload and graph semantics.
