# Independent evaluation of AiNetCodeNavigator for agentic C# development

**Research date:** 2026-10-10. All external sources below were accessed on that date.

**Repository baseline:** `daabcd82d6385ac3f8b429f0dcda24008fa7beb3`.

**Status:** Evidence-based assessment and proposals, not an approved implementation specification.

**Scope:** All twelve current MCP navigation tools, their practical alternatives, and the claims in [the earlier evaluation](evaluation-and-proposals.md). No production behavior was changed.

## 1. Decision

**AiNetCodeNavigator has a defensible technical purpose, but its net benefit for agentic programming has not been demonstrated.** Compiler-backed references and implementation mappings, explicit loaded-solution scope, and navigation into compiled assemblies are substantive capabilities. They are not equivalent to finding matching text. However, capability is not evidence of better task completion, lower cost, or faster development.

The earlier evaluation is **not reliable enough to justify its proposed redesign**. It combines some correct observations with misattributed research, unsupported numerical claims, an incomplete comparison with current clients, and a proposal that conflicts with the project's explicit product boundary. In particular, neither twelve tools nor pagination proves an unsuitable agent interface.

My recommendation is to retain the semantic navigation capability, improve its agent-facing ergonomics selectively, and measure it against two serious alternatives: shell/file tools, and shell/file tools plus a working C# language server. Do not implement the proposed five-tool rewrite, add compiler diagnostics, raise all response defaults, or remove tools merely on the authority of the earlier document.

This is conditional support, not an endorsement of unlimited investment. If a working C# LSP gives equivalent answers with less operational burden, and representative tasks show no additional benefit from Navigator, maintaining this separate MCP would be unnecessary for that environment. Removing it would then be a valid evidence-based decision. The present evidence establishes neither universal necessity nor universal uselessness.

## 2. Method and limits

Three subagents used `gpt-6-luna` with high reasoning effort: one researched official OpenAI documentation, one researched official Anthropic documentation, and one reviewed local implementation independently. They were explicitly instructed not to read the earlier evaluation before forming their assessments. The parent inspected that document, checked its citations, reviewed code and contracts, and performed local experiments. Separate inputs reduce anchoring; agents using the same model are not statistically independent experimental replications.

Evidence is distinguished throughout:

- **Verified implementation:** current source, registered schemas, and documented contracts checked against code.
- **Observed behavior:** the executed exploration and focused test selection below.
- **External evidence:** opened official product documentation and original research papers.
- **Judgment/proposal:** conclusions about likely utility, priority, or a future design. These are not measured effects.

The installed Navigator MCP was used for targeted declaration and relationship queries. Its schema inventory was compared with local registrations. The separate exploration runner built and invoked the local production handlers, establishing local-source provenance for the twelve-tool experiment. The installed process was not independently identified as the exact same build; its supplemental results are identified separately.

No end-to-end coding benchmark, paid model comparison, live Claude Code C# LSP comparison, memory benchmark, or cold/warm latency distribution was conducted. No dollar savings, token savings, task-success uplift, or agent error rate is claimed. Repository rules currently prefer Navigator for C# declarations; that is a workflow instruction, not evidence that Navigator wins an unbiased comparison.

The binding local references are [docs/README.md](../../docs/README.md), production source, and tests. Task proposals are not proof of implemented behavior. This assessment belongs under `tasks/`; it does not change current-state `docs/` or embedded `--doc` pages.

## 3. What current OpenAI and Anthropic capabilities change

### 3.1 OpenAI: MCP is supported, and ordinary repository access is already a baseline

Codex CLI can read local repositories and execute commands. Codex MCP configuration is shared between supported local surfaces. File discovery, bounded file reads, and builds are therefore available alternatives; a navigation MCP needs to add useful information or reduce recurring work. These docs do not establish a built-in compiler-backed C# index. Absence from the reviewed pages is not proof that no other Codex integration can provide one. [Codex CLI](https://learn.chatgpt.com/docs/codex/cli), [MCP integration](https://learn.chatgpt.com/docs/extend/mcp).

OpenAI's API documentation supports deferred tool loading. The Responses API requires explicit tool-search configuration; the Agents API documents automatic MCP discovery when the model/provider supports it. Discovery adds a step and depends on finding the right tool. These API features must not be assumed to have identical defaults in every local Codex client. Thus, schema overhead is client-dependent, and a fixed twelve-to-five reduction does not establish a fixed savings percentage. [Tool search](https://developers.openai.com/api/docs/guides/tools-tool-search).

Compaction supports longer interactions by carrying state forward more compactly. It does not provide missing compiler facts or demonstrate that large navigation responses are free or preferable. [Compaction](https://developers.openai.com/api/docs/guides/compaction).

### 3.2 Anthropic: a working C# LSP is now a direct competitor

Current Claude Code documentation lists `LSP` alongside file, search, editing, and execution tools. It supports definitions, references, and diagnostics; the earlier description of a five-tool production client with an optional experimental LSP flag is an inadequate current baseline. [Tools reference](https://code.claude.com/docs/en/tools-reference).

The official code-intelligence documentation lists `csharp-lsp`, backed by `csharp-ls`. Users need the plugin and the language-server executable installed and available; code-intelligence servers are supported in terminal sessions, while cloud sessions do not start plugin LSP servers. These are documented capabilities and prerequisites, not a verified installation in this workspace. The current exact minimum Claude Code version for C# LSP was not established. [Code intelligence plugins](https://code.claude.com/docs/en/plugins/code-intelligence).

Claude Code's current MCP documentation describes tool search as the default, deferring definitions until needed, with provider/model/configuration exceptions. Tool names and server instructions still matter. It also documents description-length limits, so critical selection guidance should appear early. This weakens the assumption that every connected MCP schema always occupies full context from the start. [Claude Code MCP](https://code.claude.com/docs/en/mcp#scale-with-mcp-tool-search).

Anthropic recommends clear tools, realistic agent evaluations, useful identifiers, and restrained result volume, including pagination, filtering, ranges, and truncation. It does not support the blanket claim that pagination is harmful. [Writing effective tools](https://www.anthropic.com/engineering/writing-tools-for-agents).

Anthropic's advanced-tool-use article demonstrates benefits of deferred discovery on its own tool-use evaluations, while acknowledging additional search latency. Those results concern its experimental setup, not Navigator, C# correctness, or a twelve-tool product. [Advanced tool use](https://www.anthropic.com/engineering/advanced-tool-use).

### 3.3 Consequence for the product

**Inference:** stronger models can choose better queries and reason better over retrieved code, but model ability and context capacity do not by themselves reveal which declaration a call binds to in the current build configuration. Compiler-backed navigation can still help. Its distinct value shrinks wherever the agent already has working equivalent language-server capabilities.

The relevant comparison is the complete workflow, not MCP versus no tools. A read-only MCP and an LSP plugin can expose similar compiler facts through different interfaces. For generic definitions, references, and implementations, duplication is a serious maintenance and deployment concern. Navigator's potential differentiation is its supported source/assembly workflows, owner-qualified handoffs, explicit analysis scope, selected context sections, and richer bounded relationships. Their incremental usefulness remains to be measured.

## 4. Audit of the earlier evaluation

| Earlier claim | Finding | Evidence and implication |
|---|---|---|
| Roslyn 5.9 and ILSpy form the technical basis | Correct dependency identification; quality conclusion overstated | [Central package versions](../../Directory.Packages.props) pin Roslyn 5.9.0 and ICSharpCode.Decompiler 10.0.1.8346. Packages, fingerprints, and tests alone do not prove enterprise readiness, memory safety, or agent usefulness. |
| Roslyn is indispensable and solves DI navigation | Too absolute | Compiler binding and implementation mapping are valuable. A static interface target does not identify the runtime DI registration, selected implementation, reflection path, or external consumer. See [call evidence](../../docs/navigation/get-call-tree.md). |
| Twelve tools overload LLM tool selection | Unproven | No Navigator agent-selection experiment is supplied. Current deferred discovery changes upfront overhead; descriptions and overlapping semantics still need evaluation. |
| `find_symbol` has 17 parameters; `get_context` has 14 | Incorrect for the inspected catalog | Both expose 19 model-visible arguments, excluding injected `CancellationToken`. See [symbol registration](../../src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs) and [context registration](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.Context.cs). Counts measure schema size, not mandatory call complexity. |
| Large context windows make pagination obsolete | Unsupported conclusion | Anthropic recommends bounded output; discovery, reading, and compaction still have costs. More available context does not imply that all results should be returned. |
| Replace early polling with a 15–20 second synchronous wait | Partly already implemented | [NavigatorHostRuntime.cs](../../src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs) already defines a 15-second initial response window. [LongRunningToolCallStore.cs](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs) defaults to a one-second poll wait; running responses advertise a 1,000 ms retry interval, not the claimed 500 ms. |
| MCP standards recommend avoiding polling over stdio | Not established; the blanket argument is contradicted | MCP's experimental task facility explicitly supports polling and deferred results. Navigator's custom `operationToken` is not that standard facility. [MCP Tasks](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks). |
| Polls cause thousands of thinking tokens, 15–30 seconds per turn, and loops | No product-specific evidence | No traces, model settings, latency distribution, or controlled comparison are provided. This is a risk hypothesis, not a measured failure. |
| File skeletons save 90% of reading tokens | Unsupported and not universally true | The executed counterexample in section 7 has a larger skeleton than the source in normalized UTF-8 bytes. Byte size is not billed token size; neither result supports a universal token percentage. |
| Long `handoffId` strings are fragile; simpler names/locations are proven better | General ergonomics support exists; no Navigator-specific comparison | Anthropic reports advantages of meaningful names/simple identifiers over cryptic IDs in retrieval tasks. That does not establish that raw C# names or moving line numbers outperform owner-qualified signature handles. Measure copy failures and output overhead before removing identity guarantees. [Tool-writing evidence](https://www.anthropic.com/engineering/writing-tools-for-agents). |
| Compiler diagnostics are the missing required feature | Conflicts with current scope; not a navigation necessity | [Product boundaries](../../.agents/rules/03-product-boundaries.mdc) explicitly exclude diagnostics, linting, auditing, and refactoring. Agent builds/tests are supplied externally. Diagnostics might belong in another tool, but this evaluation does not authorize that scope change. |
| `Compilation.GetDiagnosticsAsync()` is the relevant API | Inaccurate API reference | The documented API is `Compilation.GetDiagnostics(CancellationToken)`; it excludes emit diagnostics. In-memory compiler feedback is not a complete build/test replacement. [Microsoft API reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.compilation.getdiagnostics?view=roslyn-dotnet-5.3.0). |
| Five tools reduce the tool prompt by about 60% and drastically reduce errors | Unmeasured | Tool count is not schema-token count. A single broad tool can require more options and return more unwanted content. No comparison supports the number or error-rate claim. |
| The proposed simplified schemas cover the old toolset | Incomplete | Several omit the explicit source target; the inspect proposal omits selective body/member handling, hierarchy selection, outgoing traversal, dependency projections, filters, bounds, and recovery contracts. A default complete context would frequently overfetch. |

### 4.1 Research citations: real studies, exaggerated interpretation

**Rombaut is a real source, but the attribution is misleading.** [Inside the Scaffold](https://arxiv.org/html/2604.03515v1), sections 2.2, 3.5, and 7, is a qualitative source-code taxonomy of thirteen agents. The 12–82% longer failure trajectories are discussed as another study's result in related work. The paper explicitly describes model/scaffold confounding and proposes controlled experiments as future work. It does not demonstrate that Navigator's tokens cause loops, that twelve tools fail, or that scaffold design generally outweighs model differences.

**SWE-Master supports investigating semantic navigation, with substantial transfer limits.** [SWE-Master](https://arxiv.org/html/2602.03411v1), section 5.3, tests LSP with Pyright on Python-only SWE-bench Verified. Its same-model comparisons report improvements from 68.4% to 70.4% and 66.2% to 67.6%, with somewhat fewer turns. Its larger token-efficiency changes also involve continual training; the authors explicitly warn against attributing those changes solely to LSP. This is evidence for potential utility, not for C# Navigator savings or a universal industry standard.

**The combined SWE-Master/HyperAgent citation is not an identifiable paper as written.** The retrieved originals have different titles and authors; [HyperAgent](https://arxiv.org/abs/2409.16299) was submitted in September 2024, not 2026. The earlier document's generic Anthropic, OpenAI/SWE-bench, MCP, and Microsoft bibliography entries also lack precise links and reproducible locations. This assessment replaces relevant claims with directly linked sources. Failure to verify an exact title is not proof that every underlying idea is false.

## 5. Evaluation of each current MCP tool

Priorities are qualitative judgments for agentic C# work, not measured rankings. **Core** means a strong candidate for routine semantic navigation; **situational** means valuable for particular tasks; **optional** means a plausible candidate for a reduced client profile. These proposed profiles are not current server features. Argument counts include optional paging/budget controls and exclude injected cancellation.

| Tool / arguments | Actual value and alternative | Limits and costs | Recommendation |
|---|---|---|---|
| `find_symbol` / 19 | Finds indexed declarations and returns signature, owner, location, and stable reference; supports source, assembly, batching, and declared extension methods. More precise declaration discovery than general text search. LSP workspace-symbol search overlaps. | The query still matches names/patterns; it does not explain arbitrary expressions. Extension receiver filtering does not prove expression applicability. Regex detection and mutually exclusive selectors increase input rules. | **Core. Keep.** Consider one canonical pattern-array input and clearer match-mode guidance if observed mistakes justify it. Preserve signature/project disambiguation. |
| `get_symbol_body` / 9 | Reads selected declarations by discovered identity, in batches, including decompiled bodies. Avoids rediscovering file/member boundaries. Known file ranges can be read directly. | Source reads often duplicate a file reader. Body windows are declaration-relative, not physical edit coordinates, and separate calls can observe changed snapshots. | **Situational. Keep the capability.** Prefer direct reads when file/line are known. Make physical ranges available beside declaration windows; evaluate routing single-symbol reads through `get_context`, retaining batching. |
| `browse_target` / 14 | `scope` reports what Roslyn actually loaded: owners, frameworks, generated/test documents, exclusions. A file glob reports what exists on disk. `namespaces` offers bounded symbol discovery, including assemblies. | Scope loading can cost more than filesystem discovery. Namespace browsing is usually avoidable for a known symbol. Irrelevant view options are rejected. | **Situational. Keep scope visibility.** Use it for coverage/ownership questions, not as a mandatory opening call. Namespace browsing can be optional; deletion of the whole tool is unjustified. |
| `get_file_skeleton` / 6 | Gives outlines without bodies/initializers, batches files, and handles source/assembly references. Useful for unfamiliar large files. LSP document symbols overlap. | Omits nested types, does not supply physical member ranges, and repeats long handles. For small or declaration-heavy files it can be larger than source. | **Situational, not automatically the highest-value tool.** Add physical declaration ranges and test compact identity representation before claiming savings. Direct reading remains preferable for small known files. |
| `get_type_relations` / 12 | Queries hierarchy or actual implementation/override mappings. Strong for interface/member changes, explicit implementation, and cross-project ownership. LSP implementation/type hierarchy overlaps. | Requires the correct relationship/root; metadata ownership can be ambiguous. Static possible implementations are not the runtime DI choice. Coverage is the loaded scope. | **Core. Keep focused semantics.** Do not bury it in a default complete-context response. Clear examples should distinguish interface methods, overrides, and hierarchy. |
| `get_call_tree` / 15 | Bounded caller/callee traversal with sites and evidence labels. Useful for following a behavior path across methods. LSP call hierarchy overlaps. | Includes property/member access as well as calls. Interface/virtual targets are static. `topN`, depth, and the node cap can omit edges. A type seed gives hints rather than a method graph. | **Situational. Keep.** Start with a method, depth 1, one direction. Evaluate a compact edge format, but do not assume JSON necessarily outperforms the existing ASCII representation. |
| `find_references` / 13 | Compiler-backed reference locations; catches method groups/member uses rather than just name matches. Summary and paging help impact analysis. LSP references is a close alternative. | Static references are not runtime execution or external consumers. Depth 2–3 follows callers within a 200-symbol visit bound; pages do not increase analysis coverage. | **Core. Keep direct references.** Depth 1 should be the normal path. Evaluate removing transitive depth from the public surface if it confuses agents; do not equate reference-site lists with call graphs. |
| `dependency_graph` / 13 | Type/file/namespace relationships and evaluated source project references. Useful for finding ownership boundaries, architectural consequences, and cross-project feature work. Project XML/text search or LSP covers parts. | Member seeds select their containing type. Incoming/both semantic queries may scan the full eligible solution. Long owner identities inflate results; bounds need interpretation. | **Optional for daily use, valuable on demand.** Keep outside a minimal default profile if measurements support that. Do not delete it on the assumption agents only fix local bugs. Prefer shallow outgoing or project projection when adequate. |
| `resolve_type_origin` / 7 | Answers source-versus-metadata ownership and yields proven assembly paths. Bridges source use to assembly navigation. | Searching loaded metadata is different from searching source declarations. Unknown or incomplete reference coverage prevents global uniqueness claims. | **Situational. Keep the lookup capability.** Enrich discovered symbols with cheap known origin facts where useful, but do not assume that replaces metadata lookup by a type name absent from source declarations. |
| `get_context` / 19 | Shares one symbol resolution/snapshot across selected body, members, direct uses, and static test candidates. Useful when several sections are needed together. | Not a universal relationship query: no hierarchy or outgoing call tree. Tests are heuristics, not executed coverage. Sections have distinct cursors/windows; ineffective explicit options fail. | **Core for combined questions. Keep selective sections.** Improve conditional-argument guidance and recovery. Do not return every section by default or replace all specialized relationships with this route. |
| `inspect_assembly` / 16 | Compact compiled type/API overview, optional members and reference inventory. Useful without source, and for exact public API questions. | Overlaps assembly `find_symbol`, namespaces, and member context. Adds a separate schema; public-API inspection differs from arbitrary text search. | **Situational assembly capability.** Consider an assembly-profile entry point or shared internal projection. A public merger with text search needs evidence and an explicit mode, not an ambiguous `query`. |
| `search_assembly` / 16 | Literal/regex search over decompiled text, including strings or implementation details absent from declaration names. | Decompiler reconstruction and missing dependencies limit semantic confidence. For repeated broad exploration, an offline export plus `rg` can amortize retrieval work. | **Situational. Keep text search distinct from symbol search.** Test a shared assembly entry point, but preserve explicit search mode, bounds, source meaning, and body handoffs. |

Implementation anchors: [symbol tools](../../src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs), [structure tools](../../src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs), [relationship tools](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs), [type relations](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.TypeRelations.cs), [selected context](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.Context.cs), [assembly tools](../../src/AiNetCodeNavigator/Mcp/Tools/Assemblies/AssemblyTools.cs).

The semantics and limits are documented in the corresponding [navigation contracts](../../docs/README.md#navigation). In particular, [SourceTypeOriginScanner](../../src/AiNetCodeNavigator.Core/Assemblies/SourceTypeOriginScanner.cs) searches loaded metadata references when required, whereas source declaration discovery does not simply enumerate all metadata types. [FindReferencesResolver](../../src/AiNetCodeNavigator.Core/Symbols/FindReferencesResolver.cs) uses Roslyn `SymbolFinder` for references and implementation/override mapping. These are concrete capabilities, not merely wrappers around grep.

## 6. The wider operational and economic view

### 6.1 Correctness: static evidence has a defined boundary

Roslyn improves identity and binding within a loaded configuration. It does not make runtime behavior fully observable. Reflection, DI registration/selection, dynamic calls, native boundaries, omitted projects/frameworks, generated code filters, and external consumers require other evidence. A reported complete reference page can coexist with limited analysis; no-match results are meaningful only within their reported scope. Assembly source is reconstructed, not the original source. [Scope](../../docs/navigation/get-index-scope.md), [relationship contract](../../docs/navigation/relationship-contracts.md), [assembly navigation](../../docs/navigation/assembly-navigation.md).

Long owner-qualified IDs have a purpose: same-named projects, linked files, duplicate types, and overloads must not silently collapse. They also consume output space and may be difficult to reproduce. A useful experiment is compact opaque aliases with the exact owner retained in session state, or a response-local identity table. Such a design adds lifetime/staleness obligations; it must not trade small output for silent wrong-owner navigation. Current references already accept raw names/documentation IDs/locations in supported routes and provide ambiguity recovery. [Symbol resolution](../../docs/navigation/symbol-resolution.md).

### 6.2 Cost: include both the agent and the resident analysis process

The expected economics depend on how often a query avoids ambiguous searches or unnecessary reads. A cold solution needs MSBuild/Roslyn loading and compilation context; a known file read does not. Resident workspaces, refresh, caches, scratch files, and eviction add lifecycle complexity. Large incoming relationship queries can do significant analysis even when the response is small. [MSBuildSolutionLoader](../../src/AiNetCodeNavigator.Core/Workspace/MSBuildSolutionLoader.cs), [ResidentSolution](../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs), [dependency collection/cache contract](../../docs/navigation/dependency-graph.md).

A fair cost ledger includes model input/output/reasoning usage where exposed, cached-input billing, tool schemas and discovery, call/poll/page counts, build/test work, elapsed developer time, CPU/RAM, setup, and maintenance. Subscription usage and API dollar billing are different measures. A `cl100k_base` counter is a useful proxy, not the tokenizer or bill for every OpenAI/Gemini/Claude model. Navigator already makes that distinction in its [traffic-capture measurements](../../docs/mcp-traffic-capture.md).

Large context windows and compaction weaken neither the need for relevant results nor the economic question. Conversely, a few extra calls are not automatically waste: requesting ten relevant items and later continuing can cost less than reading hundreds irrelevant to the task. Measure total successful-task cost, not just response count.

### 6.3 Three continuation mechanisms: legitimate jobs, real ergonomics burden

`operationToken` identifies a running operation for polling, `continuationToken` delivers immutable outer text pages, and `resultCursor` selects another domain page. They solve different problems. Context body windows add another position to manage. The bindings preserve query/snapshot consistency, but requiring the model to preserve options and choose the correct next token increases interaction complexity. [Long-running calls](../../docs/mcp-long-running-calls.md), [response budgets](../../docs/mcp-response-budgets.md).

The critique is therefore narrower than “paging is toxic”: **recovery should become less work for the model without dropping consistency or bounds.** A client wrapper could wait/poll automatically and reconstruct outer pages within its own configurable total cap; domain paging remains a semantic decision. The local exploration runner already automates running/retry responses and outer pages, showing that this is technically separable from tool semantics. That runner is an experiment harness, not an installed client feature. [Exploration contract](../../docs/development/mcp-exploration.md).

The initial server wait is already 15 seconds. Investigate whether real hosts support experimental standard MCP tasks before replacing the custom operation contract; support cannot be presumed merely because the specification exists. Do not increase budgets to 128 KiB indiscriminately: current public maximum is 65,536 bytes, and changing it is a contract/resource decision. First measure which narrowly useful responses actually require outer pagination.

### 6.4 Safety, deployment, and maintenance

Navigation does not edit analyzed source or execute analyzed assemblies. Source loading nevertheless evaluates MSBuild projects; the repository explicitly states that this is not a sandbox for arbitrary custom targets. Isolated analysis scratch is not proof that opening an untrusted project has no effects. Source loading also needs suitable installed SDK/MSBuild tooling, regardless of the server's self-contained package. [Setup boundary](../../docs/setup/README.md), [host/scratch contract](../../docs/mcp-host.md).

Read-only navigation does not prevent repository text, comments, or decompiled strings from containing misleading instructions. Agents should treat retrieved content as evidence. Optional traffic capture records source and local paths, adding a retention/privacy consideration when enabled. These concerns also exist with ordinary readers and language servers; they are operating costs, not an argument unique to MCP.

The current test infrastructure provides substantial contract evidence, including identity, limits, snapshots, and routing. It does not by itself demonstrate robustness on every real solution or a net agent productivity benefit. Full process E2E tests are excluded from routine gates; this assessment did not execute them. Treat maintainability and enterprise reliability as properties requiring operational evidence rather than package-name endorsements. [Registration status](../../docs/navigation/mcp-registration-status.md).

## 7. Executed evidence

### 7.1 Local twelve-tool exploration

Executed successfully at the repository baseline:

```powershell
pwsh -File ./scripts/explore.ps1 -Scenario ExploreAllTools
```

The existing [scenario](../../tools/AiNetCodeNavigator.Exploration/Scenarios/ExploreAllTools.cs) supplies the exact requests. It uses this repository solution for source queries and the newly built exploration executable assembly for assembly queries. It runs the SDK-generated schemas/binder and production handlers without JSON-RPC transport. Exit code was 0. This verifies dispatch and inspected output, not complete answers to twelve realistic development tasks.

Local artifacts are under `temp/exploration/ExploreAllTools/20261010-181800-3258241/`, including requests, responses, payloads, and per-attempt files. They are Git-ignored and not durable evidence for other checkouts; the request source and numerical/qualitative observations here make the experiment reproducible. Snapshot IDs and local assembly dependency availability can differ on another machine.

| Call | Stored payload bytes | Attempts | Observed meaning |
|---|---:|---:|---|
| `find_symbol` | 625 | 1 | Located `NavigatorHostRuntime`, with source handle. |
| `get_symbol_body` | 2,030 | 1 | First 30 declaration lines; further body window available. |
| `browse_target` | 4,951 | 1 | Seven projects, 309 loaded C# documents, 25 generated, 121 test documents. |
| `get_file_skeleton` | 3,791 | 1 | One top-level type, fifteen direct members; nested types/initializers omitted. |
| `get_context` | 5,453 | 1 | Members-only query returned partial status and a section cursor. |
| `get_call_tree` | 740 | 1 | Type seed returned one node, zero edges, and method hints. This is not a method-traversal test. |
| `get_type_relations` | 455 | 1 | Hierarchy query for the selected class. |
| `find_references` | 6,796 | 1 | Bounded direct-reference query; not an exhaustive task benchmark. |
| `dependency_graph` | 16,072 | 1 | Type projection with a limit-recovery action; long endpoint owner identities visible. |
| `resolve_type_origin` | 4,693 | 1 | Proven referenced Core assembly owner, with incomplete-search omissions and diagnostics. |
| `inspect_assembly` | 1,713 | 1 | Bounded overview with members requested. |
| `search_assembly` | 5,121 | 1 | Decompiled text search for `ExplorationContext`. |

Stored payload sizes include the artifact's line endings, excluding attempt headers and outer control text. They are not token counts or complete JSON-RPC frame sizes. All twelve calls completed on their first attempt, so this run contains no observed polling/outer-page burden. It does contain partial domain/body results. The scenario does not exhaust domain cursors or every body window.

**Concrete negative result:** `NavigatorHostRuntime.cs` contained 3,471 UTF-8 bytes. After decoding both texts and normalizing CRLF to LF, the skeleton payload contained 3,759 UTF-8 bytes: about 8.3% larger, before response controls. Signatures and repeated long handoffs outweigh removed bodies in this file. This disproves universal size savings, not the usefulness of outlines for body-heavy files. The underlying [skeleton model](../../src/AiNetCodeNavigator.Core/Skeletons/SkeletonModels.cs) has no physical line ranges; the [renderer](../../src/AiNetCodeNavigator.Core/Skeletons/SkeletonMarkdownRenderer.cs) prints handles for types/members.

**Concrete assembly limit:** origin resolution found `AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec` in `AiNetCodeNavigator.Core.dll`, but reported `incompleteReferenceSearch` and `referenceDepthLimit`. Diagnostics included 865 semantic decompiler/reference diagnostics, conflicting attribute types from `mscorlib`/`System.Private.CoreLib`, a depth limit of eight, a 128-assembly resolution limit, and unresolved dependencies. The useful owner is real; global uniqueness/completeness is not established. These are diagnostics of the reconstructed navigation compilation, not proof that the original assembly cannot run. This deserves a separate focused investigation before advertising reliable arbitrary-binary semantic navigation; no cause or correction was established here.

### 7.2 Supplemental installed-MCP probe

On the installed MCP, a `find_symbol` query with `pattern=SourceSymbolBodyResolver.ResolveAsync`, `kind=method`, `signatureFilter=suppliedIdentity`, `scopeType=production`, and `maxResults=5` selected the internal overload at physical declaration line 45. Its returned handle was passed unchanged to `find_references` with depth 1, production scope, summary enabled, and page size 10. The response reported one direct site at `SymbolTools.cs:269:75`, complete within that scope. A direct source read confirmed the invocation and its arguments.

This is evidence that identity handoff and a semantic reference query work. It is not evidence of a speed advantage: `rg -n -F 'SourceSymbolBodyResolver.ResolveAsync(' src` also found the single straightforward production lead. The distinction is that Roslyn establishes the selected overload; text plus human/model inspection can establish it in this easy case as well.

An outgoing call-tree request on that exact handle, depth 1 and `topN=5`, returned six nodes, five edges, eight sites, and two hidden edges. It explicitly labeled property access as `memberAccess`, and reported the depth/top-N omission. This is useful bounded evidence, and demonstrates why a call tree should not be described as only executed calls or as complete after merely reading its full response.

### 7.3 Focused automated verification

Executed:

```powershell
pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~FindReferencesResolverTests'
```

**Result: 19 passed, zero failed, zero skipped.** Local log: `temp/test-fast.log`; TRX: `TestResults/FastTests.trx`. Among the inspected cases are actual interface/event mappings, equal-name cross-project implementations, and interface/abstract member mappings with round-tripping handles. [Test source](../../tests/AiNetCodeNavigator.FastTests/Symbols/FindReferencesResolverTests.cs).

No full solution gate, extended suite, process E2E suite, or performance suite was run. This is a documentation-only assessment; the narrow test run supplies additional factual evidence and does not certify the entire product.

## 8. Proposed next decisions

### 8.1 Changes justified for investigation now

1. **Improve source coordinates and result density.** Physical declaration ranges alongside body-relative positions and skeleton members would improve edit handoff. Measure repeated ID/owner overhead and evaluate compact representations without weakening exact ownership.
2. **Reduce model-visible recovery work.** Prototype client-side polling and outer-page reconstruction with bounded total output. Keep domain continuation and analysis omissions explicit. Evaluate before changing protocol contracts.
3. **Clarify selection rather than immediately consolidate.** Make direct references, call relationships, type relations, dependencies, and context sections unmistakably different. Provide minimal valid examples and conditional-option guidance. Simple calls already need only two or three meaningful selectors, not every optional parameter.
4. **Investigate assembly environment quality.** Reproduce the observed conflicting framework/reference closure on the same binary; separate decompiler limitations, resolution policy, and environment dependencies. Measure how often the issue affects relationship answers rather than only listing warnings.
5. **Defer broad feature investment until comparative evidence exists.** Diagnostics remain outside the current product boundary. A new broad wrapper or automatic complete context is not a demonstrated improvement.

These are proposals, not changes made by this task. No specific performance target or budget increase is justified by the current measurements.

### 8.2 Consolidation experiments, not a predetermined five-tool target

| Candidate | Possible gain | What must survive / what can get worse |
|---|---|---|
| Single-symbol body through `get_context(body)` | Fewer redundant entry points | Preserve batched body reads somewhere; do not force section-specific parameters on simple reads. |
| Assembly overview/search behind an explicit mode | Smaller selectable catalog | API inspection and literal/regex text search need different inputs; a merger can create more conditional arguments. |
| Known origin attributes in discovery/context | Fewer origin follow-ups | Keep lookup across loaded metadata references and exact assembly ownership; source declaration results alone are insufficient. |
| Direct-only reference route plus call-tree traversal | Clearer uses-versus-call semantics | References include non-call sites and aggregate locations; do not lose these in a graph-only interface. |
| Optional advanced tools/profile | Lower discovery burden for simple tasks | Do not remove namespace, dependency, or assembly capabilities needed by other tasks; no such profile is currently implemented. |

OpenAI's tool-design guidance supports grouping coherent user actions and distinguishing overlapping tools. It does not recommend minimizing tool count regardless of semantics. [Define tools](https://developers.openai.com/plugins/plan/tools).

### 8.3 A comparative experiment that can actually reject the product

Freeze the repository commit, client/server/LSP versions, model, reasoning settings, task instructions, build configuration, and installed dependencies. Build a representative C# task set from the user's actual workflow; do not select only questions Navigator is designed to answer. Use fresh sessions and counterbalanced task order, with repeated runs to expose variance.

Compare:

- **A:** native shell/file/search tools, builds/tests, and task context.
- **B:** A plus a working C# LSP integration.
- **C:** A plus the current twelve-tool Navigator catalog.
- **D:** B plus Navigator, measuring incremental value rather than treating duplication as new capability.
- **E, if warranted:** C with a compact/profiled interface, while holding underlying information coverage constant.

Include known-file fixes; same-name overload/member/owner changes; interface and explicit-implementation changes; cross-project callers and method-group usages; project-boundary feature work; generated/multi-framework scope questions; external DLL investigation; and reflection/DI cases where static navigation is deliberately insufficient. Have independent acceptance criteria, compile/test checks where appropriate, and blinded review of incorrect or missed relationships. Ground truth must not be generated solely by Navigator.

Measure task completion/correctness first, then total model usage/cost, elapsed time, tool selection/input mistakes, misleading completeness claims, polls/pages/retries, result volume, setup friction, CPU/peak memory, and cold versus warm behavior. Record edit-induced snapshot invalidation and recovery as well as initial navigation. Navigator traffic capture can measure its side; host model usage is needed for billed cost and schema/discovery overhead.

Choose success thresholds before seeing results, based on the real cost of missed changes and developer waiting. Report variation, failures, and negative results, not just means from winning tasks. Do not claim a statistically conclusive result from one run.

**Stop/remove criterion:** if B matches or improves completion and correctness, C/D add no economically meaningful improvement, and Navigator increases setup, latency, failures, or maintenance, disable it for that workload and consider discontinuing the redundant product. **Selective criterion:** if only semantic relationships or assembly investigation improve outcomes, retain those capabilities and reduce default exposure. **Expand criterion:** only invest further if a reproducible improvement remains after a serious LSP baseline and the operating costs are acceptable.

## 9. Final assessment

The earlier document identified genuine interface complexity, overlap, missing physical skeleton coordinates, and the importance of semantic navigation. It did not establish its strongest conclusions or numerical claims. Current client evolution makes its comparison incomplete, and its diagnostics proposal crosses an explicit boundary.

AiNetCodeNavigator currently offers useful, verified static-navigation capabilities with concrete limits. Its strongest case is precise C# relationships and assembly/source ownership where the agent lacks equivalent access. Its weakest case is universal use for simple file tasks or duplication of a working LSP without demonstrated incremental benefit. The defensible decision today is **selective use and comparative evaluation, not a blanket condemnation and not a claim of proven productivity gains**.
