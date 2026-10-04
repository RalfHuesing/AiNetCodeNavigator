# Build and Tests

## Prerequisites

- .NET 10 SDK (pinned via `global.json`, version `10.0.203` or compatible feature release)
- PowerShell 7+ (`pwsh`)

## Solution Structure

The solution `AiNetCodeNavigator.slnx` contains six projects:

- `src/AiNetCodeNavigator.Core/`: Core library for Roslyn workspace resolution, AST exploration, symbol queries, call hierarchies, decompilation, and caching.
- `src/AiNetCodeNavigator/`: MCP server host executable and Serilog logging bootstrap.
- `tests/AiNetCodeNavigator.TestKit/`: Shared test support infrastructure, sample code fixtures, and workspace builders.
- `tests/AiNetCodeNavigator.FastTests/`: Fast unit and component test suite. It retains one E2E MCP SDK stream fixture, which performs a protocol handshake and remains excluded from routine gates.
- `tests/AiNetCodeNavigator.IntegrationTests/`: Workspace-loading and transport-free source/assembly handler contract tests, including budgets, recovery, domain paging, and owner references. Bounded traffic-capture component tests exercise the SDK host with in-memory streams and fixture tools. Retained complete stdio/client product flows are categorized `E2EIntegration` and excluded from current completion gates.

- `tools/AiNetCodeNavigator.Exploration/`: Standalone [manual MCP exploration](mcp-exploration.md) runner. The solution build covers it; test scripts and CI do not execute its scenarios.

Core, Host, and TestKit expose internal members to the test assemblies via `InternalsVisibleTo`. Host also exposes its internal runtime setup and argument validator to the exploration executable.

## Shared Test Support

`AiNetCodeNavigator.TestKit` provides disposable in-memory Roslyn solutions through `TestWorkspaceBuilder`. A `ProjectSpec` can set project references, metadata references, nullable options, preprocessor symbols, output kind, virtual file paths, and an assembly name independent of its project name. The returned `Solution` matches the owning workspace's `CurrentSolution`. The builder reuses its BCL metadata references and validates project and document inputs before creating the workspace. Virtual paths describe documents without creating files.

`TestWorkspaceBuilder.WithCapturedCoreReferences()` opts into cached immutable framework images and captured XML documentation sidecars. Handler contract fixtures use this mode when framework metadata should remain fixed, avoiding repeated framework-file reads during resident freshness checks. The default builder and explicitly supplied additional references retain their physical-reference behavior for metadata replacement tests.

`SampleCodeFixtures` contains compilable examples for callers, interfaces and implementations, inheritance, records, record structs, and extension methods. `NavigationAssertions` checks symbol names, canonical stable source/assembly references, line ranges, and result text patterns. Fast tests verify these helpers against real Roslyn syntax trees, compilations, and symbols.

`AssemblyTestHelper.EmitMetadataInterface` writes small managed assemblies with public interfaces and explicit interface references, without a framework reference closure. Assembly paging, reference-depth, and session-capacity tests use these fixtures to exercise the real scanners and session registry with only the dependency graph required by each test. Source and method-body tests continue to use compiled C# fixtures.

`IntegrationMcpAssertions` belongs to the IntegrationTests project. It extracts MCP text responses, removes continuation headers while reconstructing response bodies, reads continuation and budget fields, and checks UTF-8 and `cl100k_base` limits using SharpToken directly. These assertions measure returned text independently of the production formatter's token counter.

`InMemorySourceTestHost` owns explicitly constructed Roslyn projects, their workspace, and the real navigation runtime for source handler contracts. Fixtures retain physical source files when testing content refresh, cursor staleness, or linked-file ownership. MSBuild loading, project-option changes, generator identity, and loading-progress contracts continue to use real MSBuild workspaces.

MSBuild-based contract fixtures share a restore helper that disables persistent build servers, limits each restore process to two minutes, and bounds redirected output-stream completion to ten seconds. A timed-out restore process tree is terminated before the test reports failure.

## Analysis Target Resolution

`AnalysisTargetResolver` accepts one absolute path to an existing file. `.sln` and `.slnx` files select source mode; `.dll` and `.exe` files select assembly mode. The resolver normalizes the path with `Path.GetFullPath`, rejects directories, missing files, unsupported extensions, and wildcard paths, and reports invalid paths as `INVALID_ARGUMENT` on `$.targetPath`. The target fingerprint is the SHA-256 hash of the target file contents. If the target becomes unavailable or cannot be read while hashing, resolution returns `TARGET_UNREADABLE` on `$.targetPath` so the caller can fix access or retry.

## Stable Symbol References

The existing public handoff fields carry stable source and assembly declaration references. Their wire grammar, owner selection, input rules, resolution errors, and relationship to snapshot evidence are documented in [Shared Symbol Resolution](../navigation/symbol-resolution.md).

The eligible `SourceAnalyzerIdentityContractTests.MsBuildOptionsGeneratorSupportsRegularAndGeneratedHandlerNavigation` Integration test restores the ordinary `Microsoft.Extensions.Options` package, loads its generator through the standard MSBuild registry, and exercises `browse_target` with `view=scope`, `find_symbol`, and `get_symbol_body` against both regular and generated declarations. `MsBuildGeneratorImageReplacementRefreshesSnapshotAndGeneratedMethodBody` uses the same standard loader with two emitted generator images; replacing the image at the same path while preserving its size and timestamp must create a fresh snapshot while retaining the declaration reference and returning the replacement body. Run both with `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceAnalyzerIdentityContractTests'`.

## Resident Solutions

`ProjectRegistry` canonicalizes solution paths, deduplicates concurrent creation, and returns leases that keep in-flight solutions out of LRU and idle-TTL eviction. `ProjectRegistryOptions.ForMSBuild()` wires the registry to the real solution loader. `DisposeAsync` waits for active lease-creation operations; a solution created after disposal starts is disposed without being published, and the lease call returns `PROJECT_REGISTRY_DISPOSED`. `ResidentSolution` starts asynchronous loads in the background and exposes `ServerLoadState.Loading` until they finish. Callers use `GetCurrentSnapshotAsync()` to obtain a current navigation snapshot and inspect its structured load error. Content-only changes are applied to every Roslyn document sharing the physical source path, including edits that preserve the file timestamp. Each source refresh reads a file once and derives its SHA-256 and published Roslyn text from those same bytes. Initial resident snapshots use the same rule; a read failure returns retryable `PROJECT_LOAD_FAILED`, and a failed multi-file refresh publishes neither partially refreshed text nor file fingerprints. Metadata references are also captured from immutable PE/module image bytes. A changed image is captured from immutable bytes and rebound into fresh references and a new Solution/Compilation before the snapshot is returned, even when its path, size, timestamp, and MVID remain the same. Changes to the solution, project files, declared MSBuild import paths (including imports nested in `ImportGroup` containers), standard `Directory.Build.*`/`Directory.Packages.props`/`global.json` inputs, C# file membership matched by evaluated `Compile` wildcards, or the match set and contents of evaluated wildcard imports trigger a full MSBuild reload before a snapshot is returned. Wildcard matching uses MSBuild's expansion and glob semantics, including an empty match set, so adding, changing, or removing a matching import file is detected. These inputs include matching files outside a project directory, edits to active imports, and creation of a previously missing exact import path declared with a condition such as `Exists(...)`; such a path is recorded as missing until it appears. Import and source fingerprints detect content edits that preserve timestamps and lengths. Import hashes sample metadata before and after SHA-256 reads, retry transient IO failures, and report persistent instability as a load failure. Expressions that remain unresolved in a successfully loaded MSBuild evaluation are included in the structure fingerprint and force reevaluation before the next snapshot. An empty or undefined property that MSBuild expands as part of a valid relative path is still tracked using the expanded path. If a reload fails, the last good state stays resident internally, the snapshot result reports `PROJECT_LOAD_FAILED` with the target path and failure cause, and the next snapshot request retries. Initial load errors are exposed the same way; a registry caller can mark the failed response on its lease so the next lease creates a fresh resident. `MSBuildSolutionLoader` registers MSBuild Locator before creating its design-time workspace and converts MSBuild workspace failure diagnostics into load errors. The compile-time `Microsoft.Build` reference is pinned to the MSBuild version selected by the repository's SDK; its runtime assets are excluded because MSBuild Locator supplies the SDK runtime assemblies.

The `NavigatorHostRuntime` owns the bounded source dependency-graph cache. Source handlers validate snapshot identity before requesting collection facts; each cache key includes the canonical target, exact private snapshot ticket, scope, and generated-document option. Root and traversal settings reuse resident facts, while changed snapshots or collection filters form separate buckets. Successful per-document facts are retained without Roslyn objects, with four buckets, 32 MiB, and ten minutes of idle retention by default. `ProjectRegistry` reports the maximum validated ticket used by a retiring resident after removing it from the registry gate; runtime cleanup retires only that target's buckets and jobs through that ticket, then releases the resident. `ApplicationStopping` cancels active navigation and cache work. Runtime disposal drains navigation operations, awaits cache cleanup, and then disposes the project registry. `DependencyGraphOutgoingCollectorTests` covers exact cold declaration-document counts, partial/cross-project frontiers, terminal depth, scope/generated exclusions, file seeds, cycles, admission limits, linked owners, broad equivalence/fill, warm reuse and failed-need retry. Source outgoing requests use those frontiers unless full eligible facts are resident; incoming/both still fill full eligible coverage. `SourceDependencyGraphCacheContractTests.DependencyGraph_WarmProjectionsReuseFactsAndFreshSnapshotCollectsAgain` covers the host route, cold and warm semantic work counts, visible handoffs, collection-filter buckets, generated documents, and content changes that preserve the file timestamp. `ProjectRegistryTests.Lease_LruRetirementReportsOldSnapshotTicketWithoutBlockingFreshOwner` covers retirement cutoff capture while a newer lease is published.

The eligible `SourceSnapshotIdentityMeasurementTests.ReportsIdentityHashSeparatelyFromResidentDiskRefresh` Integration test performs an untimed MSBuild load and warmup, then writes five uncached fingerprint durations and five steady resident-refresh durations to the test output. It keeps identity encoding and disk-backed freshness checks as separate measurements and applies no timing threshold. Run it with `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~SourceSnapshotIdentityMeasurementTests'` when collecting measurements.


`LongRunningProgressContractTests` verifies independent response windows, exact running admission/recovery budgets, opaque token collisions, monotonic immutable progress and request-specific dependency coverage. `LongRunningNavigationProgressContractTests` exercises the actual source dependency handler and SDK schema without transport, including no-work budget rejection, one job across polls, measured partial-root coverage and omitted growing totals. `LongRunningTransportComponentTests` uses an in-process SDK host with written/read in-memory JSON-RPC frames and a fixture tool delegating to the real store. Its first-frame timeout is 20 seconds and poll-frame timeout is five seconds; it has no SDK client, child server or MSBuild workspace. Select these classes through the official scripts' `-Filter` option. This bounded eligible component is distinct from the excluded complete E2E product flow.

## Compilation Cache

`CompilationCacheManager` keeps syntax trees by file path and compilations by project path in thread-safe, case-insensitive in-memory dictionaries. Callers must pass UTC timestamps; entries are reusable only when their last-write timestamp and hashes match exactly. A tree stored with a content hash cannot be retrieved without the same hash. Compilation lookups require a `CompilationInputFingerprint` with non-empty hashes for source tree paths and contents, parse options, assembly identity and compilation options, project references, and metadata references. The caller must recompute the affected category hash whenever any input in that category changes; this catches edits to a source file even when another file still supplies the same maximum MTime. File and project invalidation remove entries from their respective caches, and `Clear` removes both caches and resets hit/miss counters. Cache reuse requires current content hashes and a fingerprint covering all compilation inputs.

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

## Deployment

Deploy the MCP server executable and dependencies to a testable output directory using the PowerShell deployment script:

```powershell
# Default: builds solution (Release), runs routine tests, and deploys to <RepoRoot>/deploy
pwsh -File ./scripts/deploy.ps1

# Rapid test deployment using only fast tests
pwsh -File ./scripts/deploy.ps1 -FastTestsOnly

# Custom output directory
pwsh -File ./scripts/deploy.ps1 -OutputDir C:\Tools\AiNetCodeNavigator
```

- Builds the solution (`AiNetCodeNavigator.slnx`), runs tests, and publishes `src/AiNetCodeNavigator/` via `dotnet publish`.
- The default destination directory `<RepoRoot>/deploy` is ignored in `.gitignore`.
- Copies the repository's complete default `hostsettings.json` if the destination has no settings file, preserves existing settings, and outputs ready-to-copy JSON configuration snippets for MCP clients (Cursor, Claude Desktop, Antigravity IDE).
- Console output is streamed directly to `temp/deploy.log`.

## Running Tests

Run the test suites using the dedicated test scripts:

```powershell
# FastTests (dumps full console log to temp/test-fast.log and TRX to TestResults/FastTests.trx)
pwsh -File ./scripts/test-fast.ps1

# Routine IntegrationTests (excludes ExtendedIntegration and E2EIntegration; logs and TRX as above)
pwsh -File ./scripts/test-integration.ps1

# Routine tests across the solution (excludes ExtendedIntegration and E2EIntegration; logs to temp/test.log)
pwsh -File ./scripts/test.ps1

# Solution suite including ExtendedIntegration (E2EIntegration remains excluded)
pwsh -File ./scripts/test.ps1 -IncludeExtended
```

All standard test-script invocations exclude `Category=E2EIntegration` and combine that exclusion with any supplied `-Filter`. This category marks complete product flows over the child stdio host and MCP client/server stream handshakes. Those cases remain in the test projects but are outside the MCP server acceptance gates.

Agents and automation tools should inspect the static log files under `temp/*.log` whenever diagnosing build or test outcomes.

The full solution script runs test projects sequentially (`-m:1`) so IntegrationTests workspace snapshot checks are isolated from FastTests cache and audit report generation. Parallelism within each test assembly follows its existing runner settings.

For agent handling of slow or stalled tests, follow the [verification rule](../../.agents/rules/04-verification.mdc#slow-or-stalled-tests). Its five-minute threshold triggers separate diagnosis by the agent; it is not an automatic timeout enforced by the test scripts.

### E2E and extended integration tests

`scripts/test-integration.ps1` and `scripts/test.ps1` exclude `Category=ExtendedIntegration` by default; `-IncludeExtended` removes only that exclusion. All ordinary script invocations exclude `Category=E2EIntegration`. `scripts/test-fast.ps1 -ReviewReportsOnly` remains available to select `AiNetCodeNavigator.FastTests.Reporting.RepositoryAuditReportTests.Review_PublishesRepositoryReportsWithoutBaseline` directly. Supplied filters are parenthesized and AND-combined with exclusions, so an OR filter cannot bypass them. `-ReviewReportsOnly` cannot be combined with `-Filter`. Use the scripts' `-Filter` parameter rather than passing `--filter` through additional arguments. Direct `dotnet test` calls do not apply the scripts' exclusions.

`McpServerIntegrationTests` and `McpArgumentValidationFilterTests` carry the E2E category because they run a child stdio host or a client/server stream handshake. `RepositoryAuditReportTests` runs unconditionally in the fast test suite without an E2E category trait. `McpInputSchemaTests` and the transport-free contract tests exercise original definitions, validators, and handlers without MCP transport and remain eligible. Select affected `ExtendedIntegration` tests for shared host, workspace, symbol-analysis, or response-processing changes only when they are not `E2EIntegration`. Include eligible extended tests only for release verification or an explicitly requested complete gate.

`TrafficCaptureTransportIntegrationTests` is an eligible bounded capture component integration: it uses an in-process SDK host, explicit in-memory streams, and fixture tools to check the capture boundary. It does not launch a client or child server process, load a workspace, or execute a complete navigation flow. See [MCP Traffic Capture](../mcp-traffic-capture.md) for the recorded artifacts and measurement scope.

### Automatic audit reports

`RepositoryAuditReportTests.Review_PublishesRepositoryReportsWithoutBaseline` runs unconditionally as part of routine FastTests. It launches `C:\Daten\Tools\AiNetReview-win-x64\AiNetReview.exe review <repository-root>` asynchronously in the background without a window, without waiting for completion, and without verifying generated output. The executable must exist at that path; the test fails only when the executable is missing.

The versioned [`ainetreview.json`](../../ainetreview.json) selects `AiNetCodeNavigator.slnx`, enables all eight current analyses with their defaults, and publishes to the Git-ignored `audit-reporting/` directory. Each review creates its own timestamped run directory with a root `index.md` and `production/`, `tests/`, and `mixed/` area directories. Each area publishes separate `changed-files/` and `all-findings/` indexes. No baseline is created or updated. Without a baseline, both views include all current findings in their applicable areas.

Published reports remain available across test runs and are deleted only manually. For a later agent review, select a run and explicitly request an audit/review using its `index.md`; report generation itself does not start an agent review or modify code. AiNetReview may include baseline instructions in its generated index, but this test only invokes `review`. AiNetReview's own executable logs are stored beside that external tool under `logs/`.

E2EIntegration cases remain excluded from routine gates, including when `-IncludeExtended` is supplied. MCP completion verification uses transport-free SDK definitions, validators, handlers, formatters, stores, runtimes, and bounded component integrations. See [MCP Argument Validation](../mcp-argument-validation.md), [MCP Host](../mcp-host.md), [MCP Tools](../tools/README.md), and [MCP navigation registration status](../navigation/mcp-registration-status.md).
