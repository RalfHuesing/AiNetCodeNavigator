# R03 — Fresh snapshot identity once

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R02](R02-public-reference-migration.md). Next: [R04](R04-single-collection.md).

Contract: [04 — freshness, identity inputs and memoization](../04-snapshot-refresh-and-analysis-cache.md).

Implement runtime-owned weak-key single-flight identity memoization for the exact immutable Solution. Route remaining identity call sites through it after R02's removals. Preserve full evidence fingerprints and get-index-scope's configured-inventory identity domain.

Verify current refresh detects loaded source/project/reference changes. Narrowly correct metadata-reference freshness in the existing fingerprint/reload owner if required by the prescribed replacement regression. Do not introduce watcher-only or timestamp-only freshness.

Acceptance: unchanged and concurrent calls compute identity once; cancelled callers do not poison another waiter; new source/config/reference context recomputes; same-timestamp changes are detected; equivalent reloads retain deterministic content identity; old Solution objects are not retained by completed memo entries.

Verification: official build; affected AnalysisSymbolIdentity, resident workspace/fingerprint and routing FastTests; workspace/index-scope/source handler integration contracts; relevant targeted extended workspace/reference tests. Record hashing versus refresh measurements separately.

Completion checklist:

- [ ] R03.1 — Freshness checks and weak-key single-flight identity memoization implement specification 04 at all remaining call sites.
- [ ] R03.2 — Same-timestamp source/reference changes, options, concurrent/cancelled waits, reload identity and lifetime acceptance passed.
- [ ] R03.3 — Required build/test selections passed; separate refresh/identity measurements and affected current-state docs are recorded.
- [ ] R03.4 — Implementation commit(s) and executed evidence are recorded; the orchestrator reviewed R03.1–R03.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | In progress; started from clean verified R02 HEAD `671496d` on 2026-10-04 |
| Implementation commit(s) | — |
| Executed verification | Original reference freshness regressions failed as required before correction; first candidate build failed (see rounds below); candidate acceptance gates remain pending |
| Measurements / artifacts | — |
| Blocker / next action | Finish authorized P01/P02 corrections, shared fresh-boundary budget and provider lifetime/binding semantics; freeze all workers, execute complete affected gates and separate measurements, then independent Sol audit |

### Prerequisites and independent ownership

R02 product commit `9c1c3951649a16fbdb1b5a065bd8d2a00a79aa44` and verified evidence commit `671496d` are present; all R02 detail boxes and index checkbox are checked. The working tree and index were clean before R03 assignment. Root reread the full specification 04, specification 02 canonical paths, execution rules, all repository rules, R03 and existing identity call-site inventory. R02's verified structural checkpoint governs this point.

- `/root/r03_identity`: newly explicitly configured `gpt-6-luna`, reasoning `high`; owns strict typed fingerprint encoding, weak-key runtime identity memoization/tickets, all remaining identity consumers, request-owned projection separation, Host runtime/support wiring and focused Symbols/MCP Fast tests. Core Workspace, Integration tests and current-state docs are excluded from its ownership.
- `/root/r02_implementation`: reused with its original explicitly configured `gpt-6-luna`, reasoning `high`; new R03 freshness assignment owns Core Workspace capture/fingerprint/refresh/load integration and focused Workspace Fast tests, with narrowly necessary TestKit capture fixtures. It coordinates immutable image bytes/hash and supported binding provenance with the identity owner. Existing reproducible freshness defects require official failing regressions before correction.
- `/root/r02_tests_docs`: reused with its original explicitly configured `gpt-6-luna`, reasoning `high`; owns focused eligible Integration tests, separate refresh/identity measurement fixtures and current-state documentation/root README/navigation rule updates against actual code. Shared helpers retain meaningful independent assertions; existing large contract classes are not globally reorganized.

Root owns all verification, diagnosis, progress/checkboxes and commits. Workers do not execute competing gates. A separate explicitly configured Sol/medium read-only full audit follows actual completed gates. No R03 completion is implied by these assignments or unrun new tests.
### R03-P01 reference freshness — frozen original baseline

Workers held all production behavior changes; only two focused test fixtures and an unreferenced immutable input record file were added. Root reviewed fixture order before execution: compile/capture the original reference first, then replace it.

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, approximately 3.78 seconds reported build time.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~ResidentSolutionMetadataFreshnessTests.GetCurrentSnapshot_WhenReferencedImageChangesWithSamePathSizeMtimeAndMvid_RebindsCompilation' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failing test / zero skipped, approximately 1.6821 seconds. The original compilation's constant is 17. A valid replacement changes it to 18 while preserving path, size, timestamp and MVID (and verifies changed SHA-256). The next fresh snapshot remains the identical old Solution object, failing `Assert.NotSame`. This proves the prescribed missing reference freshness before correction; the later replacement constant assertion is not claimed executed.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceSnapshotIdentityContractTests' --logger 'console;verbosity=normal'`: FAIL, exit 1, one completed failing test, approximately 3.8436 seconds. Initial origin JSON spans outer pages; the new fixture parses its incomplete first page and fails before the replacement invariant. This is a fixture defect, not additional freshness proof. Luna is assigned tests-only proper outer-page draining with unchanged query/budgets; production correction remains held until the original handler baseline reaches its actual invariant.
- Logs/TRX preserved at `temp/roadmap-evidence/R03/metadata-baseline-run1/`. No stall or orphan observed; all gate sessions completed. No R03 checkbox is checked.

- Fixture paging correction build: `pwsh -File ./scripts/build.ps1` FAIL, exit 1, 6 CS1061 errors / zero warnings, approximately 1.84 seconds reported build time. The shared reader tuple names its reconstructed body Text; the new fixture incorrectly reads Body. Luna is assigned tests-only member corrections. Saved build log: `temp/roadmap-evidence/R03/metadata-baseline-build2/build.log`. No production correction or additional acceptance execution occurred.


### R03-P01 actual eligible handler baseline / implementation release

- The tests-only fix reads every outer response page using the existing shared reader with the original 16,384-byte / 1,024-token budgets and unchanged arguments. Snapshot metadata comes from the first-page header; origin JSON uses the reconstructed full body. Six mistaken tuple member names were corrected without changing helper or production semantics.
- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, approximately 1.94 seconds reported build time.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceSnapshotIdentityContractTests' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failing test / zero skipped, approximately 3.8674 seconds. Original discovery and `OldApi` metadata-origin presence pass. After replacement at the same path/size/restored timestamp with changed image SHA-256, the fresh handler still reports the identical source snapshot ID (`Assert.NotEqual` fails). Full-body replacement binding and source-reference follow-up assertions remain required for the eventual passing test; they are not claimed executed in this failing baseline.
- Artifacts: `temp/roadmap-evidence/R03/metadata-baseline-run3/{build.log,test-integration.log,IntegrationTests.trx}`. Original Fast baseline independently proves the same-object defect with MVID also preserved. All gates terminate normally; no stall or orphan.
- Root now authorizes Luna's complete production freshness correction and identity service integration against the binding R03 contract. Focused tests retain their failed invariants and will additionally verify replacement `NewApi` binding. Independent audit and all mandatory gates remain open.

### Candidate ownership and source review (not verified)

The metadata capture owner now carries validated inputs with the exact rebound immutable Solution and hashes the same bytes used to construct PE references, with weak reference-keyed metadata lifetime and a shared canonical image map per attempt. The identity owner has drafted the encoder/runtime memo service; all Core consumers and the final supported-provider validation remain in progress. No candidate pass is claimed.

Root source review highlighted strict logical-key ordering (framed-byte ordering differs from ordinal paths), fixed source/additional/analyzer-config categories, all eight loaded build properties with `unknown`, exact context association for duplicate physical projects, scalar-only shared retention, and failure-flight admission races. Luna is addressing these within new behavior before frozen verification; these are working review observations, not a completed independent audit.

To balance independent responsibilities, root transferred only the new `tests/AiNetCodeNavigator.FastTests/Symbols/SourceAnalysisIdentityEncodingTests.cs` to `/root/r02_tests_docs` (retaining explicitly configured Luna/high). That worker derives focused encoding/options/context requirements independently from specification 04. `/root/r03_identity` retains service concurrency/cancellation/retry/weak-lifetime tests, existing Symbols/MCP tests, production encoder/runtime and all identity consumers. Workspace tests/capture remain with the freshness owner. No competing builds/tests are authorized.

### R03-P02 loaded non-source document freshness — pending regression

Root source review found that the existing resident text refresh enumerates only regular `Project.Documents`. Specification 04 also binds loaded additional and analyzer-config texts; cached physical texts in those categories need fresh-boundary validation even with unchanged timestamps. Luna is assigned focused tests first: force the initial loaded text, replace with equal-size/restored-timestamp content, then assert the new snapshot and updated text/provider evidence. This is a source hypothesis pending actual official execution, not a claimed failed regression. The behavior correction is held until that baseline; other unfinished R03 implementation remains within the same point.

Root also requires provider provenance to cover `CompilationOptions.SyntaxTreeOptionsProvider`; a creating-owner capability may admit supported normal option transformations without guessing Roslyn internal implementation names. Private provenance stays outside shared scalar memo entries.

### Candidate build round 1 — compile corrections only

- `pwsh -File ./scripts/build.ps1`: FAIL, exit 1, 15 errors / zero warnings, 3.62 seconds reported build time. Workspace capture uses unavailable Roslyn members (`Length` on an IReadOnlyList, a non-public documentation provider and the wrong Solution metadata-reference method). Identity code has missing error-code imports, an evidence overload mismatch, shadowed local names and nullable resolver paths. No candidate tests ran.
- Artifact: `temp/roadmap-evidence/R03/candidate-build-run1/build.log`. Both production owners are assigned only their scoped compile/analyzer corrections before the next frozen build. R03-P02 production correction remains held until an actual failing regression. There is no stall or blocker established by this initial candidate compile failure.

- Candidate build round 2, same command: FAIL, exit 1, one CS1503 / zero warnings, 3.56 seconds reported time. Core and Host compile; TestKit's analyzer-config addition needs the public SourceText overload. Artifact: `temp/roadmap-evidence/R03/candidate-build-run2/build.log`. Luna corrected only the fixture overload.
- Root reviewed R03-P02 before execution and caught unequal-length configuration replacement text. Luna corrected the fixture to `Debug` / `Other` and added explicit UTF-8/file-size equality and restored-timestamp assertions for both physical inputs. Production correction remains held; no failed acceptance is claimed from this fixture inspection.
- Candidate build round 3, same command: FAIL, exit 1, 16 errors / zero warnings, 2.23 seconds reported time. Core, Host and TestKit compile. New Fast encoding tests use impossible subclasses of sealed pinned C# option types, incorrect public comparer/resolver overrides and a missing analyzer namespace; Integration has a shadowed JSON local. The test owner is assigned scoped public-API compile corrections preserving independently testable requirements; private compiler injection is outside the specified supported context. Artifact: `temp/roadmap-evidence/R03/candidate-build-run3/build.log`. No tests ran, and no mandatory gate passed.
- Candidate build round 4, same command: FAIL, exit 1, 22 errors / zero warnings, 3.51 seconds reported time. All Integration code compiles; remaining new Fast fixtures have public Roslyn overload/constructor mistakes, missing namespace imports, unsupported fixture property accesses and two MA0042 violations. Scoped compile/analyzer corrections preserve assertions and use the pinned public API. The C# option types are sealed and the AssemblyIdentityComparer constructor is internal in Roslyn 5.9; impossible public subtype fixtures are replaced by actual unsupported-language/provider cases without private compiler injection. Artifact: `temp/roadmap-evidence/R03/candidate-build-run4/build.log`.

### R03-P02 actual failing regression / correction release

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, 1.19 seconds reported build time. This establishes candidate buildability only, not R03 acceptance.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~ResidentSolutionMetadataFreshnessTests.GetCurrentSnapshot_WhenAdditionalAndAnalyzerConfigFilesChangeWithSameTimestamp_RefreshesLoadedText' --logger 'console;verbosity=normal'`: actual FAIL, exit 1, one completed failing test / zero skipped, 1.3025 seconds test-run time (354 ms test). Both initially cached texts and explicit equal UTF-8/file-size/restored-time assertions pass. The next fresh snapshot still returns the identical old Solution, failing `Assert.NotSame` at line 172. Later changed-text assertions remain required and are not claimed executed in this baseline.
- Artifacts: `temp/roadmap-evidence/R03/text-input-baseline-run1/{build.log,test-fast.log,FastTests.trx}`. No stall or orphan. Root now authorizes the owning Luna to centralize regular/additional/analyzer-config text refresh and its known provider-provenance transitions, retaining atomic publication and linked-file behavior.
- Root working review also leaves three concrete candidate obligations open: remove the obsolete null-returning identity factory/parallel hash helpers while preserving independent test semantics; admit proven normal-loader providers while rejecting untracked custom binding state; avoid global strong provenance retention and preserve metadata documentation providers across immutable image rebinding. These are assigned to the existing separate production owners before full acceptance gates and independent audit.

### Final candidate source review — required follow-up, unverified

Root found that a successful structural `TryReloadAsync` already captures references and refreshes texts, yet the caller then captures again. The new R03 implementation must share one boundary attempt budget and distinct-image map, publish only a fully validated pair, and return the validated reload without a redundant capture. The workspace owner is assigned focused actual resident/reload capture-count and total-three-attempt tests, alongside physical multi-module binding/lifetime and documentation preservation.

The encoder's per-project context must preserve reference-to-owner association in a transitive graph. The controlled encoder fixture tests two same-path/option child contexts with different metadata images, assigned to distinct left/right root aliases: swapping image bindings must change the root owner-context fingerprint, while an enumeration permutation with unchanged binding must not. This is separate from normal-loader provenance tests and does not claim MSBuild loads multiple contexts naturally. The identity owner and separate test owner coordinate this fixture without weakening ambiguity rules.

The service tests must exercise the completed memo hit after concurrent calls, transient computation cancellation followed by retry, caller-only wait cancellation, runtime-owned shutdown and weak-key collection. Current-state docs and provider provenance remain pending reconciliation with the final code. All candidate acceptance, measurements, independent audit and completion boxes remain open; only the failing baselines and buildability recorded above are verified.

Root reconciled provider implementation choices without changing specification 04: a creating-owner-proven normal DesktopStrongNameProvider with empty CryptoKeyFile/CryptoKeyContainer has no external signing-file/container binding inputs. Its public signing scalars and actual source assembly identity remain encoded. Do not invent a constructor-state digest or serialize private/scheduler-only temp paths. Non-empty signing file/container state still needs complete real captured inputs or WORKSPACE_DIAGNOSTIC; unproven opaque provider swaps remain unsupported. Controlled low-level option encoding tests are separate from actual loader/provenance acceptance.

Actually active external #load/XML-include binding inputs without a complete already materialized/captured owner contract return a concrete WORKSPACE_DIAGNOSTIC. Shared structured syntax validation must distinguish inactive directives and quoted/comment text; this does not authorize a new general external-source materializer. Metadata documentation uses existing public XmlDocumentationProvider APIs and captured XML bytes/hash; arbitrary opaque documentation providers need a real byte-backed creator contract before reusable identity.

### Final candidate compilation rounds — acceptance still pending

- `pwsh -File ./scripts/build.ps1`, final candidate round 1: FAIL, exit 1, 3 errors / zero warnings, 5.41 seconds. StructureTools request-owned identity plumbing had two missing identifiers and a derived nullable error. Luna corrected the exact-snapshot SourceIdentityRequest wiring; no test acceptance ran. Artifact: `temp/roadmap-evidence/R03/candidate-final-build-run1/build.log`.
- Same command, final candidate round 2: FAIL, exit 1, 11 errors / zero warnings, 3.89 seconds. Core, Host, TestKit and Integration projects compile; new Fast fixtures have nullability/public API/async disposal and transferred resource ownership errors. Three separate Luna owners corrected only their assigned files. Narrow CA2000 suppressions describe real exception cleanup and returned fixture ownership; no global analyzer weakening. Artifact: `temp/roadmap-evidence/R03/candidate-final-build-run2/build.log`.
- Same command, final candidate round 3: FAIL, exit 1, 2 errors / zero warnings, 1.05 seconds. The focused encoding fixture still needs the public DesktopStrongNameProvider immutable-array argument and incremental-generator adapter. Luna is assigned these API-only corrections. Artifact: `temp/roadmap-evidence/R03/candidate-final-build-run3/build.log`. Candidate gates, measurements and independent audit remain open; no stall occurred.

### Final candidate focused acceptance round 1 — confirmed failures

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, 1.18 seconds reported time.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~AnalysisSymbolIdentityServiceTests|FullyQualifiedName~SourceAnalysisIdentityEncodingTests|FullyQualifiedName~ResidentSolutionMetadataFreshnessTests|FullyQualifiedName~AnalysisSymbolIdentityTests|FullyQualifiedName~FindSymbolScannerTests|FullyQualifiedName~GetSymbolBodyTests' --logger 'console;verbosity=normal'`: FAIL, exit 1, 73 completed tests, 49 passed / 24 failed / zero skipped, 6.2494 seconds. No stall.
- Root confirmed a candidate production mismatch: metadata capture produces canonical `/` image keys, but the encoder validates them against the existing stable-target Windows separator representation. All analysis fingerprint paths must use specification 04's shared canonical `/` semantics; the already verified public stable-target token domain must be preserved.
- Other failures reach missing exact creator provenance or incomplete explicit controlled-encoder fixture inputs before their intended assertions. Existing scanner preservation tests need an actual matching request-owned identity instead of a raw caller identity. Workspace transaction selection incorrectly includes all BCL references, the capture error wording assertion differs from the concrete message, and the weak-provenance GC test requires retention/JIT diagnosis. Each owner is assigned its actual failures with full acceptance retained; no passing result or final cause is inferred for those pending diagnoses.
- Artifacts: `temp/roadmap-evidence/R03/final-candidate-gates-run1/{build.log,test-fast.log,FastTests.trx}`. Broader gates, handler contracts, measurements, independent audit and all R03 completion boxes remain pending.

### Focused failure correction ownership — frozen product candidate

Root confirmed creator-provenance transfer was applied too late: the capture owner rewrites metadata references into a new immutable Solution before ResidentSolution attempts to carry the original proof. The capture owner now carries proof only over its own PE-only rewrite, returns it with the captured pair, and the resident then carries it over its own tracked text updates. Shared `AnalysisPathIdentity` covers all analysis coordinates; existing stable public target tokens remain unchanged. These corrections are frozen but await actual passing gates.

The pinned public Roslyn API has no getter for an arbitrary physical PE reference's documentation provider. Normal MSBuild creator provenance and byte-backed reference/XML creator inputs are distinct from the TestKit boundary. `ProjectSpec.AdditionalReferences` now explicitly declares raw physical references as conventional adjacent XML sidecar documentation; custom documentation requires `CapturedMetadataReference` and immutable XML bytes. An opaque-provider negative regression retains its output but rejects reusable identity. No general detection of arbitrary foreign physical providers is claimed. The independent audit must assess this actual boundary and docs.

- `pwsh -File ./scripts/build.ps1`, focused-failure fix build round 1: FAIL, exit 1, 3 errors / zero warnings, 7.47 seconds. The independent encoding fixture's new file-backed generator setup uses an unavailable default loader and an ambiguous language-version enum. Luna is assigned public-API-only compile fixes and a canonical expected analyzer path; no test run occurred. Artifact: `temp/roadmap-evidence/R03/candidate-fix-build-run1/build.log`.

### Final candidate focused acceptance round 2

- `pwsh -File ./scripts/build.ps1`: PASS, exit 0, zero warnings/errors, 1.92 seconds reported time. Public loader fixture APIs were corrected without package/analyzer changes.
- Same six-class focused Fast command recorded in round 1: FAIL, exit 1, 74 completed tests, 68 passed / 6 failed / zero skipped, 5.6282 seconds. Canonical path, exact identity scanner, service concurrency/cancellation/retry/runtime/weak-lifetime and the original metadata/non-source text freshness regressions now pass. This partial result is not point completion.
- Five remaining encoding fixture failures: dynamic generator emission lacks System.Runtime facade metadata; a requested absent-options ProjectInfo is normalized by Roslyn to real defaults; original opaque-provider proof cannot claim a public parse mutation's newly created provider; Desktop negative fixture lacks a solution file path; pathless SourceCodeKind assertion uses an additional-document ID instead of a source document. Luna is assigned real public creator/API fixtures retaining all applicable independent acceptance. A second post-recovery transaction Assert.Single still selects all BCL evidence; workspace Luna corrected it to the actual PE owner/ordinal, retaining changed SHA and compilation assertions.
- Artifacts: `temp/roadmap-evidence/R03/final-candidate-gates-run2/{build.log,test-fast.log,FastTests.trx}`. No stall. Broader gates, measurements and independent audit remain pending.

### Final candidate focused acceptance rounds 3–4

- Round 3 build: `pwsh -File ./scripts/build.ps1` PASS, exit 0, zero warnings/errors, 1.18 seconds. Same six-class focused Fast command: FAIL, exit 1, 75 completed tests, 73 passed / 2 failed / zero skipped, 5.7312 seconds. A custom-resolver message expectation differs from the actual concrete diagnostic; more importantly, Roslyn's real materialized SourceGeneratedDocument has a relative virtual FilePath which the encoder incorrectly treats as physical.
- The product owner corrected only SourceGeneratedDocument's nonphysical relative coordinate to the specified Name/ordered Folders/SourceCodeKind encoding; ordinary nonempty relative document paths still fail validation, and absolute generated paths remain canonical. The file-backed generator fixture remains unchanged.
- Round 4 build: same command PASS, exit 0, zero warnings/errors, 1.88 seconds. Same focused Fast command: FAIL, exit 1, 75 completed tests, 74 passed / 1 failed / zero skipped, 5.5342 seconds. The actual materialized-generator/image regression now passes. The remaining custom SyntaxTreeOptionsProvider fixture is normalized by Roslyn's workspace option layer; its negative assertion must test the actually loaded provider lacking creator proof rather than claim a requested but absent provider is loaded.
- Public ProjectInfo defaults normalize absent C# options; no actual missing-options rejection is claimed tested by that impossible fixture. Defensive guards remain, actual default normalization is asserted, and supported option mutation coverage uses the exact final known creator solution. Generator materialization/image capture has a separate selectable Fact.
- Artifacts: `temp/roadmap-evidence/R03/final-candidate-gates-run3/` and `final-candidate-gates-run4/`, each with build/Fast logs and TRX. No stall. Full affected gates, measurements and audit remain open.

### R03-P03 analyzer dependency binding — tests-first correction

Read-only review confirms the candidate captures a rooted AnalyzerFileReference primary image and netmodules, but does not capture private dependent assemblies or rebind analyzer loading to those immutable bytes. A new MSBuildWorkspace alone does not prove same-path generator image consumption or indirect dependency reload. This is a concrete source gap, not yet an executed failing regression or an environmental blocker.

Workspace Luna/high owns a new focused Workspace fixture only: emit a real file generator plus separate private dependency, materialize actual output, establish its unchanged primary hash and otherwise valid creator context, then require WORKSPACE_DIAGNOSTIC for incomplete dependency inputs. Product correction is held until the official narrow regression reaches this actual invariant. Its follow-up must honor specification 04's complete captured creator inputs or explicit diagnostic boundary; no general analyzer framework or acceptance weakening is authorized. Root owns baseline execution, evidence, any correction release and subsequent gates. R03-P03 and all R03 detail boxes remain open.
