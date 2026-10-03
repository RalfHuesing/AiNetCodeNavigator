# Fresh snapshots and reusable analysis

Status: specified for implementation. Execution: [R03](roadmap.md#r03--fresh-snapshot-identity-once) and [R05](roadmap.md#r05--bounded-reuse-of-immutable-dependency-facts).

## Fresh analysis boundary

Every fresh navigation analysis obtains the current resident source snapshot after the existing disk-content and structure checks. Retained operation polls and immutable response-page reads continue their already captured analysis; they do not refresh into a different version halfway through it. A domain cursor's normal continuation validation still checks its bound current evidence and rejects a replaced snapshot.

Keep detection of changed text with unchanged timestamps and refresh all linked documents sharing that file. Do not substitute timestamps, file watchers, root-only hashing or cached declarations for content/structure validation.

A semantic change to loaded documents, document membership, project options, project references/aliases, or metadata-reference identity/content must select a new immutable analysis context before reuse. Verify metadata-reference replacement, including same path and preserved timestamp. If existing refresh does not detect such a replacement, extend its existing owning fingerprint/reload path narrowly; retaining a stale Compilation is not allowed. No general refresh redesign is authorized.

## Snapshot identity

The source analysis fingerprint remains content/context-based and independent of public declaration references. Preserve existing deterministic full-hash evidence/cursor binding; the reported compact `snapshotId` is not the internal cache key.

Identity inputs include the canonical selected solution path; loaded owner project paths and relevant compilation/parse/reference properties; transitive project-reference context and aliases; metadata-reference identities/content; and every loaded document identity/text in deterministic order. Workspace-local random ProjectIds, task completion order and cache state must not change the fingerprint for equivalent loaded input. Changes to relevant semantics must change it.

Preserve the current separate `get_index_scope` identity domain: its fingerprint additionally includes configured-framework inventory metadata. An inventory snapshot ID can therefore differ from ordinary source-tool IDs without an edit. Document this existing difference in current-state docs during implementation; do not unify snapshot domains or add new expected-snapshot parameters. Cursors retain their existing exact owner/reference/section bindings.

## Identity memoization

Use one Core identity-memoization service owned by the host/runtime. Key it by the exact immutable Roslyn `Solution` object, not only its path, displayed snapshot ID, version stamp or document timestamps. Use weak-key retention so completed memo entries cannot keep old solutions alive. Identical semantic content in a different Solution object may be recomputed; the requirement is once per unchanged object while its memo entry remains live.

For concurrent requests against that object, admit one computation and share its result. The computation captures only that immutable input and runs under runtime lifetime ownership; one cancelled caller cancels its wait, not a computation another caller needs. Failed/cancelled computations are removed for a later retry. Runtime shutdown cancels and awaits owned computations. Do not retain unbounded completed Solution/Compilation/ISymbol lists to implement memoization.

Freshness checks occur before this memoization. A new snapshot requires a new computation even if a queried declaration's stable reference stayed unchanged. Remove handle-only validation calls before optimizing remaining calls; there is no reason to retain old handoff validation to get cache hits.

## Dependency cache identity and ownership

Keep dependency retention separate from identity memoization. Core owns immutable collected navigation facts; the host owns lifetime and supplies the current context. No Core dependency on MCP tokens/transport is introduced.

Use the existing .NET primitives for weak keys, tasks, cancellation, synchronization and injectable time (`ConditionalWeakTable`, `Task`, cancellation tokens and `TimeProvider`). The typed bucket's custom coverage/admission/subscriber policy is required because successful partial document facts, exact snapshot ownership, overlapping jobs and deterministic retained-payload accounting must be coordinated; it is not a new general-purpose cache framework. Do not replace or generalize unrelated existing caches. Any newly discovered infrastructure dependency need still follows repository dependency rules.

A dependency bucket key is:

- canonical selected source target;
- exact immutable source context ticket assigned to the current Solution plus its validated project/reference context;
- source scope (`all`, `production`, `tests`);
- generated-source inclusion.

Use a host-local monotonically allocated snapshot ticket whose mapping to Solution is weak-keyed. Cache entries retain only the scalar ticket/validated context, not the Solution itself. Tickets are internal cache bookkeeping, never public symbol IDs or persistent state. A weak-key identity memo may supply the ticket and fingerprint together.

Within a bucket, key document facts by their exact snapshot-local owning project/document identities. Store typed scalar edges, ownership strings, successful coverage and diagnostics needed for projection, without Compilation/ISymbol/Solution references. Root, direction, depth, page size and output budgets are projection arguments, not bucket-key components.

Only this unchanged context can reuse those facts. Even an equal content fingerprint in a separately loaded snapshot does not permit cross-object reuse. A new reference binary, project configuration, scope or generated inclusion cannot reuse the old bucket.

## Coverage and admission

Retain successful immutable document collection facts and their exact coverage. Broad collection can fill missing eligible documents; targeted outgoing can consume already covered documents. Mark full coverage only when every required eligible document has been collected successfully. Never promote a partial outgoing bucket to full incoming coverage.

A document error is not a successful empty fact. Keep it in the requesting operation's final omissions; do not cache it as completed coverage. A later fresh operation may retry it. Cancelled/incomplete facts are not published. Published successful document facts from an otherwise incomplete operation may remain reusable within the same bucket.

Use single-flight collection per bucket/document need. A job may collect a deterministic batch of unclaimed documents; overlapping subscribers reuse those admitted document tasks rather than rescanning. Publishing facts and coverage is atomic.

Each operation owns a subscription, not another operation's cancellation source. Cancelling a subscriber releases it. If no subscribers remain, cancel the in-flight job and remove its incomplete entries; never cancel a job with another subscriber. Runtime shutdown cancels all jobs and awaits cleanup. A later operation may recompute after such cancellation, expiry or eviction.

## Retention bounds

Host-wide production defaults, internal/test-injectable and with no new public configuration knobs:

| Limit | Value |
| --- | --- |
| Retained dependency buckets | 4 |
| Accounted retained dependency payload | 32 MiB total |
| Bucket idle retention | 10 minutes |

Evict least-recently-used retained buckets to admit completed facts within both bounds. Account every retained string by UTF-8 byte length, plus 64 bytes per stored edge/coverage/diagnostic record and the key material's UTF-8 bytes. Count shared retained values once. This is a deterministic admission budget, not a claim to measure exact CLR heap consumption. Entries retain no Roslyn graph, and bucket/record counts plus payload accounting bound retained data.

If one document's facts exceed the budget, use them for the requesting operation but do not retain them. In-flight operation memory is separate from retained-cache accounting and remains subject to existing operation capacity/lifetime and cancellation. Eviction does not invalidate immutable facts already captured by an active projection; it merely means later requests may recompute.

Expire buckets on access and do not keep empty buckets or resident owners alive just for retention. Disposal clears retained data. Never cache projected MCP responses here; their existing operation/continuation stores have different ownership and limits.

## Acceptance

Verify one concurrent identity computation per exact unchanged Solution, equal identity after equivalent reloads, and recomputation/new identity on changed text with preserved timestamps, project/reference options, document membership and replaced metadata references. Verify index-inventory identity retains its distinct domain.

Verify warm graphs reuse successful facts; query projection changes do not rescan; scope/generated changes use distinct buckets; source edits and new snapshot objects prevent reuse; full/partial coverage is explicit; and errors are retried rather than cached as absence.

Exercise single-flight overlap, one/all-subscriber cancellation, failure retry, deterministic LRU/TTL, oversized facts, exact admission accounting, eviction during projection, runtime disposal and source-owner eviction. Use injected time/capacity settings, not multi-minute sleeps. Measure identity hashing separately from disk refresh so reduced hashing is not reported as eliminated freshness cost.

## Inspected entry points

[ResidentSolution](../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs), [structure fingerprint](../../src/AiNetCodeNavigator.Core/Workspace/SolutionStructureFingerprint.cs), [AnalysisSymbolIdentity](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs), [source analysis metadata](../../src/AiNetCodeNavigator/Mcp/Tools/NavigationToolSupport.cs), [dependency collector](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs) and [refresh tests](../../tests/AiNetCodeNavigator.FastTests/Workspace/ResidentSolutionStalenessTests.cs).
