# Cluster 2 Implementation Review

## Point 2.1: Target Detection and Validation

- Implementation base: `f409ae15d4c999856b95ec554b3b6cbfad21ce39`; the working tree was clean before this slice.
- Implementation commit: `69851fa`.
- Reference checked read-only through AiNetLinter MCP: `AnalysisTarget`, `AnalysisTargetResolver`, and `AnalysisTargetResolverTests` in `C:\Daten\Entwicklung\Ralf\AiNetLinter`. The reference resolver requires an existing absolute file, rejects wildcard paths and directories, normalizes with `Path.GetFullPath`, selects source or assembly mode by extension, and hashes the target contents with SHA-256.
- Existing AiNetCodeNavigator implementation matches those resolver behaviors. Its structured errors use the local `AnalysisTargetError` contract and `$.targetPath`. No production behavior change was needed. There is no repository-wide allowed-root restriction in the product contract; the resolver validates a specific target file and its type.
- Added contract tests for null/blank target paths, a null request, case-insensitive supported extensions, and the SHA-256 content fingerprint changing when the target contents change. Existing tests already cover path normalization through `..`, supported and unsupported extensions, relative/missing/directory paths, wildcard rejection, optional targets, and required-source targets.
- Updated current-state documentation in [build-and-tests.md](../../../docs/development/build-and-tests.md) with the verified target resolution contract.

### Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 216/216 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 5/5 |
| `pwsh -File ./scripts/test.ps1` | Passed, 221/221 across both test projects |
| `git diff --check` | Passed |

### Independent audit

- Audit count: 1 of 3 for point 2.1; audited commit `9f42a57` independently. The point audit checkbox remains open because the finding below is unresolved.
- Finding **P2 — target fingerprinting can escape the resolver's error contract**: `src/AiNetCodeNavigator.Core/Workspace/AnalysisTargetResolver.cs:132-150` checks `File.Exists`, then opens and hashes the same path without catching `IOException` or `UnauthorizedAccessException`. A file locked with `FileShare.None`, denied for reading, or removed between those operations can make `Resolve` throw instead of returning `AnalysisTargetResolution.Error` with a recoverable status. `tests/AiNetCodeNavigator.FastTests/Workspace/AnalysisTargetResolverTests.cs:16-195` covers invalid paths and successful hashing but no file access failure. The read-only AiNetLinter reference has the same gap at `src/AiNetLinter/Mcp/AnalysisTargetResolver.cs:146-159`; parity alone does not meet the concept's safe failure requirement. **Acceptance:** convert expected open/read/hash failures into a structured target error without masking cancellation or unexpected defects, and add a deterministic locked-file or equivalent failure test proving that the resolver does not throw and identifies `$.targetPath`.
- Scope note: the resolver is not wired into a public MCP tool at this commit (`src/AiNetCodeNavigator/Mcp/McpServerHost.cs` and tool classes are placeholders). Real transport-level `targetPath` behavior and English product output belong to clusters 9–11 and 10, respectively; they are not claimed as passed by this point audit.
- Audit verification: source, tests, and reference inspected read-only; no build or tests were run in this audit. The implementation-slice gate results above are inherited from its earlier review record. Documentation-only changes were checked with `git diff --check` before committing.
- No work on points 2.2 or 2.3 is included in this audit.

### Audit 1 Finding Fix

- Fix base: `b904b63e91eea2ff7a3b554087c9a443e954b1df`.
- **P2 — target fingerprinting error contract — fixed.** `AnalysisTargetResolver` now catches expected filesystem access failures (`IOException`, `UnauthorizedAccessException`, and `SecurityException`) from opening or hashing the canonical target and returns `TARGET_UNREADABLE` with `fieldPath: $.targetPath`, target context, retry guidance, and the standard recoverable error status. Unexpected failures are not masked. The resolver no longer throws when a target already confirmed by `File.Exists` becomes inaccessible before or during hashing.
- Added a deterministic regression test that holds the existing target open with `FileShare.None` and confirms the resolver returns the structured error without throwing.
- Updated current-state target-resolution documentation and marked the access-failure contract implemented. Removed the resolved item from the open Findings register.

#### Fix Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 217/217 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 5/5 |
| `pwsh -File ./scripts/test.ps1` | Passed, 222/222 across both test projects |
| `git diff --check` | Passed |

- The point 2.1 audit checkbox remains open for independent follow-up; no work on 2.2 or 2.3 is included.

### Independent follow-up audit

- Audit count: 2 of 3 for point 2.1; audited fix commit `891a3b0` independently. The P2 finding from audit 1 meets its acceptance condition and is closed; the point 2.1 audit checkbox is checked.
- `src/AiNetCodeNavigator.Core/Workspace/AnalysisTargetResolver.cs:75-84` catches `IOException`, `UnauthorizedAccessException`, and `SecurityException` only around fingerprint creation and routes them to `TARGET_UNREADABLE` at `:174-182`. The error includes the canonical path as context, `$.targetPath`, retry guidance, and the existing error status. Successful hashing and prior path/type validation remain on their original paths.
- `tests/AiNetCodeNavigator.FastTests/Workspace/AnalysisTargetResolverTests.cs:134-151` holds an existing `.slnx` file with `FileShare.None` and asserts the structured error without an exception. This is a deterministic regression case on the Windows target platform. The test and fix cover the reported lock/open failure; a separately timed file-removal race is not needed for this acceptance condition.
- No additional point 2.1 finding emerged from the targeted review. Public MCP transport behavior and English product text remain assigned to clusters 9–11 and 10, respectively.
- Audit verification: code, test, and documentation inspected; no build or tests were run by this auditor. The fix gates above are the implementer's reported results, not this audit's results. The documentation-only diff was reviewed and `git diff --check` passed before commit.

## Point 2.2: Resident Solution Registry

- Implementation base: `6713fceb3618d16e8e4d40b6d643e1e7a8d3c93a`; the working tree was clean.
- Implementation commit: `fa488cf`.
- Reference checked read-only through AiNetLinter MCP: `ProjectRegistry`, `ProjectLease`, `ProjectDefinitionLoader`, `ProjectRegistryTests`, and `ProjectLeaseTests`. The reference exercises normalized cache keys, same-root concurrent creation, LRU eviction that skips busy entries, load retry after failure, background-load isolation, TTL behavior, and idempotent lease disposal. Its server exposes a loading state while a non-blocking factory performs its load; response formatting is outside the registry.
- AiNetCodeNavigator already had MSBuild Locator registration/design-time workspace tests, missing-solution retry, same-root creation deduplication, LRU/TTL behavior, and active leases. `MSBuildSolutionLoader.LoadSolutionAsync` has no production caller yet; host/tool composition belongs to later clusters, and real `.slnx` lifecycle/load-error integration remains in point 2.3.
- A regression test reproduced a stale existing document when its contents changed without changing its timestamp. `ResidentSolution.RefreshStalenessUnderLock` skipped hashing on equal timestamps. It now checks each existing document's content hash on refresh and applies changed text. Tests now cover unchanged-timestamp edits, loading-state visibility with a blocked background load, retry after a failed background load, and LRU eviction preserving entries with active leases.
- Verification of the reproduced defect: before the production fix, the focused same-timestamp test failed because it returned the old document text. After the fix, all 11 focused staleness and registry tests passed.
- Updated [build-and-tests.md](../../../docs/development/build-and-tests.md) to describe asynchronous load state, lease-aware eviction, and content-hash staleness checks.

### Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 221/221 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 5/5 |
| `pwsh -File ./scripts/test.ps1` | Passed, 226/226 across both test projects |
| `git diff --check` | Passed |

### Independent audit

- Audit count: 1 of 3 for point 2.2; independently audited commit `e5af676`. Two findings remain open, so the point 2.2 audit checkbox remains unchecked.
- **P1 — a linked source file refreshes only its first project document.** `src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs:175-209` iterates documents but stores one `fileStates` entry per file path. On a changed file, the first document is updated at `:207-209` and its path hash is replaced. The next document in another project with that same path takes the equal-hash branch at `:197-205`, retaining old text indefinitely. The three tests in `tests/AiNetCodeNavigator.FastTests/Workspace/ResidentSolutionStalenessTests.cs:17-111` use distinct paths in one project, so they do not cover this case. **Acceptance:** build a two-project solution whose documents share one physical `.cs` path, change the file (including unchanged-mtime case), and prove both project documents expose the new text on the next snapshot; refresh all documents for a changed path before advancing its cached state.
- **P2 — creation may publish after registry disposal.** `src/AiNetCodeNavigator.Core/Workspace/ProjectRegistry.cs:76-105` clears and disposes current entries but does not coordinate with `reservations`; `:166-187` can still be waiting in the instance factory, and `:234-251` publishes its result without checking `disposed`. If disposal completes while creation is paused before publish, the resumed call inserts a resident solution into an already disposed registry. No monitor or later disposal owns that entry. The existing concurrency tests in `tests/AiNetCodeNavigator.FastTests/Workspace/ProjectRegistryTests.cs:97-131,168-267` do not interleave disposal with creation. **Acceptance:** use a deterministic factory or publish barrier to dispose the registry while a lease creation is pending; prove the late solution is disposed exactly once, no entry remains, and the call fails clearly or is otherwise prevented from returning a usable orphan lease.
- AiNetLinter was inspected read-only: `src/AiNetLinter/Mcp/Projects/ProjectRegistry.cs:95-124,159-205,239-270` has the same disposal interleaving, and its publish-race test checks disposal of a losing creation, not disposal during creation. The shared-file staleness issue concerns this repository's `ResidentSolution` adaptation rather than AiNetLinter's registry contract. Reference parity does not close either finding.
- Scope: the async loading state, retry after an emitted load-failure response, lease-aware LRU behavior, and same-root creation deduplication have focused tests. Real `.slnx` loading, structural staleness, and MCP load-error reporting remain point 2.3 or later integration work; this audit does not claim those gates.
- Verification: code, tests, and reference inspected read-only. No build or tests were run by this auditor; the gate table above records the implementer's results. Documentation-only diff reviewed and `git diff --check` passed before commit.

### Audit 1 Finding Fix

- Fix base: `da16d00e1472c12d25e7952af94d7aad5007f8a3`.
- Implementation commit: `f5dfea718cae6a8227909729d3e172b71e99165c`.
- The two regression tests were run before the production changes and failed as expected: a shared-file test showed the second project's document still contained old text; the disposal interleaving test showed the registry could complete disposal before publishing and return a successful lease.
- **P1 — shared physical source refresh — fixed.** `ResidentSolution` now groups loaded documents by physical file path, reads and hashes each path once, applies the changed `SourceText` to every document in the group, and advances the path's cached state only after the group update. The regression uses the same physical `.cs` path in two projects and preserves its timestamp while changing its contents.
- **P2 — publication after disposal — fixed.** `ProjectRegistry.Lease` tracks active creation operations. `DisposeAsync` marks the registry closed and waits for those operations to finish before draining entries. Creation checks the disposed state while holding the registry lock; a late successful instance is removed from its reservation, disposed, and returned as `PROJECT_REGISTRY_DISPOSED` rather than published. New lease calls after closure throw `ObjectDisposedException`.
- The disposal regression pauses at the pre-publish hook, starts `DisposeAsync`, then releases creation. It asserts disposal waits, the lease fails with `PROJECT_REGISTRY_DISPOSED`, no snapshot remains, and the created resident is disposed exactly once.
- Updated [build-and-tests.md](../../../docs/development/build-and-tests.md) with shared-path refresh and registry disposal behavior; removed both resolved items from the open Findings register.

#### Fix Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 223/223 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 5/5 |
| `pwsh -File ./scripts/test.ps1` | Passed, 228/228 across both test projects |
| `git diff --check` | Passed |

- The point 2.2 audit checkbox remains open for independent follow-up. No 2.3 implementation was included.

### Independent follow-up audit for point 2.2

- Audit count: 2 of 3 for point 2.2; independently audited commit `038eadb5aca3b580a9133bd579e8ca92d470b796`, including its code fix `f5dfea7`. Both audit 1 findings meet their acceptance conditions, so the 2.2 checklist audit checkbox is checked.
- **P1 closed:** `src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs:176-216` groups documents by physical path, calculates one hash and reads one text per path, applies that text to every document in the group, and only then advances the cached state. `tests/AiNetCodeNavigator.FastTests/Workspace/ResidentSolutionStalenessTests.cs:82-115` places the same `.cs` file in two projects, changes its contents while restoring the original timestamp, and asserts both documents expose the new text. Existing distinct-path and deletion tests remain in place.
- **P2 closed:** `src/AiNetCodeNavigator.Core/Workspace/ProjectRegistry.cs:40-63,91-143,263-330` tracks active lease operations, marks closure under the registry lock, waits for those operations before draining entries, and rejects a late publish while retiring its created resident. `tests/AiNetCodeNavigator.FastTests/Workspace/ProjectRegistryTests.cs:269-324` pauses creation before publication and verifies that disposal waits, the lease fails with `PROJECT_REGISTRY_DISPOSED`, no entry remains, and the resident is disposed once. New calls after closure are rejected at lease entry.
- The targeted review found no further point 2.2 finding. Real `.slnx` loading and structural staleness remain point 2.3; public MCP response behavior and English product output remain later-cluster gates.
- Audit verification: source, regression tests, and updated documentation inspected; no build or tests were run by this auditor. The fix verification table above records the implementer's runs only. The documentation-only diff was reviewed and `git diff --check` passed before commit.

## Point 2.3 Implementation Status

- Implementation base: `d43e3b9531c8f19c7d04fa484731b4e02a8226c3`; the working tree was clean before this slice.
- Implementation commit: `7a3c2205b0c2ff3b9700339bc532ec7ea4e111fc`.
- AiNetLinter was inspected read-only through its MCP server. Its project registry retries a failed resident load after a failed response is released; its solution reload retains the last good catalog and records the refresh error. Its MSBuild loader builds a design-time workspace and gathers workspace diagnostics. This implementation adapts those lifecycle contracts to Roslyn `Solution` snapshots without importing linting or diagnostic semantics.
- Added `ProjectRegistryOptions.ForMSBuild()` and `MSBuildSolutionLoader.CreateResidentSolution()` as the registry-to-real-solution path. The loader observes `WorkspaceFailed` failure diagnostics and preserves their cause in a structured `PROJECT_LOAD_FAILED` result.
- `ResidentSolution.GetCurrentSnapshotAsync()` now defines snapshot behavior: content-only changes update all documents for a shared source path; changes to the `.slnx`/`.sln`, project files, standard MSBuild property files, or the project's C# file inventory trigger a full MSBuildWorkspace reload. Added/removed projects and project references therefore appear in the next successful snapshot. A failed reload leaves the last good solution resident internally, returns an error result without exposing that stale snapshot as successful, and retries on each later snapshot request. Initial load failures expose the same error and can be retried through a new registry lease after the failed response is marked and released.
- Added real `.slnx` integration coverage with two SDK projects and a project reference. It verifies changed source text, file addition and deletion, project addition and removal, reference changes, MSBuild failure cause, registry retry after repairing a missing project, and retry after a failed reload even when the target is restored to the prior fingerprint. Before the fixes, the added source did not appear in the resident snapshot, and the restored-fingerprint reload test failed because the old error prevented a retry.
- Updated [build-and-tests.md](../../../docs/development/build-and-tests.md) and the 2.3 implementation checkboxes. The public MCP tool routing is not present at this cluster; the new Core snapshot result carries the error and retry fields for later tool composition. Custom MSBuild imports with resolved paths outside the project files and tracked standard property files are not independently fingerprinted.

### Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 223/223 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 7/7 including real `.slnx` lifecycle and retry cases |
| `pwsh -File ./scripts/test.ps1` | Passed, 230/230 across both test projects |
| `git diff --check` | Passed |

- The point 2.3 audit checkbox remains open for independent follow-up. The untracked-import limitation is recorded in [Findings.md](../Findings.md).

### Independent audit for point 2.3

- Audit count: 1 of 3; independently audited commit `c4ad0588aa107ec0afec1f83359ff623ed2aa8b1`. The point 2.3 audit checkbox remains open for the two structural-staleness findings below.
- **P1 — new files from external `Compile` globs remain invisible.** `src/AiNetCodeNavigator.Core/Workspace/SolutionStructureFingerprint.cs:23-47` enumerates `*.cs` only beneath each project directory and includes outside paths only for documents already in the loaded solution. If a project has `<Compile Include="../Shared/*.cs" />` and a new file is added in the sibling `Shared` directory without editing the project file, neither its path nor any other tracked input changes. `ResidentSolution.GetCurrentSnapshotAsync` at `src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs:230-254` therefore skips MSBuild reload and continues returning the prior document set. The real-solution test adds a file only beneath `src/Library` (`tests/AiNetCodeNavigator.IntegrationTests/Workspace/WorkspaceLoadingIntegrationTests.cs:72-84`). **Acceptance:** a real `.slnx` integration test starts with an external wildcard include, adds a matching file outside the project directory, and finds that document or its symbol in the next snapshot; the fingerprint or another structure trigger must cover the effective compile inputs.
- **P2 — custom MSBuild import changes do not trigger reload.** `SolutionStructureFingerprint.cs:54-73` hashes the solution, project files, and a fixed list of standard property files. A custom imported `.props` or `.targets` file is absent from those inputs. If that import changes a `ProjectReference` or compile include while the project file and enumerated C# paths remain unchanged, the fingerprint is unchanged and `ResidentSolution.cs:232-238` skips reload, retaining the old project graph. The implementation review and current-state documentation disclose this boundary; it still falls within point 2.3's project/reference-structure criterion. **Acceptance:** a real `.slnx` test changes a custom import that controls a project reference, then verifies the next snapshot reflects the new referenced project ID without editing `.slnx` or `.csproj`; track effective imports or use another reliable invalidation path. If a deliberate product scope limit is chosen instead, record the decision in the specification and leave the narrower behavior explicit.
- Positive evidence: the real `.slnx` tests at `WorkspaceLoadingIntegrationTests.cs:53-145` cover source edits, in-project file addition/removal, solution and project edits, a missing-project load cause, and retry after repair; `ResidentSolution.cs:221-259,263-310` returns a structured `PROJECT_LOAD_FAILED` result and retries after failed reload even when the target fingerprint is restored. The Core result is not yet a public MCP tool response; that composition is a later gate.
- AiNetLinter was inspected read-only: `McpCodeGraphServerRefresh.cs:75-127,251-293` bounds its source sweep to known project directories, so its behavior is not proof that external include globs are handled; its registry and server refresh preserve retryable load behavior but do not resolve these new-snapshot cases. The documented custom-import boundary is therefore accepted as an implementation limitation, not as completion of the stated 2.3 criterion.
- Verification: code, tests, documentation, and reference inspected read-only. No build or tests were run by this auditor; the gate table above is the implementer's record. Documentation-only diff reviewed and `git diff --check` passed before commit.

### Audit 1 Finding Fix

- Fix base: `bc12305146204fbc551ddfa9ae1f31b19a4723b1`.
- The new real-`.slnx` regression cases were run before the production changes. The external-glob case failed because `Added.cs` was absent from the next snapshot. Both custom import cases failed because the App project still referenced `Library` after the import changed to `Extra` with the original timestamp and file length preserved.
- **P1 — new files matched by external `Compile` wildcards — fixed.** The resident loader evaluates each loaded project's effective MSBuild imports and `Compile` definitions. It records the directory roots before wildcard segments, including roots outside the project directory, and fingerprints the C# file membership under those roots. Addition or removal of a matched source file triggers a full solution reload. The regression uses `<Compile Include="../Shared/*.cs" />` and adds a matching source in the sibling directory.
- **P2 — custom import changes do not refresh project references — fixed.** The loaded structure inputs now include effective imported files from MSBuild evaluation. Their fingerprint combines modification time, length, and SHA-256, while checking metadata before and after the read and retrying transient IO failures. The `.props` and `.targets` regressions each change an imported `ProjectReference` while restoring both timestamp and file length, then assert the refreshed Roslyn project graph points to `Extra`.
- The reference check against AiNetLinter remained read-only. Its source refresh scans known project directories and does not establish external wildcard coverage; the implemented behavior is verified by this repository's real MSBuild integration test. MSBuild evaluation uses the SDK's matching 18.9.6 engine package for compile-time APIs, with runtime assets excluded so MSBuild Locator supplies the loaded SDK runtime.
- Updated current-state documentation and removed both resolved 2.3 items from the open Findings register. The 2.3 audit checkbox remains open for independent follow-up.

#### Fix Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 223/223 on retry; the first run had an unrelated concurrent handoff test fail 19/20 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 10/10 including external glob and `.props`/`.targets` reload cases |
| `pwsh -File ./scripts/test.ps1` | Passed, 233/233 across both test projects |
| `git diff --check` | Passed after the review update |

- Remaining boundary: the collector tracks effective imports and wildcard roots from evaluated project definitions. Conditional imports not active for the loaded project evaluation are not effective inputs of that loaded snapshot.

### Independent follow-up audit for point 2.3

- Audit count: 2 of 3; independently audited commit `0c3b94d3c8d4c564fc2a785ad444ff82c9246882`, including code fix `2352ed0`. Both audit 1 findings meet their stated acceptance tests, but one newly identified structure trigger remains open, so the 2.3 audit checkbox stays unchecked.
- **P1 closed — external `Compile` glob:** `src/AiNetCodeNavigator.Core/Workspace/MSBuildStructureInputCollector.cs:61-92` derives a root before the wildcard from evaluated `Compile` items. `SolutionStructureFingerprint.cs:49-60,109-124` fingerprints source membership beneath that root; `ResidentSolution.cs:208-246` compares it before reloading. `tests/AiNetCodeNavigator.IntegrationTests/Workspace/WorkspaceLoadingIntegrationTests.cs:147-166` uses a real `.slnx`, a sibling `Shared` folder, and a newly added file, then confirms the next snapshot contains that document.
- **P2 closed — changes to active custom imports:** `MSBuildStructureInputCollector.cs:34-58` records evaluated import paths, and `SolutionStructureFingerprint.cs:51-54,67-106` hashes their contents even when timestamp and length are preserved. `WorkspaceLoadingIntegrationTests.cs:168-205` exercises both `.props` and `.targets` and checks that the App project's reference changes from Library to Extra by resolved Roslyn project ID.
- **P2 open — creation of a conditional import is invisible.** The collector at `MSBuildStructureInputCollector.cs:36-45` records only `project.Imports`, the imports effective during the last load. For `<Import Project="../../build/Optional.props" Condition="Exists('../../build/Optional.props')" />` with the file initially absent, there is no imported file to hash. Creating `Optional.props` later with a `ProjectReference` changes neither the unchanged `.csproj` nor any recorded input in `SolutionStructureFingerprint.cs:18-64`. `ResidentSolution.cs:236-246` therefore skips re-evaluation and keeps the previous project graph. The current integration tests edit imports that already exist at initial load; they do not cover activation. **Acceptance:** in a real `.slnx` test, start with a missing conditional import, create the imported file with a project reference, and verify the next snapshot resolves the new reference; include absent conditional import paths or another reliable evaluation trigger in the structure fingerprint.
- The import-activation boundary is distinct from the now-covered edits to active imports and remains within point 2.3's changed project/reference-structure contract. Public MCP transport behavior remains a later integration gate. No build or tests were run by this auditor; the fix verification table above records the implementer's runs. Documentation-only diff reviewed and `git diff --check` passed before commit.

### Audit 2 Finding Fix

- Fix base: `a3c79c93f0149839b7a1baa6f4ac197c9328523f`.
- Implementation commit: `e3f236a5d719e71aa112868c307aa03b84a0144d`.
- The new real-`.slnx` regression test failed before the implementation: after creating `build/Optional.props`, the next snapshot still had no App project reference, and the assertion for the new reference threw because the sequence was empty.
- **P2 — creation of a missing conditional import — fixed.** The collector now inspects MSBuild `ProjectImportElement`s in the root and currently imported project files. It expands supported project properties using the evaluated project and each import's containing directory, then fingerprints the exact declared candidate path even when it is currently absent or its `Condition` evaluates false. A later file creation changes the missing marker to a file fingerprint and triggers a complete MSBuild reload. This uses explicit import declarations and their path expressions; it does not scan parent directories or infer possible filenames. Unresolved expressions and wildcard import paths are skipped rather than treated as broad filesystem roots.
- The regression uses `<Import Project="../../build/Optional.props" Condition="Exists('../../build/Optional.props')" />`, starts without that file, creates it with a `ProjectReference`, then verifies the next snapshot resolves the App reference to `Extra`.
- Updated current-state documentation with the candidate-path behavior and its unresolved-expression/wildcard boundary. The point 2.3 audit checkbox remains open for the final independent audit.

#### Fix Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 223/223 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 11/11 including the conditional import activation case |
| `pwsh -File ./scripts/test.ps1` | Passed, 234/234 across both test projects |
| `git diff --check` | Passed |

### Final independent point audit for 2.3

- Audit count: 3 of 3; independently audited commit `22700effa3465bb54b9613bae2ce45aff839b96d`, including code fix `e3f236a`. The direct conditional-import finding from audit 2 meets its specific acceptance case. The point audit checkbox is checked because the three-audit limit is exhausted; point 2.3 itself stays open for the residual finding below.
- **P2 closed for a direct import:** `src/AiNetCodeNavigator.Core/Workspace/MSBuildStructureInputCollector.cs:61-106` records an exact declared import path even when the import was not active at the last evaluation. `SolutionStructureFingerprint.cs:49-65` hashes a missing marker that changes when the file appears. `tests/AiNetCodeNavigator.IntegrationTests/Workspace/WorkspaceLoadingIntegrationTests.cs:207-237` creates `build/Optional.props` after the initial real `.slnx` snapshot and verifies that App then resolves a project reference to Extra. This proves the targeted initial-absence regression, with the implementer's pre-fix failure and post-fix gates recorded above.
- **P2 remaining — nested conditional imports are not collected.** `MSBuildStructureInputCollector.cs:66-69` uses `projectFile.Children.OfType<ProjectImportElement>()`, which visits only direct root children. An import under an MSBuild `<ImportGroup>` (or another supported nested container) is a descendant, so its absent path never enters `PotentialImportPaths`. Creating that file later leaves the fingerprint unchanged and the project graph stale. The new regression contains a direct `<Import>` at `WorkspaceLoadingIntegrationTests.cs:212`; it does not exercise an `<ImportGroup>`. **Acceptance:** start with an absent conditional import nested inside an `ImportGroup`, create it with a project reference, and verify the next snapshot reflects the reference; collect supported nested import declarations as well as direct children.
- The remaining item is recorded in the open Findings register as point 2.3 Tech Debt. No fourth point audit should be scheduled; implementation and later cluster integration may still address it. Public MCP behavior remains a later gate. This audit inspected the targeted code and regression only, ran no build or tests, and reviewed the documentation-only diff with `git diff --check` before commit.

## Cluster 2 Integration Review

### First integration review

- Reviewed commit: `561811b3fda1d10a89247c98235a1363c8e5ce85`. Reviewer: independent `gpt-6-sol` audit subagent, medium reasoning effort. Point audit counts remain 2/3 for 2.1, 2/3 for 2.2, and 3/3 for 2.3; this integration review does not repeat a point audit. Cluster fix rounds used: 0 of 3.
- Core handoff: `AnalysisTargetResolver.Resolve` validates and canonicalizes a source `.sln`/`.slnx` target before a caller can pass `AnalysisTarget.CanonicalPath` to `ProjectRegistry.Lease`. The registry independently canonicalizes and validates that exact solution path through `ProjectDefinitionLoader`, while `ProjectRegistryOptions.ForMSBuild()` creates a `ResidentSolution` backed by `MSBuildSolutionLoader`. A lease holds that resident across `GetCurrentSnapshotAsync()` and prevents LRU/TTL eviction during the call. The real `.slnx` integration tests exercise registry-to-snapshot success and initial-load retry, while separate tests cover reload and structural changes.
- Error transition: invalid target paths produce a structured resolver error; a valid path whose MSBuild load later fails yields a retryable `PROJECT_LOAD_FAILED` snapshot error with its cause. After a failed initial-load response is marked and the lease released, the next registry lease creates a fresh resident. A failed reload retains the prior good state internally but returns an error, and the next snapshot call retries. These transitions are supported by `AnalysisTargetResolver.cs:46-92`, `ProjectRegistry.cs:40-64,341-357`, `ResidentSolution.cs:190-266`, and `WorkspaceLoadingIntegrationTests.cs:99-145`.
- **Existing P2 retained for cluster fix round 1:** point 2.3's nested conditional `ImportGroup` path is not collected; see the [final point audit](#final-independent-point-audit-for-23) and [open Findings](../Findings.md). It can leave the project-reference graph stale after an initially absent nested import appears. The first cluster fix round should add a real `.slnx` regression and include supported nested import declarations. No separate cluster-wide finding was found in this review.
- Boundary: no production MCP tool currently composes TargetResolver, Registry, and snapshot result into a public call, so this review does not claim transport-level `targetPath`, `operation=retry`, or error-format acceptance. That remains the later host/tool and end-to-end gate, not an additional 2.3 point audit.
- Verification provenance: source, tests, and earlier read-only AiNetLinter comparisons reviewed; no build or tests were run by this reviewer. The latest point 2.3 implementation table above records the implementer's gates. This review changed documentation only; its diff was reviewed and `git diff --check` passed before commit.

### Cluster fix round 1

- Fix base: `7779f0c93e9303b04befa35a98e2a423690e1367`.
- Implementation commit: `1d8179709407aef27aca819faba13fb5f8200235`.
- This is a cluster integration-review fix, not another point audit. Point 2.3 remains at its final audit count of 3 of 3; no fourth point audit was performed or is planned.
- The real-`.slnx` regression failed before the fix: App had no project reference after `Optional.props` was created, because its import declaration was nested inside `<ImportGroup>` and did not enter the fingerprint.
- **P2 — nested conditional imports — implemented.** `MSBuildStructureInputCollector` now walks project element containers recursively and collects `ProjectImportElement`s below the project root, including `ImportGroup` children and nested containers. It fingerprints the declared exact path even while missing, so creating the file triggers the same full reload as a direct conditional import. No directory scanning or condition guessing was added.
- Added a real-`.slnx` test for an absent `Optional.props` referenced from a conditioned `ImportGroup`. Creating the file adds a `ProjectReference`; the next snapshot resolves App's reference to `Extra`. The matching Cluster-02 subtask is checked, and the resolved nested-import item was removed from the open Findings register.
- The point 2.3 parent checkbox remains open: current-state docs still state that wildcard or unresolved declared import expressions are not collected as exact candidate paths, and public MCP composition is a later cluster gate. This fix closes the named nested-container Tech Debt without claiming those remaining boundaries or a new independent point audit.

#### Fix Verification

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 223/223 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 including nested conditional ImportGroup activation |
| `pwsh -File ./scripts/test.ps1` | Passed, 235/235 across both test projects |
| `git diff --check` | Passed |
