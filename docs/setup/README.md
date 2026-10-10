# Setup

Connect your installed `AiNetCodeNavigator.exe` to an MCP client as a local stdio process. Set the executable's absolute path as `command`; no arguments are required for built-in defaults. For a client accepting `mcpServers`:

```json
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "command": "C:\\Tools\\AiNetCodeNavigator\\AiNetCodeNavigator.exe"
    }
  }
}
```

Replace the path with your installation, enable the server in your client, and use the client's tool discovery (`tools/list`) to inspect available tools. Cursor also requires `"type": "stdio"`; client-specific examples follow below. Read `AiNetCodeNavigator.exe --doc tools` for navigation walkthroughs and response recovery. The separate `AiNetCodeNavigator.AssemblyExport.exe` is an offline export command and does not connect to an MCP client.

## Embedded command-line documentation

An installed `AiNetCodeNavigator.exe` can explain itself before a client starts the MCP transport. Run `--help` for the command summary, `--version` for the build, or `--doc topics` for the embedded topic index. `--doc overview` prints the product README, `--doc setup` prints this setup guide, and `--doc tools` prints the MCP tool usage guide. No settings file or target is needed for these discovery commands; they exit after printing.

Server help, version, documentation, and argument errors are written to stderr so stdout remains available exclusively for MCP JSON-RPC. Large topics can be saved for selective reading:

```powershell
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.exe" --doc tools 2> navigator-tools.md
```

Saving the output is optional; agents can choose to read it directly. Each page is embedded from its canonical repository Markdown at build time, so no separate documentation folder or internet connection is needed. Relative links reference optional further reading in the source repository, rather than installed files. The separate export executable provides its own `--doc topics` and `--doc guide`, written to stdout. Read that offline guide for export options and output ownership.

## Installed package types

The Windows release package contains both executables in a self-contained Windows x64 archive, `AiNetCodeNavigator-win-x64.zip`, including the .NET runtime, `hostsettings.json`, README, and license. Both the MCP server and the exporter executables and their shared dependencies are located at the archive root. Check [GitHub Releases](https://github.com/RalfHuesing/AiNetCodeNavigator/releases) for published packages. Extract the entire archive and keep the executables with their dependencies. Source-solution loading uses installed MSBuild/.NET SDK tooling appropriate to the target solution, even with a self-contained server package.

A directory produced by the repository's default local deployment is different: it is framework-dependent and needs the .NET 10 runtime installed on the host. It contains both executables, their published dependencies, and `hostsettings.json`; the deployment script does not copy a standalone README or license. Their absence in that local package does not prevent reading embedded `--doc` topics. Custom publishing arguments can change the packaging, so use the package's provenance or publisher instructions to determine runtime requirements rather than inferring them from the executable name or a few neighboring files.

When `hostsettings.json` is included, it is used only when selected with `--config`; placing it beside the executable does not change the default settings-file path described below.

## Offline assembly export

The release archive places the exporter beside the MCP server. Invoke it directly with named options:

```powershell
& "C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.AssemblyExport.exe" --output "C:\asm-dump" --source "C:\Programme" --include "foo*.exe" --include "*bar*.dll" --exclude "DevExpress*.dll"
```

The exporter has no MCP process configuration and does not read `hostsettings.json`. It writes the dump only to the explicit, marked output directory. Read `AiNetCodeNavigator.AssemblyExport.exe --doc guide` for glob limits, dependency handling, cleanup ownership, output structure, and run completeness. The [repository export guide](../assembly-export.md) is optional further reading of the same canonical page.

## Local process configuration

MCP clients start the server executable as a child process. Set `command` to its absolute path, and pass each server argument as its own string in `args`. The server reads MCP JSON-RPC from standard input and writes only protocol messages to standard output. Startup/configuration errors go to standard error. Logging goes to daily rolling files in `<exe-directory>\logs` by default; error and fatal log events are also written to standard error. Set `AINET_CODE_NAVIGATOR_LOG_DIRECTORY` in the server process environment to select another log directory. These process settings apply to the MCP server only.

The optional server argument `--config <absolute-path>` selects a host-settings JSON file. If omitted, the default is `%LOCALAPPDATA%\AiNetCodeNavigator\hostsettings.json`; if that default file does not exist, startup uses built-in `Information` logging with traffic capture disabled. A settings file can include these supported options:

```json
{
  "minimumLogLevel": "Information",
  "trafficCapture": {
    "enabled": false,
    "retentionDays": 7,
    "maxTotalBytes": 536870912
  }
}
```

Valid log levels are `Verbose`, `Debug`, `Information`, `Warning`, `Error`, and `Fatal`. `trafficCapture.enabled` is a boolean; `retentionDays` is an integer from 1 through 365; `maxTotalBytes` is an integer from 1 through 10,737,418,240 (10 GiB). Omitted capture fields use the defaults shown above, and existing files containing only `minimumLogLevel` remain supported. Unknown or duplicate fields, invalid types, and unsupported values fail startup. The server reads the file at startup, does not create or watch it, and requires a process restart to apply changes.

To use the release package's settings file, select its absolute path with `--config`, or place your settings at the default settings-file path.

Set `trafficCapture.enabled` to `true` to record raw tool-call requests and responses under `<log-directory>/traffic`. The optional repository reference [MCP Traffic Capture](../mcp-traffic-capture.md) describes the session layout, live JSONL summaries, byte/token measurements, and storage limits.

Use this process entry for a client that accepts an MCP `mcpServers` JSON configuration:

```json
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "command": "C:\\Tools\\AiNetCodeNavigator\\AiNetCodeNavigator.exe",
      "args": ["--config", "C:\\Tools\\AiNetCodeNavigator\\hostsettings.json"]
    }
  }
}
```

If you rely on default settings, omit `args`. These examples describe the server process contract; clients have not been started or handshake-tested as part of this setup reference.

## Claude Desktop

For a directly configured local executable, open **Claude menu → Settings → Developer → Edit Config**. The official [MCP local-server guide](https://modelcontextprotocol.io/docs/develop/connect-local-servers) documents this route and the Windows file `%APPDATA%\Claude\claude_desktop_config.json`; add the `mcpServers` process entry above. Claude Desktop also documents local MCP management under **Settings → Extensions**, including installation of packaged `.mcpb` desktop extensions. This repository provides the executable process configuration above, not an `.mcpb` package. See Anthropic's [local MCP guide](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop) for the current Extensions interface. Local servers use the computer's network; remote custom connectors connect from Anthropic's cloud.

## Cursor

For the current IDE interface, put this Cursor-specific entry in project `.cursor/mcp.json` or global `~/.cursor/mcp.json`, then enable the server under Cursor's available tools. Cursor requires `type: "stdio"` for a local process entry:

```json
{
  "mcpServers": {
    "AiNetCodeNavigator": {
      "type": "stdio",
      "command": "C:\\Tools\\AiNetCodeNavigator\\AiNetCodeNavigator.exe",
      "args": ["--config", "C:\\Tools\\AiNetCodeNavigator\\hostsettings.json"]
    }
  }
}
```

Cursor CLI detects the same `mcp.json` configuration. See the official [MCP guide](https://cursor.com/docs/mcp) for the required `type`, local configuration locations, and `command`/`args` shape, and [Using Agent in CLI](https://cursor.com/docs/cli/using) for CLI configuration detection.

## Antigravity

In Antigravity IDE, open the Agent panel's **MCP Servers → Manage MCP Servers → View raw config** and place the same entry in `mcp_config.json`. The current global path is `~/.gemini/config/mcp_config.json`; a workspace-level configuration is `.agents/mcp_config.json`. Antigravity 2.0 also exposes installed servers under **Settings → Customizations → Installed MCP Servers**. The official [Antigravity MCP guide](https://antigravity.google/docs/mcp) covers IDE, CLI, and desktop surfaces, local stdio, the `mcpServers` schema, and both file locations.

Client paths and UI directions were checked against official client documentation on 2026-10-02. No client configuration was changed, no client was launched, and no handshake was verified.

## Source checkout and further reading

Source-solution navigation needs the MSBuild/.NET SDK tooling required by the target solution, regardless of packaging. Source loading uses MSBuild design-time evaluation and is not a sandbox for arbitrary custom build targets. The server does not edit analyzed source or execute analyzed assemblies.

For building or deploying from a source checkout, use the optional repository [Build and Tests](../development/build-and-tests.md) guide. On-demand development scenarios are described in [Manual MCP Exploration](../development/mcp-exploration.md). These pages require a checkout; installed-product navigation is covered by `--doc tools`.
