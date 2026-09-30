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
