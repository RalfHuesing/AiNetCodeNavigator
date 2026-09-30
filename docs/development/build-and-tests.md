# Build and Tests

## Prerequisites

- .NET 10 SDK (pinned via `global.json`, version `10.0.203` or compatible feature release)
- PowerShell 7+ (`pwsh`)

## Solution Structure

The solution `AiNetCodeNavigator.slnx` contains five projects:

- `src/AiNetCodeNavigator.Core/`: Core library for Roslyn workspace resolution, AST exploration, symbol queries, call hierarchies, decompilation, and caching.
- `src/AiNetCodeNavigator/`: MCP server host executable and Serilog logging bootstrap.
- `tests/AiNetCodeNavigator.TestKit/`: Shared test support infrastructure, sample code fixtures, and workspace builders.
- `tests/AiNetCodeNavigator.FastTests/`: Fast unit and component test suite.
- `tests/AiNetCodeNavigator.IntegrationTests/`: Workspace-loading and host-startup integration tests. MCP protocol handshakes and tool calls are not covered yet.

Core, Host, and TestKit expose internal members to the test assemblies via `InternalsVisibleTo`.

## Shared Test Support

`AiNetCodeNavigator.TestKit` provides disposable in-memory Roslyn solutions through `TestWorkspaceBuilder`. A `ProjectSpec` can set project references, metadata references, nullable options, preprocessor symbols, output kind, and virtual file paths. The returned `Solution` matches the owning workspace's `CurrentSolution`. The builder reuses its BCL metadata references and validates project and document inputs before creating the workspace. Virtual paths describe documents without creating files.

`SampleCodeFixtures` contains compilable examples for callers, interfaces and implementations, inheritance, records, record structs, and extension methods. `NavigationAssertions` checks symbol names, handoff identifiers using the core product alphabet, line ranges, and result text patterns. Fast tests verify these helpers against real Roslyn syntax trees, compilations, and symbols.

## Analysis Target Resolution

`AnalysisTargetResolver` accepts one absolute path to an existing file. `.sln` and `.slnx` files select source mode; `.dll` and `.exe` files select assembly mode. The resolver normalizes the path with `Path.GetFullPath`, rejects directories, missing files, unsupported extensions, and wildcard paths, and reports invalid paths as `INVALID_ARGUMENT` on `$.targetPath`. The target fingerprint is the SHA-256 hash of the target file contents. If the target becomes unavailable or cannot be read while hashing, resolution returns `TARGET_UNREADABLE` on `$.targetPath` so the caller can fix access or retry.

## Symbol Identity

`AnalysisSymbolIdentity` uses Roslyn documentation comment IDs for declarations that have them. Symbols without a documentation ID can still be identified by a unique source location, encoded as a canonical file path and one-based line and column. Source handoffs include a stable marker derived from the normalized project path, target-specific project options, metadata reference paths/properties/module IDs, and the transitive project-reference graph. Equivalent workspace reloads do not depend on Roslyn's generated ProjectIds. Projects without absolute, stable paths or metadata reference identities cannot produce a source marker; duplicate indistinguishable project markers are suppressed so a handoff cannot select an arbitrary project. Source snapshot hashes, identity comparisons, and handoff target tokens use `SymbolHandoffToken`'s shared absolute path normalization, including case normalization on Windows. The generation number is an in-memory lifecycle detail and does not change an identity for the same target content. The source-location key is an identity primitive; the current handoff wire format still accepts documentation comment IDs only.

## Resident Solutions

`ProjectRegistry` canonicalizes solution paths, deduplicates concurrent creation, and returns leases that keep in-flight solutions out of LRU and idle-TTL eviction. `ProjectRegistryOptions.ForMSBuild()` wires the registry to the real solution loader. `DisposeAsync` waits for active lease-creation operations; a solution created after disposal starts is disposed without being published, and the lease call returns `PROJECT_REGISTRY_DISPOSED`. `ResidentSolution` starts asynchronous loads in the background and exposes `ServerLoadState.Loading` until they finish. Callers use `GetCurrentSnapshotAsync()` to obtain a current navigation snapshot and inspect its structured load error. Content-only changes are applied to every Roslyn document sharing the physical source path, including edits that preserve the file timestamp. Changes to the solution, project files, declared MSBuild import paths (including imports nested in `ImportGroup` containers), standard `Directory.Build.*`/`Directory.Packages.props`/`global.json` inputs, or the C# file membership matched by evaluated `Compile` wildcards trigger a full MSBuild reload before a snapshot is returned. This includes matching files outside a project directory, edits to active imports, and creation of a previously missing exact import path declared with a condition such as `Exists(...)`; such a path is recorded as missing until it appears. Import and source fingerprints detect content edits that preserve timestamps and lengths. Import hashes sample metadata before and after SHA-256 reads, retry transient IO failures, and report persistent instability as a load failure. Declared import paths with unresolved property/item expressions or wildcard patterns are not tracked as exact candidates. If a reload fails, the last good state stays resident internally, the snapshot result reports `PROJECT_LOAD_FAILED` with the target path and failure cause, and the next snapshot request retries. Initial load errors are exposed the same way; a registry caller can mark the failed response on its lease so the next lease creates a fresh resident. `MSBuildSolutionLoader` registers MSBuild Locator before creating its design-time workspace and converts MSBuild workspace failure diagnostics into load errors. The compile-time `Microsoft.Build` reference is pinned to the MSBuild version selected by the repository's SDK; its runtime assets are excluded because MSBuild Locator supplies the SDK runtime assemblies.

## Compilation Cache

`CompilationCacheManager` keeps syntax trees by file path and compilations by project path in thread-safe, case-insensitive in-memory dictionaries. Callers must pass UTC timestamps; entries are reusable only when their last-write timestamp and hashes match exactly. A tree stored with a content hash cannot be retrieved without the same hash. Compilation lookups require a `CompilationInputFingerprint` with non-empty hashes for source tree paths and contents, parse options, assembly identity and compilation options, project references, and metadata references. The caller must recompute the affected category hash whenever any input in that category changes; this catches edits to a source file even when another file still supplies the same maximum MTime. File and project invalidation remove entries from their respective caches, and `Clear` removes both caches and resets hit/miss counters. The AiNetLinter reference cache validates analysis entries against their current content checksum; this cache applies the same rule and adds a fingerprint for all compilation inputs.

## Host Logging

The host entry point initializes Serilog and flushes it before exit. By default, logs are written beneath `AppContext.BaseDirectory/logs` in daily rolling files, with an additional 10 MiB size limit and 30 retained files. Error and fatal events are also written to `stderr`; the logger has no `stdout` sink so it cannot corrupt MCP protocol output. The host integration test launches the built executable and checks its streams and log location.

## Building

Build the solution using the official PowerShell build script:

```powershell
pwsh -File ./scripts/build.ps1
```

- Console output is streamed directly to `temp/build.log`.
- `TreatWarningsAsErrors` and `Nullable` reference types are enabled across all projects in `Directory.Build.props`.
- Roslyn analyzers (`Meziantou.Analyzer` and `Microsoft.CodeAnalysis.NetAnalyzers`) are enforced with `.editorconfig` severity mappings.

## Running Tests

Run the test suites using the dedicated test scripts:

```powershell
# FastTests (dumps full console log to temp/test-fast.log and TRX to TestResults/FastTests.trx)
pwsh -File ./scripts/test-fast.ps1

# IntegrationTests (dumps full console log to temp/test-integration.log and TRX to TestResults/IntegrationTests.trx)
pwsh -File ./scripts/test-integration.ps1

# All tests across the solution (dumps full console log to temp/test.log)
pwsh -File ./scripts/test.ps1
```

Agents and automation tools should inspect the static log files under `temp/*.log` whenever diagnosing build or test outcomes.
