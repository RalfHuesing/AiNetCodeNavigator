# Dependency collection and traversal

Status: implemented and verified through R04–R06; see their executed gates, work-count evidence and independent audits. Expanded measurement matrices remain explicitly deferred as F03 in [findings.md](findings.md). Execution: [R04](roadmap/R04-single-collection.md), [R05](roadmap/R05-dependency-cache.md) and [R06](roadmap/R06-targeted-outgoing.md). Cache ownership and bounds: [specification 04](04-snapshot-refresh-and-analysis-cache.md).

## Required separation

Separate semantic collection from graph projection. Collection produces immutable raw type-reference edges, project-reference facts, exact ownership and collection coverage/errors. Projection merges those facts, applies root selection/direction/depth/limits, derives project/namespace/file summaries, orders results and renders the existing payload.

For one collection job, each selected `(ProjectId, DocumentId)` is semantically collected at most once. Linked physical files in different projects are different semantic documents. Compilations are acquired once per participating project within that job. Paging offsets, output budgets and different traversals over collected input must never re-enter semantic collection.

Internal scanner batches contain at most the existing 1,000 documents. A drained batch is not an overall document cap; public source incoming/both queries drain all required batches. Relationship pages only project a collected batch. Internal batching adds no public cursor.

Canonical comparison paths use `Path.GetFullPath`, `/` separators, Windows invariant uppercase for physical identity on Windows and unchanged case elsewhere, without symlink or Unicode normalization. Order documents ordinal by (canonical owner project path, owner context fingerprint from specification 04, canonical document path or empty, document name, folders in their stored order, SourceCodeKind). Workspace-local ProjectId/DocumentId are not ordering keys. An indistinguishable duplicate document key has a deterministic ordinal within identical-content duplicates; swapping those identical duplicates cannot change evidence. Retain those duplicate documents separately for counts/coverage. If indistinguishable keys have different text, include their full text hash as the final ordering key.

Keep public type-oriented semantics: selecting a member selects its containing type. This is not a member call graph. Scope/generated filters select originating collection documents; they do not automatically remove a proven dependency endpoint whose own declarations are outside those filters. Its outgoing expansion uses only eligible declaration documents, which can be empty. Explicit roots remain query anchors even when their own documents are excluded. Preserve source type-reference evidence and unsupported/unresolved evidence behavior. Assembly scanning uses the shared separated collector/projector where applicable; assembly demand-driven traversal and new assembly dependency caches are outside this plan.

## Collection result and coverage

The Core result must distinguish:

- Exact immutable source snapshot/context, scope and generated-inclusion selection.
- Eligible semantic document identities and attempted/successful collection identities.
- Raw edges keyed by exact source/destination type ownership, with existing file/namespace/project evidence.
- Recoverable per-document errors, cancellations and actual uncollected coverage.
- Full selected-document coverage versus selected outgoing-root coverage.

Use the same ordering and duplicate merge on both paths. A node's exact identity includes owning project path/context and original type identity; equal display names never merge different owners/contexts. Existing graph type keys remain relationship evidence, not generated navigation references. Visible handoff fields follow specification 01.

For equal directed source/destination keys, the representative is the minimum tuple (ordered source document key above, FromFile, ToFile) under ordinal comparison, not the first completed task. Sort traversal candidates by (FromTypeId, ToTypeId, FromFile, ToFile) ordinal. Sort visible edges by (minimum hop Depth, FromProject, FromFile, FromTypeId, ToTypeId) ordinal, with numeric Depth first. Both paths use this common projector; project dependencies remain the distinct full loaded-solution project-reference pairs by displayed (FromProject, ToProject), sorted ordinal, independent of type roots. Namespace summaries group selected edges by (FromProject, FromNamespace, ToProject, ToNamespace); file summaries by (FromProject, FromFile, ToProject, ToFile), retaining current display fields. Referenced/CrossingTypes are distinct ToTypeName values sorted ordinal. Those summaries sort by their grouping tuple; all list projections use the same offset/page size. Exact declaration separation is provided by type-edge keys/references, not by display-only summary labels.

Use these internal counters/coverage values; they are not new public JSON fields:

| Internal value | Exact meaning |
| --- | --- |
| eligibleDocumentCount | All loaded semantic documents passing source/generated filters, before traversal |
| requiredDocumentCount | Distinct document needs discovered for this query; outgoing can grow this count |
| coveredDocumentCount | Successfully satisfied required needs, including valid cached/shared facts |
| newSemanticScanCount | Actual new document collection attempts performed for this operation's jobs; performance instrumentation only |
| full coverage | Every eligible document has successful immutable facts |
| targetedComplete | Every required declaration document of each admitted expanded outgoing type has successful facts |
| partial | At least one required need is unfulfilled due to an error or actual limit |

Map existing `TotalDocumentCount` to eligibleDocumentCount and `ScannedDocumentCount` to coveredDocumentCount for the returned projection; despite its legacy name, the latter describes evidence coverage and includes cache reuse. Do not use it as fresh-work performance evidence. Public source handler `DocumentOffset=0` and `NextDocumentOffset=null` after draining internal batches or completing demand-driven collection. Public `DocumentLimitReached` is true only when an actual enforced limit excludes required documents, not because coveredDocumentCount is less than the eligible whole-solution total on outgoing. Preserve true internal scanner-window cursors for Core consumers that request undrained windows.

Recoverable per-document errors return an ordinary partial-analysis result with existing `Errors` and `scannerErrors` omission; they do not turn into a whole-request error or proof of no edges. Missing internal collection/projection input sets `ContinuationInputIncomplete`; an intentionally unused outgoing subset does not. Cancellation propagates cancellation without a successful partial payload. A failed need cannot mark coverage successful. Required needs are attempted at most once per fresh operation; failed needs may be retried by a later fresh operation.

## Broad incoming and both analysis

For `incoming` or `both`, obtain full raw selected-document coverage, reusing eligible completed collection data. Traverse after that collection. A partial targeted bucket cannot establish absence of incoming edges.

All required scope/generated filters, document-limit signals, errors, depth/node clamps, hidden relationships and continuation-input completeness remain truthful. Output/result-page truncation is separate from semantic coverage. Existing `maxResults` limits a projection; increasing a response budget does not collect more documents.

There is no new dedicated incoming index. Any derived adjacency lookup is a projection helper within the existing immutable collection, rebuilt from retained facts as needed.

## Targeted outgoing analysis

When a full completed collection is available, traverse it directly. Otherwise:

1. Resolve the exact root type. For a file root, match the canonical physical path against all loaded documents before scope/generated filtering. No matching loaded document returns `INVALID_ARGUMENT` at `$.filePath` with an instruction to use a loaded file or discover a unique owner-bound symbol with `find_symbol`. Multiple matching project documents return that same error, list candidate owning project paths in ordinal order in its hint, and instruct use of a unique owner-bound symbol reference. No semantic dependency collection starts before this ownership decision. In one uniquely owned document, every declared named type is a seed: outer/nested class, record, struct, interface, enum and delegate. Deduplicate partial occurrences by exact original type identity. A uniquely loaded file containing no named type is valid and produces zero seeds, zero visited types and no type edges; keep the existing full-solution project-reference summary. A uniquely loaded file excluded by scope/generated rules still supplies its query-anchor seeds. Apply these root validation/seed rules to broad incoming/both projection as well.
2. Include all declaration documents for each root's original type, including partial declarations outside the selected file. Apply source scope and generated filters before collection.
3. Collect each required semantic document once, retaining its full raw document facts. For traversal, select only edges whose source type is in the current frontier; unrelated types sharing the document do not become traversal roots.
4. Use breadth-first expansion of exact source type identities. Collect a destination's declaration documents only when its distance is less than the effective requested depth and its outgoing edges are needed. Depth 1 collects roots' documents only. Cycles and repeated partial/linked documents do not trigger repeated collection within an owner.
5. Use the common traversal rules below; admit seeds/neighbor nodes before scheduling their declaration-document work.
6. Derive the existing project/namespace/file summaries from the selected traversed edges and existing project-reference semantics, exactly as the broad projection for that query does.

Compilation may still require project/reference-wide work; the guarantee concerns dependency document scans, not zero background compilation. Metadata/external types retain existing evidence rules and are not silently added as source roots.

A successfully expanded type whose every eligible declaration document was collected can establish its outgoing edges at that requested traversal position. This never means full-solution incoming coverage. Eligible-document totals and scanned-document counters report actual coverage, not an invented whole-solution scan. Do not report a document-limit omission merely because unrelated documents were intentionally unnecessary; report real omitted required work, errors and existing traversal limits.

If a required declaration's owner/documents cannot be proven, report `continuationInputIncomplete` and a next action to rediscover a uniquely owned declaration; do not choose a display-name match. A known root with no eligible declaration documents contributes no collected outgoing facts but still participates as an admitted query anchor; incoming/both may find eligible origins pointing to it. Filtering alone creates no document-limit/error omission.

## Common traversal limits

Depth is the number of edges from any admitted seed. Seeds have distance 0. Clamp requested depth to the existing 1–3 range; depth 1 exposes direct edges and never collects neighbors' declarations merely to expand them. `VisitedTypeCount` counts distinct admitted exact nodes, including edge-free admitted seeds and terminal-depth destinations. Source type seeds are resolved independently of whether raw edges exist.

The existing node maximum is 200. Sort exact seed keys ordinal and admit up to the effective node limit. If seeds exceed it, discard later seeds and set `NodeLimitReached`; do not scan their documents. For each BFS distance, process admitted nodes ordinal and outgoing/incoming incident edges in the candidate ordering above. For `both`, a node can traverse either endpoint, while the returned edge retains its original direction. An edge already seen at a shorter distance retains its minimum Depth.

Before adding a new endpoint, check the node cap. If it cannot be admitted, skip that visible edge, set `NodeLimitReached`, and record its directed key once as hidden. Never emit an edge with a non-admitted endpoint. Already admitted endpoints may still yield edges. Expand only admitted nodes with distance less than effective depth.

`HiddenTypeDependencyCount` counts unique eligible edges encountered from admitted expanded nodes and skipped specifically by the node cap. It does not count hypothetical downstream edges or edges of unadmitted roots. Broad and targeted projection use this same observable definition; full raw data must not inflate hidden counts from nodes that this traversal never expands. Intentional requested depth is query scope; a requested-depth clamp, node cap, real missing collection or scanner error remains partial analysis under the existing omission names.

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

[DependencyGraphScanner](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs) separates immutable document collection from projection. [DependencyGraphCache](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphCache.cs) owns bounded successful facts and subscriptions; [DependencyGraphOutgoingCollector](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphOutgoingCollector.cs) schedules only admitted outgoing declaration frontiers. [RelationshipTools](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs) selects broad or targeted collection and formats the common projection. [Models](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphModels.cs) and [traversal](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphTraversal.cs) own the payload and shared graph semantics. Point evidence records actual verification separately from these source links.
