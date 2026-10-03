# Setup

AiNetCodeNavigator is a local MCP server that uses standard input/output (stdio). Use a Windows `AiNetCodeNavigator.exe` built or published from this repository. Building requires the .NET 10 SDK version in [`global.json`](../../global.json) (10.0.203 with latest-feature roll-forward); a framework-dependent executable also needs the .NET 10 runtime on the host. The source build command is `pwsh -File ./scripts/build.ps1`; deploy the executable and its dependencies to a testable directory with `pwsh -File ./scripts/deploy.ps1`.

## Windows release package

The [release workflow](../../.github/workflows/release.yml) publishes a self-contained Windows x64 build as `AiNetCodeNavigator-win-x64.zip`, including the .NET runtime, `hostsettings.json`, README, and license. Check [GitHub Releases](https://github.com/RalfHuesing/AiNetCodeNavigator/releases) for published packages. Extract the entire archive and keep the executable and its dependencies together. Source-solution loading uses installed MSBuild/.NET SDK tooling appropriate to the target solution, even with a self-contained server package.

The included `hostsettings.json` is used only when selected with `--config`; placing it beside the executable does not change the default settings-file path described below.

## Local process configuration

Clients start the executable as a child process. Set `command` to the executable's absolute path, and pass each CLI argument as its own string in `args`. The server reads MCP JSON-RPC from standard input and writes only protocol messages to standard output. Startup/configuration errors go to standard error. Logging goes to daily rolling files in `<exe-directory>\logs` by default; error and fatal log events are also written to standard error. Set `AINET_CODE_NAVIGATOR_LOG_DIRECTORY` in the server process environment to select another log directory.

The optional server argument `--config <absolute-path>` selects a host-settings JSON file. If omitted, the default is `%LOCALAPPDATA%\AiNetCodeNavigator\hostsettings.json`; if that default file does not exist, startup uses built-in `Information` logging with traffic capture disabled. The supplied [hostsettings.json](../../hostsettings.json) includes every supported option:

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

The local deployment script copies the supplied defaults only when the destination has no `hostsettings.json`; it preserves an existing settings file. Select that destination file explicitly with `--config`, or copy the supplied file to the default settings-file path.

Set `trafficCapture.enabled` to `true` to record raw tool-call requests and responses under `<log-directory>/traffic`. [MCP Traffic Capture](../mcp-traffic-capture.md) describes the session layout, live JSONL summaries, byte/token measurements, and storage limits.

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
