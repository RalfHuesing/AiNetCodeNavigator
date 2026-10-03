# R01 — Reference primitives and exact resolvers

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: None. Next: [R02](R02-public-reference-migration.md).

Contract: [01 — grammar, ownership, unavailable references and errors](../01-symbol-identity-and-recovery.md).

Implement the shared typed source/assembly reference model and codec, deterministic Git/worktree/non-Git base selection, relative project matching, exact Roslyn declaration lookup and owner-bound assembly lookup. Keep declaration selection separate from snapshot hashing. Reuse existing metadata/provenance/session owners rather than introducing another assembly-loading subsystem.

Inventory every current handoff producer/consumer and handle-only component. Record the actual inventory in this point's evidence so R02 covers every path. Include physical-file inputs that also accept handles, candidate/body/context routes, text/Mermaid renderers, test helpers, configuration and docs.

Acceptance: all reference-level cases in specification 01 pass, including malformed escaping, no-ID declarations, exact duplicate-owner ambiguity, multi-context behavior, partial/linked sources and same-name binary owner pairing. Resolution never chooses a first match or invalidates an unchanged declaration due to content changes.

Verification: official build and affected FastTests for the new codec/source/assembly resolvers and path ownership. Add focused integration coverage where exact assembly provenance requires it. Tests for the new resolver do not depend on the old registry.

Boundary: no new public emission or dual public mode yet. The old public behavior remains until the atomic R02 switch; this preparatory commit must not be advertised as the completed migration.

Completion checklist:

- [x] R01.1 — Shared codec, owner-base selection and exact source/assembly resolvers implement specification 01; no public dual mode is enabled.
- [x] R01.2 — Complete producer/consumer/removal inventory is recorded from current local sources.
- [x] R01.3 — Point acceptance and its required build/test selections passed; exact commands and results are recorded.
- [x] R01.4 — Implementation commit(s) and the internal-only boundary are recorded; the orchestrator reviewed R01.1–R01.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Complete; the orchestrator individually verified R01.1–R01.4 on 2026-10-03 after independent audit PASS |
| Implementation commit(s) | `538291c9bd6671d3164caa48b80923fafa7db34a` — internal primitives, exact resolvers, regression/acceptance tests and current internal-only docs |
| Executed verification | Official build PASS (0 warnings/errors); final affected FastTests 64/64 PASS plus unchanged owner tests 19/19 from prior combined 83/83 PASS; Sol/medium audit rounds 1/2 FAIL then round 3 PASS with all findings closed |
| Measurements / artifacts | All logs/TRX/audit reports preserved under `temp/roadmap-evidence/R01/`; test durations approximately 1–2 seconds, no performance claim; verified inventory persisted below |
| Blocker / next action | No R01 blocker. Begin R02 public migration from this verified commit and the complete inventory below |

### Initial inspection

- Starting HEAD: `4729bb8f27816af0f5244284bacbd0a88b407f85` (`main`); working tree and index clean.
- R01 is the first unchecked point; no implementation completion evidence exists for R01–R08.
- Read AGENTS.md, all eight linked repository rules, the task README/index/shared execution contract, R01 and specifications 01/04.
- The existing public source/assembly paths still use `HandoffHandleRegistry`, snapshot-bound identifiers and their existing resolvers. R01 preserves that public behavior; R02 owns the switch.
- Only the orchestrator runs verification scripts, stages/commits or edits checkboxes. The implementation agent has explicit internal-only file boundaries and must report a complete removal inventory and actual test selections.

### Implementation and verification round 1

- Luna/high supplied internal primitives and initial codec/source/path tests; full acceptance remains incomplete.
- Official build: `pwsh -File ./scripts/build.ps1`, exit 1, approximately 4 seconds. Errors: `CS8619` in `ExactSourceSymbolResolver.cs:137`, `CS1503` in `StableSymbolReferenceCodec.cs:135`, `CS8602` in `ExactAssemblySymbolResolver.cs:36,62`.
- Fix round assigned to Luna, including missing assembly provenance/lease coverage and remaining reference-level acceptance tests. No product commit or acceptance checkbox is authorized yet.

### Implementation and verification round 2

- Luna corrected the initial production compilation errors and expanded codec, source, owner-base and leased assembly component coverage. Public consumers remain unchanged.
- Official build: `pwsh -File ./scripts/build.ps1`, exit 1, approximately 8 seconds. Core, host, TestKit and IntegrationTests compile; nine errors remain in new FastTests: four `CS1061` calls to `ISymbol.GetMembers` and five nullable `CS8604` arguments. Preserved log: `temp/roadmap-evidence/R01/round-2/build.log`.
- A focused test-code fix was assigned to Luna. Tests and independent Sol audit are still pending; no acceptance box is checked.

### Implementation and verification round 3

- Official build: `pwsh -File ./scripts/build.ps1`, PASS (exit 0, 0 warnings/errors, approximately 2 seconds).
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests|FullyQualifiedName~SourceReferenceOwnerTests|FullyQualifiedName~ExactAssemblySymbolResolverTests'`, FAIL (exit 1): 55 passed, 3 failed, 0 skipped, 58 total. Failure cases: missing-compilation-options fixture returns `SYMBOL_NOT_FOUND`, discovery-revealed prefix preparation has no parse error yet, duplicate type declaration fixture resolves successfully.
- Logs/TRX preserved under `temp/roadmap-evidence/R01/round-3/`. The Git directory-junction and leased binary component tests completed without skips.
- Luna is assigned to diagnose the actual Roslyn/codec behavior, fix confirmed causes and use fixtures that prove the required failure preconditions. Acceptance must remain unchanged. Independent audit remains pending.

### Implementation and verification round 4 / independent audit round 1

- The failing fixtures were corrected after the executed reproductions: original-input handling is tested through the full parse API; duplicate methods establish distinct Roslyn symbols with equal IDs; Roslyn automatically provides default C# options, so a genuinely loaded unsupported VB context establishes the diagnostic condition instead.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests|FullyQualifiedName~SourceReferenceOwnerTests|FullyQualifiedName~ExactAssemblySymbolResolverTests'`: PASS, exit 0, 58 passed, 0 failed, 0 skipped, approximately 1 second test duration.
- Preserved build/test logs, TRX and inventory: `temp/roadmap-evidence/R01/round-4/`.
- A separate read-only `gpt-6.1-sol` / medium auditor is inspecting the actual code, full R01 contract, tests, documentation and removal inventory. Implementation remains uncommitted and all acceptance checkboxes remain open until findings and evidence are reviewed.
- Explicit coverage limit: no natural null-compilation fixture was established through the supported Roslyn loader APIs. The resolver handles a null compilation and loading exceptions as diagnostics; the executed test proves the unsupported loaded-context path, not the null branch.

### Independent audit round 1 — findings / fix assignment

Sol/medium verdict: FAIL. The auditor read actual production code, callers, tests, documentation and inventory; no builds/tests or tracked edits were performed by the auditor. Full local report: `temp/roadmap-evidence/R01/round-4/audit-round-1.md`.

| Finding | Confirmed issue | Disposition |
| --- | --- | --- |
| R01-A01 (P2) | Legacy drive exception wrongly accepts `h:/` / `i:\` with empty path remainder | Open; first add failing regression, then correct lexical routing |
| R01-A02 (P2) | Reference parse/resolution errors omit executable recovery hints; formatter does not supply them | Open; first reproduce missing actions, then correct owner errors without changing codes |
| R01-A03 (P2) | Missing material acceptance for generated/file-local sources, options/reload/absence, Unicode/pipe/external loaded owners, assembly changed members/eviction/cross-owner hops | Open; add actual immutable-solution and leased-binary coverage |
| R01-A04 (P2) | Inventory omits old-handoff content in root README, `docs/mcp-host.md`, `docs/development/build-and-tests.md` | Open; complete and review actual documentation/removal inventory |

Luna/high is assigned the regression-first fix round. No acceptance/tests/categories may be weakened. Sol concluded no additional blanket Integration/Extended run is needed for this internal preparation when real eligible Component tests establish assembly provenance. Public handler gates remain mandatory in R02.

### Audit fix — regression reproduction before correction

- Luna changed tests only for R01-A01/A02; production primitives were unchanged.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests|FullyQualifiedName~ExactAssemblySymbolResolverTests'`: FAIL, exit 1, 17 failed / 35 passed / 0 skipped / 52 total. Empty drive-remainder cases fail routing; actual parse/resolver error branches lack recovery hints or provide only a project path.
- Preserved log/TRX: `temp/roadmap-evidence/R01/audit-fix-regression/`. This executes both reproducible code-defect findings before production correction.
- Luna is now assigned production correction and all remaining R01-A03/A04 acceptance/inventory work, followed by official build/tests and independent Sol re-audit.

### Audit fix — expanded candidate build

- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, two new test-fixture errors: `CS1061` on `IReadOnlyList<AnalyzerReference>.Add` at `StableSymbolReferenceTests.cs:619`, and Roslyn analyzer `RS1042` rejecting the legacy source-generator interface at line 753.
- Preserved log: `temp/roadmap-evidence/R01/audit-fix-build/build.log`. Production projects compile; expanded tests have not run yet. Luna is assigned a supported incremental generator fixture and collection API correction without suppressing analyzers.

### Audit fix — expanded verification run 1

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors after incremental generator fixture correction.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests|FullyQualifiedName~SourceReferenceOwnerTests|FullyQualifiedName~ExactAssemblySymbolResolverTests'`: FAIL, exit 1, 80 passed / 3 failed / 0 skipped / 83 total.
- Remaining failures reproduce null `Hint` in source producer owner mismatch, unloaded external owner and assembly producer owner mismatch. Source inspection confirmed action text occupied the explanatory Message argument while the Hint argument was missing. Luna is assigned full error-branch argument correction without changing assertions.
- Preserved build/test logs and TRX: `temp/roadmap-evidence/R01/audit-fix-run1/`. Expanded generated/file-local/declaration-kind/reload/absence and assembly owner-lifetime cases otherwise completed successfully.

### Audit fix — expanded verification run 2 / independent re-audit round 2

- Luna corrected explanatory Message versus executable Hint arguments on all affected producer/consumer branches; original recovery assertions remain intact.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests|FullyQualifiedName~SourceReferenceOwnerTests|FullyQualifiedName~ExactAssemblySymbolResolverTests'`: PASS, exit 0, 83 passed / 0 failed / 0 skipped, approximately 2 seconds test duration.
- Preserved build/test logs, TRX and corrected inventory: `temp/roadmap-evidence/R01/audit-fix-run2/`.
- The separate Sol/medium auditor is rechecking every finding against actual code and expanded coverage. No checkbox or product commit is authorized until this review and the orchestrator's acceptance review complete.

### Independent audit round 2 — closure and remaining recovery finding

- Sol/medium verified actual code and 83-test TRX: R01-A01, R01-A03 and R01-A04 CLOSED. Full report: `temp/roadmap-evidence/R01/audit-fix-run2/audit-round-2.md`.
- R01-A02 remains OPEN (P2): duplicate-ID recovery promises a stable reference after selecting file/line and rediscovering, while the exact producer correctly refuses either ambiguous declaration. Source and assembly hints must require unique owner/context/ID or offer independent raw-location navigation without promising a stable reference.
- The orchestrator confirmed this recovery loop against the actual producer. Luna is assigned a failing recovery regression followed by a narrow hint correction; acceptance remains unchecked pending gates and final Sol review.

### Audit round 2 — regression reproduction

- Tests-only change strengthens the existing true duplicate-method producer/consumer recovery assertions: unique owner/context/ID prerequisite plus independent raw source location.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests.SourceReference_ExactDuplicateDeclarationIdIsAmbiguous'`: FAIL, exit 1, 1 failed / 0 passed / 0 skipped; actual hint lacks the uniqueness prerequisite.
- Preserved evidence: `temp/roadmap-evidence/R01/audit-round2-regression/`. Luna is assigned the narrow source/assembly ambiguity and unsupported-roundtrip hint correction before required build/test revalidation.

### Audit round 3 — final candidate checks

- Luna changed only the three affected recovery hints, preserving explanatory messages, codes, matching/uniqueness checks and regression assertions.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~StableSymbolReferenceTests|FullyQualifiedName~ExactAssemblySymbolResolverTests'`: PASS, exit 0, 64 passed / 0 failed / 0 skipped, approximately 2 seconds test duration. The 19 unchanged SourceReferenceOwner tests retain their successful executed evidence from the prior combined 83-test run.
- Preserved logs/TRX: `temp/roadmap-evidence/R01/audit-round3-final/`. Sol/medium is performing the final read-only review of recovery semantics and the overall R01 acceptance.

### Final acceptance review and independent audit PASS

- Sol/medium round 3: PASS; R01-A01–A04 are CLOSED, with no further concrete correctness, integration, regression, lifetime, error, test or documentation finding. Report: `temp/roadmap-evidence/R01/audit-round3-final/audit-round-3.md`.
- Orchestrator review of R01.1: source/assembly references contain only owner coordinate plus canonical Roslyn declaration ID; exact ownership, all-match resolution and symbol/codec round-trips remain separate from hashing. Current public producers/consumers still use the original handles; no public dual mode was enabled.
- Orchestrator review of R01.2: actual source/candidate/body/context/file/rendering/assembly-owner/host/persistence/test/helper/documentation paths and independent identities are inventoried below. R02 must migrate or remove every path and preserve independent evidence/lifetime duties.
- Orchestrator review of R01.3: build and affected eligible Unit/Component gates executed successfully; error regressions were executed failing before correction. Real binary scope tests satisfy required R01 provenance without an additional blanket Integration/Extended run. No mandatory R01 check is unrun; public handler/transport acceptance remains in its later owning point.
- Orchestrator review of R01.4: verified implementation commit `538291c9bd6671d3164caa48b80923fafa7db34a` contains code/tests/current docs. The checkmarks record the internal preparation only; R02–R08 remain open.
- Remaining factual limit: natural null-compilation injection through the supported Roslyn API was not established. Explicit null/exception diagnostic handling exists, and the actual unsupported loaded-context failure is tested. This is not presented as executed null-branch coverage.
- Reviewed task/code/documentation diff and `git diff --check` / `git diff --cached --check`: PASS. No push or deployment.

### Verified producer/consumer/removal inventory

Captured from local source at the R01 implementation checkout. Public tools still emit and consume `h:` values; R01 adds internal reference primitives only. R02 must switch every listed public route in one migration and remove the old handle-only components after their independent responsibilities are preserved.

#### Shared old-handle identity and registry

- `src/AiNetCodeNavigator.Core/Symbols/HandoffHandleRegistry.cs` — static default registry with process-local bidirectional maps, lock ownership, opaque handle allocation/output mapping, and input restoration. `NavigatorHostRuntime.DisposeAsync` clears these maps; it does not erase the persistent counter high-water mark.
- `src/AiNetCodeNavigator.Core/Symbols/HandoffCounterStore.cs` and `HandoffCounterAlphabet.cs` — counter alphabet plus persisted high-water mark. The default state file is `%LOCALAPPDATA%/RalfHuesing/AiNetCodeNavigator/handoff-counter.json`; batches of 1,000 are reserved under an in-process lock and a sidecar `.lock` file opened with `FileShare.None`, with a five-second acquisition timeout and atomic temp-file replacement. There is no product setting for its path/size/timeout; tests inject a store/path. R02 must remove the old alphabet, counter persistence, lock ownership and runtime file I/O; the existing out-of-repository state file is not deleted by the code migration.
- `src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffIdentifier.cs` and `SymbolHandoffToken.cs` — old internal `i:` grammar, target/content tokens, and path/content normalization.
- `src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs` — current source/assembly handoff creation. It also owns assembly content-hash composition and project context evidence; split or retain those independent duties when removing handoff IDs.
- `src/AiNetCodeNavigator.Core/Symbols/SourceHandoffFormatter.cs`, `SourceHandoffResolver.cs`, and `HandoffFollowUpTools.cs` — source handoff emission, snapshot-bound restoration, and shared follow-up operations.

#### Source producers and Core payload routes

- `src/AiNetCodeNavigator.Core/Symbols/FindSymbolScanner.cs` and `FindSymbolModels.cs` — source discovery candidate/output handoffs.
- `src/AiNetCodeNavigator.Core/Symbols/SourceSymbolResolver.cs` and `SourceSymbolBodyResolver.cs` — raw/source identifier routing, old handoff resolution, candidate data, and body selection.
- `src/AiNetCodeNavigator.Core/Symbols/ClassStructureScanner.cs` and `ClassStructureModels.cs` — class/member candidate handoffs.
- `src/AiNetCodeNavigator.Core/Symbols/SymbolBodyModels.cs` — body response handoff fields.
- `src/AiNetCodeNavigator.Core/Skeletons/FileSkeletonBuilder.cs`, `SkeletonModels.cs`, and `SkeletonMarkdownRenderer.cs` — file/type/member handles and rendered Markdown.
- `src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs`, `CallTreeModels.cs`, `CallGraphTextRenderer.cs`, and `CallTreeMermaidRenderer.cs` — graph node handles plus text and Mermaid formatting.
- `src/AiNetCodeNavigator.Core/Symbols/FindReferencesResolver.cs`, `ReferenceModels.cs`, `ImpactAnalyzer.cs`, `ImpactModels.cs`, `RelationshipSymbolIdentity.cs`, `TestRecommendationBuilder.cs`, and `TestContextModels.cs` — relationship/test candidate handoffs and identities.
- `src/AiNetCodeNavigator.Core/Hierarchy/TypeHierarchyScanner.cs`, `TypeHierarchyModels.cs`, and `GetTypeHierarchyFormatter.cs` — hierarchy candidate handoffs and text rendering.
- `src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs` and `DependencyGraphModels.cs` — dependency graph node/candidate handoffs.
- `src/AiNetCodeNavigator.Core/FileStructure/NamespaceTreeScanner.cs` and `NamespaceTreeModels.cs` — namespace/type candidate handoffs.

#### Assembly producers, consumers, and owner closure

- `src/AiNetCodeNavigator.Core/Assemblies/AssemblyFindSymbolScanner.cs`, `AssemblySymbolInputResolver.cs`, `AssemblySymbolHandoffResolver.cs`, and `AssemblySymbolBodyScanner.cs` — assembly discovery, old handoff restoration, exact symbol input/body selection.
- `src/AiNetCodeNavigator.Core/Assemblies/InspectAssemblyScanner.cs` and `InspectAssemblyModels.cs` — API inspection result handles.
- `src/AiNetCodeNavigator.Core/Assemblies/AssemblySearchScanner.cs` — decompiled text search declaration handles.
- `src/AiNetCodeNavigator.Core/Assemblies/FindAssemblyExtensionsScanner.cs` — extension method handles including owner-specific targets.
- `src/AiNetCodeNavigator.Core/Assemblies/AssemblyAnalysisSessionRegistry.cs`, `AssemblyAnalysisSession.cs`, `AssemblyNavigationSessionScope.cs`, and `AssemblyNavigationModels.cs` — resident session ownership and leases. Preserve exact owner generation/lease semantics for new assembly references.
- `src/AiNetCodeNavigator/Mcp/Tools/Relationships/AssemblyHandoffFormatting.cs`, `AssemblyReferenceClosureSession.cs`, `AssemblyCallTreeClosureBuilder.cs`, `AssemblyReferencesClosureScanner.cs`, and `AssemblyImpactClosureScanner.cs` — cross-owner internal/external handoff transformation, reference closure checks, and owner leases.
- `src/AiNetCodeNavigator.Core/Assemblies/AssemblyReferenceSnapshotFingerprint.cs`, `AssemblyReferenceSnapshotValidator.cs`, `AssemblyFingerprintCalculator.cs`, and `AssemblyReferenceResolver.cs` — independent binary/reference freshness and provenance evidence. Preserve these checks; declaration references must not absorb their content-bound role.

#### Public routing, physical paths, and host wiring

- `src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs` — public source and assembly symbol/body/context/relationship routes, including `symbolIdentifiers` and handoff-aware selected inputs.
- `src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs` — `get_file_skeleton.filePaths` route, which accepts physical source paths and handles identifying declaration files.
- `src/AiNetCodeNavigator/Mcp/Tools/Assemblies/AssemblyTools.cs` — assembly-specific public tool routing and ownership.
- `src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs` — public relationship and cross-owner routes.
- `src/AiNetCodeNavigator.Core/Common/InputNormalizer.cs` — broad discovery cleanup (wrappers, method parentheses, generics) and legacy prefix classification. R02 must route a reference from the original argument before this cleanup and retain cleanup for raw discovery inputs.
- `src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs` — binds the static default handle registry and clears the process-local mapping at host shutdown.
- `Directory.Packages.props` and the Core/MCP/FastTests project files contain no reference-mode or handoff-mode setting. R01 added no dependencies or configuration knobs.
- `src/AiNetCodeNavigator.Core/Workspace/NavigationErrorCodes.cs` and `NavigatorErrorFormatter.cs` — stable machine error codes and serialized error/recovery formatting.

#### Independent identities R02 must retain

- `RelationshipSymbolIdentity.GetStableId` identifies local functions using declaration/container plus source position; it is an internal relationship key, not the public handoff ID.
- `CallTreeBuilder` node/edge identity, dependency graph symbol/document coordinates, and impact/reference site keys preserve relationship topology and caller locations. Replace only their handoff field projection; do not replace these analysis keys with public declaration-reference strings.
- `AnalysisSymbolIdentity` currently mixes the old public declaration handoff with source project context and assembly snapshot hash composition. Keep snapshot freshness, owner context evidence, and binary provenance separately owned after extracting public stable references.
- Assembly generation number, reference snapshot hash, reference closure checks, and lease transfer in `AssemblyReferenceClosureSession`/`AssemblyNavigationSessionScope` remain evidence consistency mechanisms. A stable declaration reference itself is independent of them.
- `InputNormalizer` remains necessary for non-reference name/location discovery; only stable reference inputs bypass its cleanup.

#### Tests and test helpers

- `tests/AiNetCodeNavigator.TestKit/Assertions/NavigationAssertions.cs` validates the current opaque alphabet and must be migrated with public emission.
- `tests/AiNetCodeNavigator.FastTests/Symbols/HandoffHandleRegistryTests.cs`, `HandoffCounterStoreTests.cs`, `HandoffCounterAlphabetTests.cs`, `SymbolHandoffIdentifierTests.cs`, and `AnalysisSymbolIdentityTests.cs` cover old handle internals and source context identity.
- `tests/AiNetCodeNavigator.FastTests/Symbols/FindSymbolScannerTests.cs`, `GetSymbolBodyTests.cs`, `ClassStructureScannerTests.cs`, `CrossFeatureRelationshipContractTests.cs`, `FindReferencesResolverTests.cs`, and `ImpactAnalyzerTests.cs` cover candidate/follow-up paths.
- `tests/AiNetCodeNavigator.FastTests/CallTree/CallTreeTests.cs`, `Skeletons/FileSkeletonTests.cs`, `Hierarchy/TypeHierarchyTests.cs`, `Dependencies/DependencyGraphScannerTests.cs`, `FileStructure/NamespaceTreeScannerTests.cs`, and `Assemblies/AssemblyNavigationScannerTests.cs` cover renderer and owner producer routes.
- `tests/AiNetCodeNavigator.FastTests/Assemblies/AssemblySymbolHandoffResolverTests.cs`, `AssemblyIdentityMatcherTests.cs`, and `AssemblyFingerprintAndReferenceTests.cs` cover owner/session behavior and independent provenance checks.
- `tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceToolsContractTests.cs`, `RelationshipToolsContractTests.cs`, `SourceRelationshipToolsContractTests.cs`, `AssemblyToolsContractTests.cs`, and `McpServerIntegrationTests.cs` cover public tool routes and owner wiring. Keep public tests on the R02 switch; new R01 resolver tests do not use the old registry.

#### Current documentation / agent navigation

- `README.md` — public quick-start examples pass opaque `h:` values between discovery and follow-up calls; update when R02 changes the user-visible reference contract.
- `docs/mcp-host.md` — documents runtime disposal clearing process-local opaque handoffs and the lifecycle of assembly/session owners; preserve shutdown ordering and session disposal while removing only the handle map.
- `docs/development/build-and-tests.md` — records `NavigationAssertions`, source location/context identity, `SymbolHandoffToken` path/content normalization, the opaque alphabet/counter persistence, sidecar lock ownership, and build/test gates; R02 must remove the old alphabet/counter/lock documentation and runtime I/O, while retaining independent lexical path identity and assembly snapshot/provenance behavior.
- `docs/README.md`, `docs/tools/README.md`, and navigation pages: `docs/navigation/symbol-resolution.md`, `find-symbol.md`, `get-symbol-body.md`, `get-context.md`, `get-file-skeleton.md`, `get-class-structure.md`, `assembly-navigation.md`, `assembly-decompilation.md`, `find-references-and-implementations.md`, `get-call-tree.md`, `impact-analysis.md`, `get-type-hierarchy.md`, `dependency-graph.md`, `get-namespace-tree.md`, `test-context.md`, `relationship-contracts.md`, `resolve-type-origin.md`, `get-index-scope.md`, and `mcp-registration-status.md` describe current public `h:` behavior. R01's internal-only implementation is recorded in `docs/navigation/symbol-resolution.md`; public route documentation still describes current `h:` behavior.
- `.agents/rules/08-ainetcodenavigator-mcp-navigation.mdc` instructs clients to reuse `h:` handles and will switch at R02.
- Root `AGENTS.md` and `.agents/rules/07-code-quality.mdc` mention handoff notes generically; neither defines handoff behavior. `.agents/rules/01`–`06` contain no handle contract.
- Configuration inventory: no existing user configuration option controls handoff generation or reference mode; R01 introduces no new configuration.
