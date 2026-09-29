# AiNetCodeNavigator

AiNetCodeNavigator is a .NET project for an MCP server that helps agents navigate C# source code and managed assemblies. Its scope is code navigation; linting, diagnostics, metrics, and automated refactoring are outside the project.

## Status

The project is under development. The core library contains components for loading solutions, resolving symbols, examining code structure and relationships, and inspecting assemblies. The MCP host and public tool registrations are still placeholders, so the repository does not yet provide a usable MCP server.

The [implementation roadmap](tasks/AiNetLinter-Uebernehmen/Konzept.md) tracks planned work. Check the code and tests for the current implementation state.

## Build and test

Requirements: .NET 10 SDK and PowerShell 7 or later. The SDK version is specified in [`global.json`](global.json).

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test.ps1
```

The scripts write logs to `temp/`. See [Build and Tests](docs/development/build-and-tests.md) for the project layout and individual test commands.

## Documentation

The [documentation index](docs/README.md) covers implemented behavior. Specifications and checklists are under [`tasks/`](tasks/AiNetLinter-Uebernehmen/Konzept.md).

## License

[MIT](LICENSE).
