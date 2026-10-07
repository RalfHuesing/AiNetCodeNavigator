# Independent product and architecture assessment

Date: 2026-10-07. Source baseline: `b48a2bf9fb6d2a143f969204ba15b17ea096598b`. This is a proposal, not an approved implementation specification. See [decisions](decisions.md), [experiment](experiment.md), and [review status](README.md).

## Judgment

AiNetCodeNavigator is a credible specialist for static C# and managed-library investigation. Stable declaration references, explicit ownership, shared snapshots, bounded relationship analysis and recoverable omissions are valuable foundations. It already serves finding declarations, reading selected implementations, following static relationships and inspecting dependencies well at the contract level. It is not yet demonstrated to be a complete or cost-effective information service for the user's actual application portfolio.

The strongest next direction is a **read-only service for evidence needed to understand and prepare a code change**, retaining the navigation engines. Extend its understanding of the evaluated technical environment and the links between C# and relevant project artifacts; simplify delivery; measure completed agent tasks. Do not expand into a replacement IDE, autonomous development platform or universal runtime graph.

This conclusion does not establish speed, memory usage or agent-success improvements: no comparative end-to-end benchmark was run. Existing tests are evidence of specified cases, not proof that every supported SDK or application type works. No existing task specifications were present under `tasks/` at review start; the new material here is proposed work only.

## What exists and how it serves an agent

The [tool catalog](../../../docs/tools/README.md) and attributed methods in [SymbolTools](../../../src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs), [StructureTools](../../../src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs), [RelationshipTools](../../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs), its partial classes and [AssemblyTools](../../../src/AiNetCodeNavigator/Mcp/Tools/Assemblies/AssemblyTools.cs) establish the 12-tool public surface. The [separate exporter](../../../docs/assembly-export.md) is an additional explicit writing CLI, not an MCP navigation tool.

| Typical task | Current usefulness | Material limit or friction |
| --- | --- | --- |
| Find a declaration from a name | Strong static capability: batched patterns, owner filters, overload/signature filtering, generated/test scope and stable follow-ups. | Requires a solution/assembly target; declaration search is not literal/configuration search. No need to replace `rg` for those questions. |
| Understand an unfamiliar file/type | File skeleton, batched bodies and selected context sections avoid indiscriminate full-file reads. | File skeleton intentionally omits nested types and initializers; body is a selected declaration, not its complete compilation environment. Known physical file ranges are often cheaper to read directly. |
| Prepare a method/interface change | Direct uses, call graphs, implementations and static test candidates cover different questions. | Agent must compose the evidence and inspect wiring/configuration. Static evidence is not all runtime consumers; source-generated declarations are excluded by default in several tools. |
| Follow broader impact | References and dependency graphs preserve project ownership, limits and representative locations. | Graph caps/depth are analysis boundaries, not merely response pages. Type dependency roots on members mean the containing type, not a method impact graph. Incoming/both dependency queries may scan the eligible solution. |
| Investigate a library | Decompiled source, API overview, literal/regex search, type origin and selected reference expansion are useful differentiators. | Decompiled code is reconstructed; reference closure is bounded and not a reverse inventory of every external consumer. Assembly implementation relations stay within the selected owner source. |
| Verify a change | Fresh source identity and test candidate provenance help select evidence. | No build/test execution or measured coverage, by design. A current navigation result is not a successful build or an assertion that tests exercise the modified behavior. |

These claims are supported by [workspace evidence](workspace-evidence.md), [relationship and assembly evidence](navigation-evidence.md), [symbol identity](../../../docs/navigation/symbol-resolution.md), [skeleton contract](../../../docs/navigation/get-file-skeleton.md), [dependency semantics](../../../docs/navigation/dependency-graph.md), and [static test candidates](../../../docs/navigation/test-context.md). The inspected tests were not rerun in this documentation-only review.

## Strongest recommendations, in order

### P0 — Establish a task-level baseline before approving major implementation

**Agent problem:** Correct individual calls can still produce an incomplete answer or an expensive change workflow. The repository's identity timing fixture measures a tiny one-project example; traffic capture measures tool text but explicitly excludes schemas, input context and billed model usage.

**Proposal:** Run the small paired experiment in [experiment.md]. Treat missed required evidence and false completeness as first-class failures. Compare file/search tools alone, those tools plus Navigator, and a small Navigator workflow prototype. Add an IDE/LSP comparator only if it can answer a concrete build-versus-integrate question without a separate setup project.

**Benefit:** Decides whether the product saves work, where it is unique, and which of the following investments actually matters. **Cost:** Small for the first manually scored set; medium for representative legacy and large-solution fixtures. **Risk:** Toy benchmarks and model self-assessment flatter retrieval tools. Use independently prepared expected evidence and identical agent/task conditions. **Feasibility:** High; use existing exploration/capture infrastructure, with known measurement limits. No production change required for the first comparison.

Evidence: [identity measurement test](../../../tests/AiNetCodeNavigator.IntegrationTests/Workspace/SourceSnapshotIdentityMeasurementTests.cs), lines 22–63; [traffic capture](../../../docs/mcp-traffic-capture.md), measurements section. Do not confuse the existing all-tools exploration with an outcome benchmark.

### P1 — Make the evaluated environment an explicit navigation object

**Agent problem:** A correct answer within one loaded context can still miss the API or wiring that matters under another TFM, configuration, markup file or legacy build setup.

**Proposal:** Extend `browse_target` with a distinct, bounded environment view rather than turning the existing scope inventory into an ambiguous disk listing. Report actual SDK/MSBuild identity, loaded and configured contexts, parse/preprocessor options, evaluated project/reference/package ownership, relevant props/targets and technical artifacts with provenance. Reuse captured inputs; do not echo full assets files or configuration values. Expose inventory categories as loaded, excluded, not analyzed or unknown. Add a selectable TFM/configuration/platform context when the framework experiment shows the current target cannot express the needed owner. Bind references/cursors to that context without silently choosing among duplicate project contexts.

Start UI support with reliable links: partial declarations/code-behind, XAML `x:Class` and explicit event names, Razor component declarations and generated-source locations where SDK mappings prove them. Return unresolved binding/resource/DI leads as candidates with source locations. Parsing a string that looks like a binding is not proof of its runtime target.

**Benefit:** Fewer false absence claims; a clear entry point to technical context outside C#. **Cost:** Medium for captured build facts and asset links; high for complete Razor/XAML language services or context-aware identity migration. **Risks:** MSBuild evaluation differences, SDK-specific generators, linked files and stale generated output. A context selector affects loading, cache keys, references and every follow-up, not just one optional argument. **Feasibility:** High for inventory and explicit links; qualified for semantic markup binding. Stage these separately.

Evidence: [index scope](../../../docs/navigation/get-index-scope.md), lines 3–11; [symbol references](../../../docs/navigation/symbol-resolution.md), lines 5–11, explicitly lack a TFM selector and reject ambiguous loaded contexts; [loader](../../../src/AiNetCodeNavigator.Core/Workspace/MSBuildSolutionLoader.cs), lines 101–126 and 167–220, uses default MSBuild registration and whole-solution loading. This extends technical navigation, not code auditing or business knowledge.

### P1 — Reduce protocol work visible to the model, preserving completeness

**Agent problem:** There are independent operation tokens, outer text pages, domain cursors and body windows. The agent repeats the full query, preserves argument presence, interprets ordering, and may receive a fragment of JSON before any useful item. `partial` can mean undisplayed results even when analysis is complete.

**Proposal:** Prefer self-contained semantic result pages with stable identity, scope/coverage, delivered items, omissions and one explicit next action. Keep delivery completeness separate from analysis coverage. Offer concise and detailed projections, but never drop ownership, ambiguity or material omissions to satisfy a smaller budget. Retain chunking as an explicit last resort for one oversized body. Where clients permit it, let an adapter handle operation polling and outer transport assembly; do not automatically drain every semantic result page into the model. A compatibility path can keep current tools while measuring a versioned envelope.

Reduce inconsistent defaults and ineffective-argument traps in the next contract revision: normalize harmless explicit defaults; reject options that change meaning; use explicit pattern mode rather than inferred regex where practical. Document each tool's cheapest useful path and expensive scan boundaries in its discoverable schema, not only in repository rules. Native MCP tasks are worth a compatibility spike, not an unconditional migration.

**Benefit:** Fewer model decisions, calls, retries and output tokens while retaining evidence. **Cost:** Medium to high because response stores, error budgets and clients are coupled. **Risks:** Losing immutable-page recovery, breaking clients, duplicated text/structured payload costs, and hiding legitimate partial results. **Feasibility:** High for better projections and consistent status fields; depends on client support for transparent polling.

Evidence: [continuation contract](../../../docs/mcp-long-running-calls.md); [context implementation](../../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.Context.cs), lines 229–290; [SymbolTools schema](../../../src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs), lines 20–50; [search matching](../../../src/AiNetCodeNavigator.Core/Symbols/SymbolNameMatcher.cs); [result tests](../../../tests/AiNetCodeNavigator.FastTests/Mcp/McpToolResultsTests.cs). The live [single-page observation](observed-context-response.json) delivered three members in 2,435 UTF-8 text bytes; the [1,024-byte experiment](observed-paged-context.json) needed four outer calls and 3,299 bytes for the same three members, then still advertised more members through a domain cursor. The intentionally small budget is not the default and does not prove typical latency or savings. The connected server's build identity was not verified; checked-in source remains the implementation authority.

### P2 — Compose a bounded change-preparation context, without a new analysis engine

**Agent problem:** Preparing a change crosses discovery, bodies, incoming uses, implementations, relevant tests and technical wiring. Many calls repeat ownership and snapshot work; separate calls can span different edits.

**Proposal:** Prototype a narrow extension of `get_context`, or a client recipe first: accept a small explicit set of symbols/current changed spans and selected questions; return exact declarations, deduplicated direct relationship evidence, implementation links, ranked test candidates and relevant artifact links under one request-owned snapshot. Use deterministic sections with their own evidence reasons and limits. Reuse existing scanners; do not recursively include all related bodies. Support explicit snapshot expectations for body continuations or fail with a clear changed-snapshot action.

A diff adapter should map additions/edits to current declarations and mark deleted/renamed symbols as requiring baseline evidence. Current source alone cannot discover deleted callers or reconstruct old contracts. Build/test artifacts can later be linked read-only with command, configuration, revision/content identity, time and outcome; stale or differently scoped artifacts must not become current verification. The first version should simply direct the agent to the existing external verification workflow.

**Benefit:** Fewer calls and inconsistent snapshots; a useful bridge from browsing to preparing a safe change. **Cost:** Medium for a client recipe/small symbol batch, high for trustworthy historical impact or generalized artifact ingestion. **Risks:** Overfetching, ranking away necessary evidence, slow all-solution scans, conflating recommendations with coverage. **Feasibility:** High for bounded composition. Diff-wide or historical impact is a separate decision.

Evidence: [context](../../../docs/navigation/get-context.md) already resolves once and runs selected sections only; [body windows](../../../docs/navigation/get-symbol-body.md) resolve current text per call; [cross-feature tests](../../../tests/AiNetCodeNavigator.FastTests/Symbols/CrossFeatureRelationshipContractTests.cs) cover reusable identity and distinct limits. Existing location selectors already exist in [SourceSymbolResolver](../../../src/AiNetCodeNavigator.Core/Symbols/SourceSymbolResolver.cs), lines 318–362: do not invent a redundant location-to-symbol subsystem.

### P2 — Optimize resident analysis only against measured workload costs

**Agent problem:** Small answers can require large hidden work. Every fresh source boundary checks structure and disk-backed documents; a fixed count of four resident solutions does not bound Roslyn memory. Extra navigational calls can amplify refresh cost.

**Proposal:** Measure cold load, warm refresh, identity generation, semantic analysis, formatting, working set and reloads independently. First exploit batching and request-owned snapshots; preserve content-based freshness. If needed, add observable resource budgets/pressure-aware eviction and explicit selective project loading. A project subset must report its boundary, especially for incoming references. File watchers can be hints only, with a trustworthy rescan/failure fallback. Consider an opt-in pinned read session only if measurements justify its stale-state and lifecycle costs.

**Benefit:** Targets real latency and resource bottlenecks rather than cosmetic response size. **Cost:** Small/medium for measurement; medium/high for loading and invalidation redesign. **Risks:** False freshness, reload thrashing, missing callers, imprecise memory estimates and generator overhead. **Feasibility:** High for measurement and shared snapshots; selective loading needs representative MSBuild projects. No present evidence establishes that this repository already exceeds an acceptable memory or latency budget.

Evidence: [ResidentSolution](../../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs), lines 310–339 and 586–680; [structure fingerprint](../../../src/AiNetCodeNavigator.Core/Workspace/SolutionStructureFingerprint.cs), lines 22–75; [registry limits](../../../src/AiNetCodeNavigator.Core/Workspace/ProjectRegistryOptions.cs), lines 7–18. Dependency collection already has bounded scalar caching and optimized cold outgoing scans: reuse that design rather than proposing a new universal graph cache ([current scanner contract](../../../docs/navigation/dependency-graph.md)).

## Tool decisions

All decisions below are reviewer recommendations. There is no evidence-based reason to remove an entire current tool immediately. Several earlier-looking overlap problems are already addressed by selected views/sections in the current design.

| Tool | Disposition | Concrete reason/change |
| --- | --- | --- |
| `find_symbol` | Keep; revise matching contract | Batched discovery and extension-only search are useful. Prefer explicit matching semantics; preserve overload/project disambiguation. Do not create a separate extension tool. |
| `get_symbol_body` | Keep | Batch/window extraction is a narrower, cheaper intent than general context. Share its extraction and snapshot rules with `get_context.body`; keep the public shortcut unless measured client usage favors an alias. |
| `get_file_skeleton` | Keep; clarify completeness | File-level orientation and batched file selection are distinct. Clearly signal top-level-only/nested omissions; consider bounded nesting only for demonstrated tasks. Do not sell it as full-file understanding. |
| `browse_target` | Keep and extend | Scope and namespaces are already combined. Add a separate environment view with defined coverage; preserve cheap explicit selection. |
| `get_call_tree` | Keep | Call paths differ from general references/type dependencies. Prefer shallow graphs; defer alternative renderings unless users need them. No evidence warrants deleting Mermaid now. |
| `find_references` | Keep; strengthen agent guidance | Incoming symbol-use evidence and summaries matter. Explain depth semantics, runtime gaps and result-vs-analysis limits. Share direct-use projection with context, not one giant public graph tool. |
| `get_type_relations` | Keep | Hierarchy and implementations are already sensibly merged through a required relation. Assembly scope limits must remain explicit. |
| `dependency_graph` | Keep; make analysis cost/scope discoverable | Project/file/namespace/type views reuse facts. A member root selects its type; an incoming query is broad. Do not imply missing edges can always be recovered by another page. |
| `resolve_type_origin` | Keep; reuse in follow-up links | Correct metadata/source ownership bridges source and DLL navigation. Where already known, return the origin link directly rather than forcing an extra call; retain explicit lookup for ambiguity. |
| `get_context` | Keep as composition point; change envelope | Selected sections and one shared analysis are the right foundation. Keep test evidence reasons; add bounded multi-root/change recipe only after measurement. |
| `inspect_assembly` | Keep | Public API overview and reference inventory differ from C# declaration-name search. Unify field/default semantics; do not merge just to lower tool count. |
| `search_assembly` | Keep | Literal decompiled implementation search answers questions semantic declaration search cannot. An eventual shared text-search backend can remain an implementation detail. |
| Offline exporter | Keep separate | Broad/offline browsing and targeted MCP use have different costs and write/lifetime contracts. Preserve catalog/manifests and partial-state checks. |

Consolidate shared extraction, direct-use evidence and delivery mechanics, not every intent into a generic `query` tool. Removal candidates are redundant fields/control loops and inconsistent aliases/defaults after migration, not substantiated product capabilities.

## Framework and UI-stack priorities

| Stack | Practical next step | Limit of a reliable claim |
| --- | --- | --- |
| .NET Framework 4.8 | Test an old-style csproj/packages.config solution and an SDK-style net48 project with reference assemblies, imported targets and adjacent/GAC dependencies. Report selected tooling and exact binary identity; inspect binding redirects as technical evidence. | Running on .NET 10 does not prove these projects load, nor that they cannot load. GAC resolution with exact identity checks is implemented for assembly analysis/decompilation and export; that does not establish MSBuildWorkspace loading of net48 source projects or final runtime binding after redirects/plugins. |
| .NET 10 | Verify generators, additional inputs, conditional compilation and multi-TFM ownership in realistic solutions. Preserve current captured generator/metadata identity. | Repository net10 fixtures establish selected cases, not every SDK/generator. External generator environment/file reads are explicitly outside complete provenance. |
| Blazor | Prove how Razor SDK inputs and generated documents appear; connect `.razor`, partial code-behind and generated C# with validated source mappings. Include explicit component/event/parameter relationships incrementally. | Render lifecycle, cascading values, dynamic components and JavaScript interop cannot become a complete static execution graph. |
| WPF | Link XAML `x:Class`, code-behind and explicit handlers; return resource/binding/command names as inspectable evidence with resolved or candidate classification. Include legacy and SDK-style WPF fixtures. | Inherited or runtime-assigned `DataContext`, templates, dynamic resources and reflection prevent universal static binding resolution. |

Microsoft's [Razor SDK documentation](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/sdk?view=aspnetcore-10.0) describes Razor items and source generation; its [WPF binding overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/) explains DataContext-based binding resolution. The [Framework developer guide](https://learn.microsoft.com/en-us/dotnet/framework/install/guide-for-developers) distinguishes targeting/developer packs from installed runtimes. These sources support the proposed fixture boundaries; they do not prove Navigator compatibility. See the local [workspace review](workspace-evidence.md) for the absence of direct UI/legacy fixture evidence in this pass.

## Alternative product cuts and external evidence

1. **Keep a specialized independent read-only service — preferred provisionally.** Valuable where agents lack IDE integration, where exact snapshot provenance matters, and for managed DLL investigation. Expand technical evidence deliberately.
2. **Use an IDE/LSP backend for routine source queries — compare, do not rewrite yet.** [Serena](https://github.com/oraios/serena) already offers symbolic retrieval and editing, and documents a [Roslyn C# backend](https://oraios.github.io/serena/02-usage/050_configuration.html). Its existence makes generic symbol navigation less distinctive. Its advertised efficiency is not an independent benchmark or evidence of equivalent legacy/UI coverage. Test integration costs and gaps before replacing custom source engines.
3. **Concentrate on libraries/environment and delegate source navigation — legitimate fallback.** If a controlled comparison finds no incremental value in Navigator's source workflows, reduce that product surface and retain unique binary/ownership/environment capabilities. There is currently insufficient comparative evidence to make that cut.

The official [MCP Tasks extension](https://modelcontextprotocol.github.io/ext-tasks/specification/2026-07-28/tasks.html) and [C# SDK tasks guide](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tasks/tasks.html) describe client opt-in and automatic polling support. This supports a feasibility investigation for reducing model-level polling. It does not establish support in the user's clients, a drop-in replacement for current budgets/cursors, or that the separate extension package is present in this repository.

## Explicitly not recommended now

- A universal runtime call/DI/UI binding graph. Return proven static facts and attributed candidates; runtime observations may be imported later as scenario-specific evidence, never universal completeness.
- Adding editing, refactoring, linting, auditing, build execution or a custom test runner merely to broaden the product. Existing tools already perform those jobs; preserve the current read-only navigation boundary. Linking existing verification evidence would be a deliberate narrower extension.
- A vector database, LLM summaries or whole-repository knowledge graph as the default source of truth. First demonstrate a name-discovery failure that lexical/semantic search cannot solve. Any later semantic retrieval should propose leads with source evidence, not answer exact-reference questions by similarity.
- Always load every TFM, crawl every dependency, decompile everything or return every body. These can multiply work while still failing to establish runtime completeness. Offer explicit scope expansion.
- Drop content hashing, snapshot identities or omission metadata for speed/token savings. Optimize scheduling and repeated work before weakening correctness guarantees.
- Immediately merge the catalog into one powerful query tool or replace the server with Serena/LSP. Reduced tool count is not reduced cognitive complexity; parity and actual task cost must decide.
- Persist complete captured repository context as automatic memory. References, binaries and tests change. Store decision/evidence provenance and reacquire current source evidence when needed.

## Verification and confidence

Reviewed current README/docs, public schemas, targeted implementation and test sources; two independent focused reviews contributed evidence. Live MCP discovery and context calls were successful, including a complete four-page outer response reconstruction. Raw observations are retained here. No build, automated test suite, framework fixture, large-solution benchmark, client handshake or comparative agent trial was run. Tests mentioned above were inspected, not reported as passed.

The review changes documentation/evidence artifacts only. `docs/` is intentionally unchanged because implemented product behavior is unchanged. The final documentation checks and commit are recorded in the [status page](README.md).
