# Cluster 3 Implementation Review

## Point 3.1: Symbol Identity

- Implementation base: `a5f2ae277561e79c754524de644ed8f51b133993`; the working tree was clean before this slice.
- Reference review: AiNetLinter's `AnalysisSymbolIdentity` was inspected read-only through its MCP server (`find_symbol`, `get_feature_context`, and `get_symbol_body`). The adapted implementation matched its identity behavior. The reference relies on DocumentationCommentIds for symbol identities, generates project markers from project paths when available, falls back to ephemeral Roslyn `ProjectId` values for pathless projects, and treats an absent canonical target path as a wildcard in `Matches`. The candidate test context also pointed to the handoff-token tests; the relevant envelope test was inspected read-only. No reference code or tests were changed or run.
- Findings and fixes:
  - **P2 — Symbols without a usable DocumentationCommentId had no canonical source key.** Roslyn can provide a DocumentationCommentId for local functions, but this handoff contract intentionally rejects local-function declarations. Added `CreateCanonicalSymbolIdentifier`: accepted declarations use their DocumentationCommentId; local functions and other symbols without one use an absolute, normalized source path and one-based line and column. Ambiguous or pathless source locations return `null`.
  - **P2 — Source identity could depend on transient or incomplete project/target identity.** Source handoffs now require a project marker from a project with an absolute project path in the captured solution. Public formatting helpers that could omit the project marker or use an arbitrary ProjectId are private. `Matches` requires both identities to have absolute target paths, normalizes equivalent paths, and uses the host platform's path case rules before comparing content hashes.
- FastTests cover same DocumentationCommentId in separate projects, foreign-solution symbols, pathless projects, overload DocumentationCommentIds, local-function file/line keys, and strict target-path matching. Existing source and assembly identity tests remain in place.
- Current-state documentation updated: [build-and-tests.md](../../../docs/development/build-and-tests.md).
- Scope boundary: this slice does not change the handoff wire format or connect producers to consumers. Handoff roundtrips, opaque `h:...` behavior, stale handles, and Assembly tool consumers remain point 3.3.

### Verification

All official gates passed after the final implementation:

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 229/229 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 241/241 across both test projects |
| `git diff --check` | Passed before commit |

An initial FastTests run failed in the new local-function identity test because the helper selected Roslyn's local-function DocumentationCommentId. The implementation was corrected to use the promised file/line fallback for local functions; all official gates above were rerun afterward.

A later FastTests rerun transiently failed in the unrelated `HandoffHandleRegistryTests.ConcurrentRequests_AreThreadSafeAndDeduplicated` test (`19` IDs observed, `20` expected), matching the race previously recorded in the Cluster 1 review. The immediate retry passed, as did the full suite.

- The point 3.1 audit checkbox remains open for independent follow-up. No 3.2 or 3.3 implementation work was included.

### Audit 2 Finding Fix

- Fix base: `a976b89b906130e0134766eb0b1a83863717c14a`.
- Regression tests were run before the production change. Two projects with identical project path/options and a shared declaration produced the same handoff when their metadata references differed; the same collision occurred when their project references differed. The Windows case-variant assertions now also prove each handoff is non-null, parses successfully, and has the requested source or assembly origin.
- **P1 — Reference-context project collisions — fixed.** Stable markers now include each project's stable metadata-reference descriptors (normalized absolute file path, module version IDs, metadata reference kind, interop setting, and aliases) and the transitive project-reference graph (target project path/context and edge aliases/interop setting). Traversal uses `ProjectId` only to detect revisits; it is never serialized. Missing project references, unstable project paths, or metadata references without a stable PE identity fail closed with no marker. If multiple projects still have the exact same marker, all are suppressed to avoid selecting one arbitrarily. Regression tests independently vary metadata and project references while preserving the root path, options, and declaration ID, verify distinct handoffs remain stable across equivalent solution reloads, and verify indistinguishable contexts produce no handoff.
- **P3 — Case-variant assertions — fixed.** Source and assembly tests require both generated handoffs to be non-null and parse to the expected origin before asserting equality.
- Current-state documentation updated: [build-and-tests.md](../../../docs/development/build-and-tests.md).

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 236/236 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 248/248 across both test projects |
| `git diff --check` | Passed before commit |

- The point 3.1 audit checkbox remains open for the final audit after this fix. No 3.2 or 3.3 implementation work was included.

### Independent audit 1/3 of point 3.1

- Reviewed commit: `3afd5c80e468510428c49c1f869d0d669917b9a3` (clean working tree before review).
- Reviewer: `gpt-6-sol`, medium reasoning effort. AiNetLinter's `AnalysisSymbolIdentity` was inspected read-only through its MCP `find_symbol` and `get_symbol_body` tools. No reference files were changed. This audit did not run a build or tests; the gate results above belong to the implementation slice, not this audit.
- **P1 — Equal project paths collapse distinct target-framework projects.** `AnalysisSymbolIdentity.GetStableProjectMarker` hashes only `Project.FilePath` (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:169-183`), then `FormatHandoff` appends that marker to the DocumentationCommentId (`:32-36`, `:61-64`). Two Roslyn projects loaded from the same multi-target `.csproj`, with different target frameworks and the same declaration ID, therefore produce identical internal source identities within the same solution snapshot. The existing different-project test uses separate project directories (`tests/AiNetCodeNavigator.FastTests/Symbols/AnalysisSymbolIdentityTests.cs:39-69`) and does not exercise this collision. AiNetLinter uses the same path-only marker; this is a reference limitation that conflicts with this product's requirement to avoid resolving an ID to the wrong project/target. **Reproduction/acceptance:** Build a solution containing two Roslyn projects with the same absolute `.csproj` path but distinct target frameworks and a shared declaration; demonstrate unequal canonical identities and successful resolution to the intended target after the fix. Include a stable marker across equivalent reloads and avoid ephemeral `ProjectId` values.
- **P2 — Equivalent Windows target paths produce different handoff target tokens.** `Matches` explicitly treats case variants of an absolute path as equal on Windows (`AnalysisSymbolIdentity.cs:142-150`), but `Format` passes the original `CanonicalPath` (`:20-29`) to `SymbolHandoffToken.TryCreateTarget`, which hashes `Path.GetFullPath` without normalizing Windows case (`src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffToken.cs:15-39`). For the same content hash and symbol, `C:\\Repo\\App.slnx` and `c:\\repo\\app.slnx` thus match as identities while yielding different `i:` target tokens. `AnalysisTargetResolver` also preserves caller path case (`src/AiNetCodeNavigator.Core/Workspace/AnalysisTargetResolver.cs:122-151`). **Reproduction/acceptance:** On Windows, format the same symbol with case-variant absolute target paths and assert identical target token or a demonstrably equivalent lookup; keep path comparison and token construction consistent. Cover source and assembly targets.
- **Boundary:** `CreateCanonicalSymbolIdentifier` creates an `L:` file/line/column key for local functions (`AnalysisSymbolIdentity.cs:97-140`), while `FormatHandoff` intentionally rejects them (`:88-91`). This helper has no production caller yet; connecting file/line identifiers to the public handoff contract is part of point 3.3. Its current isolated test is not evidence of a local-function roundtrip.
- Point 3.1 audit checkbox remains open pending fixes and a follow-up audit. Point 3.2 and 3.3 gates are outside this review.

### Audit 1 Finding Fix

- Fix base: `1dbd72df9b2ae8966a2861331e14a44de19730f2`.
- Regression tests were run before the production changes. The multi-target case returned equal handoff IDs for two Roslyn projects sharing one `.csproj` path and declaration ID; case-variant Windows paths generated different target tokens for both source and assembly handoffs.
- **P1 — Multi-target project identities — fixed.** Stable project markers now hash the normalized project path plus stable project context: project/assembly names, language, C# language version and preprocessor symbols, compilation output settings, and available target framework/platform/configuration properties from Roslyn's global analyzer options. No generated `ProjectId` is used. The test constructs two project configurations with the same absolute `.csproj` path, assembly name, and declaration but distinct `NET8_0`/`NET9_0` symbols; their handoffs differ and recreated solutions produce identical corresponding IDs.
- **P2 — Windows path case mismatch — fixed.** `SymbolHandoffToken.TryNormalizeTargetPath` now owns the shared path contract: absolute paths are normalized with `Path.GetFullPath`, trailing separators are removed, and Windows paths are uppercased invariantly. Handoff target tokens, `AnalysisSymbolIdentity.Matches`, project markers, and source snapshot hashes use this routine. Tests verify complete source and assembly handoff IDs and source snapshot hashes are identical for case variants.
- Current-state documentation updated: [build-and-tests.md](../../../docs/development/build-and-tests.md).
- Verification: each new regression test was observed failing against its corresponding pre-fix behavior, then all four passed after the fixes. The multi-target collision was reproduced before the project-context fix; source/assembly target tokens and the source snapshot hash were reproduced with the prior case-sensitive path hashing. No unrelated tests failed on the final official-gate runs.

| Gate | Result |
|---|---|
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 233/233 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 245/245 across both test projects |
| `git diff --check` | Passed before commit |

- The point 3.1 audit checkbox remains open for independent follow-up. No 3.2 or 3.3 implementation work was included.

### Independent audit 2/3 of point 3.1

- Reviewed commit: `a08d980b08f38b907472a19479638149f23cdd3b` (clean working tree before review). This audit inspected code, regression tests, and the AiNetLinter reference already recorded above. It did not execute a build or test; the gate results in the preceding fix section are the implementer's reported runs.
- **P1 — Project/target collision remains possible with identical selected options.** `GetStableProjectContext` includes a selected set of names, parse/compilation fields, and global analyzer properties (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:217-259`), but not the project's metadata or project references. `ForSource` accepts both projects into its marker map without checking for duplicate markers (`:160-167`). Two projects with the same absolute `.csproj` path, name, assembly name, parse/compilation options, and selected analyzer properties, but different references and therefore different target contexts, still receive the same marker and same handoff for a shared DocumentationCommentId. This can arise when target or build context is represented by references without the selected properties. The new test varies `NET8_0`/`NET9_0` preprocessor symbols (`tests/AiNetCodeNavigator.FastTests/Symbols/AnalysisSymbolIdentityTests.cs:273-335`), so it proves one distinct-option case only. **Reproduction/acceptance:** Construct two same-path projects with identical fields currently hashed but different references and a shared declaration; require distinct stable identities, or suppress ambiguous handoffs with a typed resolution path. Preserve identity across equivalent reloads. This is the unresolved portion of audit 1's P1.
- **P3 — Case-variant handoff tests can pass without a handoff.** The new source and assembly tests compare two nullable results with `Assert.Equal` but never assert either is non-null (`AnalysisSymbolIdentityTests.cs:104-137`). A regression that returns `null` for both variants would pass. **Acceptance:** Assert that both handoffs are non-null and parse as the expected origin before comparing equality. The shared normalization in `SymbolHandoffToken.TryNormalizeTargetPath` (`src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffToken.cs:15-49`) and `Matches` (`AnalysisSymbolIdentity.cs:143-151`) addresses audit 1's P2 code defect; this remaining item is test coverage.
- The File/Line primitive remains isolated from handoff creation as already assigned to point 3.3. Public producer/consumer roundtrips and stale-handle checks were not audited here. Point 3.1's audit checkbox remains open for the final permitted audit after fixes.

### Independent audit 3/3 of point 3.1

- Reviewed commit: `9fe9047c7a7558e6c448d218dd4bc5f500318191` (clean working tree before review). Scope was limited to audit 2's reference-marker and test-assertion findings. This audit inspected the code and tests; it did not run a build or tests. The gate table in the fix section records the implementer's runs, not independent audit gates.
- **P1 resolved within point 3.1.** `BuildSourceProjectMarkers` suppresses duplicate marker values (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:168-191`). Stable markers now incorporate metadata-reference paths, module version IDs, aliases, kind, interop settings, and the transitive project-reference graph (`:242-381`). The tests vary metadata references and project references independently for same-path projects with identical declarations, check stable distinct handoffs across recreated solutions, and check suppression when contexts cannot be distinguished (`tests/AiNetCodeNavigator.FastTests/Symbols/AnalysisSymbolIdentityTests.cs:96-130`, `:380-498`). This satisfies audit 2's distinguish-or-suppress condition. The original multi-target marker and Windows path-normalization fixes remain intact.
- **P3 resolved.** Source and assembly path-variant tests now require non-null, parseable handoffs of the expected origin before comparing equality (`AnalysisSymbolIdentityTests.cs:170-179`).
- No point 3.1 finding remains open after the third audit. The point 3.1 audit checkbox is closed. Public handoff producer/consumer roundtrips, typed lookup errors, and File/Line integration remain point 3.3 and were not audited here.

## Point 3.2: Handoff Tokensystem

- Implementation base: `ca61142e0e4891520666ef53a55f6eb634c2c4ca`; working tree was clean before this slice.
- Reference review: AiNetLinter's `HandoffCounterAlphabet`, `HandoffCounterStore`, `HandoffHandleRegistry`, `SymbolHandoffIdentifier`, and their FastTests were inspected read-only through AiNetLinter MCP (`find_symbol` and `get_symbol_body`). The counter sequence, lock-file reservation strategy, volatile bijection, and identifier wire format broadly match the reference. No AiNetLinter files or tests were changed or run.
- **P2 — Invalid values could be formatted as valid handles/identifiers.** `FormatHandle` prefixed any input, while `SymbolHandoffIdentifier.TryCreate` accepted unknown origin enum values and `Format` silently mapped every non-source value to the assembly code. Regression tests were run first and failed for each case. Formatting now rejects invalid counters and origins; `TryCreate` returns false for an unsupported origin.
- **P2 — Token validator threw for null input.** `SymbolHandoffToken.IsValid(null)` threw `NullReferenceException`. The regression test failed before the fix; the validator now returns false for null or empty input.
- **P2 — Counter-store failures escaped the Result contract and used the wrong code.** Creating a counter under a path whose parent was a file caused `Next` to throw `IOException` from lock-directory creation instead of returning a failure. It now catches directory-creation failures and returns `HANDOFF_COUNTER_UNAVAILABLE`; the counter persistence path uses the same error code. This distinguishes unavailable counter state from an unknown user handle.
- **P3 — Registry concurrency test was stochastic.** The existing test generated 100 random requests across 20 IDs, so some IDs were occasionally never requested. Repeated pre-fix runs failed 4/30 times. The test now schedules five concurrent requests per ID, checks every result, verifies per-key deduplication and handle uniqueness, and resolves each returned handle. Ten post-fix repeated runs passed.
- Added two-store parallel allocation coverage and corrupt-file preservation tests. Current-state documentation updated in [build-and-tests.md](../../../docs/development/build-and-tests.md).
- The point 3.2 audit checkbox remains open pending independent review. Producer/consumer composition, stale-snapshot checks, and public MCP roundtrips remain point 3.3 and were not changed.

### Verification

| Gate | Result |
|---|---|
| Focused handoff tests | Passed, 81/81 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 252/252 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 264/264 across both test projects |
| `git diff --check` | Passed before commit |

### Independent audit 1/3 of point 3.2

- Reviewed commit: `6e33ecf8107fd42b45ee1453f6df399de37f39f4` (clean working tree before review). AiNetLinter's counter, registry, and identifier implementations were inspected read-only through its MCP `find_symbol` and `get_symbol_body` tools. This audit did not run tests or a build; the gate table above reports the implementer's runs.
- **P1 — A newly returned handle can briefly fail reverse lookup.** `HandoffHandleRegistry.GetOrCreateOpaqueHandleForOutput` reads `internalToExternal` without `syncLock` (`src/AiNetCodeNavigator.Core/Symbols/HandoffHandleRegistry.cs:50-53`), while a writer publishes that map before `externalToInternal` (`:68-70`). A second thread can observe the first entry, return the handle, and call `RestoreInternalHandoffForInput` before the reverse entry exists, receiving `HANDOFF_UNKNOWN` (`:113-121`). The concurrency test restores handles only after `Parallel.For` completes (`tests/AiNetCodeNavigator.FastTests/Symbols/HandoffHandleRegistryTests.cs:121-140`), so it misses the publication window. AiNetLinter has the same map ordering; this is a reference limitation. **Reproduction/acceptance:** Add a concurrent get-and-immediate-restore regression that exercises publication overlap, then make every successfully returned handle immediately resolvable without a transient unknown error. Preserve per-ID deduplication and bijection.
- **P2 — Public identifier formatting can emit an invalid internal ID.** `SymbolHandoffIdentifier` is a public record struct with a public constructor (`src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffIdentifier.cs:19-23`). `Format` now rejects unknown origins but does not validate `TargetToken`, `ContentToken`, or `DocumentationCommentId` (`:31-40`). For example, `new SymbolHandoffIdentifier(Source, "bad", "bad", "bad").Format()` returns an `i:0:` string that `TryParse` rejects (`:94-103`). The origin-only regression test does not cover these fields (`tests/AiNetCodeNavigator.FastTests/Symbols/SymbolHandoffIdentifierTests.cs:47-57`). AiNetLinter also formats unchecked fields, but this public API should maintain its own format/parse invariant. **Reproduction/acceptance:** Add malformed-token and malformed-DocID constructor cases; formatting must reject them or construction must prevent them. Every formatted identifier must parse back to the same value.
- Counter reservation, persisted high-water mark, corruption handling, case-sensitive handle validation, and the repaired parallel-allocation test had no additional finding in this audit. Producer/consumer wiring, snapshot staleness, and public MCP roundtrips remain point 3.3. The point 3.2 audit checkbox stays open pending fixes and a follow-up audit.

### Audit 1 Finding Fixes

- Fix base: `b860f684d412a1da6d6ed860b16d95875a17c2d2`.
- Regression tests were run before the production changes. A parallel test that resolves each returned handle immediately failed with `HANDOFF_UNKNOWN`; direct construction of invalid token or documentation-ID fields produced `i:` strings rejected by `TryParse`.
- **P1 — Atomic registry publication — fixed.** Registry maps now use ordinary dictionaries behind one synchronization gate. Creation checks mappings under the gate, obtains a counter without holding it, then double-checks and publishes both directions together. `RestoreInternalHandoffForInput` and `Count` read under that same gate. The deterministic parallel test gives each of 128 IDs 128 requests and resolves each returned handle immediately, checking deduplication and the complete registry count. The test passed in five repeated focused runs after the fix.
- **P2 — Identifier format/parse invariant — fixed.** `Format` now validates both opaque tokens, the canonical documentation comment ID, and the origin before emitting an internal ID. Invalid public constructor values throw `InvalidOperationException`; valid identifiers roundtrip by value through `TryParse`.
- Current-state documentation updated: [build-and-tests.md](../../../docs/development/build-and-tests.md).
- The point 3.2 audit checkbox remains open for the required follow-up audit. No 3.3 implementation work was included.

### Fix Verification

| Gate | Result |
|---|---|
| Focused registry and identifier tests | Passed, 25/25 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 256/256 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 268/268 across both test projects |
| `git diff --check` | Passed before commit |

### Independent audit 2/3 of point 3.2

- Reviewed commit: `4fb74a35d581e39f408f823293630bed100e5201` (clean working tree before review). Scope was limited to audit 1's registry-publication and identifier-format findings and their regression tests. This audit inspected code and tests; it did not run a build or tests. The preceding fix-verification table records the implementer's runs.
- **P1 resolved.** Both mapping dictionaries are now read and written under `syncLock` (`src/AiNetCodeNavigator.Core/Symbols/HandoffHandleRegistry.cs:19-21`, `:36-45`, `:59-102`, `:139-146`). A successful `GetOrCreateOpaqueHandleForOutput` cannot expose a forward mapping before the reverse mapping to another registry call. The test restores each returned handle during concurrent creation and checks the resolved internal ID and final count (`tests/AiNetCodeNavigator.FastTests/Symbols/HandoffHandleRegistryTests.cs:143-164`). Its scheduling is nondeterministic, but the shared-lock code establishes the publication guarantee.
- **P2 resolved.** `SymbolHandoffIdentifier.Format` now checks origin, both tokens, and DocumentationCommentId before formatting (`src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffIdentifier.cs:31-47`). New tests reject malformed direct-constructor values and check a valid format/parse value roundtrip (`tests/AiNetCodeNavigator.FastTests/Symbols/SymbolHandoffIdentifierTests.cs:12-34`, `:66-85`).
- No point 3.2 finding remains open. The audit checkbox is closed after two audits. Producer/consumer composition and stale-snapshot behavior remain point 3.3 and were not audited here.

## Point 3.3: Producer and Consumer Contract

- Implementation base: `fe317d75f1e3a610e9261616f88ed77771a44ed6`; working tree was clean before this slice.
- **P1 resolved for source navigation.** `find_symbol`, `get_file_skeleton`, feature context, class structure, references, implementations, impact, hierarchy, test recommendations, call trees, and source-body resolution no longer register a raw DocumentationCommentId as an opaque handle. Source producers format target-, solution-snapshot-, and stable-project-bound identifiers. `SourceHandoffResolver` checks the current target, content snapshot, project marker, and symbol before returning a source symbol. Unknown handles return `HANDOFF_UNKNOWN`; different targets return `TARGET_MISMATCH`; changed snapshots return `STALE_SNAPSHOT`. Feature-context and class-structure responses preserve these typed errors.
- **Source roundtrips verified.** FastTests follow `find_symbol` handles into feature context and class structure, and `get_file_skeleton` handles into feature context. They assert the source origin, project marker, target binding, symbol result, and typed unknown/foreign/stale failures. `inspect_assembly` tests verify that its visible `h:` and structured internal DTO ID map to the same Assembly-origin identifier bound to the inspected assembly path.
- **Assembly follow-up blocker remains.** Assembly IDs are already produced with target/content identity by `InspectAssemblyScanner`, but no assembly handoff resolver or stable resident assembly-session follow-up path exists in this slice; those APIs and the session lifecycle are assigned to Cluster 7. Therefore the `inspect_assembly` to follow-up-tool roundtrip remains open and is recorded in `Cluster-03.md`; no claim is made that assembly handles are consumable yet.
- The point 3.3 audit checkbox remains open for an independent audit. AiNetLinter source files were inspected read-only; no reference files or tests were changed.

### Verification

| Gate | Result |
|---|---|
| Focused source/assembly handoff tests | Passed, 31/31 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 258/258 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 270/270 across both test projects |
| `git diff --check` | Pending before commit |

### Audit 1 Finding Fixes

- Fix base: `63ad1e5604e8bc265de4437f0aea00e75a77d3b6` (clean working tree before implementation). Reproduction tests failed before the fixes below.
- **P2 — Solution path casing changed the source snapshot hash.** `ForSourceAsync` hashed `Solution.FilePath` in its original casing, so equivalent Windows solution paths produced different content tokens. The solution target is now normalized before it enters the snapshot hash. A test builds equivalent workspace snapshots using differently cased solution paths and requires identical hashes.
- **P2 — A public producer identity was trusted without validation.** `FindSymbolScanRequest.SourceIdentity` could name another target or stale snapshot, and `FindSymbolScanner` emitted handoffs using it. The scanner now compares supplied identities to the current normalized solution target and snapshot and returns `TARGET_MISMATCH` or `STALE_SNAPSHOT` without entries on disagreement. Tests cover both cases.
- **P1 — Assembly fallback IDs were labeled as handoffs.** `InspectAssemblyScanner.StableId` fell back to a raw DocumentationCommentId or display string while DTOs unconditionally set `Handoff=true` and exposed follow-up tools. It now returns only a canonical identity; DTO handoff flags and follow-up lists depend on a non-null ID. A malformed identity regression verifies no raw ID, false handoff flag, and no follow-up tools.
- Assembly follow-up resolution/session lifecycle remains the Cluster 7 blocker described above. The point 3.3 audit checkbox remains open for follow-up audit.

### Audit 1 Fix Verification

| Gate | Result |
|---|---|
| Focused audit regression tests | Passed, 3/3 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 261/261 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 273/273 across both test projects |
| `git diff --check` | Passed before commit |

### Independent audit 1/3 of point 3.3

- Reviewed commit: `caa849e24ebfe42ee0bb621f5945205881e52108` (clean working tree before review). AiNetLinter's symbol identifier resolver and assembly session/tool boundaries were inspected read-only through its MCP `find_symbol`/`get_symbol_body` tools. This audit did not run a build or tests; the verification table above records the implementation slice's reported runs, and its `git diff --check` row is not an independent gate.
- **P1 — Assembly handoffs are advertised before a consumer/session exists (local blocker).** `InspectAssemblyScanner` sets `Handoff: true` and follow-up tools for type/member DTOs (`src/AiNetCodeNavigator.Core/Assemblies/InspectAssemblyScanner.cs:222-258`), and the formatter registers visible `h:` handles (`src/AiNetCodeNavigator.Core/Assemblies/InspectAssemblyFormatter.cs:156-159`). There is no Assembly handoff resolver or resident session follow-up path in this slice; the test only restores the registry value and parses its Assembly origin (`tests/AiNetCodeNavigator.FastTests/Assemblies/InspectAssemblyScannerTests.cs:64-81`). The implementation review already identifies the Cluster 7 dependency. **Impact:** `inspect_assembly` cannot roundtrip to an allowed follow-up tool. **Acceptance/next step:** Build the Cluster 7 assembly session and target/content-aware resolver, then test `inspect_assembly` output through each advertised follow-up, including unknown, foreign, and stale handles. Keep point 3.3 open until this passes.
- **P1 — Assembly fallback values are still exposed as handoffs.** `StableId` falls back from `FormatHandoff` to a raw DocumentationCommentId or display string (`InspectAssemblyScanner.cs:261-264`), while type/member DTOs unconditionally set `Handoff: true` (`:232-258`) and `InspectAssemblyFormatter` wraps any nonempty ID in an opaque `h:` (`InspectAssemblyFormatter.cs:156-159`). Thus a symbol for which `FormatHandoff` returns null is advertised with an `h:` whose registry value is not a canonical `i:` identifier; any canonical consumer must reject it. **Reproduction/acceptance:** Exercise an assembly DTO symbol without a canonical declaration ID (or force that branch in a focused test); retain its display identity but omit `handoffId`, set `Handoff` false, and omit follow-up claims unless a canonical identifier exists. Assert every emitted `h:` restores to a parseable Assembly-origin identifier.
- **P2 — Source snapshots differ for equivalent Windows path case.** `AnalysisSymbolIdentity.ForSourceAsync` appends `solution.FilePath` verbatim into the snapshot hash (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:203-211`), although target tokens and identity matching normalize Windows case (`SymbolHandoffToken.cs:15-49`, `AnalysisSymbolIdentity.cs:145-153`). Equivalent solution snapshots opened as `C:\\Repo\\App.slnx` and `c:\\repo\\app.slnx` therefore produce different content tokens; a valid handoff is reported `STALE_SNAPSHOT` on the other spelling (`SourceHandoffResolver.cs:67-73`). The existing case test covers the older `CreateSourceSnapshotHash` helper, not `ForSourceAsync`. **Acceptance:** Normalize the solution path in `ForSourceAsync`; test equal content tokens and a successful roundtrip across path-case variants with identical project/doc snapshots.
- **P2 — Injected source identity can label a current result with another target or snapshot.** `FindSymbolScanner` uses `request.SourceIdentity` without checking it against `ForSourceAsync(request.Solution)` (`src/AiNetCodeNavigator.Core/Symbols/FindSymbolScanner.cs:46-49`) and formats found symbols with it (`:111-116`). The public request permits an identity built with a foreign target or stale hash; its returned `h:` then fails its own current-solution follow-up as `TARGET_MISMATCH` or `STALE_SNAPSHOT`. Similar optional identities exist on feature/class-structure requests. **Reproduction/acceptance:** Pass a foreign or stale `SourceIdentity` with a current solution; producers must recompute or reject before emitting handoffs, and every emitted source handle must roundtrip against that same solution. Cover at least `find_symbol` and the optional identity paths that produce follow-up handles.
- The default `find_symbol` and `get_file_skeleton` source roundtrips to feature context/class structure, plus typed unknown/foreign/stale cases shown by the new FastTests, are supported by their Core code paths. The tests use a single project; add same-DocID, two-project producer-to-consumer coverage while closing the source findings. Public MCP transport coverage belongs to later clusters. The point 3.3 implementation and audit checkboxes remain open; this is audit 1 of at most 3.

### Independent audit 2/3 of point 3.3

- Reviewed commit: `6c138b245ababbd478b7949ccf2238f9c8b90aa9` (clean working tree before review). Scope was limited to audit 1's source-path/source-identity and Assembly-fallback findings. This audit inspected code and tests; it did not run a build or tests. The preceding fix-verification table records the implementer's runs.
- **Resolved — source path casing.** `ForSourceAsync` now uses `SymbolHandoffToken.TryNormalizeTargetPath` for the solution path before snapshot hashing and identity creation (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:170-211`). A regression constructs equivalent Windows-cased solutions and checks equal snapshot hashes (`tests/AiNetCodeNavigator.FastTests/Symbols/FindSymbolScannerTests.cs:121-136`). The test does not perform the previously requested cross-spelling handoff roundtrip; add that while completing source multi-project coverage.
- **Resolved — Assembly fallback handoff flags.** `InspectAssemblyScanner.StableId` now returns only `FormatHandoff`, and type/member DTOs set `Handoff` and allowed follow-ups only when that canonical ID exists (`src/AiNetCodeNavigator.Core/Assemblies/InspectAssemblyScanner.cs:222-264`). The regression forces a noncanonical identity and checks null ID, false flag, and empty follow-ups (`tests/AiNetCodeNavigator.FastTests/Assemblies/InspectAssemblyScannerTests.cs:93-125`). The normal `inspect_assembly` text/DTO mapping test remains, but an actual consumer is still absent.
- **P2 — Supplied source identity is checked only by `find_symbol`.** `FindSymbolScanner` now rejects foreign target and stale content identities (`src/AiNetCodeNavigator.Core/Symbols/FindSymbolScanner.cs:27-49`), with matching tests (`FindSymbolScannerTests.cs:94-119`). `FeatureContextScanner.ScanAsync` and `ClassStructureScanner.ScanAsync` still accept `request.HandoffIdentity` without comparing it to the current solution (`src/AiNetCodeNavigator.Core/Symbols/FeatureContextScanner.cs:25-44`; `ClassStructureScanner.cs:28-45`). For a semantic-name input, resolution bypasses the handoff validator, then declaration/member handoffs are formatted with the supplied foreign/stale identity. **Reproduction/acceptance:** Pass a semantic name with foreign and stale `HandoffIdentity` to both scanners; reject it with `TARGET_MISMATCH`/`STALE_SNAPSHOT`, or recompute the current identity before any handle is emitted. Also ensure any matching target/hash identity with a different project-marker map cannot emit a nonroundtripping handoff.
- **Local blocker unchanged:** Assembly follow-up resolution and session lifetime require the Cluster 7 work described in audit 1. Keep point 3.3 and its audit checkbox open. One audit remains under the three-audit limit; use it after the remaining source and Assembly acceptance work is ready.

### Audit 2 Finding Fix Verification

- Base: `9404371ca98c452485cb1344fd39e71695068a21` (clean working tree before this slice). The forged-marker repros failed before the fix: `find_symbol` emitted a handoff, and feature/class context accepted the supplied identity for semantic-name input.
- `AnalysisSymbolIdentity.Matches` now compares the stable source project-marker values, independent of workspace-local ProjectIds. `SourceHandoffResolver.ValidateIdentityAsync` returns typed target, snapshot, or project-context failures. `find_symbol`, feature context, and class structure reject mismatched identities before output and use the freshly computed current identity for emitted handles.
- Added regressions for forged markers across all three scanners, case-variant producer-to-consumer roundtrip, and two projects sharing the same DocCommentId resolving to the correct project-specific members.
- The Cluster 7 assembly resolver/session blocker remains unchanged; the 3.3 audit checkbox remains open for the final point audit.

| Gate | Result |
|---|---|
| Focused marker and project roundtrip tests | Passed, 4/4 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 265/265 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 277/277 across both test projects |
| `git diff --check` | Passed before commit |

### Independent audit 3/3 of point 3.3

- Reviewed commit: `77a1a5e4ded6d3636babc434757f949846bf09ef` (clean working tree before review). Scope was limited to audit 2's source project-marker and cross-case/two-project roundtrip findings. This audit inspected code and tests; it did not run a build or tests. The preceding gate table records the implementer's runs.
- **Source findings resolved.** `AnalysisSymbolIdentity.Matches` now compares stable project-marker multisets in addition to target and content (`src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs:145-166`). `SourceHandoffResolver.ValidateIdentityAsync` reports typed target, snapshot, or project-context mismatches and returns the current solution identity for valid requests (`src/AiNetCodeNavigator.Core/Symbols/SourceHandoffResolver.cs:16-61`). `find_symbol`, feature context, and class structure validate supplied identities before emitting handles and use the current identity (`FindSymbolScanner.cs:27-73`; `FeatureContextScanner.cs:25-47`; `ClassStructureScanner.cs:28-49`). A forged project-marker test covers all three producers; the new roundtrips cross Windows path-case variants and resolve equal DocumentationCommentIds to the intended project-specific members (`tests/AiNetCodeNavigator.FastTests/Symbols/FindSymbolScannerTests.cs:139-227`). No remaining source finding from audits 1–2 is open.
- **P1 local blocker remains — Assembly consumer/session.** `inspect_assembly` emits canonical Assembly-origin handoffs, but a persistent Assembly session and resolver for the advertised follow-up tools do not yet exist; the Cluster 7 dependency and acceptance conditions are recorded in audit 1. Impact: Assembly handoffs cannot complete a producer-to-consumer roundtrip. Next step: implement the Cluster 7 session/resolver and verify target/content binding, unknown/foreign/stale errors, and every advertised follow-up path. The point 3.3 implementation checkbox stays open.
- The third and final point audit is complete, so its audit checkbox is closed. The Assembly blocker remains in `Findings.md` as local technical debt. Any later Cluster 3 integration review must respect the three-audit limit and must not repeat a fourth point 3.3 audit.

## Cluster Integration Review 1

- Reviewed commit: `9a990b64bd4afb4ce94608415cf16b37cbc7da38` (clean working tree before review). Reviewer: independent audit agent, `gpt-6-sol` with medium reasoning. This was a cross-component review of symbol identity, opaque registry/token handling, source producer/consumer routing, and the Assembly boundary, not another point 3.3 audit. Code and tests were inspected read-only; no build or tests were run for this review. Earlier gate tables above describe implementer runs, not this review.
- **Integration path:** Source producers bind canonical `i:` values to target, snapshot, and project markers, then the shared registry exposes `h:` handles. `SourceHandoffResolver` restores the registry value and checks the current source identity before resolving a symbol. The existing source roundtrip tests cover the usual emitted handles and unknown/foreign/stale cases. The issue below lies at the two consumer dispatch boundaries before this resolver is called.
- **P2 — Handle-like input bypasses typed validation in two source consumers.** `HandoffHandleRegistry.RestoreInternalHandoffForInput` recognizes the `h:` prefix without regard to case and rejects an invalid uppercase `H:` handle with `INVALID_HANDOFF` (`src/AiNetCodeNavigator.Core/Symbols/HandoffHandleRegistry.cs:124-136`). `FeatureContextScanner.ResolveSymbolResultAsync` and `ClassStructureScanner.ResolveTypeSymbolResultAsync` only dispatch exact lowercase `h:` or `i:` prefixes to `SourceHandoffResolver` (`src/AiNetCodeNavigator.Core/Symbols/FeatureContextScanner.cs:96-105`; `src/AiNetCodeNavigator.Core/Symbols/ClassStructureScanner.cs:99-108`). An input such as `H:a` therefore enters semantic-name lookup and can return an empty result instead of the registry's typed error. **Reproduction/acceptance:** Pass `H:a` to both scanner consumers and assert `INVALID_HANDOFF`; align prefix routing with registry validation while preserving genuine Windows drive paths such as `H:\\repo\\file.cs`. Also verify normal `h:` source roundtrips still resolve.
- **P1 existing local blocker — Assembly consumer/session.** `inspect_assembly` advertises canonical Assembly handoffs but has no resident Assembly resolver and follow-up session yet. This remains the Cluster 7 dependency described in the point 3.3 audit, not a new point-audit finding. Keep the point 3.3 implementation checkbox open until the advertised follow-up roundtrips and unknown/foreign/stale errors pass through the Assembly consumer.
- **Review outcome:** One new P2 integration finding is open. The Assembly P1 remains open as local technical debt. Point-audit counts and closed audit checkboxes stay unchanged. The only gate for this documentation-only review is `git diff --check` before commit; no product gate is claimed.
