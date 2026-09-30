# Cluster 7 Implementation Review

## Point 7.1: Decompiler and Virtual Roslyn Workspace

- Implementation base: `358b2f21fd6cdf2045b8b8c67f7ac006219c811f`, initially clean working tree.
- Reference review: AiNetLinter's `AssemblyDecompilationAdapter`, `AssemblyDecompilationCache`, `AssemblyRoslynWorkspaceFactory`, `AssemblyAnalysisCacheTests`, and managed-binary tests were inspected read-only through AiNetLinter MCP (`find_symbol`, `get_symbol_body`). No reference files or tests were changed or run.
- The local tests previously covered a successful cache roundtrip and a basic virtual workspace, but did not exercise read-only input preservation, native/invalid adapter inputs, cache fingerprint rejection, concurrent publication, or workspace failure and target-reference boundaries. Added component tests for these contracts.
- Regression tests failed before the production fixes:
  - **P1 — Native PE escaped the decompilation error contract.** `PEFile` throws `MetadataFileNotSupportedException` for a native PE, which was not in the adapter's handled exception set. The adapter now returns an error diagnostic that identifies the requirement for managed .NET IL.
  - **P1 — Invalid input could crash while constructing a secondary diagnostic.** After an invalid-image failure, the empty staging directory had no project file. `ReadProjectOutput` requested `AssemblyDecompilationAdapter.ProjectFilePath` from the diagnostic-code map, but the entry was missing, producing `KeyNotFoundException`. Added the code mapping so this path returns an incomplete result with diagnostics.
- Regression and boundary coverage now includes source DLL bytes/timestamp unchanged after decompilation; invalid and native PE diagnostics; caller cancellation; fingerprint mismatch rejection; concurrent identical cache publication; empty/canceled virtual workspace requests; and exclusion of the target DLL from the synthetic project's metadata references.
- Current-state documentation added at [assembly-decompilation.md](../../../docs/navigation/assembly-decompilation.md) and linked from the documentation index.
- Audit status: independent point 7.1 audit remains open in [Cluster-07](../Clusters/Cluster-07.md). This implementation review does not close the audit checkbox and does not include points 7.2 or 7.3.

### Verification

| Gate | Result |
|---|---|
| Focused pre-fix boundary tests | 6 passed, 2 failed with the defects above |
| Focused post-fix boundary tests | Passed, 8/8 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 425/425 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 437/437 across both test projects |
| `git diff --check` | Passed before commit |

## Point 7.2: Assembly Navigation Core Scanners

- Implementation base: `c891a13c52aabfaaa254ea84de05d880efe25bd8`, clean working tree.
- AiNetLinter reference code and test contracts were inspected read-only through AiNetLinter MCP. The implementation adapts the assembly-context summary, built-in data-access and external-call search patterns, declaration filtering, extension-method discovery, and referenced-assembly type lookup as Core navigation scanners. No AiNetLinter files or linter tools were changed.
- The pre-fix regression test was red at compile time because the four request/model/scanner APIs did not exist (`CS0103`/`CS0246`). After implementation, the focused assembly-navigation tests passed.
- Implemented shared read-only assembly-session validation and failure mapping, bounded assembly context and reference summaries, literal/regex search with timeout and result bounds, `external_calls`/`data_access` built-in patterns, extension-method lookup, and local/reference/framework type-origin resolution with ambiguity and NuGet path metadata.
- Tests cover context and search bounds, built-in search categories, declaration-only results, extension receiver filtering, local/external/framework origins, ambiguous and missing names, missing/native binaries, and invalid regexes.
- Parallel use exposed a cache-retention race: `RetainGenerations` deleted every `*.staging` folder, including one still used by a concurrent scanner. A focused pre-fix cache test failed, and four concurrent scanner calls returned `WORKSPACE_DIAGNOSTIC`. Staging creation now holds a cross-process owner lock; retention deletes only directories whose lock it can acquire. The regression test and concurrent scanner roundtrip pass.
- Current-state documentation: [assembly-navigation.md](../../../docs/navigation/assembly-navigation.md). Point 7.2 review/audit remains open for independent audit; no point 7.3 follow-up/session resolver was implemented.

### Verification

| Gate | Result |
|---|---|
| Pre-fix requested API repro | Failed as expected: requested scanner/request types were absent |
| Pre-fix staging-retention repro | Failed as expected: active staging directory was deleted |
| Focused post-fix assembly navigation and cache regressions | Passed, 9/9 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 436/436 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 448/448 across both test projects |
| `git diff --check` | Passed; only expected LF-to-CRLF working-copy notices |

### Independent audit 1/3 of point 7.1

- Reviewed commit: `9430da952afef7abc902494b198f50d2e4531169` (clean working tree before review). The adapter, cache, workspace factory, component tests, and AiNetLinter's corresponding classes and cache concurrency tests were inspected read-only. No product build or tests were run in this audit; the verification table above belongs to the implementation review.
- **P2 — Modified cached source can be accepted as the DLL's decompilation.** `AssemblyCacheGenerationStorage.ReadGeneration` validates the manifest input fingerprint, then `ReadDocuments` reads each listed `.cs` file without comparing its contents to a persisted digest (`src/AiNetCodeNavigator.Core/Assemblies/Coordinators/AssemblyCacheGenerationStorage.cs:20-32,131-152`). The manifest records generated paths but no source digests (`src/AiNetCodeNavigator.Core/Assemblies/AssemblyDecompilationCache.cs:323-374`). A same-length, syntactically valid edit to a cached source file therefore passes `TryRead` while its target DLL fingerprint is unchanged; the subsequent Roslyn snapshot can describe code that is absent from the binary. AiNetLinter has the same cache shape, so this is a reference limitation rather than a deliberate product adaptation. **Acceptance:** Publish a generation, alter one cached `.cs` file without changing the DLL or manifest, and require `TryRead` to reject that generation with a diagnostic. Bind every generated document to the published manifest and retain normal cache hits and concurrent publication behavior.
- **P2 — Timeout recovery can exceed the configured deadline while reading partial output.** After `WholeProjectDecompiler` throws for the deadline, `DecompileAsync` calls `ReadProjectOutput` with `CancellationToken.None` (`src/AiNetCodeNavigator.Core/Assemblies/AssemblyDecompilationAdapter.cs:63-69`). That method enumerates and reads every generated `.cs` file and parses nonempty content (`:145-179`), so a large partially generated project is still processed without a deadline. Caller cancellation is propagated on the separate branch (`:59-62`); the missing bound concerns timeout recovery. AiNetLinter uses the same fallback. **Acceptance:** With a short timeout and substantial staged partial output, require prompt return of an incomplete timeout diagnostic without scanning the entire tree; preserve caller-cancellation propagation and cleanup.
- **Verified boundary:** Normal decompilation writes to staging, cache publication writes within the cache entry, and the virtual Roslyn workspace is in memory. The existing native/invalid-image, caller-cancellation, target-byte/timestamp, fingerprint-mismatch, concurrent-identical-publish, empty-workspace, and target-reference tests cover their stated cases. They do not exercise either finding above. Point 7.1's audit checkbox remains open for fixes and a follow-up audit; points 7.2 and 7.3 were not audited here.

### Independent audit 1/3 finding fixes

- Base: `81de8832525b03e2fb3b1eabe4159b57f6c530c5`. Both new regression tests failed before the production changes: editing a same-length cached `.cs` file still returned a cache hit, and a simulated internal timeout escaped the adapter after staging a large partial document.
- **P2 — Cached decompiled source content is bound to its manifest — fixed.** The cache manifest now records a SHA-256 digest for each generated source document. Cache reads hash the exact decoded source content they return and reject a digest mismatch with the existing invalid-cache diagnostic path. The cache schema advanced to `assembly-cache-v3`, so generations from the prior manifest shape cannot be reused.
- **P2 — Timeout fallback no longer scans partial output — fixed.** The adapter now runs its override within the timeout/cancellation handling and passes it the linked deadline token. Internal timeout recovery returns an empty incomplete result with the timeout diagnostic and leaves partial files for normal staging cleanup; it does not enumerate or parse them. Caller cancellation remains propagated.
- Regression tests cover same-length source tampering after publication and an internal timeout with a 8 MiB staged C# document. The existing cache roundtrip, concurrent publication, source read-only, and caller-cancellation tests remain in the focused 7.1 suite.
- Audit status remains open for the independent follow-up. The reviewer may close the audit checkbox after verifying these fixes. No points 7.2 or 7.3 were included.

| Gate after audit fixes | Result |
|---|---|
| Focused audit regression tests before fixes | Both failed as expected |
| Focused 7.1 boundary tests after fixes | Passed, 10/10 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 427/427 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 439/439 across both test projects |
| `git diff --check` | Passed before commit |

### Independent audit 2/3 of point 7.1

- Reviewed commit: `80c56e5fa478f78b94c1950c42e18f7a568b11e7` (clean working tree before review). Scope was the two findings from audit 1, their regression tests, and immediate cache/adapter interactions. Code and tests were inspected read-only; this auditor did not run product gates. The preceding gate table records the implementer's runs.
- **P2 cached-source integrity resolved.** The v3 cache manifest records a SHA-256 digest per generated C# document (`AssemblyDecompilationCache.cs:355-363`); the converter requires the new field and rejects duplicate entries (`AssemblyDecompilationManifestJsonConverter.cs:24-29,220-243`). Cache reads validate the expected key set and digest format, then compare each decoded source with its published digest before constructing the returned document (`Coordinators/AssemblyCacheGenerationStorage.cs:119-126,154-166`). The new regression test changes a same-length source file after publication and requires a rejected cache read with a diagnostic (`AssemblyDecompilationBoundaryTests.cs:189-222`). The schema bump separates prior manifests. Normal cache hit and concurrent-identical-publish coverage remains in the focused suite.
- **P2 timeout recovery resolved.** The adapter now passes a linked deadline token to decompilation, including the test override, and returns empty incomplete output directly after its own timeout (`AssemblyDecompilationAdapter.cs:44-73`). It no longer calls `ReadProjectOutput` on that branch. The new test stages an 8 MiB partial C# file and confirms the result contains no documents and has a timeout diagnostic (`AssemblyDecompilationBoundaryTests.cs:122-155`). The existing caller-cancellation test still covers propagation. Ordinary non-timeout errors still use the prior partial-output diagnostic path.
- No point 7.1 finding remains open after this follow-up. The point 7.1 audit checkbox is closed. This audit did not review points 7.2 or 7.3.
