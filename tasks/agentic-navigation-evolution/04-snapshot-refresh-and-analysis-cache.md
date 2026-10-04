# Fresh snapshots and reusable analysis

Status: specified for implementation. Execution: [R03](roadmap/R03-snapshot-identity.md) and [R05](roadmap/R05-dependency-cache.md).

## Fresh analysis boundary

A fresh request without an operation/outer token obtains the current resident source snapshot after disk-content and structure checks. A valid fresh domain `resultCursor` request performs the same refresh/identity validation before reading retained facts; malformed/expired/query-mismatched cursors may fail their existing validation first; a changed bound snapshot returns `STALE_SNAPSHOT`. Offset/page requests without a retained token are also fresh.

A valid `operationToken` poll, including a poll of a domain continuation, continues the operation's already selected immutable snapshot. Replaying its completed result does the same. A `continuationToken` only reads its stored immutable outer text and does not refresh. Thus outer pages can finish an older result, but a subsequent new domain-cursor call must validate against current evidence. Never change snapshot halfway through a retained operation.

Keep detection of changed text with unchanged timestamps and refresh all linked documents sharing that file. Do not substitute timestamps, file watchers, root-only hashing or cached declarations for content/structure validation.

A change to loaded documents, membership, project options/references/aliases or metadata image bytes selects a new immutable context before reuse. At each fresh boundary, capture/hash each distinct physical metadata image once and share that verified immutable capture across all references to that path. Verify SHA-256 even when path/size/mtime/MVID are unchanged. Hash the same captured immutable image bytes used to construct the corresponding PortableExecutableReference; never hash bytes A and subsequently reopen bytes B for compilation. Retain image hashes in snapshot-owned input records, not public declaration IDs.

A byte change requires fresh reference objects and Solution/Compilation state. Extend existing fingerprint/reload owners narrowly when needed. For in-memory images, require the retained immutable image and SHA-256 supplied by their creating owner. An image whose bytes/identity cannot be established produces `WORKSPACE_DIAGNOSTIC`; no mtime/MVID/object-hash substitute is accepted.

On unreadable/invalid/changing capture, make at most three capture/reload attempts at that boundary, publishing no partially validated state. Exhaustion returns the existing retryable `PROJECT_LOAD_FAILED` loading control with a concrete input reason. A filesystem change after successful capture is detected at the next fresh boundary; the current result uses its captured image. No general watcher/refresh redesign is authorized.

## Snapshot identity

The fingerprint is independent of declaration references. Preserve full-hash evidence/cursor binding; compact reported `snapshotId` is not a cache key. Corrected encoding/input coverage may change fingerprint bytes once when implemented; there is no old-hash compatibility mode.

Use SHA-256 with domain `source-analysis-v1`. Serialize structured records in the fixed category order below. All lengths/counts are unsigned 32-bit big-endian. A nullable value starts with one byte: `0x00` for null (nothing follows), `0x01` for present. A present string is its UTF-8 byte length followed by strict UTF-8 bytes, with no BOM. A non-nullable string omits the presence byte. Every list has its count, then each element's encoded record length and bytes; a record concatenates fields in the order specified below. Non-nullable scalar integers/enums use the non-nullable string encoding of their invariant integral decimal value; booleans are a single `0x00`/`0x01` byte. Nullable scalars use the same presence byte before their scalar representation. Dictionary/set entries sort ordinal by their logical keys; a dictionary entry records key before value. Ordered lists preserve order. Never serialize runtime object hashes, ProjectId/DocumentId, culture-dependent output, arbitrary option `ToString()` or asynchronous completion order.

The supported source-context contract is the pinned Roslyn C# option types produced through the repository's normal loader/public option APIs. Every loaded source project must have LanguageNames.CSharp, CSharpParseOptions and CSharpCompilationOptions; an absent option set, other language (including a loaded VB project) or custom option subtype returns `WORKSPACE_DIAGNOSTIC` identifying the project before identity/cache use. Referenced compiled binaries remain supported independently of their implementation language. This limitation does not add VB navigation or silently drop a project from the fingerprint.

Canonical paths follow specification 02: lexical full path, `/`, invariant uppercase physical identity on Windows, no symlink/Unicode normalization. The categories are:

1. Domain and canonical selected solution path.
2. Loaded project records sorted by canonical project path then encoded context. Each contains project name, assembly name, language, parse/compilation values below, effective loaded TargetFramework/TargetFrameworkIdentifier/TargetFrameworkVersion/TargetPlatformIdentifier/TargetPlatformVersion/RuntimeIdentifier/Configuration/Platform (`unknown` if unavailable), and actual source assembly identity components (name/version/culture/public key/retargetable/content type).
3. Parse-option values: LanguageVersion and SpecifiedLanguageVersion, Kind, SpecifiedKind (SourceCodeKind values), DocumentationMode, sorted PreprocessorSymbolNames, sorted Features key/value pairs. Compilation-option values: OutputKind, ModuleName, MainTypeName, ScriptClassName, OptimizationLevel, CheckOverflow, AllowUnsafe, NullableContextOptions, Platform, MetadataImportOptions, ordered Usings, WarningLevel, GeneralDiagnosticOption, sorted SpecificDiagnosticOptions, ReportSuppressedDiagnostics, Deterministic; CryptoPublicKey as uppercase hex, nullable DelaySign and PublicSign; canonical CryptoKeyFile plus captured content hash and CryptoKeyContainer. Non-empty signing-file/container state is supported only when the load owner can capture and validate its complete binding inputs; otherwise fail WORKSPACE_DIAGNOSTIC. Record AssemblyIdentityComparer as the known Default or Desktop mode; other comparers require an explicit captured binding-state contract or fail WORKSPACE_DIAGNOSTIC. These names refer to the pinned Roslyn public typed property values, not display strings.
4. For every project, its source, additional and analyzer-config document records, each category sorted independently. Identity is owner project coordinate plus canonical document path; a pathless document uses (Name, ordered Folders, SourceCodeKind). Include those identity components and SHA-256 of the exact loaded SourceText encoded as strict UTF-8. Sort equal logical identities by text hash; retain duplicate multiplicity. Analyzer-config text covers the loaded syntax-tree diagnostic/configuration provider; include captured generator/analyzer image hashes when they affect materialized source/binding. Unsupported untracked generator/provider inputs fail WORKSPACE_DIAGNOSTIC rather than silently reuse. Include regular/source-generated files exposed as loaded documents; do not invent an inventory of unmaterialized generator output.
5. Project-reference edges sorted by canonical referenced project path and properties, with EmbedInteropTypes and sorted aliases. Serialize the complete reachable project graph once as sorted nodes/edges, not recursive object traversal.
6. Metadata-reference records sorted by canonical image path or `in-memory:<SHA-256>`, full immutable image hash, metadata kind, EmbedInteropTypes and sorted aliases. Include every referenced module image for multi-module metadata; MVID may be descriptive but is never the content key.

The per-project owner context fingerprint used for dependency ordering is SHA-256 of that project's category 2/3/5/6 records and its transitive referenced project context graph; exclude document text and runtime IDs. Non-default custom resolvers/options providers whose binding state is not already fully materialized in the recorded document/reference/configuration inputs are unsupported for reusable analysis and return `WORKSPACE_DIAGNOSTIC` instead of an incomplete fingerprint. Scheduler-only ConcurrentBuild and derived Errors are excluded; public Language is already captured in category 2. The remaining public provider/resolver properties are covered by the captured-input requirement, not object identity.

In Roslyn 5.9.0, CompilationOptions.Features, ReferencesSupersedeLowerVersions, CurrentLocalTime and CSharpCompilationOptions.TopLevelBinderFlags are non-public. Do not access private members by reflection or require their serialization. Only state created by the supported loader/public APIs is admitted; custom injection through internal compiler hooks is outside that contract and returns `WORKSPACE_DIAGNOSTIC` when its creating owner cannot establish supported provenance. ParseOptions.Features is public and is recorded above. Actual resolved source assembly versions are recorded in category 2, including wildcard-derived versions; a later compilation producing another version requires a new Solution/identity. A pinned Roslyn upgrade must re-audit its public option surface and explicitly capture or reject newly supported binding inputs before reuse.

Preserve the current separate `get_index_scope` identity domain: its fingerprint additionally includes configured-framework inventory metadata. An inventory snapshot ID can therefore differ from ordinary source-tool IDs without an edit. Document this existing difference in current-state docs during implementation; do not unify snapshot domains or add new expected-snapshot parameters. Cursors retain their existing exact owner/reference/section bindings.

## Supported generator creator inputs

The user approved this explicit creator boundary on 2026-10-04. A normal MSBuild origin, rooted analyzer path, static assembly-reference closure or absence of an observed loader failure is not by itself a complete generator-input contract.

Admit a source generator for reusable identity only through an internal capability supplied by its actual trusted creating owner. Bind that capability to the exact captured generator artifact and its project input context. The owner contract identifies every effective input, supplies immutable image/text/configuration evidence, and guarantees that execution binds those captured inputs. Validate external declared inputs at each fresh boundary; a changed artifact or input creates a new reference/Solution before reuse. Capabilities and their producer-owned resources have explicit weak/runtime lifetime ownership and are not retained in completed scalar identity memo entries.

Built-in SDK generator support requires an explicit trusted SDK producer contract and complete recorded snapshot inputs. File names, path resemblance, object identities and synthetic provenance flags do not establish that contract. A repository test factory may supply a capability only for the real artifact and input contract it creates; tests must not attest arbitrary already loaded generator code as complete.

An unknown generator or one whose effective inputs cannot be established returns concrete WORKSPACE_DIAGNOSTIC before identity/cache publication. Identify the project/generator and missing creator/input proof. Preserve supported positive generator and public-handler behavior through real creator capabilities, and keep untracked dynamic/external input cases negative. Distinguish source generators from analyzers that are not executed by navigation. This boundary introduces no public configuration, general sandbox, code-audit/IL-analysis feature or dependency platform.

## Identity memoization

Use one Core identity-memoization service owned by the host/runtime. Key it by the exact immutable Roslyn `Solution` object, not only its path, displayed snapshot ID, version stamp or document timestamps. Use weak-key retention so completed memo entries cannot keep old solutions alive. Identical semantic content in a different Solution object may be recomputed; the requirement is once per unchanged object while its memo entry remains live.

For concurrent requests against that object, admit one computation and share its result. The computation captures only that immutable input and runs under runtime lifetime ownership; one cancelled caller cancels its wait, not a computation another caller needs. Failed/cancelled computations are removed for a later retry. Runtime shutdown cancels and awaits owned computations. Do not retain unbounded completed Solution/Compilation/ISymbol lists to implement memoization.

Freshness checks occur before this memoization. A new snapshot requires a new computation even if a queried declaration's stable reference stayed unchanged. Remove handle-only validation calls before optimizing remaining calls; there is no reason to retain old handoff validation to get cache hits.

## Dependency cache identity and ownership

Keep dependency retention separate from identity memoization. Core owns immutable collected navigation facts; the host owns lifetime and supplies the current context. No Core dependency on MCP tokens/transport is introduced.

Use the existing .NET primitives for weak keys, tasks, cancellation, synchronization and injectable time (`ConditionalWeakTable`, `Task`, cancellation tokens and `TimeProvider`). The typed bucket's custom coverage/admission/subscriber policy is required because successful partial document facts, exact snapshot ownership, overlapping jobs and deterministic retained-payload accounting must be coordinated; it is not a new general-purpose cache framework. Do not replace or generalize unrelated existing caches. Any newly discovered infrastructure dependency need still follows repository dependency rules.

The bucket key is exactly (canonical selected source target, snapshot ticket, scope, includeGenerated). Scope is the existing normalized enum; generated inclusion is a boolean. There is no separately serialized ambiguous "context" key part.

Assign one monotonically allocated, never recycled runtime-local ticket atomically to each exact Solution object only after freshness/reference validation. The weak-key identity memo associates that ticket with the full validated fingerprint. A changed reference/configuration cannot keep the same Solution/ticket; reload creates a new object. A cache entry retains the scalar ticket and comparison path, not the Solution. Tickets are private cache bookkeeping, never symbol IDs, persisted counters or wire output.

Within a bucket, key document facts by their exact snapshot-local owning project/document identities. Store typed scalar edges, ownership strings, successful coverage and diagnostics needed for projection, without Compilation/ISymbol/Solution references. Root, direction, depth, page size and output budgets are projection arguments, not bucket-key components.

Only this unchanged context can reuse those facts. Even an equal content fingerprint in a separately loaded snapshot does not permit cross-object reuse. A new reference binary, project configuration, scope or generated inclusion cannot reuse the old bucket.

## Coverage and admission

Retain successful immutable document collection facts and their exact coverage. Broad collection can fill missing eligible documents; targeted outgoing can consume already covered documents. Mark full coverage only when every required eligible document has been collected successfully. Never promote a partial outgoing bucket to full incoming coverage.

A document error is not a successful empty fact. Keep it in the requesting operation's final omissions; do not cache it as completed coverage. A later fresh operation may retry it. Cancelled/incomplete facts are not published. Published successful document facts from an otherwise incomplete operation may remain reusable within the same bucket.

Single-flight identity is bucket + exact document need. Each operation subscribes separately to each needed document task. A batch of at most 1,000 unclaimed documents is only a scheduling/compilation-acquisition unit; it does not give one operation ownership of all subscribers. Publishing a successful fact/coverage is atomic.

For A needing {a,b} and B needing {b,c}, cancelling A cancels only its waits/subscriptions: a has no subscriber and is abandoned/cancelled; b and c continue for B. Remove a's incomplete slot only once its running work has acknowledged cancellation/completion. A new a subscriber arriving while that slot is quiescing waits for quiescence, then starts fresh; no overlapping duplicate scan is admitted.

If a shared batch cannot stop one already executing document independently, it may finish that document while serving others, but an abandoned result is not published/admitted. Skip unstarted documents with no subscribers. Cancellation of the whole batch occurs when no document in it has a subscriber, or on runtime shutdown. Do not cancel b's compilation job because a unsubscribed. Facts already successfully published before cancellation remain valid. Failed/abandoned slots are removed for a later retry, never cached as absence. Runtime shutdown cancels all jobs and awaits cleanup outside cache locks.

## Retention bounds

Host-wide production defaults, internal/test-injectable and with no new public configuration knobs:

| Limit | Value |
| --- | --- |
| Retained dependency buckets | 4 |
| Accounted retained dependency payload | 32 MiB total |
| Bucket idle retention | 10 minutes |

Retention is distinct from the in-flight job registry. Use injected monotonic `TimeProvider.GetTimestamp()`/elapsed time. Set lastAccess at successful retained-bucket lookup (even a partial hit), retained fact use, or successful fact admission. Expire when idle elapsed is at least 10 minutes, checking all retained buckets on every lookup/admission. Failed misses do not refresh unrelated buckets.

Serialize admission/eviction under the cache lock. Account unique string values ordinally per bucket and physically share their stored values within that bucket; count their strict UTF-8 bytes once. Do not use permanent `string.Intern`. Account the bucket key once as the non-nullable framed canonical target string, then an unsigned 64-bit big-endian ticket, signed 32-bit big-endian normalized scope enum and one generated-inclusion boolean byte. The target string payload is already in the unique-string total, so add only its four length bytes plus the thirteen scalar bytes; never double-count the target. Each stored per-document edge, successful coverage record or retained non-error diagnostic record adds 64 bytes, even if another document has an equal edge. Containers/CLR object/lock/task overhead are excluded from this deterministic payload budget; do not describe it as measured heap size.

First calculate the fact's standalone cost plus an empty destination bucket key. If that exceeds 32 MiB, return it to current subscribers without admitting it or evicting unrelated data. Otherwise merge with existing bucket facts and repeatedly evict the minimum (lastAccess, canonical target, ticket numeric, scope ordinal, generated false-before-true) until both bounds hold. Recompute the candidate union if the destination bucket itself was evicted. Update destination lastAccess at admission. Never retain empty buckets.

Retained buckets may be evicted even with active subscribers/projections: those owners already hold immutable facts outside retention accounting. Eviction does not cancel in-flight jobs, recycle tickets, invalidate acquired data or remove single-flight slots. A later successful job may re-create a retained bucket under the same admission rules. Active job/operation memory is separately bounded by existing operation capacity/lifetime; no retained entry holds Roslyn objects.

Disposal clears retained data and cleans up jobs. Never cache projected MCP responses here; their operation/continuation stores have different owners and limits.

## Acceptance

Verify one concurrent identity computation per exact unchanged Solution, equal identity after equivalent reloads, and recomputation/new identity on changed text with preserved timestamps, project/reference options, document membership and replaced metadata references. Verify index-inventory identity retains its distinct domain.

Verify warm graphs reuse successful facts; query projection changes do not rescan; scope/generated changes use distinct buckets; source edits and new snapshot objects prevent reuse; full/partial coverage is explicit; and errors are retried rather than cached as absence.

Exercise single-flight overlap, one/all-subscriber cancellation, failure retry, deterministic LRU/TTL, oversized facts, exact admission accounting, eviction during projection, runtime disposal and source-owner eviction. Use injected time/capacity settings, not multi-minute sleeps. Measure identity hashing separately from disk refresh so reduced hashing is not reported as eliminated freshness cost.

## Inspected entry points

[ResidentSolution](../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs), [structure fingerprint](../../src/AiNetCodeNavigator.Core/Workspace/SolutionStructureFingerprint.cs), [AnalysisSymbolIdentity](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs), [source analysis metadata](../../src/AiNetCodeNavigator/Mcp/Tools/NavigationToolSupport.cs), [dependency collector](../../src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs) and [refresh tests](../../tests/AiNetCodeNavigator.FastTests/Workspace/ResidentSolutionStalenessTests.cs).
