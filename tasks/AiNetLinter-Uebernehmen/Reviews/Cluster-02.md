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
