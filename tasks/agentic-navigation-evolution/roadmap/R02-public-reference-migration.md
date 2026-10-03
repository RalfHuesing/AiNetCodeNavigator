# R02 — Switch all public routes and remove handles

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R01](R01-reference-primitives.md). Next: [R03](R03-snapshot-identity.md).

Contract: [01 — output/input integration and complete removal](../01-symbol-identity-and-recovery.md).

Use R01 references in every inventoried output/input and owner hop. Preserve existing field names, renderer formats and raw discovery selectors. Preserve snapshot-bound pages, analysis evidence and assembly generation leases. Replace handle-only recovery with the specified reference errors/actions.

Delete Base62 registry/counter/alphabet/persistence/locks, runtime setup/disposal, handle-only settings, old `i:` symbol serialization and obsolete tests/errors. Extract independent hashing/path/cursor behavior before removing shared classes. No compatibility flag or old-handle resolution path survives. Update the inventory with each path's completed migration/removal.

Acceptance: discovery → body/unrelated edit → follow-up → runtime restart succeeds for an unchanged exact declaration. Rename/delete/changed-ID cases fail clearly. Source/assembly/skeleton/context/graph/body/candidate paths use only new references. Byte/token budgets and outer/domain continuations remain executable; mixed batches preserve successes. Active handle machinery and counter-file I/O are absent.

Verification: official build; affected FastTests across symbols, assemblies, structures, dependencies, calls, hierarchy, formatting and schemas; source/assembly/relationship/index-scope transport-free handler contracts; narrowly selected relevant ExtendedIntegration tests under repository rules. Search active source/config/tests/docs for legacy machinery and classify each remaining legacy literal (negative test or historical artifact). Do not remove arbitrary files outside this repository, including a user's old counter file.

Update affected current-state docs, tool descriptions, navigation rule 08 and test helper assumptions in this point. Final public schemas must expose the specified input semantics without a second ID.

Completion checklist:

- [x] R02.1 — Every R01 inventory route emits/consumes the new references; handle-only runtime/configuration/persistence paths are removed.
- [x] R02.2 — Edit/restart/error/owner/budget/paging acceptance and required build/test selections passed; remaining legacy literals are classified.
- [x] R02.3 — Current-state docs, schemas/descriptions, navigation rule 08 and test helpers match the public switch.
- [x] R02.4 — Inventory disposition, executed evidence and implementation commit(s) are recorded; the orchestrator reviewed R02.1–R02.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | Complete; started from clean verified R01 HEAD `19e9dd128375d13cea4bf15cd13d9f1916a91e75` |
| Implementation commit(s) | `9c1c3951649a16fbdb1b5a065bd8d2a00a79aa44` (product, tests, current-state docs and rule 08) |
| Executed verification | Official build passed (0 warnings/errors); six initial regressions, owner closure and 532 affected FastTests passed; after audit fixes, 142 affected Host FastTests, all 61 mandatory/focused Integration cases and both narrow Extended cases passed; independent re-audit round 2 PASS |
| Measurements / artifacts | Per-run elapsed times and failing/passing logs/TRX below; final artifacts `temp/roadmap-evidence/R02/audit-final-gates-run1/` and `extended-run2/`; 64 legacy-prefix lines classified; 26 changed documents / 111 local file links / zero missing; diff checks passed |
| Blocker / next action | None; all four detailed acceptance items reviewed and checked by root after audit PASS and product commit; continue R03 after this evidence commit |

### Assignment and prerequisite evidence

R01 production commit `538291c9bd6671d3164caa48b80923fafa7db34a` and completion/evidence commit `19e9dd128375d13cea4bf15cd13d9f1916a91e75` are present. R01's four acceptance boxes and index box are checked after official gates and independent audit round 3 PASS. The working tree was clean before R02's production assignment; later edits belong to the assigned workers.

- `/root/r02_implementation`: explicitly configured `gpt-6-luna`, reasoning `high`; owns production `src/**` and the complete producer/consumer/removal disposition, except the two explicitly transferred files below.
- `/root/r02_tests_docs`: explicitly configured `gpt-6-luna`, reasoning `high`; initially owned tests and documentation, then confirmed transfer of all documentation edits and now owns `tests/**` exclusively. Coordinates exact public APIs with the production worker.
- `/root/r02_docs`: explicitly configured `gpt-6-luna`, reasoning `high`; owns current-state `docs/**`, root `README.md` and navigation rule 08 after the confirmed handoff. Reviews and completes the earlier partial documentation edits against actual production code. After production-worker confirmation, also owns only `src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs` and `src/AiNetCodeNavigator.Core/Common/InputNormalizer.cs` to remove handle setup/disposal and the obsolete prefix helper, preserving lifecycle order and all raw-discovery cleanup.
- The orchestrator owns all verification runs, task/specification evidence, checkbox changes and commits. All three workers must pause before any build/test; no concurrent verification is permitted in this checkout. The separate Sol audit starts against the actual completed candidate.

The assignments include the full R02 contract, specification 01, R01's verified inventory, all linked repository rules, preserved snapshot/evidence/lease boundaries, complete legacy removal, negative reference-input coverage, public follow-up acceptance and required eligible test categories. R02 remains unchecked until actual verification and independent audit pass.

### Candidate review observations (not yet verified)

- The request-owned formatting context must remain transient during projection; shared results/caches must retain scalar payloads rather than Solution/Compilation/ISymbol or leased assembly scopes. Assembly projection uses the already leased owner directly.
- Legacy positive `i:` formatting tests are obsolete, but independent local-function relationship keys and metadata/project-reference context/hash evidence remain required. The tests worker retained equivalent coverage in their actual owners.
- R02-P01: source inspection found the existing assembly raw selector coupling owned-declaration success to stable-ID availability. This discarded local/decompiler declarations and could collapse null-reference candidates. The production worker separated current compilation/source-tree ownership from optional reference creation and retained nullable candidate metadata. This WIP correction arrived before its paired new regression was ready; no pre-fix test failure is claimed. At candidate pause, reproduce the original availability coupling with the independent regression through the official script, then reapply the final correction and complete verification. No test-only compatibility mode or weakened assertion is permitted.
- Reference routing must preserve the original input and select the exact consumer owner before preparing projection identity. The production worker moved source identity preparation after successful reference resolution.

### Initial frozen candidate — official build run 1

- All implementation workers paused; only read-only disposition review continued.
- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 8 errors / 0 warnings, approximately 4 seconds. Core errors comprise nullable `ResultError` forwarding at three call sites, a nested `sourceContext` name collision, a missing workspace namespace import at three body-scanner references, and CA2000 on the successful returned disposable access wrapped in `Result`.
- Preserved log: `temp/roadmap-evidence/R02/build-run1/build.log`.
- Luna/high is assigned compiler corrections and explicit assembly scope ownership/error cleanup. Analyzers remain errors; a proven ownership-transfer false positive may use the same narrowly documented convention as the existing scope/registry factory, without blanket suppression or changed configuration. No tests have executed for this candidate; R02 remains unchecked.

### Compile correction / official build run 2

- Luna corrected Core nullable forwarding/import/shadowing and added `finally` scope cleanup on failed resolution or exception. The exact success path transfers ownership to the returned access. Its method-local CA2000 annotation documents the same proven transfer convention as the existing scope factory; no analyzer configuration changed.
- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 8 Host nullable `ResultError` forwarding errors / 0 warnings, approximately 4 seconds. Core and TestKit compile. Affected host owners: assembly closure, structure, symbols, and relationships.
- Preserved log: `temp/roadmap-evidence/R02/build-run2/build.log`. Luna is assigned the remaining host compile corrections, preserving current behavior pending regression execution.
- R02-P02 confirmed by frozen-source review: body-batch reference preflight globally returns the first malformed/wrong-origin error and drops successful items. Production correction is explicitly held until focused source/assembly mixed-batch regressions execute failing. The tests worker owns tests-only additions; no test/gate result is claimed yet.

### Expanded frozen-candidate regression review

- Luna completed the eight Host nullable forwarding corrections and paused. Source/assembly body batch regressions now exist but have not run.
- R02-P02 also affects `get_file_skeleton`: its global preflight aborts before the existing per-item processing. Focused source/assembly skeleton regressions are assigned before production correction.
- R02-P03: skeleton metadata calls `Path.GetFullPath` on every original selector, including stable wire references. A valid-reference skeleton regression must establish the actual selector-metadata corruption before correction; a thrown exception is not assumed as executed evidence.
- Assembly raw ambiguity with unavailable stable references also needs executable location recovery rather than an unconditional instruction to use a null handoff. The tests worker is investigating a deterministic two-local-function fixture; no failing execution or confirmed fix is claimed yet.
- The documentation reconciliation independently reproduced the body/skeleton preflight and selector-metadata concerns in actual source. It also found broad availability wording: only existing stable-handoff availability flags become false; body-syntax availability and owner/location metadata remain independent. Luna is correcting this wording against specification 01, without changing its acceptance or production behavior.

### Official build run 3

- Workers paused; `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 1 error / 0 warnings, approximately 2 seconds. `RelationshipTools.cs:1390` still forwards a nullable route error to a nonnullable result API. No regression tests executed yet.
- Log preserved at `temp/roadmap-evidence/R02/build-run3/build.log`; Luna is assigned the remaining compiler correction only, with behavioral regressions still held.

### Official build run 4

- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 8 test-project compile errors / 0 warnings, approximately 4 seconds. Core, TestKit and Host now compile. Test errors: duplicate Fact attribute, missing builder namespace and unavailable reference helper (plus derived array type errors), and an omitted codec error out-argument in maintained E2E assertions.
- Preserved log: `temp/roadmap-evidence/R02/build-run4/build.log`. Luna owns tests-only compile corrections; E2E remains excluded from execution even though its sources must compile.

### Official build run 5

- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 2 FastTests compile errors / 0 warnings, approximately 3 seconds. IntegrationTests now compile. Remaining errors: nullable inspection reference dereference and wrong object owner for the eviction disposal assertion.
- Preserved log: `temp/roadmap-evidence/R02/build-run5/build.log`. Luna is assigned corrections preserving actual nullable checks and observable generation eviction; no regression run yet.

### Official build run 6 / focused regression run 1

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings / errors, approximately 2 seconds. Preserved log: `temp/roadmap-evidence/R02/build-run6/build.log`.
- The eviction regression expires the idle session, then asserts different generation and workspace objects on reacquisition and resolves the same reference. No unsupported disposal property or claim of executed disposal-branch coverage remains.
- Executed command:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceToolsContractTests.SourceBodyBatchPreservesValidItemAndEveryTypedReferenceFailure|FullyQualifiedName~AssemblyToolsContractTests.AssemblyBodyBatchPreservesValidItemAndEveryTypedReferenceFailure|FullyQualifiedName~SourceToolsContractTests.SourceSkeletonReferenceBatchPreservesMixedAndAllFailedItemsAndMetadata|FullyQualifiedName~AssemblyToolsContractTests.AssemblySkeletonReferenceBatchPreservesMixedAndAllFailedItemsAndMetadata|FullyQualifiedName~AssemblyToolsContractTests.RawAssemblyAmbiguousLocalFunctionsRetainCandidatesAndRecoverByLocation|FullyQualifiedName~AssemblyToolsContractTests.RawAssemblyLocalFunctionLocationKeepsBodyWithoutStableReference' --logger 'console;verbosity=normal'
```

- FAIL, exit 1, 6 failed / 0 passed / 0 skipped; approximately 8.5 seconds, observable individual progress, no stall. Log/TRX: `temp/roadmap-evidence/R02/regression-run1/`.
- P02 actual product failure: assembly body and skeleton mixed batches return a global `INVALID_SYMBOL_REFERENCE`, dropping their valid item. Source cases instead exposed an incorrect test-helper assumption when reading the foreign assembly reference. These helper failures are not claimed as source regression proof.
- The local-function raw selector reached success but its candidate path is intentionally solution-relative; the new test incorrectly expected an absolute path. The bare-name ambiguity fixture used an unsupported local-function name lookup; it returned `SYMBOL_NOT_FOUND`. Luna is correcting test setup against the existing physical-selector semantics, using a supported line with two local-function references for ambiguity. No new raw-discovery mode is authorized.
- Valid-only skeleton calls succeeded on this Windows/.NET environment. The suspected exception was not reproduced; stricter original-reference analyzed-scope assertions are assigned to establish metadata corruption. No product behavior fix has occurred since the baseline run.

### Focused regression run 2 / actual finding confirmation

- Re-executed the exact six-test official command above after correcting the fixture setup; FAIL, exit 1, 6 failed / 0 passed / 0 skipped, approximately 7.7 seconds, observable per-test progress, no stall. Evidence: `temp/roadmap-evidence/R02/regression-run2/`.
- P02: source and assembly body mixed batches now both fail at the global reference preflight; assembly skeleton's mixed failure was already executed in run 1. Source skeleton's same preflight branch remains source-inspected until the earlier metadata failure is corrected.
- P03: both source and assembly valid-reference-only skeleton responses report `fileSkeleton(paths=C:\\...)` instead of preserving their `src:`/`asm:` selector. Exact analyzed-scope assertions fail. This is measured metadata corruption, not a reproduced exception.
- P04: the supported file:line ambiguity fixture actually selects two different local functions through a return line referencing both. Both candidates have null stable references. The result incorrectly tells the caller to choose a handoff and has no recovery hint; the hint assertion fails before recovery. Product correction is now authorized after the P01 baseline capture.
- The single-local regression still exposed a fixture mistake: the decompiled Solution has no FilePath, so existing candidate formatting intentionally returns a basename. Luna is correcting only that expectation; metadata/selector behavior is preserved. The production worker is temporarily restoring P01's prior ID-availability coupling after saving the corrected file, solely to execute its real regression before final reapplication. This ordering does not retroactively claim that the initial WIP change was test-driven.
- Read-only documentation validation: 25 changed README/docs/rule/spec/point sources, 107 links (106 local / 1 external), all local targets and heading anchors found. Diagnostic: `temp/r02-doc-links.md`. Current documentation legacy-prefix matches are rejection/drive-path guidance; task matches state acceptance/removal requirements. Final candidate will be rechecked after any later link changes.

### P01 baseline restoration / failing execution

- Luna saved the exact corrected `AssemblySymbolInputResolver.cs` under `temp/roadmap-evidence/R02/p01-baseline-restoration/AssemblySymbolInputResolver.corrected.cs`, SHA-256 `8D4D2A7C579624BF59A4F8C0FA4FA60434BB583E4581EDE8D9E01342F800B2A5`.
- Only the former `FromOwnedSymbol` availability gate was restored: reject a null candidate or null/empty stable ID with the original `INVALID_ARGUMENT` message, “The selected declaration has no canonical assembly handoff identity.” No compatibility mode or test-only branch was introduced.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyToolsContractTests.RawAssemblyLocalFunctionLocationKeepsBodyWithoutStableReference' --logger 'console;verbosity=normal'`: FAIL, exit 1, 1 failed / 0 passed / 0 skipped, approximately 2 seconds test duration. The assertion fails at raw resolution success with precisely the restored availability error; fixture path assertions are not involved.
- Logs/TRX saved beside the baseline file. The original no-ID WIP correction preceded the regression; this executed reconstruction proves the rejected policy before final reapplication and is not presented as an original pre-change test run. Luna may now restore the corrected file and fix P02–P04, preserving the failing regression assertions and complete acceptance.

### Active legacy inventory during the fix round

- Executed `rg` searches across active `src`, `tests`, `docs`, root README, `.agents` and root build/package/global configuration found zero Base62/counter/registry/old-parser/old-error machinery matches. Build/dependency configuration and official scripts have no diff.
- The quoted/backtick legacy-prefix scan found 59 lines, preserved in `temp/roadmap-evidence/R02/legacy-prefix-inventory.txt`: production codec rejection branches, negative helper/route/schema/rejection tests and physical H:/I: drive-path tests, plus three current documentation rejection-guidance lines. None is a positive legacy producer or consumer. Retained E2E negative assertions are compiled but their flows remain excluded and unrun.
- No out-of-repository counter file was deleted or modified. Public field names and independent relationship/site/hash/cursor identities remain intentional; their generic handoff labels are not a legacy registry path.

### P01–P04 fix round / official fix-build run 1

- Luna restored the saved corrected P01 file after verifying its SHA-256. Body/skeleton classification now preserves each item and can aggregate all malformed/wrong-origin items before owner loading; valid work keeps its existing source snapshot/assembly lease. Skeleton metadata preserves wire references and physical relative paths use the existing selected root. Raw assembly ambiguity recovery distinguishes optional references from raw locations. No acceptance assertion or category changed.
- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 1 error / 0 warnings, approximately 3 seconds: nested `discoveryProbe` declaration shadows an outer local in `StructureTools.cs:353`. Preserved log: `temp/roadmap-evidence/R02/fix-build-run1/build.log`. Luna is assigned the name correction; behavior regressions remain unchanged.

### Official fix-build run 2 / focused fix-regression run 1

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 3 seconds; `temp/roadmap-evidence/R02/fix-build-run2/build.log`.
- Re-executed the exact six-test official regression command above: FAIL, exit 1, 2 passed / 4 failed / 0 skipped, approximately 8.1 seconds, per-test progress, no stall. Source and assembly body mixed/all-failed regressions pass. Skeleton stable selectors now retain exact analyzed-scope metadata and mixed successful outlines reach the later assertions. Raw ambiguity reaches recovery after its reported hint assertions pass.
- Evidence: `temp/roadmap-evidence/R02/fix-regression-run1/`. Remaining failures expose test assumptions: decompiled block-body syntax differs from original expression-body syntax, the second path comparison also assumed a nonnull Solution.FilePath, a recovery assertion required one exact wording despite an executable same-origin action, and the all-failed skeleton's standard outer typed error adds an additional code occurrence to its four malformed per-item codes.
- Luna is correcting these assumptions with stronger per-item code/action assertions and actual raw-location body recovery. Required available/null-reference behavior, all five failed items, typed aggregate error, exact selector metadata and byte/token budgets remain required. No product behavior is changed to hide the failures, and no acceptance/category is weakened.

### Focused fix-regression run 2 / candidate gates run 1

- The exact six-test regression command above now PASSES, exit 0, 6 passed / 0 failed / 0 skipped, approximately 8.1 seconds; `temp/roadmap-evidence/R02/fix-regression-run2/`. Every mixed/all-failed selector has its own required code/action; raw local and ambiguous location follow-ups return actual decompiled bodies with null stable references.
- Final readability cleanup changed indentation and an XML summary only. `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 1.4 seconds; `temp/roadmap-evidence/R02/candidate-gates-run1/build.log`.
- Affected FastTests command:

```powershell
pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.FastTests.Symbols|FullyQualifiedName~AiNetCodeNavigator.FastTests.Assemblies|FullyQualifiedName~AiNetCodeNavigator.FastTests.Skeletons|FullyQualifiedName~AiNetCodeNavigator.FastTests.CallTree|FullyQualifiedName~AiNetCodeNavigator.FastTests.Hierarchy|FullyQualifiedName~AiNetCodeNavigator.FastTests.Dependencies|FullyQualifiedName~AiNetCodeNavigator.FastTests.FileStructure|FullyQualifiedName~AiNetCodeNavigator.FastTests.Mcp|FullyQualifiedName~AiNetCodeNavigator.FastTests.TestKit' --logger 'console;verbosity=normal'
```

- FAIL, exit 1, 527 passed / 5 failed / 0 skipped, 532 total, approximately 15.2 seconds. All individual tests made observable progress; no stall. Saved log/TRX in `temp/roadmap-evidence/R02/candidate-gates-run1/`.
- P05 confirmed by actual existing regression: `FeatureAndClassScanners_RejectSourceIdentityWithForgedProjectMarker` fails because raw source resolution accepts the supplied forged project markers. The current formatting context alone proves Solution membership, not independent analysis metadata. Luna is assigned validation against freshly computed current identity without reintroducing content-bound declaration references.
- Four other failures are under tests-only contract reconciliation: the indistinguishable-root fixture also contains uniquely represented library contexts; independent local-function relationship keys use `@line:column`; Markdown table references require escaping the literal pipe for rendering; assembly follow-ups require the returned explicit owner target. The corresponding original independent duties and stronger assertions remain required. Required public Integration/Extended gates and independent Sol audit have not yet executed.

### P05 correction / candidate gates run 2

- Luna centralized source analysis-evidence mismatch classification in `AnalysisSymbolIdentity.SourceMismatch`, used by the source resolver and find-symbol scanner. Exact reference resolution validates caller-supplied evidence against a fresh current identity before formatting. Class structure forwards the original supplied identity for validation rather than discarding it. Declaration references remain independent of content changes.
- Test corrections retain unique library contexts, the independent `@line:column` relationship key, escaped Markdown table columns and actual returned assembly owner targets.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 4 seconds. The exact affected FastTests command above PASSES: 532 passed / 0 failed / 0 skipped, approximately 15.14 seconds. The unchanged forged-marker regression now passes.
- Required transport-free Integration command:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.SourceToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.SourceRelationshipToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.RelationshipToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.AssemblyToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.IndexScopeContractTests' --logger 'console;verbosity=normal'
```

- FAIL, exit 1, 39 passed / 13 failed / 0 skipped, 52 total, approximately 2 minutes 24 seconds. Individual tests continued to finish; the longest observed test was approximately 42 seconds. No stall or timeout adjustment. Logs and TRX are preserved under `temp/roadmap-evidence/R02/candidate-gates-run2/`.
- Confirmed reconciliation work: JSON reference readers retain serialization delimiters or drop generic-ID backticks; a source namespace subcase requires an assembly prefix; two error assertions require obsolete literal recovery wording; a context body assertion expects another tool's wrapper; a long method-name count ignores the name inside its stable reference. Required owner/body/error/paging assertions remain intact while these helpers are corrected through shared semantic owners.
- The SDK definition contract exposes four missing stable-reference descriptions in relationship parameters. Luna changed only those descriptions after the failing gate. Physical-path selectors and public signatures are unchanged.
- Two JSON paging failures require response-fragment diagnosis before classifying helper versus production errors. The cross-owner incoming closure test requires independent reconciliation of exact selected-owner semantics with the existing root traversal anchor; no behavior or acceptance replacement is authorized without that review.
- `/root/r02_contract_review` is explicitly configured `gpt-6.1-sol`, reasoning `medium`, for an independent read-only contract/structure review. This is not the final complete-candidate audit. Required Extended tests and final independent audit remain open; R02 is unchecked.
- The user's structure steering is binding in the shared execution rules. The next verified checkpoint will record concrete remaining responsibilities in `RelationshipTools` and the large contract tests, including explicit exclusions from this task. Draft observations are not yet reported as verified acceptance.

### Independent contract review — owner coordinate versus traversal anchor

- Sol/medium independently read specification 01, exact assembly resolution, closure acquisition, context composition, current docs and the actual failing test. No mandatory source contradiction was found. Report delivered by `/root/r02_contract_review`; this review does not establish final R02 acceptance.
- Specification 01 preserves raw documentation IDs (serialization rule 5) while requiring an assembly reference to be resolved at its exact returned owner target. The former global opaque-handle lookup let the test combine a leaf handle with a distinct root traversal anchor; that input behavior is deliberately replaced by R02.
- The already supported raw closure path opens only the selected root's bounded current reference owners, validates their captured provenance, and requires an unambiguous owned declaration. It does not search unrelated resident DLLs. Context body projection retains the selected leaf owner while callers use the root closure.
- Assigned tests-only reconciliation: use the existing raw exact declaration ID for root-anchored closure analyses and their cursor replays; retain all root/bridge/leaf graph, body-hop, owner, unique-site, page-count, budget and snapshot assertions. Add explicit root plus leaf `asm:` reference rejection (`TARGET_MISMATCH`) and retain positive leaf-reference plus leaf-target body navigation. Actual successful execution remains required.

### R01 inventory disposition — current candidate source review

This table accounts for all groups in R01's complete inventory. It records inspected implementation disposition; final gates and the complete-candidate independent audit remain open.

| R01 inventoried group | R02 disposition and retained responsibility |
| --- | --- |
| Source discovery, resolver/candidates, class/member structure and body | `FindSymbolScanner`, `SourceSymbolResolver`, `ClassStructureScanner` and `SourceSymbolBodyResolver` project through `AnalysisSymbolIdentity.FormatHandoff` / `StableSourceReferenceFormatter`; exact consumption uses `ExactSourceSymbolResolver`. Request-owned `SourceReferenceFormattingContext` proves current compilation/tree owner membership. Existing model fields and raw display/location/body paths remain. |
| Skeletons, call graphs, hierarchy, references, implementations, impact and test recommendations | Their producer callbacks use the shared exact source projection or explicit leased assembly projection. Existing `SkeletonModels`, graph/hierarchy/reference/impact/test models and text/Markdown/Mermaid renderers retain field names and render shape. Internal topology, local-function and site keys remain independent. |
| Dependency and namespace graphs | Existing scanners receive stable projection callbacks from their selected source/assembly owner. Document/symbol collection and graph identities are retained; R04–R06 own later collection/cache/traversal work. |
| Assembly discovery, input/body, inspection, search and extensions | `ExactAssemblySymbolResolver` creates optional verified references. `AssemblySymbolReferenceResolver` consumes only the explicit supplied owner target through normal loading. Raw owned declarations without references retain candidates and body/location recovery. Inspection availability and follow-up arrays do not promise absent references. |
| Cross-owner closure and graph/reference/impact projection | `AssemblyReferenceFormatting` replaces `AssemblyHandoffFormatting`; closure services retain owner leases, unique original PE mapping, captured reference validation, depth/fanout/site limits and owner target fields. Stable references require their own owner target; root-anchored raw closure discovery remains supported. |
| Registry/scope/fingerprint/provenance | Only handle-specific resident/target-token acquisition APIs are removed from registry/scope. Normal session acquisition, generation leases, `AssemblyReferenceSnapshotFingerprint`, validator, fingerprint calculator and reference resolver retain binary evidence duties. |
| Public tool routes and SDK metadata | Symbol, structure, assembly and relationship handlers route original reference arguments through the Core codec. Skeleton references select all exact-owner declaration documents before existing filters; physical path bases remain unchanged. SDK descriptions expose `src:`/`asm:` without a second ID or new mode. |
| Legacy registry, counter, alphabet, token/parser and runtime | Registry/counter/alphabet/persistence/locks, old `i:` parser, source restore resolver and handle-only errors are deleted. `StableTargetPath` retains only independent lexical target/hash duties. Runtime handle binding/clear and opaque-prefix helper are removed; shutdown ordering and raw normalization remain. No external counter file is touched. |
| Analysis, cursor and response identities | `AnalysisSymbolIdentity` retains content/context evidence separately from declaration references; supplied metadata is checked through shared mismatch classification. Existing result cursors, outer pages, operation tokens and response storage remain independent. |
| Tests and TestKit | Old positive registry/counter/alphabet/parser tests are removed; independent target-path, analysis marker, assembly lease/provenance and relationship-key coverage remains. TestKit reference assertions use the Core codec. Actual transport-free handler tests cover migration; maintained E2E sources compile but official scripts exclude their execution. |
| Current docs, root README and navigation rule 08 | Public emission/input, owner and restart semantics replace opaque handles; current host/build docs remove lifecycle/counter assertions while retaining session/evidence duties. Unchanged context/index pages contain no positive old-prefix contract. Generic handoff field labels in AGENTS/code-quality rules remain valid. |
| Configuration/dependencies | No reference mode, additional ID, dependency, package or build/test configuration change; official script files have no diff. |

### Candidate gates run 3 — focused reference and paging diagnosis

- Workers frozen; `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 4 seconds.
- Executed narrow command:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceExtractionTests|FullyQualifiedName~AssemblyToolsContractTests.AssemblyReferenceClosureHandsOffAcrossRootBridgeAndLeafOwners|FullyQualifiedName~AssemblyToolsContractTests.AssemblyInspectAndSearchHandlersKeepDomainCursorsSeparateFromOuterPages|FullyQualifiedName~SourceRelationshipToolsContractTests.RelationshipScopesFilterBeforeReferenceAndImplementationTotalsAndPages' --logger 'console;verbosity=normal'
```

- FAIL, exit 1, 2 passed / 3 failed / 0 skipped, 5 total, approximately 18.18 seconds; no stall. Build/log/TRX: `temp/roadmap-evidence/R02/candidate-gates-run3/`.
- Both new `StableReferenceExtractionTests` pass. They exercise exact JSON references and null/invalid omission, generic punctuation through actual ASCII/Mermaid graph renderers, and actual class Markdown table escaping with codec round-trips.
- Source scope paging now reaches its broad comparison: the page-size-100 response at unchanged 65536-byte/4096-token bounds is parsed before consuming its outer continuation. The captured 14,863-character fragment ends inside the references collection. Its smaller domain-page parsing passed; full broad reconstruction is still required.
- Assembly inspection domain reconstruction passed; the subsequent first search response is parsed directly for a domain cursor. Its 65,320-character response contains only a 64,896-character JSON fragment ending inside the collection. The existing reader already supports outer pages, but this consumer does not invoke it before parsing.
- The added leaf body assertion similarly reads one outer page (`operation=ok`, `completeness=truncated`). Full body-window reconstruction and actual decompiled body semantics are required before the new stronger proof; original local variable names are not assumed.
- Luna is assigned tests-only reconstruction through a cohesive shared outer-page responsibility. All existing response budgets, cursor kinds, query binding, owner/site counts, broad-versus-paged content comparisons and closure proofs remain required. No timeout, category or production change is authorized by these failures.

### Paging helper integration / candidate build run 4

- Luna extracted one shared test-only outer-response reader; existing assembly and source relationship consumers delegate to it before JSON or reference extraction. It keeps domain cursors in the caller, accepts only nonrepeating numeric outer tokens, verifies each original budget and reconstructs exact fragments. First/next/replay assembly search pages and broad relationship comparisons now use it.
- The closure test explicitly requests a complete supported body window for its stronger leaf-versus-context body proof; byte/token budgets are unchanged. It checks availability, preserved literal and actual decompiled semantics without assuming original local names. Impact pagination verifies one semantic caller site and that caller's stable ID, alongside exact full-payload equality and exact minimum-budget recovery.
- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 1 error / 0 warnings, approximately 2.6 seconds: the duplicate-generic-owner dependency-graph test omitted the new continuation-reader callback (`SourceRelationshipToolsContractTests.cs:384`, CS7036). No tests ran. Evidence: `temp/roadmap-evidence/R02/candidate-gates-run4/build.log`.
- Luna is assigned this remaining consumer wiring correction; production remains unchanged. Required final gates and audit remain open.

### Candidate gates run 5 — corrected paging consumers

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 2.65 seconds.
- Re-executed the exact five-test command from run 3: FAIL, exit 1, 4 passed / 1 failed / 0 skipped, approximately 27.66 seconds. Both reference extraction tests, source filter/total/paging reconstruction and assembly inspect/search domain-versus-outer reconstruction pass. Evidence: `temp/roadmap-evidence/R02/candidate-gates-run5/`.
- Closure failure moved to the incoming root graph: its first response is `operation=ok`, `completeness=truncated`, and the first-fragment assertion cannot find `ClosureBridge`. Full graph reconstruction and metadata diagnostics are assigned before deciding whether this is only another outer-page consumer or a production traversal defect. The owner-coordinate contract is unchanged.
- The tests worker must apply the existing shared reader to all remaining full-response consumers in this closure scenario, preserving every graph/owner/cursor/site/budget assertion. The production worker performs read-only traversal diagnosis. No failing graph assertion is removed and no production correction is authorized without actual completed-response evidence.

### P06 actual complete-graph regression / candidate gates run 6

- After reconstructing all closure responses, `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 2.76 seconds.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyToolsContractTests.AssemblyReferenceClosureHandsOffAcrossRootBridgeAndLeafOwners' --logger 'console;verbosity=normal'`: FAIL, exit 1, 1 failed / 0 passed / 0 skipped, approximately 11.98 seconds; no stall. Logs/TRX: `temp/roadmap-evidence/R02/candidate-gates-run6/`.
- This is a production regression, not an unread outer page: exactly one response, no `continuationToken`, `analysisCompleteness=partial`, `omissions=traversalLimit`. The complete graph contains `Bridge.Forward -> Leaf.Read` but lacks the Bridge owner reference and further Root caller. Its unchanged owner/path and graph assertions fail.
- P06 source cause: Core produces internal graph nodes with reference and assembly identity but no `OwnerTargetPath`; the new closure mapper requires that external projection field before frontier expansion, while merge populates it afterward. Consequently the proven Bridge caller cannot enter the next frontier.
- Luna is assigned the owning production correction: carry/prove originating leased scan-owner evidence before external projection, centralize frontier/merge mapping, and keep exact current compilation/source declaration, original PE identity, uniqueness, captured provenance and generation leases. No all-resident lookup, first simple-name match, second ID or new SDK argument.
- A separate narrow eligible `AssemblyCallTreeOwnerContractTests` fixture is assigned to isolate owner mapping and transitive incoming traversal from the larger paging/lifecycle closure scenario. Both the original complete-graph regression and this focused contract must pass; limits, owner and error acceptance remain unchanged.

### P06 correction / focused fix run 1

- Luna changed `AssemblyCallTreeClosureBuilder` only. Frontier expansion and merge use the same mapping helper with their producer scope. An internal node without a projected path must prove that scope/path is uniquely present in the already leased closure, its metadata simple name matches, the declaration is owned source in that compilation, and exact reference recreation is byte-identical. Already bound paths and unbound metadata identity/uniqueness checks remain separate.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 4.12 seconds.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyCallTreeOwnerContractTests|FullyQualifiedName~AssemblyToolsContractTests.AssemblyReferenceClosureHandsOffAcrossRootBridgeAndLeafOwners' --logger 'console;verbosity=normal'`: FAIL, exit 1, 1 passed / 1 failed / 0 skipped, approximately 13.04 seconds; no stall. Saved build/log/TRX: `temp/roadmap-evidence/R02/p06-fix-run1/`.
- The original fully reconstructed strict closure regression PASSES, including transitive Root/Bridge graph, actual owner follow-ups, two-page site/count/cursor assertions and exact complete leaf/context body equality. P06's reproduced production defect is corrected for that acceptance; final independent audit remains open.
- The small new owner contract reaches complete/no-omission graph assertions, then its reader incorrectly expects Mermaid's `handoffId:` label in default ASCII output. Luna is assigned this test-renderer assumption, exact current declaration-ID suffixes and actual decompiled call-body verification. No producer/owner/completeness assertion is weakened and no production change is needed for this reader failure.

### P06 focused test correction / candidate gates run 7

- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 2 errors / 0 warnings, approximately 2.68 seconds: the new focused assertion uses `.Count` on an array (method group), not `.Length`. Saved log: `temp/roadmap-evidence/R02/p06-fix-build2/build.log`. Luna corrected only the assertion and diagnostic.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 2.66 seconds. `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyCallTreeOwnerContractTests' --logger 'console;verbosity=normal'`: PASS, exit 0, 1 passed / 0 failed / 0 skipped, approximately 3.22 seconds. Build/log/TRX: `temp/roadmap-evidence/R02/p06-fix-run2/`.
- The focused contract proves complete/no-omission incoming traversal, exact three separate owner coordinates for same-named methods, actual owner-body calls and a single complete outer response. It reads actual ASCII handoff output and canonical return-type declaration IDs rather than assuming another renderer or raw-discovery ID shape.
- Re-executed the exact affected FastTests command recorded above after P06: PASS, exit 0, 532 passed / 0 failed / 0 skipped, approximately 21.36 seconds. No stall; individual progress observed.
- Expanded mandatory Integration command includes both focused new classes:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.SourceToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.SourceRelationshipToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.RelationshipToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.AssemblyToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.IndexScopeContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.StableReferenceExtractionTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.AssemblyCallTreeOwnerContractTests' --logger 'console;verbosity=normal'
```

- FAIL, exit 1, 51 passed / 4 failed / 0 skipped, 55 total, approximately 2 minutes 58 seconds. Longest observed individual test approximately 46 seconds; no stall. Build/Fast/Integration logs and TRX preserved under `temp/roadmap-evidence/R02/candidate-gates-run7/`.
- Remaining demonstrated test failures: two impact broad-baseline helpers compare only their first outer fragment with a fully reconstructed smaller-budget result; source skeleton global reference counts read only its first outer page; a source namespace subcase still calls the assembly-only reference reader; actual CRLF skeleton lines have a terminal structural CR that the shared closing-backtick boundary reader does not recognize.
- Luna is assigned these tests-only corrections, using the shared page/reference semantics and adding real skeleton-renderer extraction coverage. All production owner/closure regressions, SDK descriptions, source edit/options/restart acceptance, raw local/ambiguous recovery, mixed batches and existing paging/provenance tests in this run pass. No category, budget, count or production acceptance is changed to bypass the four failures. Required Extended tests and final independent audit remain open.

### Candidate correction gate 8 — shared reader consumers

- Luna reconstructed both impact broad baselines and source skeleton responses through the shared outer-page reader, used the codec-neutral namespace reader, and removed only terminal structural CR from rendered lines. A new actual skeleton-renderer CRLF test covers generic/member references.
- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 1 CS8604 error / 0 warnings, approximately 2.3 seconds; the new fixture passed a nullable Solution path. No tests ran. Saved log: `temp/roadmap-evidence/R02/candidate-fix-build8/build.log`. Luna added an explicit nonblank fixture-path invariant and guard; no analyzer configuration changed.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 2.35 seconds. Executed focused command:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~RelationshipToolsContractTests.GetImpact_OriginalSdkContractRequiresSymbolAndRoutesSourceAndAssemblySymbols|FullyQualifiedName~SourceToolsContractTests.SourceHandlersReturnNavigableResultsAndTypedDomainErrorsWithoutTransport|FullyQualifiedName~AssemblyToolsContractTests.AssemblyClassStructureUsesDeclarationOrderBeforeCapAndContextAndMemberHandlesStayNavigable|FullyQualifiedName~AssemblyToolsContractTests.AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes|FullyQualifiedName~StableReferenceExtractionTests' --logger 'console;verbosity=normal'
```

- FAIL, exit 1, 5 passed / 2 failed / 0 skipped, 7 total, approximately 16.84 seconds; no stall. Saved build/log/TRX: `temp/roadmap-evidence/R02/candidate-fix-run8/`.
- Previously failing impact, skeleton global counts and neutral namespace consumers now pass. The all-routes test advances past skeleton extraction to its recovery repetition comparison: independently allocated numeric outer continuation tokens differ while comparing entire response envelopes. The new CRLF fixture extracts canonical references successfully but expects `!0` instead of Roslyn's actual source declaration-ID parameter spelling `` `0 ``. These are tests-only assumptions; Luna is assigned exact semantic correction while retaining payload/metadata equivalence, valid tokens, budget/error/status and exact generic round-trip assertions.
- Required full Integration, Extended selection and independent final audit remain open; no acceptance checkbox is changed.

### Focused correction gate 9 / mandatory selection rerun

- Luna corrected the new fixture to Roslyn's actual ``(`0,System.Int32)`` spelling while retaining exact method arity, generic return type and codec round-trips. Recovery repetition compares the complete envelope/payload after normalizing only a structural independently allocated outer continuation token; nonempty ASCII-digit validation, token presence, error/status, budgets and all remaining snapshot/domain metadata remain checked.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 2.72 seconds.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyToolsContractTests.AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSeventeenRoutes|FullyQualifiedName~StableReferenceExtractionTests.SkeletonMarkdownCrlfLinesPreserveEveryGenericAndMethodReference' --logger 'console;verbosity=normal'`: PASS, exit 0, 2 passed / 0 failed / 0 skipped, approximately 5.25 seconds; no stall. Saved build/log/TRX: `temp/roadmap-evidence/R02/candidate-fix-run9/`.
- The exact expanded mandatory command from run 7 is now running with 56 cases, including the new CRLF fact. No further product or FastTests changes occurred since the 532-case PASS. Extended selection and final independent audit remain open.

### Mandatory candidate gates run 10 / narrow Extended gate

- The exact seven-class Integration command from run 7 PASSES: exit 0, 56 passed / 0 failed / 0 skipped, approximately 2.834 minutes. Longest observed individual test approximately 47 seconds; continuous progress, no stall. Build/log/TRX: `temp/roadmap-evidence/R02/candidate-gates-run10/`. The source edits/options/restart, all public route/batch/error/budget/page/schema tests, owner closure/replacement and focused new contracts pass.
- Executed required narrow Extended command:

```powershell
pwsh -File ./scripts/test-integration.ps1 -IncludeExtended -Filter 'FullyQualifiedName~WorkspaceLoadingIntegrationTests.MSBuildSolutionLoader_CustomTargetsAndScratchCleanupPreserveWorkspaceSnapshot|FullyQualifiedName~WorkspaceLoadingIntegrationTests.MSBuildSolutionLoader_ColdSolutionsWithSameNamedProjectsHaveIsolatedSnapshots' --logger 'console;verbosity=normal'
```

- PASS, exit 0, 2 passed / 0 failed / 0 skipped, approximately 3.196 seconds; no stall. Log/TRX: `temp/roadmap-evidence/R02/extended-run1/`. Source workspace read-only scratch/cleanup/recovery and independent same-name cold solution snapshots remain intact. E2E tests are excluded by the official script; no transport proof is claimed here.
- Separate Sol/medium complete-candidate audit round 1 is assigned against the actual whole production/test/docs diff, R01 inventory and all R02 requirements. Its earlier owner-coordinate reconciliation was a limited source review, not this final audit. Workers remain frozen. Required routine solution-wide selection is scheduled once at R08; no R02 checkbox is marked before the current audit and root acceptance review finish.

### Independent complete-candidate audit round 1 — interim findings

- Separate Sol is inspecting actual production, test, schema and documentation sources read-only. Three concrete source findings have been reported and confirmed by the root; the complete report remains pending, and files remain frozen for that review.
- R02-A01: `StructureTools.BuildSkeletonsAsync` formats an exact-resolution failure using Code/Message only, dropping the actionable Hint. Preflight failures use the shared formatter correctly. The existing passing mixed tests do not cover valid same-origin references with an unloaded/wrong owner or absent exact declaration.
- R02-A02: `RelationshipTools.BuildAssemblyContextWithReferencesAsync` creates a valid closure formatter but omits it when constructing `ContextResult.Target`; assembly `BuildContextDeclaration` therefore sets its stable reference null. The non-reference-closure context path already passes its formatter.
- R02-A03: `RelationshipTools.ResolveAssemblySymbolAsync` acquires the raw selected-owner scope but lacks exception/cancellation-safe cleanup around its subsequent await. Returned failures dispose correctly; thrown/cancelled raw resolution can leak the access. Exact reference access already has a transfer/finally contract.
- These findings remain open. After the complete round, Luna must add focused executable failing regressions before behavior fixes; official affected gates and independent re-audit remain mandatory. The current green gates do not override these missing behaviors or authorize completion.
- Root rechecked legacy literals with `rg -n '(?i)(?:\bh:|\bi:)' src tests docs README.md .agents -g '*.cs' -g '*.md' -g '*.mdc'`: 64 matched lines, saved under `candidate-gates-run10/legacy-prefix-inventory.txt`. Five codec lines are rejection/physical-drive exception code or comments; 56 test lines are negative routing/schema assertions and physical-drive cases, including one unrelated `{i:D2}` formatting false positive; three current docs/rule lines describe rejection. No active acceptance/emission path remains. Search for removed registry/counter/Base62/token/handle errors yields no active machinery match. Task requirements/history remain intentionally outside this current-product search.
- Root checked 26 changed Markdown/rule sources: all 114 parsed local file targets exist. Earlier documentation-worker validation also passed all 106 local targets and heading anchors across its 25-source/107-link selection; its one external link was not fetched. No additional external behavior claim follows from link checking.

### Complete-candidate audit round 1 — FAIL / regression-first assignment

- Sol/medium independently read the entire current code/test/docs diff, untracked replacement owners, all R02 requirements, R01 inventory and saved passing gate logs. Final verdict FAIL: three confirmed P2 findings, corresponding to R02-A01/A02/A03 above. No auditor edits or verification runs occurred.
- A01 requires actual Source and Assembly mixed/all-failed exact-resolution variants with per-item `TARGET_MISMATCH`/`SYMBOL_NOT_FOUND` recovery. A02 requires Target reference/owner/round-trip and body follow-up for direct stable leaf and raw root-closure contexts, also compared with the non-closure path. A03 requires deterministic cancellation/throw after successful scope acquisition, unchanged active-access count and subsequent navigation, plus normal failure/success transfer coverage.
- Sol's remaining requirements matrix passed source review: canonical/original input, exact current Source owner/Compilation/tree/ID, explicit Assembly owner/reopen, independent raw/no-reference body/candidates, producer families aside from Context, partial/generated Skeleton selection aside from recovery, P06 shared proven producer mapping, independent analysis/site/cursor identities, request-only Roslyn formatting state, provenance, complete legacy machinery removal, unchanged public fields/SDK parameters/dependencies, documentation/inventory and independent replacement tests. No confirmed assertion weakening was found. Green gates alone do not close the three missing variants.
- Luna/high `/root/r02_implementation` owns this complete fix slice: the two production owners and narrowly scoped eligible new Integration contract classes. Other workers remain frozen. First stage is regression preparation only, with a minimal inert internal seam permitted if deterministic after-acquisition fault injection needs it; this instrumentation must be disclosed and does not constitute a behavior correction. The root runs the actual baseline before authorizing fixes. No product correction, commit or checkbox has happened for these findings yet.
- The auditor confirmed the draft remaining-structure assessment and explicit out-of-task blanket handler/SDK/test-framework reorganization exclusions. Its durable verified-checkpoint assessment remains pending final acceptance.

### Audit regression preparation / build run 1

- Luna added five separately filterable eligible `StableReferenceResolutionContractTests` facts: Source and Assembly semantic Skeleton failures (mixed/all-failed); direct Leaf versus Leaf-closure versus Root/raw-Leaf Context owner references/body follow-ups; after-acquisition exception and cancellation lifetime cases. Missing-ID fixtures construct fresh positional values and verify the decoded declaration ID; a record `with { Id = ... }` would leave the inherited declaration ID unchanged and is not used as a false reproduction.
- The only production instrumentation is an inert internal `AfterAssemblySymbolScopeOpenedForTesting` hook after acquisition and before resolution; the helper becomes instance-owned for that seam. The faulty lifetime, Context formatter and Skeleton recovery behavior remain unchanged.
- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 2 errors / 0 warnings, approximately 5.84 seconds. New fixture errors: CA2000 on a registry constructor subsequently held by an alias; MA0042 requires asynchronous cancellation. No tests ran. Log: `temp/roadmap-evidence/R02/audit-regression-build1/build.log`.
- Luna is assigned only direct fixture ownership and `CancelAsync` corrections; no behavioral fix is authorized before actual regression execution. Original 56 Integration/532 Fast/2 Extended PASS remains pre-fix evidence, not closure of audit findings.

### R02-D01 — abnormal audit-regression stage

- Corrected fixture build PASS, exit 0, 0 warnings/errors, approximately 2.79 seconds.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests' --logger 'console;verbosity=normal'`: ABORTED / session exit 1. VSTest reports a crashed testhost at 2026-10-04 00:03:16 Europe/Berlin, approximately two minutes after start; TRX records zero completed/executed tests. This is not a valid failing-regression proof and no PASS is claimed.
- Saved build/log/TRX and verified remaining owned-process inventory under `temp/roadmap-evidence/R02/audit-regression-run1/`. Root stopped only its matching original script process tree after the host abort; no unrelated process or checkout was touched.
- Opened executable [R02-D01](R02-D01-audit-regression-diagnosis.md) before further fixes/gates. Luna is assigned fixture lifetime/cancellation/teardown diagnosis and smallest-fact reproduction. Hypothesis: a deliberately leaked lease makes test cleanup wait, and cancelling a request waiter does not establish job cancellation. Source observation alone does not establish the host crash cause. No broad retry, timeout increase, skips or behavior correction is authorized until this diagnosis is closed with finite narrow execution.

### Finite audit baseline — confirmed A01 and A03

[R02-D01](R02-D01-audit-regression-diagnosis.md) isolated the original cancellation fact, corrected fixture cancellation/cleanup and verified no orphan remains. Official corrected build PASS, 0 warnings/errors. `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests' --logger 'console;verbosity=normal'` completes with exit 1, all five facts failed, approximately 3.176 seconds (`temp/roadmap-evidence/R02/diagnosis-regression-run2/`). Source/Assembly missing item Next action and exception/cancel active count 1 instead of 0 are actual pre-fix proofs. Context instead exposes an invalid fixture combination (explicit includeReferences false without callers), so A02 behavior correction remains held until its corrected singleton executes. No finding is closed from source-only inference; no product behavior fix yet.

### All audit findings reproduced / D01 closed

Root-reviewed [D01](R02-D01-audit-regression-diagnosis.md) is closed after actual narrow isolation, finite fixture cleanup and exact orphan attribution. Analyzer-clean official build PASS. The corrected Context singleton fails because the closure Target omits `handoffId`; the final five-fact baseline completes in approximately 3.314 seconds with all five actual required-invariant failures (exit 1, no skips). Logs/TRX: `temp/roadmap-evidence/R02/audit-regression-run4/`. Both A03 facts execute normal raw absence and raw/stable success/released-count checks before failing the captured pre-cleanup count 1 versus 0. Root now authorizes Luna's shared Skeleton failure formatting, closure Target formatter wiring and exception-safe Scope ownership transfer. All three audit findings remain open until corrected gates and independent re-audit pass.

### Audit correction gate 1

- Luna's production corrections are confined to `StructureTools` and `RelationshipTools`. Semantic Skeleton failures use the existing shared item failure formatter and Hint; closure Context passes its already proven/formatted exact owner reference into declaration projection; acquired Scope transfer is protected by `finally`, including hooks, returned errors, exceptions and cancellation. A method-local CA2000 ownership-transfer explanation matches the existing factory convention; no analyzer configuration changes.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, 0 warnings/errors, approximately 4.49 seconds.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~StableReferenceResolutionContractTests' --logger 'console;verbosity=normal'`: PASS, exit 0, 5 passed / 0 failed / 0 skipped, approximately 4.191 seconds. All previously reproduced invariants pass, including mixed/all-failed semantic recovery, direct/Leaf-closure/Root-raw-Leaf Target/body follow-ups and pre-cleanup count zero on injected cancelled-task/exception paths. Saved build/log/TRX: `temp/roadmap-evidence/R02/audit-fix-run1/`.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.FastTests.Mcp' --logger 'console;verbosity=normal'`: PASS, exit 0, 142 passed / 0 failed / 0 skipped, approximately 12.223 seconds. Host cancellation, schema, response budgets, operation/continuation retention, errors and shutdown remain intact. Saved log/TRX in the same directory. The unchanged Core families retain their earlier 532-case affected selection PASS; the new production changes are Host handlers.
- Evidence correction: the executed existing `InitialRequestCancellationStopsWorkButPollingCancellationDoesNot` contract and `WaitForResultAsync` source show that initial-request cancellation does cancel owned work. The early D01 waiter-mismatch hypothesis was incorrect for the original initial request; its documentation is corrected. The confirmed nonfinite teardown cause was the uncollected deliberately leaked scope. The new deterministic cancelled-task case proves ownership cleanup on cancellation exceptions; it does not claim new transport/job cancellation behavior.
- Expanded mandatory Integration selection now includes `StableReferenceResolutionContractTests`, for 61 cases. Full selection and narrow Extended repeat remain in progress/pending before independent audit round 2.

### Post-fix mandatory gates / independent re-audit round 2

Executed expanded command:

```powershell
pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.SourceToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.SourceRelationshipToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.RelationshipToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.AssemblyToolsContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.IndexScopeContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.StableReferenceExtractionTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.AssemblyCallTreeOwnerContractTests|FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.StableReferenceResolutionContractTests' --logger 'console;verbosity=normal'
```

- PASS, exit 0, 61 passed / 0 failed / 0 skipped, approximately 3.1033 minutes. Longest observed individual test approximately 45 seconds; continuous progress, no stall. Saved build/log/TRX: `temp/roadmap-evidence/R02/audit-final-gates-run1/`.
- Repeated the exact narrow Extended command recorded above after the Host corrections: PASS, exit 0, 2 passed / 0 failed / 0 skipped, approximately 3.0635 seconds; no stall. Saved log/TRX: `temp/roadmap-evidence/R02/extended-run2/`.
- No active IntegrationTests apphost or verification remains; all workers frozen. Separate Sol/medium complete-candidate re-audit round 2 is assigned against the actual code, all requirements and closed-regression evidence. D01 and ongoing audit evidence were persisted in documentation-only commit `dc65054448f547dd4d3e3a43e0cb1bc4b3862f2c`; it is not the R02 implementation commit.
- All three audit corrections have passing actual regressions; their final audit disposition and R02 acceptance boxes remain pending independent round 2 and root review. E2E remains excluded/compile-only, and the one full routine solution gate remains R08's obligation.

### Independent re-audit round 2 and root acceptance — verified checkpoint

- Separate `gpt-6.1-sol` / `medium` auditor reviewed actual complete production/tests/docs and archived logs read-only, independently of the Luna implementation/fix workers: PASS, no open actionable findings. A01 shared Skeleton recovery, A02 exact closure Context Target projection and A03 exception/cancellation-safe lease transfer are closed. The five-test finite failing baseline and passing regression remain distinct from the initial aborted zero-result run. Initial-request cancellation and polling-waiter cancellation retain their existing different contracts.
- Root reviewed the inventory disposition, exact input/owner handling, transient source formatting, successful/error ownership transfer, preserved independent identity/cursor boundaries and actual verification evidence. R02.1–R02.3 are satisfied. Source/config/tests/docs have no active legacy registry/counter/Base62/old resolver/error machinery. Remaining legacy prefixes are classified rejection tests/code, physical drive cases or negative documentation; an unrelated format-specifier match is not a symbol route. No external counter file was modified.
- Required gates are build (zero warnings/errors), 532 affected Fast tests before Host-only audit fixes, 142 affected Host Fast tests after them, all 61 mandatory/focused Integration tests, and the repeated two narrowly relevant Extended workspace tests. No E2E execution is claimed. Full routine solution verification remains R08.

### Structure assessment at the verified R02 checkpoint

`RelationshipTools` (1,571 lines at this checkpoint) still owns several independent duties: SDK registration and argument contracts, source/assembly routing, dependency selection/collection/projection, context-section composition, metadata/cursor binding and recovery rendering. Existing assembly closure owners retain their lease and provenance duties. R03 must place snapshot identity single-flight and scalar memoization in a runtime-owned service, keeping exact source-reference projection request-owned. R04 must centralize collection for a complete document batch; R05 must separate typed scalar facts, coverage and cache lifetime; R06 must own targeted traversal; R07 must keep operation progress and polling in the existing host operation owner. Host methods retain selection, metadata and cursor integration. Shared semantics must have one owner, rather than parallel source/assembly or host implementations.

The large `AssemblyToolsContractTests` (2,404 lines) and source/relationship contract classes mix ownership/lifetime, batches, body windows, budgets, domain/outer paging and cross-owner closure fixtures. Their cohesive behavioral families matter more than their line counts. R02 introduced independently selectable `StableReferenceExtractionTests`, `AssemblyCallTreeOwnerContractTests` and `StableReferenceResolutionContractTests` (the latter 319 lines), with shared exact-reference extraction and outer-page reconstruction where semantics match. R03–R07 must add similarly focused contract tests and reuse helpers without hiding independent assertions.

Explicitly outside this task: blanket reorganization of every RelationshipTools handler or SDK signature/registration; splitting classes merely by line count; replacement of the test framework; global rewrite of existing fixture/parser helpers or all large contract classes; and restructuring the excluded E2E suite. A narrowly required extraction or confirmed regression in one of those owners remains in scope when its roadmap contract needs it. No blanket cleanup is a prerequisite for the next point, and no structural debt excuses an omitted gate.

- Final root closure: product commit `9c1c3951649a16fbdb1b5a065bd8d2a00a79aa44` established the verified slice. Root deliberately checked R02.1 (full route inventory/removal), R02.2 (actual acceptance/gates/literal disposition), R02.3 (docs/schema/rules/helpers) and R02.4 (inventory/commands/audit/fix/commit evidence), then the index R02 checkbox. Final documentation file-link and diff checks passed. Historical pending statements above describe their original execution stage, not current status.
