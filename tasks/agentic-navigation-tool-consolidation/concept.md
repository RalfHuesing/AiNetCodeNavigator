# Agentic navigation: tool consolidation and practical improvements

## 1. Purpose and status

This is the implementation concept for making AiNetCodeNavigator easier, faster, and more reliable for coding agents. It describes planned behavior, not implemented product behavior. The requested deliverable at this stage is this concept and its reviews; production implementation is a later task.

The target is twelve purposeful MCP tools instead of seventeen overlapping tools, with preserved navigation capabilities, smaller useful answers, better source-to-metadata navigation, and better static test discovery. Tool count alone is not the success criterion.

The baseline was inspected at repository commit `ed2143da044d67cafb0a933ea95e624f3987123f`. Earlier live probes exercised all seventeen tools against the repository solution and assemblies in its local deployment. Three Luna agents reviewed discovery/context, relationships, and assembly navigation. The live server was not rebuilt for those probes and its build identity was not verified against this baseline. Therefore source-confirmed behavior and historical live observations are distinguished below.

Current contracts remain authoritative until each implementation slice replaces them: [tool reference](../../docs/tools/README.md), [symbol resolution](../../docs/navigation/symbol-resolution.md), [relationships](../../docs/navigation/relationship-contracts.md), and [shared context](../../docs/navigation/get-context.md).

## 2. Product boundaries

- Keep an exclusively read-only C# navigation MCP server for existing `.sln`/`.slnx` and managed `.dll`/`.exe` targets.
- Do not modify or execute analyzed source or assemblies. Builds and test execution remain agent/client responsibilities.
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
| Impact is a projection of reference traversal. | [ImpactAnalyzer](../../src/AiNetCodeNavigator.Core/Symbols/ImpactAnalyzer.cs) delegates to `FindReferencesResolver.FindReferencesAsync`. | Remove the independent impact tool; optional aggregates belong to references. |
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

`find_references` adds `includeSummary=false`. The optional summary reports known direct and deeper reference-site counts, and distinct owner-qualified project/file counts. These are reference sites, not unique callers and not all real consequences of a modification. Compute aggregates from the existing traversal, before display paging, without a second scan. If analysis is bounded or partial, the counts describe only the discovered set and carry the same omissions. Page continuation must not add the same totals again.

Retain call trees and references as separate entry points. References answer all symbol uses; graphs answer directional relationships and paths. Do not rename a method-group reference to a call merely because it has an enclosing method.

Both expose the evidence kind at every site and preserve candidate/unresolved information. Graph totals must distinguish nodes from sites. Bound interface/virtual targets remain static targets, not proof of the runtime implementation. Do not silently remove existing property/member edges to make the name more literal.

### 5.2 Symbol context

Rename the current context section `callers` to `uses`, and `callerScope` to `usageScope`, in the same breaking release. `uses` means direct symbol references, not only invocation sites. Do not retain two equivalent section names indefinitely.

`members` reuses the current class-structure scanner and preserves visibility, signatures, locations, declaring files, member kinds, primary-constructor information, stable references, name/kind filtering, and sorting. Add `memberNameFilter`, `memberKindFilter`, and `memberSortBy`; member options are invalid without `members`. `maxResults` remains the list page size for each requested section, including members, with the current context maximum of 100; larger member sets remain fully reachable through cursors. The old 200-entry class-structure page maximum is not a capability that must survive.

Keep explicit nonempty `sections`, independent body windows, independent test scope, per-section status and cursors, and one common validated owner/snapshot. Unselected sections must not run. `get_symbol_body` keeps its batch behavior; do not replace it with several context calls.

### 5.3 Scope and namespace browsing

`browse_target` requires `view=scope|namespaces`; there is no implicit combined view. Reuse the two current scanners. Scope view must never invoke namespace traversal, and namespace view must never perform the full scope inventory just to render its output.

Scope view accepts the old scope inventory arguments and remains source-only. Namespace view accepts the old project, prefix, depth, type-kind, generated-source, and page arguments. Arguments inapplicable to the selected view fail with a specific argument path. Cursors bind to the selected view and its unchanged arguments.

Clearly distinguish loaded Roslyn document inventory, physical disk files, and materialized generated documents. Preserve configured-but-not-analyzed framework information and unknown framework states. Namespace totals state whether they cover the selected project or the selected prefix; do not leave agents to infer the denominator.

### 5.4 Type relationships and metadata contracts

`get_type_relations` requires `relation=hierarchy|implementations` and a `symbolIdentifier`. Hierarchy accepts named types and preserves bases, interfaces, derived types, declaration locations, and partial declarations. Implementations preserves type implementations, interface-member mappings, abstract/virtual overrides, ordering, scope/generated filters, and paging. Unsupported seeds fail explicitly. Run only the requested scanner.

For a source target, additionally resolve fully qualified metadata contract names and metadata documentation IDs against that solution's loaded compiler references. Example: `System.IDisposable`, or `M:System.IDisposable.Dispose`, can find source implementations without first loading a DLL as the analysis target. Inspect actual compilation relationships; do not match implementations by method name alone.

Ambiguous contracts return owner/assembly candidates; an optional `metadataOwnerPath` selects exactly one of the solution's already loaded metadata owners. It must not load an arbitrary extra DLL or change the source search scope. Existing `src:` ownership rules and the rejection of `asm:` references against source targets remain unchanged. Return external owner information separately from source implementation references. If exact DLL ownership cannot be proven, do not invent a reference.

Assembly traversal keeps its existing owner boundary. Consolidation alone does not add transitive assembly implementer discovery.

### 5.5 Declaration discovery and extensions

`find_symbol` adds an exact source `project` selector, a namespace-prefix selector, and `signatureFilter` for disambiguating declaration results. Project selection is invalid for assembly targets. Signature filtering is a documented ordinal substring of the returned signature, not a new identifier format.

Add `extensionOnly=false` and `receiverType`. Receiver filtering requires extension-only search and uses the existing declared-receiver normalization/matching semantics. Extension names use the existing pattern/name-patterns inputs. Exactly one pattern input is required for ordinary discovery; extension-only discovery may omit both to enumerate all matching extension declarations. The existing one-to-ten pattern bound remains.

Preserve current extension owner scope, referenced-owner opt-in, namespaces, signatures, completeness evidence, and paged access. Source extensions use actual Roslyn extension symbols from the source solution. A receiver match does not prove applicability to an expression, generic constraint satisfaction, implicit conversion, import visibility, or overload selection. A compiler-backed expression-applicability/completion feature is explicitly deferred beyond this concept.

`search_assembly` retains literal/regex text matching, context lines, file limits, and stable references where available. Keep its declaration-only filter for callers needing textual declaration matching; direct agents to `find_symbol` for semantic discovery rather than adding another declaration search engine.

### 5.6 Dependency projection and API output

`dependency_graph` adds `level=type|file|namespace|project`, default `type`, and emits only that requested relationship list. Keep the existing file-or-symbol root, direction, depth, scope/generated filters, owner identities, and existing bounds. State explicitly that a member root projects dependencies of its owning type, not a method-call graph.

Type/file/namespace lists derive from the admitted root traversal. Project view contains loaded project-reference relationships reachable from the root's owning project(s), using the requested direction and depth; do not present an unfiltered solution-wide inventory as root-specific evidence. Project references are build relationships, not observed method calls.

Reuse existing scalar collections and the existing outgoing scheduling. Incoming discovery may require a complete eligible-document scan to justify absence; do not label that necessary scan a defect or hide missing coverage to reduce latency. Each rendered edge includes a representative navigation location or an explicit project-reference origin. If several facts aggregate into one edge, one representative is sufficient; do not claim it is the only cause.

`inspect_assembly` adds `includeMembers=false` for a compact default API overview. Selecting a member-name filter requires `includeMembers=true`. A member-rich view keeps all selected declarations reachable through the existing continuation delivery. Agents can use `get_context.members` for a focused, paged type-member listing. Do not add a separate member cursor protocol to assembly inspection.

### 5.7 Static test candidates

Extend the existing test builder with optional `testHelperDepth=0..2`, default one, valid only with the tests section. Zero preserves direct matching and name-derived fixtures. One/two allow that many intermediate source helper members between a recognized test method and the selected symbol or a proven implementation.

Use existing bound source relationship evidence and a breadth-first walk with cycle detection. Exclude candidate/unresolved edges from proven helper paths. Keep the current fixture/implementation/reference caps and add a maximum of 200 expanded helper members. Generated-source policy remains explicit. Scope remains the full loaded source solution, independent of `usageScope`.

Each indirect test candidate includes its shortest proven path, project/file/location information, and available references. Sort direct evidence first, indirect evidence second, and name heuristics last; use deterministic owner/location ordering within each group. Do not promote a heuristic fixture into proof of test use. A reached traversal bound reports partial analysis; depth zero does not promise indirect candidates.

Return the owning test project path, qualified fixture/method names, locations, and available declaration references. This is navigation data that a client can use to select its test runner. Do not generate executable commands, runner-specific filter expressions, or data-row identities. Never execute tests or claim runtime coverage.

## 6. Performance, compactness, and concurrency

Reproduce the actual source and assembly slow paths on a pinned build before assigning causes. Separate load, freshness refresh, identity fingerprinting, reference formatting, semantic traversal, and response formatting. The old `identifying` observation alone does not prove that hashing is the bottleneck.

Reuse existing runtime owners, weak identity memoization, dependency fact caching, and generation leases. Correct a demonstrated cause in its owner. Do not add a persistent database, another index, watcher-driven correctness, general query planner, global symbol cache, scheduler framework, or duplicate reference representation. Any extra cache must have a measured benefit and defined snapshot key, bounded lifetime, cancellation, and disposal; preserved timestamps must not bypass content validation.

Keep response budgets hard, and preserve recoverable errors, concrete omissions, and known-result continuation. Reduce repeated owner/path/signature data only where all results remain understandable and follow-up arguments remain available. Do not shorten, parse, or replace stable references. Paging delivery completeness and analysis completeness remain distinct.

For saturation, keep the existing bounded admission model and `TOO_MANY_OPERATIONS` response. Add a clear retry delay and next action to that existing error. A rejected request must start no work and allocate no operation token; the client waits and retries its original request. Existing admitted operations still poll normally and must release capacity on completion/cancellation. Do not add a queue, queued state, automatic server retry, or priority scheduler. First remove measured redundant work and test overlapping requests; increasing the active-work limit is not a substitute for this work.

Existing retry/poll/page tokens remain opaque. Provide a concise next action at every transition; keep the original query identity and domain section visible where needed. Do not introduce a fourth continuation mechanism.

### 6.1 Keep the implementation small

Use direct selection in the existing handlers: one browse-view branch, one type-relation branch, and the existing explicit context sections. A shared formatter or collector is appropriate only where the same domain behavior is actually reused. Do not introduce a universal tool dispatcher, provider registry, generic graph framework, query language, or new service layer merely to reduce public tool names.

Consolidate entry points, not unrelated analysis algorithms. Call trees, type references, namespace inventory, and API inspection need not share an engine. Context bodies and batched bodies should share their current extraction behavior, not be routed through nested MCP calls. Keep the current relationship scanners until a concrete defect or measured duplicate traversal requires a focused correction.

The source metadata-contract lookup uses the existing compilation/reference information; the test helper walk uses existing relationship evidence with a small local breadth-first traversal. Neither needs a second persistent index. Dependency project traversal can be a bounded projection over already collected project references; it does not need a general graph package or another semantic collection pass.

Profile first, then make the smallest demonstrated fix. Do not preemptively add any new cache. A performance finding that no longer reproduces on the identified baseline is closed with its reproduction evidence rather than by adding speculative infrastructure.

## 7. Implementation sequence

| Slice | Deliverable | Required acceptance |
|---|---|---|
| P0: establish baseline | Reproduce representative queries on an identified executable and inspect current consumers. | Record arguments, fixtures, owners, snapshot, latency, pages, error/retry counts, and visible output metrics. Reconfirm old findings; do not implement an obsolete suspected defect. |
| P1: references and evidence | Remove impact registration, add reference summary, rename context uses, clarify graph evidence. | Summary agrees with the discovered reference set across pages; method groups/member accesses remain distinct from calls; no independent impact route remains. |
| P2: coherent entry points | Class structure into context, two type tools into one, scope/namespace views into one. | Complete preserved result sets, filtered partial/generated members, unsupported-mode errors, and no execution of unselected scanners; twelve-tool count is completed after P3. |
| P3: discovery and assembly transitions | Integrate extensions, add symbol filters and source metadata-contract resolution. | No lost extension results; declared receiver limits explicit; BCL/interface-member implementation lookup works; ownership ambiguity fails rather than guesses. |
| P4: focused output | Single-level dependency output and compact assembly overview. | Root-specific relationships with evidence, truthful incoming coverage, all selected API members reachable, and documented defaults. |
| P5: test navigation | Bounded helper paths and navigable test identities. | Direct/indirect/heuristic evidence remains separate; cycles and caps terminate; no runner adapter or generated execution filters. |
| P6: measured runtime improvements | Fix reproduced shared/runtime hotspots and handle contention. | Compare identical queries and scopes; freshness, owner leases, cancellation, and resource limits hold; no arbitrary limit increase masks the cause. |
| P7: final verification | Update docs, examples, rules, catalog and agent comparisons. | Exactly twelve public tools; no removed names remain registered; preserved capabilities and task-level improvements demonstrated. |

Performance corrections discovered in P0 can precede feature slices when they block useful verification. A stalled verification run is handled according to the repository's verification rules before dependent implementation continues.

## 8. Verification and completion

Implementation tests must cover externally observable contracts: ambiguous owners, overloads, partial/generated declarations, source and assembly ownership, member mappings, direct versus non-call references, body batches, mixed failures, paging and analysis limits, cursor mismatches, same-timestamp edits, replaced assemblies, cancellation, and saturation. Extend existing fixtures and tests; do not build an additional test framework.

For repeated before/after measurements, keep the sample inputs, target configuration, queries, page sizes, and response budgets identical, except for explicitly mapped replacement names/arguments. Use at least five sequential measured repetitions after warmup and a separate cold-load run. Record raw measurements and median latency, all polling/page round trips, and complete visible text bytes/tokens. Token encoding and client/tool schema overhead must be reported separately; text tokens are not billed account usage.

Use a small paired task comparison: unique overloaded-symbol discovery, reference ownership, body-plus-selected-context navigation, interface-member implementations, helper-based test discovery, source-to-DLL ownership, and assembly extension declaration search. Compare shell/file/decompiler workflows with those workflows plus MCP; add an LSP comparison only when it is actually accessible. Keep model, fixture, requested answer, and stopping rule equal. Include failures instead of dropping them. This is a repeatable task comparison, not a new evaluation platform.

Completion requires all P1-P6 capabilities to be implemented and verified, not just new tool names or narrower output. Show a measured improvement in every still-reproduced runtime hotspot selected in P0 and smaller default replies without losing requested evidence. A historical hotspot that no longer reproduces needs no speculative fix, but its baseline reproduction result must be recorded. Demonstrate that metadata-contract and helper-test scenarios previously missing now work. Any remaining material performance regression or unverified owner/snapshot behavior remains open; do not declare the server substantially improved on tool count alone.

For production slices run the official `scripts/build.ps1` and the narrowest affected fast/integration selection, relevant selected extended relationship/host/workspace contracts where required, and the eligible routine solution selection once at completion. Official scripts exclude `E2EIntegration`; do not present transport-free tests as client handshake coverage. Final validation also performs actual MCP calls against the identified implementation build, exercises polling/pages and follow-up references, and records that separate transport smoke result.

Update the affected `docs/`, root README, SDK catalog expectations, client examples, and `.agents/rules/08-ainetcodenavigator-mcp-navigation.mdc` with implemented behavior in each relevant production commit. Do not put this future concept into current-state documentation. Preserve unrelated user changes and commit explicit paths only.

## 9. Explicitly deferred directions

Expression-aware completion or extension applicability, automatic DI/Reflection interpretation, complete runtime impact, batch references for a modification set, and runner-specific test execution integration are not required by this concept. A universal navigation mega-tool, a new storage/indexing system, an admission queue/scheduling platform, and a new benchmark platform are not implementation requirements. Git-derived functionality is excluded, not deferred.

## 10. Concept review record

The required review order is: commit the complete initial concept; review it for overengineering and commit the resulting clarifications; ask a Luna reviewer to explain and challenge the concept; resolve concrete misunderstandings/findings in this file and commit the result. These are concept reviews, not evidence that production work has been completed.

| Review | Status | Disposition |
|---|---|---|
| Initial concept | Complete. | Covers all seventeen tools, final catalog, navigation improvements, boundaries, verification, and implementation sequence. |
| Overengineering review | Complete. | Removed the admission-queue proposal and executable test-filter generation. Required direct reuse of current scanners, simple bounded traversals, existing saturation/recovery, and profiling before any new cache. Historical non-reproducing slow paths do not justify speculative fixes. |
| Luna comprehension review | Pending. | Reviewer must identify unclear decisions, missing contracts, contradictions, and unnecessary complexity. |
