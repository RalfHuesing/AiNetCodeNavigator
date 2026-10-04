# AiNetCodeNavigator — MCP server for C# code navigation

AiNetCodeNavigator gives AI coding agents read-only navigation tools for **C# source code and compiled .NET assemblies** through the **Model Context Protocol (MCP)**. Find symbols, read method bodies, trace callers and references, inspect type hierarchies, and explore decompiled libraries without editing the analyzed code.

The server runs locally over stdio. Source navigation uses Roslyn; assembly navigation uses the ILSpy decompiler (`ICSharpCode.Decompiler`). Targets are existing `.sln` / `.slnx` solutions or managed `.dll` / `.exe` files.

## What you can do

| Task | Tools |
| --- | --- |
| Find declarations and read source or decompiled bodies | `find_symbol`, `get_symbol_body` |
| Browse namespaces, file outlines, and type members | `browse_target` with `view=namespaces`, `get_file_skeleton`, `get_context` (`sections=[members]`) |
| Trace callers, callees, references, and summarize discovered sites | `get_call_tree`, `find_references` (`includeSummary=true` for totals) |
| Follow inheritance, implementations, and dependency edges | `get_type_hierarchy`, `find_implementations`, `dependency_graph` |
| Inspect library APIs, search decompiled code, and find extension methods | `inspect_assembly`, `search_assembly`, `find_assembly_extensions` |
| Resolve a type to its source project or framework/NuGet assembly | `resolve_type_origin` |
| Read selected body, members, direct uses, and static test candidates | `get_context` (`usageScope` filters source uses) |

The server exposes **14 read-only navigation tools**. The [tool reference](docs/tools/README.md) lists their supported targets, parameters, result limits, and examples.

Navigation results provide stable source (`src:<project-path>|<declaration-id>`) or assembly (`asm:<simple-name>|<declaration-id>`) references for follow-up calls in the existing handoff fields. Keep the returned owner target with assembly references. Byte and optional token budgets bound responses; polling and continuation tokens let clients retrieve long-running or paged results. See the [tool reference](docs/tools/README.md#shared-request-and-response-behavior) and [reference contract](docs/navigation/symbol-resolution.md).

## Quick start on Windows

### 1. Get the server

Check [GitHub Releases](https://github.com/RalfHuesing/AiNetCodeNavigator/releases) for published builds. The release workflow produces `AiNetCodeNavigator-win-x64.zip`, a self-contained Windows x64 package. Extract the entire archive to a folder such as `C:\Tools\AiNetCodeNavigator`; the package includes the .NET runtime. Source-solution loading still needs the MSBuild/.NET SDK tooling required by the target solution.

To build from source instead, use PowerShell 7 or later and the .NET 10 SDK specified in [`global.json`](global.json). From a checkout of this repository, run:

```powershell
pwsh -File ./scripts/deploy.ps1 -SkipTests
```

This builds the solution and publishes the executable and its dependencies to `deploy/`, skipping the test suite. Keep the published files together. This local deployment is framework-dependent and requires the .NET 10 runtime. See [setup requirements](docs/setup/README.md) for both installation options.

### 2. Configure your MCP client

Use the absolute path to the published executable. For clients that accept an `mcpServers` process configuration:

```json
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "command": "C:\\Tools\\AiNetCodeNavigator\\AiNetCodeNavigator.exe"
    }
  }
}
```

Replace the example path with your installation path. The client starts the server as a child process; no server arguments are required for default settings. The [setup guide](docs/setup/README.md) covers optional configuration, logging, and client-specific entries for Claude Desktop, Cursor, and Antigravity. Cursor additionally requires `"type": "stdio"` in its server entry.

### 3. Navigate a solution or assembly

For example, ask your agent to find `OrderService.Save`, read its implementation, and identify its callers. The corresponding MCP tool requests are:

```json
{"name":"find_symbol","arguments":{"targetPath":"C:\\work\\AiNetCodeNavigator\\AiNetCodeNavigator.slnx","pattern":"StableSymbolReference"}}
{"name":"get_symbol_body","arguments":{"targetPath":"C:\\work\\AiNetCodeNavigator\\AiNetCodeNavigator.slnx","symbolIdentifiers":["src:src/AiNetCodeNavigator.Core/AiNetCodeNavigator.Core.csproj|T:AiNetCodeNavigator.Core.Symbols.StableSymbolReference"]}}
{"name":"get_call_tree","arguments":{"targetPath":"C:\\work\\AiNetCodeNavigator\\AiNetCodeNavigator.slnx","symbolIdentifier":"src:src/AiNetCodeNavigator.Core/AiNetCodeNavigator.Core.csproj|T:AiNetCodeNavigator.Core.Symbols.StableSymbolReference","direction":"incoming"}}
```

Pass the exact reference returned by `find_symbol`. For a compiled library, start with `inspect_assembly` and its absolute DLL path, then pass a returned type or member reference together with its owner target to structure or body tools. An unchanged source declaration reference remains usable after body or unrelated edits and a server restart; rediscover after a rename, signature change or project move. The sample reference identifies `StableSymbolReference` in the Core project when the repository solution is selected. See [symbol resolution](docs/navigation/symbol-resolution.md) for the wire and recovery contract.

## Scope and limitations

- Navigation is read-only. The server writes its own logs, caches, and temporary analysis files; navigation tools do not edit analyzed source files or binaries. Source loading uses MSBuild design-time evaluation, which is not a sandbox for arbitrary custom build targets.
- The product focuses on C# navigation. Linting, compiler diagnostics, code-quality scoring, and automatic refactoring are outside its scope.
- Assembly navigation requires managed .NET binaries with IL. Native binaries are unsupported; the server does not execute analyzed assemblies.
- Test context identifies static test candidates, including heuristic name matches. It does not measure test coverage.
- Tools differ in source and assembly support. `browse_target` with `view=scope` is source-only; `get_context` supports source and assembly targets, with static test candidates available only for source solutions. Indexed scope is not a complete physical-file inventory.
- The navigation verification selection uses SDK contracts and transport-free source/assembly handlers. The documented client configurations are not handshake-tested compatibility claims; retained stdio/client end-to-end tests are excluded from the official test scripts. See [MCP Host](docs/mcp-host.md) and [Build and Tests](docs/development/build-and-tests.md).

## Development and documentation

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test.ps1
```

The scripts write logs to `temp/`. Routine tests exclude extended integration cases; `scripts/test.ps1 -IncludeExtended` includes them while still excluding `E2EIntegration`. The fast suite includes an audit launcher that requires a local AiNetReview installation at the [documented path](docs/development/build-and-tests.md#automatic-audit-reports).

- [Documentation index](docs/README.md): implemented behavior and architecture.
- [MCP tool reference](docs/tools/README.md): tool contracts and usage patterns.
- [Setup guide](docs/setup/README.md): executable configuration and client setup.
- [Build and test guide](docs/development/build-and-tests.md): project layout and verification commands.

## License

[MIT](LICENSE).
