# Setup

AiNetCodeNavigator is a local MCP server that uses standard input/output (stdio). Use a Windows `AiNetCodeNavigator.exe` built or published from this repository. Building requires the .NET 10 SDK version in [`global.json`](../../global.json) (10.0.203 with latest-feature roll-forward); a framework-dependent executable also needs the .NET 10 runtime on the host. The source build command is `pwsh -File ./scripts/build.ps1`; this is a build command, not a published self-contained distribution.

## Local process configuration

Clients start the executable as a child process. Set `command` to the executable's absolute path, and pass each CLI argument as its own string in `args`. The server reads MCP JSON-RPC from standard input and writes only protocol messages to standard output. Startup/configuration errors go to standard error. Logging goes to daily rolling files in `<exe-directory>\logs` by default; error and fatal log events are also written to standard error. Set `AINET_CODE_NAVIGATOR_LOG_DIRECTORY` in the server process environment to select another log directory.

The optional server argument `--config <absolute-path>` selects a host-settings JSON file. If omitted, the default is `%LOCALAPPDATA%\AiNetCodeNavigator\hostsettings.json`; if that default file does not exist, startup uses built-in `Information` logging. A settings file contains only a supported `minimumLogLevel` value:

```json
{
  "minimumLogLevel": "Warning"
}
```

Valid values are `Verbose`, `Debug`, `Information`, `Warning`, `Error`, and `Fatal`. The server does not create or watch the file. `reload_config` reloads the same active file.

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

For the current IDE interface, put the entry above under `mcpServers` in project `.cursor/mcp.json` or global `~/.cursor/mcp.json`, then enable the server under Cursor's available tools. Cursor's official [MCP guide](https://docs.cursor.com/context/model-context-protocol) documents local `stdio` servers, these configuration locations, and the `command`/`args` JSON shape. Cursor CLI detects the same `mcp.json` configuration; see [Using Agent in CLI](https://docs.cursor.com/en/cli/using).

## Antigravity

In Antigravity IDE, open the Agent panel's **MCP Servers → Manage MCP Servers → View raw config** and place the same entry in `mcp_config.json`. The current global path is `~/.gemini/config/mcp_config.json`; a workspace-level configuration is `.agents/mcp_config.json`. Antigravity 2.0 also exposes installed servers under **Settings → Customizations → Installed MCP Servers**. The official [Antigravity MCP guide](https://antigravity.google/docs/mcp) covers IDE, CLI, and desktop surfaces, local stdio, the `mcpServers` schema, and both file locations.

Client paths and UI directions were checked against official client documentation on 2026-10-02. No client configuration was changed, no client was launched, and no handshake was verified.
