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
