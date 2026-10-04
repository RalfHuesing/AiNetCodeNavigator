# Agentic navigation: tool consolidation and practical improvements

## 1. Purpose and status

This is the implementation concept for making AiNetCodeNavigator easier, faster, and more reliable for coding agents. It describes planned behavior, not implemented product behavior. Preparation now includes the [roadmap](roadmap.md) and [task-specific orchestrator prompt](orchestrator.md); production implementation starts only when the user starts that execution task.

The target is twelve purposeful MCP tools instead of seventeen overlapping tools, with preserved navigation capabilities, smaller useful answers, better source-to-metadata navigation, and better static test discovery. Tool count alone is not the success criterion.

The baseline was inspected at repository commit `ed2143da044d67cafb0a933ea95e624f3987123f`. Earlier live probes exercised all seventeen tools against the repository solution and assemblies in its local deployment. Three Luna agents reviewed discovery/context, relationships, and assembly navigation. The live server was not rebuilt for those probes and its build identity was not verified against this baseline. Therefore source-confirmed behavior and historical live observations are distinguished below.

Current contracts remain authoritative until each implementation slice replaces them: [tool reference](../../docs/tools/README.md), [symbol resolution](../../docs/navigation/symbol-resolution.md), [relationships](../../docs/navigation/relationship-contracts.md), and [shared context](../../docs/navigation/get-context.md).

## 2. Product boundaries

- Keep an exclusively read-only C# navigation MCP server for existing `.sln`/`.slnx` and managed `.dll`/`.exe` targets.
- Do not intentionally modify analyzed files, execute analyzed application entry points, or run tests. Builds and test execution remain agent/client responsibilities. Retain current MSBuild design-time loading and source-generator materialization; they are not a sandbox and can execute loading/generator code. Read-only navigation permits host-owned logs, caches, and temporary analysis artifacts, not source edits.
- Do not add linting, compiler diagnostics as a product feature, code auditing, quality scores, risk scores, automatic refactoring, or runtime simulation. Navigation load/binding errors remain necessary operational evidence.
- **Do not consume Git diffs, Git references, revisions, changed-file lists, patches, or change-context inputs. Do not add Git-based impact analysis.** A normal caller-selected file or symbol is a navigation root, without any claim that it changed.
- Do not add a new changed-symbol batch or changed-file batch feature as a substitute for Git integration. Existing body, skeleton, and discovery batching remains available.
- Reflection, DI configuration, runtime dispatch, and external consumers are not completely observable through static navigation. Expose proven relationships and uncertainty; do not infer complete runtime behavior.
- Preserve stdout exclusively for MCP transport and the separation between Core navigation and MCP protocol concerns.

## 3. Findings and evidence

### 3.1 What the server adds

Against shell search and file reads, semantic ownership, overload resolution, implementations, and cross-project relationships are useful additions. Text matches do not prove that a reference binds to the selected declaration. Against an agent-accessible C# language server, many individual navigation functions overlap. The extra value must come from convenient combined answers, stable declaration references, bounded results, assembly navigation, and dependable recovery.

For known physical files and lines, direct bounded reading is often the right agent workflow. The server should not require solution discovery or semantic analysis before ordinary file reading. Assembly navigation is valuable even with a decompiler CLI because the server supplies owner-qualified follow-up references and connected navigation, not just decompiled text.

### 3.2 Confirmed overlap and limitations

| Finding | Local evidence | Consequence |
|---|---|---|
| Impact is a projection of reference traversal. | Historical baseline `ImpactAnalyzer` (removed in M1; replacement [reference summary](../../docs/navigation/reference-summary.md)) delegated to `FindReferencesResolver.FindReferencesAsync`. | Remove the independent impact tool; optional aggregates belong to references. |
| Hierarchy and implementation discovery overlap for type roots, but implementations also support members. | [TypeHierarchyScanner](../../src/AiNetCodeNavigator.Core/Hierarchy/TypeHierarchyScanner.cs), [FindReferencesResolver](../../src/AiNetCodeNavigator.Core/Symbols/FindReferencesResolver.cs). | Merge the entry points while preserving member implementation/override behavior. |
| Context members overlap with class structure, but currently lack its full filters and information. | [StructureTools](../../src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs), [RelationshipTools](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs). | Extend the context member section before removing class structure. |
| Caller labels can include non-call references. | [RelationshipEvidence](../../src/AiNetCodeNavigator.Core/Symbols/RelationshipEvidence.cs), [CallTreeBuilder](../../src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs). | Distinguish uses, bound calls, statically selected dispatch targets, and unresolved evidence. |
| Extension receiver filtering is name matching, not applicability analysis. | [FindAssemblyExtensionsScanner](../../src/AiNetCodeNavigator.Core/Assemblies/FindAssemblyExtensionsScanner.cs), especially `MatchesReceiver`. | Integrate declaration filtering into symbol search without promising completion or expression applicability. |
| Test candidates do not follow helper calls. | [test context](../../docs/navigation/test-context.md), [TestRecommendationBuilder](../../src/AiNetCodeNavigator.Core/Symbols/TestRecommendationBuilder.cs). | Add a small bounded helper traversal with evidence paths. |
| Identity memoization and dependency fact caching already exist. | [AnalysisSymbolIdentityService](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentityService.cs), [dependency graph](../../docs/navigation/dependency-graph.md). | Profile before changing caches; preserve freshness and ownership. |
| Background work and result delivery already have separate bounded lifecycles. | [long-running calls](../../docs/mcp-long-running-calls.md). | Reuse operation tokens, outer pages, and result cursors; do not invent another continuation protocol. |

### 3.3 Historical live observations

These are motivation for reproduction, not current performance guarantees or complete benchmark results. Concurrent probes, source snapshot changes, and an unverified deployed build limit comparisons.

| Probe | Observation |
|---|---|
| `find_references` and `get_impact` on `ImpactAnalyzer.AnalyzeSymbolImpactAsync`, depth one | Both returned the same twenty sites. Impact added two project and four file totals. |
| Hierarchy and implementations on `DependencyGraphProgress` | Both returned the same three derived records. |
| `get_call_tree`, depth one, `topN=50` | Twenty sites were grouped into seventeen caller nodes. A narrower depth-two query correctly reported omissions. |
| Minimal source body of `StableSymbolReference` | After approximately 15 seconds the operation was still in `identifying`; a subsequent poll returned nine declaration lines. Reading the known physical file took approximately 10 ms, but provides a different service. |
| `get_context` with body, callers, and tests | Approximately 58 KB of visible text across three outer pages. A focused caller section was much smaller. |
| Production dependency graph, both directions, depth two | Covered 147 documents and reported broad project, namespace, file, and type relationships despite a symbol root. |
| Source implementations for `System.IDisposable` | Returned `SYMBOL_NOT_FOUND`, although a hierarchy could display metadata interfaces. |
| Assembly extensions on `Humanizer.dll` for `System.String` | Five of forty-four declarations were returned with owner paths and usable references. |
| Assembly type origin for `Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode` | Identified the concrete `Microsoft.CodeAnalysis.CSharp.dll` owner. |
| Extension search including references on the Core deployment assembly | Approximately ninety seconds, ending with incomplete-reference/snapshot evidence and no extension results. This is a specific problematic closure, not general assembly performance. |
| Parallel agent requests | Encountered active-operation saturation; the current default admits four active operations. |

No task-level token savings, billing savings, or controlled LSP comparison were measured. Byte counts must not be presented as billed model token usage.

## 4. Final tool portfolio

The final catalog contains exactly these twelve tools. The seven removed names are not permanent public aliases. These are intentional breaking changes in a documented tool-contract release; removing old registrations happens only after replacement behavior passes its contract tests. Update callers, examples, navigation rules, and current-state documentation in that release.

| Final tool | Responsibility | Target support |
|---|---|---|
| `find_symbol` | Find declarations, including extension declarations, with owner-qualified filters. | Source and assembly; source extension search is added. |
| `get_symbol_body` | Read one or more bounded declaration bodies. | Source and assembly. |
| `get_file_skeleton` | Read selected declaration-file outlines without executable bodies. | Source and materialized assembly source. |
| `get_context` | Read explicitly selected body, members, uses, or static test candidates for one symbol. | Source and assembly; tests remain source-only. |
| `browse_target` | Select either loaded source scope or namespace/type browsing. | `scope` is source-only; `namespaces` supports source and assembly. |
| `get_call_tree` | Traverse incoming/outgoing relationships as a graph with evidence. | Source and existing bounded assembly ownership support. |
| `find_references` | Read direct/transitive symbol uses and optional known-result summaries. | Source and existing bounded assembly ownership support. |
| `get_type_relations` | Select type hierarchy or member/type implementations. | Source and existing assembly scope; source metadata contracts are added. |
| `dependency_graph` | Read a selected level of static type-derived dependencies. | Source and existing materialized assembly scope. |
| `resolve_type_origin` | Identify source or metadata ownership of a type. | Source and assembly. |
| `inspect_assembly` | Inspect public or selected assembly API declarations. | Assembly. |
| `search_assembly` | Search literal or regex text in decompiled source. | Assembly. |

| Current tool | Decision | Capability destination |
|---|---|---|
| `find_symbol` | Keep and extend. | Same tool. |
| `get_symbol_body` | Keep. | Same tool; body batching is deliberately retained. |
| `get_file_skeleton` | Keep with low expansion priority. | Same tool; multiple declarations/files differ from a type-member view. |
| `get_class_structure` | Remove after migration. | `get_context`, section `members`. |
| `get_namespace_tree` | Remove after migration. | `browse_target`, view `namespaces`. |
| `get_index_scope` | Remove after migration. | `browse_target`, view `scope`. |
| `get_call_tree` | Keep and clarify evidence. | Same tool. |
| `find_references` | Keep and extend. | Same tool. |
| `get_impact` | Remove without adding another impact entry point. | References already provide traversal; optional reference aggregates retain useful summaries. |
| `get_type_hierarchy` | Remove after migration. | `get_type_relations`, relation `hierarchy`. |
| `find_implementations` | Remove after migration. | `get_type_relations`, relation `implementations`. |
| `dependency_graph` | Keep and narrow output. | Same tool. |
| `resolve_type_origin` | Keep and improve ownership transitions. | Same tool. |
| `get_context` | Keep and extend. | Same tool. |
| `inspect_assembly` | Keep and bound member output. | Same tool. |
| `search_assembly` | Keep text responsibility. | Same tool. Declaration-only compatibility stays; discovery documentation points to `find_symbol`. |
| `find_assembly_extensions` | Remove after migration. | `find_symbol` extension-declaration filters. |

The reduction is seventeen minus seven removed old entry points plus two new entry points, resulting in twelve. `get_symbol_body` remains separate because it batches independent bodies; `get_context` deliberately stays a single-symbol operation.

## 5. Planned contracts

Unless explicitly replaced below, retain current target validation, defaults, filter meaning, response limits, stable-reference grammar, error classification, and supported owner boundaries. New arguments listed here are planned wire arguments. They are not available in the current server.

### 5.1 Reference results and call evidence

`find_references` adds `includeSummary=false`. The optional summary reports known direct and deeper reference-site counts, distinct owner-qualified project/file counts, and sorted distinct project/file identities. These are reference sites, not unique callers and not all real consequences of a modification. Compute aggregates from the existing traversal, before display paging, without a second scan. If analysis is bounded or partial, the counts describe only the discovered set and carry the same omissions. The same summary is returned on each logical result page of the same query/snapshot; it is not a page subtotal or an accumulating total. Clients must not sum it across pages. Large optional summaries use the existing outer text pages, not a new summary cursor.

Retain call trees and references as separate entry points. References answer all symbol uses; graphs answer directional relationships and paths. Do not rename a method-group reference to a call merely because it has an enclosing method.

Both expose the evidence kind at every site and preserve candidate/unresolved information. Graph totals must distinguish nodes from sites. Bound interface/virtual targets remain static targets, not proof of the runtime implementation. Do not silently remove existing property/member edges to make the name more literal.

### 5.2 Symbol context

Rename the current context section `callers` to `uses`, and `callerScope` to `usageScope`, in the same breaking release. `uses` means direct symbol references, not only invocation sites. Do not retain two equivalent section names indefinitely.

`members` reuses the current class-structure scanner and preserves visibility, signatures, start/end lines, line counts, declaring files, total declaration lines, member kinds, primary-constructor information, stable references, name/kind filtering, and sorting. Add `memberNameFilter`, `memberKindFilter`, `memberSortBy=lines|kind|name` (default `lines`), and source-only `memberScope=all|production|tests` (default `all`). Name/kind filter matching retains the class-structure scanner's behavior. Member options are invalid without `members`; `memberScope` is invalid on assembly targets. The shared `includeGenerated` retains current source meaning. Member scope changes only the members section, not root identity, uses, body, or tests.

`usageScope=all|production|tests` defaults to `all`, is source-only, and is invalid without `uses`. Tests keep their independent all-source scope. `maxResults` remains the list page size for each requested section, including members, with the current context maximum of 100; larger member sets remain fully reachable through cursors. The old 200-entry class-structure page maximum is not a capability that must survive. An empty filtered section is a successful empty result, not proof that the selected type has no other members.

Keep explicit nonempty `sections`, independent body windows, independent test scope, per-section status and cursors, and one common validated owner/snapshot. Unselected sections must not run. `get_symbol_body` keeps its batch behavior; do not replace it with several context calls.

### 5.3 Scope and namespace browsing

`browse_target` requires `view=scope|namespaces`; there is no implicit combined view. Reuse the two current scanners. Scope view must never invoke namespace traversal, and namespace view must never perform the full scope inventory just to render its output.

Scope view accepts the old scope inventory arguments and remains source-only. Namespace view accepts the old project, prefix, depth, type-kind, generated-source, and page arguments. Arguments inapplicable to the selected view fail with a specific argument path. Cursors bind to the selected view and its unchanged arguments.

Clearly distinguish loaded Roslyn document inventory, physical disk files, and materialized generated documents. Preserve configured-but-not-analyzed framework information and unknown framework states. Namespace totals state whether they cover the selected project or the selected prefix; do not leave agents to infer the denominator.

### 5.4 Type relationships and metadata contracts

`get_type_relations` requires `relation=hierarchy|implementations` and a `symbolIdentifier`. Hierarchy accepts named types and preserves bases, interfaces, derived types, declaration locations, and partial declarations. Implementations preserves type implementations, interface-member mappings, abstract/virtual overrides, ordering, scope/generated filters, and paging. Unsupported seeds fail explicitly. Run only the requested scanner.

For a source target, additionally resolve fully qualified metadata contract names and metadata documentation IDs against that solution's loaded compiler references. Example: `System.IDisposable`, or `M:System.IDisposable.Dispose`, can find source implementations without first loading a DLL as the analysis target. Inspect actual compilation relationships; do not match implementations by method name alone.

For metadata lookup, qualified type names and `T:` documentation IDs select named types. Exact `M:`, `P:`, and `E:` documentation IDs select interface or abstract/virtual members for `implementations`; `hierarchy` rejects member IDs. A member ID includes its Roslyn signature, including parameter types when present; do not resolve overloads by simple member name. Concrete/nonvirtual metadata members are unsupported implementation seeds. This does not change existing source-selector behavior.

Without `metadataOwnerPath`, first use the existing source declaration resolution; a source ambiguity stays an ambiguity. Only a source `SYMBOL_NOT_FOUND` allows metadata lookup for the qualified names/IDs above. `metadataOwnerPath` explicitly selects metadata lookup and must be an absolute canonical DLL path returned in the loaded-owner candidates. It is invalid for source references and assembly targets. Validate it against the current solution's captured reference set; never load an arbitrary extra DLL or change the source search scope.

Inspect all loaded source-project compilations for metadata candidates. Collapse repeated occurrences only when validated owner/image identity and declaration identity are equivalent. Different paths/images that cannot be proven equivalent remain separate candidates even when simple assembly/type names match. Ambiguity returns `AMBIGUOUS_SYMBOL` with assembly identity, exact declaration ID, and proven owner path when available. The caller repeats the same raw identifier with one returned `metadataOwnerPath`; an unproven owner cannot be selected by guessing. Missing contracts return `SYMBOL_NOT_FOUND`. Lookup paths outside the loaded candidates return an argument error.

Implementations are searched in the loaded source solution using actual Roslyn contract/member relationships, then filtered by the existing source scope/generated rules. `metadataOwnerPath` restricts contract resolution only, not which source projects may implement it. Existing `src:` ownership rules and rejection of `asm:` references against source targets remain unchanged. Return external root-owner information separately from source implementation references. If exact DLL ownership cannot be proven, do not invent a reference. A source implementation reference follows up against the source solution; a proven external root reference follows up against its returned DLL target.

Assembly traversal keeps its existing owner boundary. Consolidation alone does not add transitive assembly implementer discovery.

### 5.5 Declaration discovery and extensions

`find_symbol` adds `project`, `namespaceFilter`, and `signatureFilter`. `project` selects one loaded source project by exact name (ordinal ignore-case) or its returned canonical project path. Duplicate names require the returned path; unknown projects fail instead of broadening the search. Project selection is invalid for assembly targets. `namespaceFilter` is an ordinal ignore-case substring of the full declared namespace, matching the old extension namespace filter. It is deliberately not a prefix-only filter, which would lose old extension searches. `signatureFilter` is an ordinal case-sensitive substring of the returned signature, not a new identifier format. Omitted or whitespace-only optional namespace/signature filters mean no filter. An explicitly empty project selector is invalid.

Add `extensionOnly=false` and `receiverType`. Receiver filtering requires extension-only search and uses the existing declared-receiver normalization/matching semantics. Extension names use the existing pattern/name-patterns inputs. Exactly one pattern input is required for ordinary discovery; extension-only discovery may omit both to enumerate all matching extension declarations. The existing one-to-ten pattern bound remains.

For `extensionOnly=true`, `kind` must be omitted or `method`; other kinds fail. Omitted/whitespace-only receivers mean no receiver filter. Nonempty receivers use current alias normalization and ordinal ignore-case equality/qualified-suffix matching, without assignability checks. Apply project/namespace/signature/scope filters before result paging, and bind cursors to all effective filters. Map the old extension-name substring search to the existing equivalent name pattern (for example, `*Humanize*`) rather than changing ordinary symbol-pattern semantics.

Preserve current extension owner scope, referenced-owner opt-in, namespaces, signatures, completeness evidence, and paged access. Source extensions use actual Roslyn extension symbols from the source solution. A receiver match does not prove applicability to an expression, generic constraint satisfaction, implicit conversion, import visibility, or overload selection. A compiler-backed expression-applicability/completion feature is explicitly deferred beyond this concept.

`search_assembly` retains literal/regex text matching, context lines, file limits, and stable references where available. Keep its declaration-only filter for callers needing textual declaration matching; direct agents to `find_symbol` for semantic discovery rather than adding another declaration search engine.

### 5.6 Dependency projection and API output

`dependency_graph` adds `level=type|file|namespace|project`, default `type`, and emits only that requested relationship list. Keep the existing file-or-symbol root, direction, depth, scope/generated filters, owner identities, and existing bounds. State explicitly that a member root projects dependencies of its owning type, not a method-call graph.

Type/file/namespace lists derive from the admitted root traversal. Project view is source-only and contains loaded project-reference relationships reachable from the root's owning project(s); do not present an unfiltered solution-wide inventory as root-specific evidence. An assembly target with `level=project` fails argument validation at `level`; assembly references are not source project-reference relationships. Type/file/namespace views retain existing source and materialized-assembly support. Project references are build relationships, not observed method calls. `level` changes projection, not allowed target selectors: `.csproj` targets and project roots are not added.

For project view, resolve the file/symbol root's exact loaded owner before filtering. A file ambiguously linked into several loaded owners remains an error, as today; do not silently use every project as a seed. A unique file with no named types still has its owning project as the project-view seed. Depth retains the current valid range 1..3; zero is invalid. Outgoing depth one emits direct root-project references; incoming depth one emits direct projects referencing the root. Both is the union. Greater depth follows the same directed relationship up to that many edges. Return participating endpoint identities and edges, not an extra root/self dependency. Include a compact root identity separately even when there are no edges. Cycles do not repeat identical directed edges; page/display and node bounds remain explicit.

Project view uses existing loaded project-reference facts and needs no semantic document scan. Source/generated document filters do not change evaluated project references; explicitly supplied `scopeType` or `includeGenerated` is invalid for this view. Type/file/namespace views retain those filters and their existing document-coverage requirements. Argument validation distinguishes omitted options from explicitly supplied inapplicable ones.

Reuse existing scalar collections and the existing outgoing scheduling. Incoming discovery may require a complete eligible-document scan to justify absence; do not label that necessary scan a defect or hide missing coverage to reduce latency. Each rendered edge includes a representative navigation location or an explicit project-reference origin. If several facts aggregate into one edge, one representative is sufficient; do not claim it is the only cause.

`inspect_assembly` adds `includeMembers=false` for a compact default API overview. Selecting a member-name filter requires `includeMembers=true`. The overview retains type identity/signature, public visibility, namespace, owner path, available reference, and analysis limitations; it does not silently fetch or serialize member lists. A member-rich view keeps all selected declarations reachable through the existing continuation delivery. Agents can use `get_context.members` for a focused, paged type-member listing. Do not add a separate member cursor protocol to assembly inspection.

### 5.7 Static test candidates

Extend the existing test builder with optional `testHelperDepth=0..2`, default one, valid only with the tests section. Zero preserves direct matching and name-derived fixtures. One/two allow that many intermediate source helper members between a recognized test method and the selected symbol or a proven implementation.

Keep existing direct target/implementation-use matching as the first step, including its supported bound non-call uses. An indirect path may start from a helper's existing proven use of the selected target or one previously discovered exact implementation. Every further link from a test/caller method to that helper must be a bound source invocation or constructor call. Method-group assignments, general member accesses, candidate/unresolved bindings, delegate invocation without a proven method target, and guessed runtime implementations do not form helper-call links. A bound virtual/interface link records its statically selected declaration and dispatch limitation, not an invented runtime implementation.

Depth counts intermediate helper members, excluding the recognized test method and the selected target/implementation endpoint: `Test -> Target` needs zero, `Test -> Helper -> Target` needs one, and `Test -> Helper2 -> Helper1 -> Target` needs two. A helper's final target use can be a non-call use, but the evidence must say so. Output is evidence of static use, never evidence that the test executes that path.

Use a small breadth-first walk with cycle detection. Keep the current fixture/implementation/reference caps and add a maximum of 200 expanded non-test helper members for the entire request, shared across target/implementation seeds. The 4,096 reference-inspection bound is also request-wide across direct and helper discovery, not reset for every helper. Reuse inspected references where possible. Generated-source policy remains explicit. Scope remains the full loaded source solution, independent of `usageScope`. Choose shortest paths, with owner/location ordering as the tie-breaker; encountering one path must not erase stronger direct evidence for the same test.

Each indirect test candidate includes its shortest proven path, project/file/location information, and available references. Sort direct evidence first, indirect evidence second, and name heuristics last; use deterministic owner/location ordering within each group. Do not promote a heuristic fixture into proof of test use. A reached traversal bound reports partial analysis; depth zero does not promise indirect candidates.

Return the owning test project path, qualified fixture/method names, locations, and available declaration references. This is navigation data that a client can use to select its test runner. Do not generate executable commands, runner-specific filter expressions, or data-row identities. Never execute tests or claim runtime coverage.

### 5.8 Small examples of the planned API

These examples describe future calls, not evidence that the current server supports them. Paths/names are illustrative; use real returned references and owners in actual follow-ups.

```json
{"name":"browse_target","arguments":{"targetPath":"C:\\work\\Sample.slnx","view":"scope"}}
{"name":"get_context","arguments":{"targetPath":"C:\\work\\Sample.slnx","symbolIdentifier":"Sample.OrderService","sections":["members"],"memberKindFilter":"method","memberScope":"production","maxResults":5}}
{"name":"get_type_relations","arguments":{"targetPath":"C:\\work\\Sample.slnx","symbolIdentifier":"M:System.IDisposable.Dispose","relation":"implementations"}}
{"name":"find_symbol","arguments":{"targetPath":"C:\\work\\Humanizer.dll","extensionOnly":true,"receiverType":"System.String","maxResults":5}}
{"name":"dependency_graph","arguments":{"targetPath":"C:\\work\\Sample.slnx","symbolIdentifier":"Sample.OrderService","level":"project","direction":"outgoing","depth":1}}
```

The first call inventories loaded source scope. The second pages production members of one type. The third finds source implementations of the exact metadata method. The fourth lists extension declarations whose declared receiver matches, without claiming expression applicability. The fifth returns direct project-reference edges from the type's source owner, not method calls.

## 6. Performance, compactness, and concurrency

Reproduce the actual source and assembly slow paths on a pinned build before assigning causes. Separate load, freshness refresh, identity fingerprinting, reference formatting, semantic traversal, and response formatting. The old `identifying` observation alone does not prove that hashing is the bottleneck.

Reuse existing runtime owners, weak identity memoization, dependency fact caching, and generation leases. Correct a demonstrated cause in its owner. Do not add a persistent database, another index, watcher-driven correctness, general query planner, global symbol cache, scheduler framework, or duplicate reference representation. Any extra cache must have a measured benefit and defined snapshot key, bounded lifetime, cancellation, and disposal; preserved timestamps must not bypass content validation.

Keep response budgets hard, and preserve recoverable errors, concrete omissions, and known-result continuation. Reduce repeated owner/path/signature data only where all results remain understandable and follow-up arguments remain available. Do not shorten, parse, or replace stable references. Paging delivery completeness and analysis completeness remain distinct.

For saturation, keep the existing bounded admission model and `TOO_MANY_OPERATIONS` response. Include `retryAfterMilliseconds: 1000` and the action to wait before retrying the original request, within the existing budgeted recoverable-error envelope. A rejected request must start no work, retain no admitted operation, and return no usable operation token; a temporary token reservation for existing budget preflight is allowed and must be released. The client waits and retries its original request. Admitted operations still poll normally and release capacity when their work completes or is cancelled. Cancelling a poll cancels only that wait, while cancelling the initial request cancels its owned work; preserve the existing distinction. Do not add a queue, queued state, automatic server retry, or priority scheduler. First remove measured redundant work and test overlapping requests; increasing the active-work limit is not a substitute for this work.

Existing retry/poll/page tokens remain opaque. Provide a concise next action at every transition; keep the original query identity and domain section visible where needed. Do not introduce a fourth continuation mechanism.

### 6.1 Keep the implementation small

Use direct selection in the existing handlers: one browse-view branch, one type-relation branch, and the existing explicit context sections. A shared formatter or collector is appropriate only where the same domain behavior is actually reused. Do not introduce a universal tool dispatcher, provider registry, generic graph framework, query language, or new service layer merely to reduce public tool names.

Consolidate entry points, not unrelated analysis algorithms. Call trees, type references, namespace inventory, and API inspection need not share an engine. Context bodies and batched bodies should share their current extraction behavior, not be routed through nested MCP calls. Keep the current relationship scanners until a concrete defect or measured duplicate traversal requires a focused correction.

The source metadata-contract lookup uses the existing compilation/reference information; the test helper walk uses existing relationship evidence with a small local breadth-first traversal. Neither needs a second persistent index. Dependency project traversal can be a bounded projection over already collected project references; it does not need a general graph package or another semantic collection pass.

Profile first, then make the smallest demonstrated fix. Do not preemptively add any new cache. A performance finding that no longer reproduces on the identified baseline is closed with its reproduction evidence rather than by adding speculative infrastructure.

## 7. Implementation sequence

| Slice | Deliverable | Required acceptance |
|---|---|---|
| P0: establish baseline | Reproduce representative queries on an identified executable and inspect current consumers. Use the completed Exploration runner to retain the production request/result flow. | Record arguments, fixtures, owners, snapshot, latency, pages, error/retry counts, and visible output metrics. Reconfirm old findings; do not implement an obsolete suspected defect. |
| P1: references and evidence | Remove impact registration, add reference summary, rename context uses, clarify graph evidence. | Summary agrees with the discovered reference set across pages; method groups/member accesses remain distinct from calls; no independent impact route remains. |
| P2: coherent entry points | Class structure into context, two type tools into one, scope/namespace views into one; intermediate catalog of thirteen tools. | Complete preserved result sets, filtered partial/generated members, unsupported-mode errors, and no execution of unselected scanners. |
| P3: discovery and assembly transitions | Integrate extensions, add symbol filters and source metadata-contract resolution; finish the catalog of twelve tools. | No lost extension results; declared receiver limits explicit; BCL/interface-member implementation lookup works; ownership ambiguity fails rather than guesses. |
| P4: focused output | Single-level dependency output and compact assembly overview. | Root-specific relationships with evidence, truthful incoming coverage, all selected API members reachable, and documented defaults. |
| P5: test navigation | Bounded helper paths and navigable test identities. | Direct/indirect/heuristic evidence remains separate; cycles and caps terminate; no runner adapter or generated execution filters. |
| P6: measured runtime improvements | Fix reproduced shared/runtime hotspots and handle contention. | Compare identical queries and scopes; freshness, owner leases, cancellation, and resource limits hold; no arbitrary limit increase masks the cause. |
| P7: final verification | Update docs, examples, rules, catalog and agent comparisons; run and inspect the Exploration scenarios described in 8.3. | Exactly twelve public tools; no removed names remain registered; preserved capabilities and task-level improvements demonstrated; saved production input/output and required follow-ups inspected. |

Performance corrections discovered in P0 can precede feature slices when they block useful verification. A stalled verification run is handled according to the repository's verification rules before dependent implementation continues.

## 8. Verification and completion

Verification serves implementation and protects externally observable behavior. Preserve existing substantive regressions, including ambiguous owners, overloads, partial/generated declarations, source and assembly ownership, member mappings, direct versus non-call references, body batches, mixed failures, paging and analysis limits, cursor mismatches, same-timestamp edits, replaced assemblies, cancellation, and saturation. These are established correctness concerns, not an instruction to create a new combination matrix for every tool. Migrate tests for renamed/merged tools while keeping their original defect-preventing assertions. Extend existing fixtures only for new contracts or a concrete uncovered defect; do not build an additional test framework.

Run the official build and the narrowest relevant tests for each production slice, then stop when those checks pass. Broaden or repeat only for changed behavior, a failure, a concrete unresolved finding, or the required final solution check. A small deterministic limit/cycle test is appropriate for a bounded traversal; enormous generated inputs and hypothetical stress conditions are not required. Scope-bounded refactoring of this server's implementation is authorized when it supports these contracts or makes the affected code simpler. It does not authorize a refactoring product feature or an unrelated rewrite.

### 8.1 Fixed representative workloads

P0 records exact arguments and immutable sample contents for the following workloads, using existing TestKit samples or repository targets. Keep the requested answer and expected declarations/edges explicit. New capabilities that fail on the baseline are correctness improvements, not successful baseline latency measurements.

| Workload | Requested answer and comparison |
|---|---|
| W1: symbol/body | Find one overloaded source declaration, retain its owner/reference, and read its first twenty lines. Compare identical filters and body limits. |
| W2: references/context | Read depth-one references and body plus five direct uses for one source member. Map old `callers/callerScope` to new `uses/usageScope`; keep scope, limits, and expected sites equal. |
| W3: API overview | List up to twenty assembly type identities/signatures and usable owner references, without requesting members. Compare old and new default `inspect_assembly` calls; irrelevant old member output is not required evidence. |
| W4: dependency view | Read depth-one outgoing type dependencies for one source symbol. Compare the same type edges; unrelated old graph-level lists are not required evidence. |
| W5: metadata contract | Find source implementations of `M:System.IDisposable.Dispose`, with a competing-owner fixture to test ambiguity and selected-owner retry. |
| W6: tests | Find direct, one-helper, two-helper, cyclic, and name-only test candidates; include a method-group-only non-path. Verify the depth examples and evidence separation. |
| W7: assemblies | Resolve one referenced type owner and follow a returned extension-declaration reference to its body. Compare equivalent filters and owner scope; include a deliberately missing reference case. |
| W8: other preserved views | Read file skeletons, source scope, namespaces, context members, incoming/outgoing call trees, and both type-relation modes with fixed narrow queries. Compare each mapped view's complete requested result set. Existing tests and final Exploration provide this coverage; timing every view repeatedly is not required. |

### 8.2 Proportionate improvement checks

P0 inspects W1-W8 once through representative calls and existing tests, then identifies the actual runtime findings worth addressing. A reproduced warm query around 500 ms or more is a useful investigation lead, not an automatic obligation to optimize every scanner. Record concrete slow paths and serious failures as well as successes; do not select only favorable results. A historical delay that no longer reproduces closes with evidence and requires no speculative fix.

For an actual performance correction, compare that affected workflow before and after on the same samples, target configuration, scope, limits, and response budgets. Time the complete logical query, including required polls/pages. Record the build/snapshot identity, separate cold loading from warm analysis, and avoid concurrent writers or measurements. Start with three sequential warm runs per build and their median. Additional runs need a concrete reason, such as substantial noise or an observed regression; record an inconclusive measurement honestly instead of running indefinitely. No repeated latency series for every preserved view is required.

The earlier 20% latency and 25% output reductions remain useful improvement objectives, not mandatory release thresholds. Do not add infrastructure or weaken results to reach a percentage. Release evidence must instead demonstrate the following concrete outcomes:

- Corrected runtime findings have a measured benefit on their reproduced workflow, or a documented root-cause correction with a functional check when timing is inconclusive. Unresolved observed slowdowns that materially harm the affected workflow remain findings; small timing variation alone is not a defect.
- W3 and W4 default answers omit unrequested member/graph-level output and are smaller on the representative samples while retaining requested identities, references, edges, and limitations. Compare full delivered visible text bytes; token counts may also be recorded with the tokenizer name if readily available. Detailed member listings, context cursors, and selected graph levels remain correct and reachable.
- Metadata implementation and helper-test cases work; a formerly fast error is not a valid latency baseline for a newly successful capability.
- An existing or focused functional test covers saturation and capacity recovery: with a four-operation limit and five overlapping requests, admitted work completes/polls, rejected work receives the documented recoverable delay without leaked work/tokens, and retry after released capacity succeeds. Do not create a broader concurrency benchmark or soak test.

Reuse existing captures, tests, and a small measurement script if needed. Stop after the required evidence is obtained; rerun only affected checks after a correction. Do not infer billed usage from bytes or visible text tokens.

### 8.3 Required final Exploration inspection

Use [AiNetCodeNavigator.Exploration](../../tools/AiNetCodeNavigator.Exploration/) as the shared on-demand way to inspect real arguments, production binding/validation, navigation results, polling, and output delivery. The user has confirmed that the runner implementation is complete; its code, script, and documentation are present in the repository. M0 verifies the identified build and baseline flow during execution; this completion note is not a claim that this preparation task ran those checks. Consult [Manual MCP Exploration](../../docs/development/mcp-exploration.md) and the [exploration script](../../scripts/explore.ps1) at execution time for authoritative usage and artifact layout.

Agents should reuse it during implementation whenever the actual input/output flow is unclear. Extend its existing scenarios for a concrete question rather than introducing another harness, recording framework, or duplicate navigation implementation. The current starter scenarios are `ExploreFindSymbol` and `ExploreSymbolBody`; scenarios for the new contracts are added in their implementation milestones as needed. Runner completion does not mean that the future twelve-tool portfolio is already covered. Coordinate shared-file edits if another agent is actively working there, and preserve unrelated changes.

Final completion requires an on-demand Exploration inspection of all twelve final tools and their distinct views/relations/sections, using representative source and assembly targets where supported. Cover W1-W8 through a small set of readable scenarios; one scenario may cover several tools through genuine follow-ups. Reuse returned references and owner paths unchanged. Include compact and detailed output, selected filters, an ambiguous owner, and the missing-reference case. Do not run all combinations of every parameter merely to enlarge the matrix.

The inspection procedure is:

1. Use an identified build and unchanged sample inputs. List available scenarios with `pwsh -File ./scripts/explore.ps1 -List`, then run the selected scenario with `-Scenario <registered-name>`. The current script builds the runner and its referenced production project; record which production build was actually exercised rather than assuming it is the configured MCP deployment.
2. Read the saved initial request, raw response, visible text, reconstructed payload, and attempt-level requests/responses under `temp/exploration/`, according to the finished runner documentation. Inspect the initial call and the polls/pages, not only the final payload or exit code.
3. Verify that a coding agent can choose the right declaration, understand scope and omissions, and perform the next call directly from the answer. Check totals, evidence kinds, availability, argument names, owner targets, and advertised follow-up tools against this concept. An exit code of zero does not prove content quality; an empty/partial result or skipped follow-up is not sufficient for a scenario whose expected navigation result is known.
4. Explicitly follow domain `resultCursor` pages and body windows in scenarios where required. The currently described runner handles polling and outer `continuationToken` pages, but does not automatically exhaust domain lists or body windows. Preserve the query and each returned section/reference owner. Do not mistake the reconstructed outer payload for a complete domain result.
5. Review expected failure cases from their saved request/error response even if the runner exits nonzero, as currently documented for MCP errors. They pass manual review only when the expected error and recovery information are present; unexpected technical failures, missing artifacts, or omitted required follow-ups remain open. After a production or scenario correction, rerun the affected scenario and retain the new artifacts.
6. Record scenario names, build/target identity, artifact paths, inspected follow-ups, useful results, and unresolved findings in the final milestone's completion evidence. Do not create a second execution log or reporting system. Final acceptance requires no unresolved correctness, required content, ownership, or follow-up finding for the requested scenarios; subjective polish suggestions are not release blockers.

Keep these experiments on demand, outside routine tests and CI. They supplement the automated contract/build gates and the separate transport smoke check; they replace neither. The current runner invokes real production handlers and SDK binding/validation without JSON-RPC transport, so its artifacts are production tool-flow evidence, not a client handshake or full stdio traffic trace. Keep that boundary explicit if the runner evolves.

Exploration runs are not automatically controlled performance measurements. If used for 8.2, keep the runner version, capture policy, and warmup/repetition procedure equal before and after; account for artifact-writing overhead consistently. A fresh process for each scenario is not a warm-query batch. Do not compare runner measurements directly with the historical live-server timings in 3.3.

If the runner cannot execute or lacks a required scenario at final verification, report that concrete missing check and complete it in the existing runner before declaring P7 finished. Do not silently skip Exploration or build a parallel replacement just to close the concept's gates.

### 8.4 Completion and release verification

Use the W1-W8 tests and Exploration follow-ups to demonstrate agent usefulness: an agent can select a declaration, retain exact ownership, obtain focused context, and make the next supported call without guessing. Explain the practical gain over text/file/decompiler access from those concrete flows. A separate multi-model or LSP benchmark campaign is not required.

Completion requires all P1-P6 capabilities, the proportionate checks in 8.2, and the final Exploration inspection in 8.3 to be implemented and verified, not just new tool names or narrower output. Demonstrate that metadata-contract and helper-test scenarios previously missing now work. Unresolved functional failures, material observed regressions, unverified owner/snapshot behavior, or missing required Exploration/transport checks remain open. Do not reopen completed milestones for hypothetical extremes or extra measurements without a concrete finding.

For production slices run the official `scripts/build.ps1` and the narrowest affected fast/integration selection, relevant selected extended relationship/host/workspace contracts where required, and the eligible routine solution selection once at completion. Official scripts exclude `E2EIntegration`; do not present transport-free tests as client handshake coverage. Final validation also performs actual MCP calls against the identified implementation build, exercises polling/pages and follow-up references, and records that separate transport smoke result.

Update the affected `docs/`, root README, SDK catalog expectations, client examples, and `.agents/rules/08-ainetcodenavigator-mcp-navigation.mdc` with implemented behavior in each relevant production commit. Update advertised follow-up-tool lists and recovery hints as well as registrations: a valid result must not recommend a removed tool. Remove superseded wrappers/formatters after checking their consumers; do not keep a parallel impact/class/extension implementation solely for old names. Do not put this future concept into current-state documentation. Preserve unrelated user changes and commit explicit paths only.

## 9. Explicitly deferred directions

Expression-aware completion or extension applicability, automatic DI/Reflection interpretation, complete runtime impact, batch references for a modification set, and runner-specific test execution integration are not required by this concept. A universal navigation mega-tool, a new storage/indexing system, an admission queue/scheduling platform, and a new benchmark platform are not implementation requirements. Git-derived functionality is excluded, not deferred.

## 10. Concept review record

The required review order is: commit the complete initial concept; review it for overengineering and commit the resulting clarifications; ask a Luna reviewer to explain and challenge the concept; resolve concrete misunderstandings/findings in this file and commit the result. These are concept reviews, not evidence that production work has been completed.

| Review | Status | Disposition |
|---|---|---|
| Initial concept | Complete. | Covers all seventeen tools, final catalog, navigation improvements, boundaries, verification, and implementation sequence. |
| Overengineering review | Complete. | Removed the admission-queue proposal and executable test-filter generation. Required direct reuse of current scanners, simple bounded traversals, existing saturation/recovery, and profiling before any new cache. Historical non-reproducing slow paths do not justify speculative fixes. |
| Luna comprehension review | Complete; findings resolved. | Reviewer correctly explained the twelve-tool target, boundaries, and sequence. Clarified helper edge kinds/depth, metadata selectors/ownership, measurable success/regression gates, filter comparison rules, project-level graph behavior, and the thirteen-to-twelve intermediate catalog. A second read confirmed the original blockers resolved and identified selective hotspot/regression coverage; all comparable workloads, including other preserved views, now have explicit gates. Also restored member scope, old extension namespace matching, safe saturation token/cancellation wording, and small future-call examples. |

Execution preparation subsequently applies the user's proportionate-verification direction: section 8.2 replaces mandatory repeated whole-catalog timing series and hard percentage release gates with measured corrections to actual findings, focused output comparisons, and functional checks. The product contracts and substantive regression protections remain required. The task-local roadmap and orchestrator prompt define Sol implementation/review, bounded fix rounds, inexpensive narrow Luna support, and scope-bounded internal refactoring; no production implementation is claimed by that preparation.
