# AiNetCodeNavigator

AiNetCodeNavigator is a .NET project for an MCP server that helps agents navigate C# source code and managed assemblies. Its scope is code navigation; linting, diagnostics, metrics, and automated refactoring are outside the project.

## Status

The MCP stdio host registers `get_server_health`, `reload_config`, and nineteen navigation tools. Current contract evidence exercises the original SDK tool definitions and transport-free handlers, including source and assembly handoffs, response budgets, paging, and runtime lifecycle. Retained stdio/client end-to-end cases are categorized separately from the eligible completion gates. See the [tool reference](docs/tools/README.md), [setup guide](docs/setup/README.md), [MCP host](docs/mcp-host.md), and [build and test guide](docs/development/build-and-tests.md) for current behavior and verification limits. The core library contains components for loading solutions, resolving symbols, examining code structure and relationships, and inspecting assemblies.

AiNetCodeNavigator is autonomous. Its binding product references are the [current-state documentation](docs/README.md) and local code and tests.

## Build and test

Requirements: .NET 10 SDK and PowerShell 7 or later. The SDK version is specified in [`global.json`](global.json).

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test.ps1
```

The scripts write logs to `temp/`. Routine test scripts exclude extended integration tests, and every official test script excludes `E2EIntegration`; `scripts/test.ps1 -IncludeExtended` includes eligible extended tests while keeping E2E excluded. See [Build and Tests](docs/development/build-and-tests.md) for the project layout, test selection, and individual test commands.

## Documentation

The [documentation index](docs/README.md) covers implemented behavior.

## License

[MIT](LICENSE).
