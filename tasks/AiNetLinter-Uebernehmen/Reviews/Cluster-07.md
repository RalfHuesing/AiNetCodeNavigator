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

### Independent audit 1/3 of point 7.2

- Reviewed commit: `bfb5e1888331644f492b8a610dc88fd31b202136` (clean working tree before review). The five Core scanners, request/payload models, navigation tests, cache staging owner lock, and relevant AiNetLinter search/type-origin code were inspected read-only. No product gates were run by this auditor; the table above records the implementer's runs. Point 7.3 session follow-ups were excluded.
- **P1 — Type origin can report the wrong DLL and NuGet package for a resolved symbol.** `ResolveTypeOriginScanner.ResolveAssemblyPath` takes the first resolved reference with a case-insensitive simple-name match (`src/AiNetCodeNavigator.Core/Assemblies/ResolveTypeOriginScanner.cs:128-135`), although the reference graph distinguishes entries by name, version, culture, and resolved path (`AssemblyReferenceResolver.cs:399-404`). If the graph contains two versions of one simple name and Roslyn's type symbol belongs to the later entry, the payload can report the earlier DLL path and derive the wrong NuGet package from it. The current origin test has one external assembly only (`tests/AiNetCodeNavigator.FastTests/Assemblies/AssemblyNavigationScannerTests.cs:101-140`). **Acceptance:** Use two reference identities sharing a simple name but differing in version/path; resolve a type from the later identity and require its actual DLL and package path, or return a typed ambiguity when the mapping cannot be proven. Match at least the full assembly identity, including version and culture, rather than the simple name.
- **P2 — C# qualified nested type names are reported missing.** `ResolveTypeOriginScanner.AddMatches` passes the user name directly to `GetTypeByMetadataName` and returns when that fails for any name containing `.` or `+` (`src/AiNetCodeNavigator.Core/Assemblies/ResolveTypeOriginScanner.cs:88-99`). A normal C# name such as `Probe.Outer.Inner` uses `.` for the nested type, whereas the metadata lookup expects `Probe.Outer+Inner`; the fallback traversal is skipped. Existing tests cover top-level types only (`AssemblyNavigationScannerTests.cs:101-140`). **Acceptance:** Emit a public nested type and resolve it using its C# fully qualified name, including a generic nested-type case, while preserving metadata-name and simple-name lookup and ambiguity behavior.
- **P2 — Declaration-only search drops field and enum-member declarations.** The filter enumerates `MemberDeclarationSyntax` nodes, yet `GetDeclarationNameSpan` has no `FieldDeclarationSyntax`, `EventFieldDeclarationSyntax`, or `EnumMemberDeclarationSyntax` arm (`src/AiNetCodeNavigator.Core/Assemblies/AssemblySearchScanner.cs:124-173`). Therefore a query matching a declared field or enum member is removed when `DeclarationOnly=true`, despite being a declaration name. AiNetLinter's declaration filter explicitly handles those declaration kinds. The current test exercises only a method (`AssemblyNavigationScannerTests.cs:42-76`). **Acceptance:** Search decompiled field, event-field, and enum-member names with `DeclarationOnly=true`; return their declaration lines while continuing to exclude comments, strings, and method-body uses.
- **Verified boundaries:** The shared scope maps missing/native inputs to structured errors and disposes its session. Search uses bounded result counts and a regex timeout; context reports reference truncation. Reference traversal caps resolved paths at 128, so the extension scanner's matching `.Take(128)` does not introduce a separate omission. The staging owner lock is acquired before the staging directory is created, retained until discard, and checked before retention cleanup; existing concurrency coverage exercises that path. No target-binary write was found. The three findings keep the point 7.2 audit checkbox open for fixes and a follow-up audit.

### Independent audit 1/3 finding fixes for point 7.2

- Fix base: `b5efe89fab75ff7275526742863aca428045c5f8` (clean working tree). Three focused regression tests failed before the production fixes: a version-2 symbol was mapped to the first version-1 path, `Probe.Outer.Inner` returned `SYMBOL_NOT_FOUND`, and declaration-only field/event-field/enum-member searches returned no hit.
- **P1 — Type origin uses the compilation's full assembly identity mapping — fixed.** Resolved symbols are mapped through `Compilation.References` by Roslyn `AssemblyIdentity.Equals`, then the matching `PortableExecutableReference.FilePath` is returned only when exactly one path matches. If a reference's origin path cannot be proven uniquely, the payload is marked ambiguous rather than selecting a same-name reference. The regression fixture supplies two `SharedDependency` versions and requires the version-2 package path for its symbol.
- **P2 — C# dotted nested names resolve — fixed.** Lookup now tries dotted namespace/nested-type splits, normalizes generic arity per type segment, and supports closed generic nested names such as `Probe.GenericOuter<int>.GenericInner<string>`. The test keeps existing local, reference, simple-name ambiguity, and missing-name coverage.
- **P2 — Declaration-only search includes field-like declarations — fixed.** The filter now includes every declarator in field and event-field declarations plus enum-member identifiers. The regression covers multiple fields in one declaration and confirms a field name used in a method body, string, or comment still produces only the declaration hit.
- The point 7.2 audit checkbox remains open for independent follow-up. Point 7.3 remains excluded.

| Gate after 7.2 audit fixes | Result |
|---|---|
| Focused pre-fix regressions | 3 failed as expected |
| Focused post-fix assembly navigation tests | Passed, 11/11 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 439/439 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 451/451 across both test projects |
| `git diff --check` | Passed before commit; only expected LF-to-CRLF working-copy notices |

### Independent audit 2/3 of point 7.2

- Reviewed commit: `f2f31266e0019beb14840b9b46a34641a511187e` (clean working tree before review). Scope was audit 1's three findings, the added regression tests, and immediate search-result DTO behavior. Code and tests were inspected read-only; this auditor did not run product gates. The preceding table reports the implementer's runs.
- **P1 type-origin path mismatch resolved.** `ResolveAssemblyPaths` now inspects the compilation's `PortableExecutableReference` values and compares Roslyn's full `AssemblyIdentity` with the resolved symbol (`ResolveTypeOriginScanner.cs:178-190`). A path is returned only when exactly one distinct path matches; an unproven or multiple-path reference becomes an explicit ambiguous payload (`:59-88`). The regression builds version 1.0 and 2.0 assemblies with one simple name in different package paths and asserts the version-2 symbol maps to its version-2 file (`AssemblyNavigationScannerTests.cs:198-262`).
- **P2 nested-name and declaration coverage resolved.** Dotted nested names are retried as metadata nested names, and generic arity is normalized at each type segment (`ResolveTypeOriginScanner.cs:118-170`); tests cover plain and generic nested types (`AssemblyNavigationScannerTests.cs:175-196`). Declaration-only search now gathers all field and event-field declarators plus enum-member identifiers (`AssemblySearchScanner.cs:129-179`). The new test confirms those names are found and a repeated field name in a method body, string, and comment does not add false hits (`AssemblyNavigationScannerTests.cs:81-109`).
- **P2 — Multi-field search hit can identify the wrong symbol.** `SearchAsync` reduces matches to line numbers and calls `GetContainingSymbolName` with the whole line, not the matched span (`AssemblySearchScanner.cs:88-102`). For `int TargetField, NeighborField;`, that helper selects the first declaration-name span intersecting the line (`:204-219`). A `DeclarationOnly` query for `NeighborField` therefore returns the correct line but may label its `AssemblySearchHit.Symbol` as `TargetField`; the new test asserts only the hit text, not `Symbol` (`AssemblyNavigationScannerTests.cs:81-109`). **Acceptance:** Assert the matched field name in the hit's `Symbol` for the second declarator (and for an enum member on a shared line); derive the symbol from the actual match span or leave it unset when the line cannot identify one unambiguously. Preserve the absence of body/comment/string false positives.
- The three audit-1 findings are resolved, but the new DTO accuracy finding keeps the point 7.2 audit checkbox open for a focused fix and a final audit. Point 7.3 remains outside this audit.

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
