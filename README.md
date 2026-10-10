# AiNetCodeNavigator — C# navigation and assembly export tools

AiNetCodeNavigator provides a local, read-only MCP server for C# source code and compiled .NET assemblies, plus a separate offline assembly exporter. Connect an installed server to an MCP client to find declarations, read implementations, trace static relationships, and explore decompiled libraries.

## Start with an installed server

Use the absolute path to `AiNetCodeNavigator.exe` in your client's local stdio server configuration. For clients that accept `mcpServers`:

```json
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "command": "C:\\Tools\\AiNetCodeNavigator\\AiNetCodeNavigator.exe"
    }
  }
}
```

Replace the path with your installation. The client starts the process; no arguments are required for default settings. Read `AiNetCodeNavigator.exe --doc setup` for client examples, optional host settings, logging, and installation requirements. Cursor's process entry also requires `"type": "stdio"`.

After connecting, use the client's tool discovery (`tools/list`) to read the installed server's tool names, descriptions, input schemas, defaults, and limits. Client prefixes can vary. Choose an absolute existing `.sln` / `.slnx` for source navigation or managed `.dll` / `.exe` for assembly navigation. Read `AiNetCodeNavigator.exe --doc tools` for complete source and assembly walkthroughs and response recovery.

## Read documentation offline

```powershell
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.exe" --help
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.exe" --doc topics
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.exe" --doc tools 2> navigator-tools.md
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.AssemblyExport.exe" --doc guide > assembly-export-guide.md
```

The server embeds `overview` (this page), `setup`, and `tools`; the exporter embeds `guide`. Discovery commands print and exit without an MCP client, settings file, or target. Server documentation goes to stderr; exporter documentation goes to stdout. These are the canonical repository pages embedded at build time and describe the installed build. Relative links are optional further reading in the source repository, not files beside the executable.

## What you can do

The server exposes 12 navigation tools. Choose a tool by the question it answers; the offline `tools` topic explains the choices and practical query semantics. Start with `find_symbol` for source declarations or `inspect_assembly` for a compiled API, retain returned references and owner targets, then request only the body or relationship needed. When a physical source path and line are already known, read a bounded file range directly.

## Installation and offline export

Check [GitHub Releases](https://github.com/RalfHuesing/AiNetCodeNavigator/releases) for published `AiNetCodeNavigator-win-x64.zip` packages. Extract the whole archive and keep the executables with their dependencies. The Windows x64 release is self-contained and includes the .NET runtime. Loading source solutions still requires the MSBuild/.NET SDK tooling appropriate to the selected solution; see `--doc setup`.

The separate exporter creates searchable C# project trees from selected managed DLLs and EXEs:

```powershell
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.AssemblyExport.exe" --output "C:\asm-dump" --source "C:\Programme" --include "foo*.exe" --include "*bar*.dll" --exclude "DevExpress*.dll"
```

The output root is disposable: each subsequent export replaces it only when its exact ownership marker is present. Read `AiNetCodeNavigator.AssemblyExport.exe --doc guide` before choosing it. Inspect the generated `README.md`, `assemblies.json`, `last-run.log`, and selected child's `export-manifest.json` before treating an export as current or complete.

## Scope and limitations

- Navigation tools do not edit analyzed sources or binaries. The server writes its own logs, caches, and temporary analysis files; the exporter writes only beneath its validated, marked output directory.
- Source loading uses MSBuild design-time evaluation, which is not a sandbox for arbitrary custom build targets.
- Server stdout is reserved for MCP JSON-RPC. Startup errors and command-line documentation use stderr.
- Navigation focuses on C#. Linting, compiler diagnostics, code-quality scoring, and automatic refactoring are outside the product scope.
- Assembly targets must be managed .NET binaries with IL. Native binaries are unsupported; analyzed assemblies are never executed. Assembly bodies are decompiled text.
- Static relationships do not establish runtime dispatch. Test candidates do not measure execution or test coverage. Indexed source scope is not a complete physical-file inventory.
- Tool target support varies. `browse_target` with `view=scope` and `get_context` static test candidates require source solutions.
- Inspect reported scope, snapshot, omissions, section/item errors, and analysis limits before concluding completeness or absence. Paging delivers known results; larger response budgets do not expand analysis.

## For repository visitors

The same Markdown is the canonical source for embedded documentation. Optional repository references:

- [Setup](docs/setup/README.md) and [MCP tools](docs/tools/README.md): installed-product usage.
- [Assembly export guide](docs/assembly-export.md): export operation and completeness.
- [Documentation index](docs/README.md): current implemented contracts and architecture.
- [Build and tests](docs/development/build-and-tests.md): source builds, deployment, verification, and development prerequisites.
- [Manual MCP exploration](docs/development/mcp-exploration.md): on-demand scenarios and real response inspection.

Client configuration examples describe the process contract; they are not handshake-tested compatibility claims. The optional [build and test guide](docs/development/build-and-tests.md) describes the verification boundary.

## License

[MIT](LICENSE).
