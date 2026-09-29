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
- `tests/AiNetCodeNavigator.IntegrationTests/`: End-to-end and MCP protocol integration test suite.

Core, Host, and TestKit expose internal members to the test assemblies via `InternalsVisibleTo`.

## Shared Test Support

`AiNetCodeNavigator.TestKit` provides disposable in-memory Roslyn solutions through `TestWorkspaceBuilder`. A `ProjectSpec` can set project references, metadata references, nullable options, preprocessor symbols, output kind, and virtual file paths. The returned `Solution` matches the owning workspace's `CurrentSolution`. The builder reuses its BCL metadata references and validates project and document inputs before creating the workspace. Virtual paths describe documents without creating files.

`SampleCodeFixtures` contains compilable examples for callers, interfaces and implementations, inheritance, records, record structs, and extension methods. `NavigationAssertions` checks symbol names, handoff identifiers using the core product alphabet, line ranges, and result text patterns. Fast tests verify these helpers against real Roslyn syntax trees, compilations, and symbols.

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
