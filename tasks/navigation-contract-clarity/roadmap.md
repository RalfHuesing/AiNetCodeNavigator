# Roadmap: Navigation Contract Clarity

## Purpose and authority

Make existing navigation behavior easier for agents to interpret and keep symbol matching consistent. This roadmap is the executable specification for the four small improvements selected in the user discussion on 2026-10-07. It does not adopt the broader architecture proposals in the [independent review](../ideen/review-2026-10-07/review.md).

Planning baseline: `8a26274876c284661efb24f9dda31f623d9995ba`; working tree clean. Status: planned; implementation has not started. The current request authorizes creating and committing this roadmap. Start implementation only when the user requests it.

## Execution contract

- Use `gpt-6.1-sol` with reasoning effort `medium` for every implementation, correction and audit agent. These explicit user settings override repository subagent defaults. Do not substitute a different model or effort silently.
- The orchestrator dispatches one implementation point at a time, checks its diff, acceptance evidence and commit, and then advances to the first unchecked point. It coordinates rather than editing production code itself.
- Each implementation agent owns exactly one point, including source inspection, implementation, appropriate tests, affected current-state documentation, acceptance evidence and an atomic Conventional Commit. Preserve unrelated changes and stage explicit paths only, following the [Git rule](../../.agents/rules/05-git.mdc).
- Read the [repository rules](../../.agents/rules/README.md) and each point's linked current-state sources before editing. Links identify owners and contracts; recheck current code rather than treating this planning baseline as permanent truth.
- Verification follows the [verification rule](../../.agents/rules/04-verification.mdc) and [build/test guide](../../docs/development/build-and-tests.md). For each code slice, run the official build and narrowest affected eligible test selection. Select relevant extended tests only as required by the rule. Run the complete eligible routine solution selection at point NCC-05. No performance benchmark is required for this roadmap.
- Mark a checkbox complete only after its acceptance criteria and verification pass and the slice is committed. Record the commit, exact commands/results and material limitations under that point's completion evidence; do not create a separate execution log.
- Raise missing decisions, conflicting contracts, unavailable requested models and scope changes in this chat. Do not infer approval from elapsed time or expand the roadmap into the broader review proposals.
- Perform an independent audit of the entire final change, followed by correction and re-audit rounds until all in-scope findings are resolved. The user's explicit request for finding rounds takes precedence over the optional workflow kit's single-correction/no-loop convention. This roadmap does not start another workflow step.

## Ordered implementation points

- [ ] **NCC-01 — Make existing search and relationship contracts discoverable**

  **Problem:** MCP tool/parameter descriptions omit semantics that are already documented and implemented. `find_symbol` does not explain its matching modes; `find_references.depth` says only traversal depth; `dependency_graph` does not clearly explain the containing-type interpretation of member roots or broad incoming analysis. An agent selecting tools can miss these facts without reading repository documentation.

  **Solution:** Update the owning description attributes so the exposed schemas explain the existing substring/wildcard/automatically detected regex and qualified-name behavior, direct references at depth 1 versus bounded caller traversal at depths 2–3, and dependency-root/projection semantics. Explain that incoming/both dependency analysis can require a broad eligible-source scan, that shallow outgoing queries are a useful focused starting point, and that output pages do not remove analysis bounds. State source/assembly differences where applicable. Use short descriptions; do not paste entire documentation pages into schemas.

  **Owners and contracts:** [SymbolTools](../../src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs), [RelationshipTools](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs), [find-symbol semantics](../../docs/navigation/find-symbol.md), [reference semantics](../../docs/navigation/find-references-and-implementations.md), [dependency semantics](../../docs/navigation/dependency-graph.md), [public tool reference](../../docs/tools/README.md).

  **Acceptance:** The exposed tool metadata contains the relevant distinctions above and agrees with current implementation. No arguments, defaults, validation, matching behavior, traversal behavior or wire fields change. Verify the generated metadata through the existing SDK/schema contract test infrastructure where available; do not rely only on reading attributes. Update affected canonical documentation only where needed.

  **Completion evidence:** Pending.

- [ ] **NCC-02 — Declare file skeleton coverage in metadata and output**

  **Problem:** The skeleton walker intentionally omits nested types, but the public description broadly promises declarations. The omission is explained in repository documentation rather than being reliably visible to the consuming agent.

  **Solution:** State in the tool description and a compact per-file output scope note that the outline covers top-level types and their direct members; nested types, implementation bodies and initializers are omitted. Put the output note in the existing shared skeleton rendering/projection owner so source and assembly paths remain consistent. Preserve supported references, declaration content, batching and outer-page recovery. The note describes intentional scope; it must not turn a successful scoped result into an analysis failure or fabricate omitted counts.

  **Owners and contracts:** [StructureTools](../../src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs), [skeleton implementation directory](../../src/AiNetCodeNavigator.Core/Skeletons), [skeleton contract](../../docs/navigation/get-file-skeleton.md). Extend [FileSkeletonTests](../../tests/AiNetCodeNavigator.FastTests/Skeletons/FileSkeletonTests.cs), [SkeletonMapTests](../../tests/AiNetCodeNavigator.FastTests/Skeletons/SkeletonMapTests.cs), and the affected [source](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceToolsContractTests.cs)/[assembly](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/AssemblyToolsContractTests.cs) handler cases as appropriate.

  **Acceptance:** A file with a nested type yields a visible scope note, retains its top-level declarations/direct members and navigable references, and still excludes nested declarations and bodies. Source and assembly outputs communicate the same scope. Budgeted outer-page reconstruction retains the scope note. Existing empty-file and mixed-success batch behavior remains valid.

  **Completion evidence:** Pending.

- [ ] **NCC-03 — Surface unanalysed framework contexts in the scope summary**

  **Problem:** Scope project items already report loaded contexts, whether configured frameworks are known, and configured frameworks not analysed. The top-level summary chiefly reports document counts, making an important analysis boundary easy to overlook, especially when project items are paged.

  **Solution:** Build a concise target-wide framework-coverage summary from the existing complete scope inventory before result paging. Report how many project entries have known configured frameworks not analysed and how many entries have unknown configured-framework coverage. Refer readers to project items for exact framework names and loaded contexts instead of duplicating the inventory. Preserve existing item fields and cursors. Calculate the summary from all discovered project entries, not just the visible page; retain it on every domain page. No extra MSBuild evaluation, additional TFM loading or new environment view is needed.

  **Owners and contracts:** [StructureTools scope projection](../../src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs), [IndexScopeModels](../../src/AiNetCodeNavigator.Core/FileStructure/IndexScopeModels.cs), [scope contract](../../docs/navigation/get-index-scope.md), [IndexScopeContractTests](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/IndexScopeContractTests.cs), [IndexScopeScannerTests](../../tests/AiNetCodeNavigator.FastTests/FileStructure/IndexScopeScannerTests.cs).

  **Acceptance:** Tests cover known missing frameworks, unknown configuration coverage, and known configuration with no missing frameworks. A small page that omits affected project items still carries the correct target-wide summary. Unknown coverage never becomes a claim that all frameworks were analysed; no-project results remain truthful. Existing loaded-context identities, cursor binding and analysis/delivery status semantics remain unchanged. No new framework-support claim is introduced.

  **Completion evidence:** Pending.

- [ ] **NCC-04 — Centralize simple symbol-name matching without changing behavior**

  **Problem:** The same rules for the `*text*` shortcut, wildcards, inferred regex and substring fallback are implemented separately for declaration prefiltering and final simple-name matching in `SymbolNameMatcher`. Future changes can make those paths disagree. This is duplicated logic, not a demonstrated existing incorrect result.

  **Solution:** Make both simple-name paths use one internal predicate implementation in the existing matcher. Preserve normalization, precedence, anchoring, case rules, invalid-regex fallback and regex timeout behavior. Keep qualified-name prefiltering intentionally broader than final symbol matching; do not incorrectly make the two whole algorithms identical. Add behavioral characterization cases before the refactor and run them again afterwards.

  **Owners and contracts:** [SymbolNameMatcher](../../src/AiNetCodeNavigator.Core/Symbols/SymbolNameMatcher.cs), [find-symbol contract](../../docs/navigation/find-symbol.md), [FindSymbolScannerTests](../../tests/AiNetCodeNavigator.FastTests/Symbols/FindSymbolScannerTests.cs), affected source/assembly discovery contracts linked in NCC-02. Inspect the referenced normalization and regex helpers before extracting shared logic.

  **Acceptance:** Tests demonstrate unchanged results for case-insensitive substrings, `*text*`, anchored `*`/`?` patterns, inferred regex, invalid-regex fallback, normalized identifiers and qualified type/member queries, including non-matching decoys. Declaration prefiltering cannot discard an eventual valid match in these cases. Only one owner implements the simple matching rules. No public matching-mode argument, new abstraction/package, cache or performance claim is introduced.

  **Completion evidence:** Pending.

- [ ] **NCC-05 — Complete solution verification and audit preparation**

  **Problem:** Individual slice checks do not establish that the combined metadata, output and matcher changes remain coherent across the final repository state.

  **Solution:** Review the combined diff against NCC-01 through NCC-04, run `pwsh -File ./scripts/test.ps1` for the complete eligible routine solution selection, and complete any outstanding required affected build/extended checks under the linked verification rule. Review the affected canonical documentation and tool metadata together. Record the implementation baseline-to-final commit range and verification evidence here for the auditor.

  **Acceptance:** Required gates pass on the implementation state sent to audit; unrun or failed checks remain explicit blockers. No unrelated changes are included. All four implementation points contain reproducible evidence and commits. No performance, runtime-coverage or new framework-support claims appear in the changes.

  **Completion evidence:** Pending.

- [ ] **NCC-06 — Audit the entire change and resolve findings through re-audit**

  **Problem:** Local self-checks can miss interactions, misleading completeness claims, behavior drift or inadequate verification across the combined implementation.

  **Solution:** Dispatch a fresh independent `gpt-6.1-sol` / `medium` audit agent with the complete change range, this roadmap, linked contracts, final source/tests/docs and recorded verification evidence. The auditor reads and reports; it does not edit production code. Check all four points, shared source/assembly paths, exposed schemas, result budgets/paging, unknown/missing framework coverage, matcher compatibility, and repository boundaries. Report actionable findings with severity, exact location, problem, expected correction and verification requirement.

  For every round, append its report below. If findings exist, insert ordered unchecked correction points here before final closure, each with explicit finding IDs, scope, acceptance and tests. Dispatch correction agents with the same model/effort; commit verified corrections, then dispatch a fresh audit agent to check fixes and the complete final change. Repeat until no in-scope findings remain. Escalate findings requiring a product decision or scope expansion in this chat; do not silently discard them or implement broader proposals. Repeat broad verification only when changes, failures or concrete concerns justify it, as required by the verification rule.

  **Acceptance:** The final independent audit has no unresolved in-scope findings. Every finding has a traceable verified resolution and correction commit, or an explicit user decision recorded here. Required gates cover the final production state. This checkbox stays open while findings or required checks are unresolved. Commit the final roadmap evidence and report the result to the user.

  **Audit rounds and finding resolutions:** Pending.

## Excluded work

Workspace double-read removal, watchers, cache/resource-budget redesign, performance experiments, a new paging protocol, native MCP Tasks migration, explicit matching-mode APIs, multi-symbol change context, extra TFM loading/selectors and Razor/XAML semantic navigation remain outside this roadmap. Their motivations and unresolved choices remain in the [review](../ideen/review-2026-10-07/review.md) and [decision log](../ideen/review-2026-10-07/decisions.md); do not copy them into implementation points here.
