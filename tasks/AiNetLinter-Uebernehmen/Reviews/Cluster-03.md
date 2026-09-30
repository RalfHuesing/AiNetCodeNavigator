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
