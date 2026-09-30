# MCP Host

The executable runs a Model Context Protocol server over stdio. Standard output is reserved for JSON-RPC messages; command-line help and parse errors go to standard error, and Serilog writes rolling files under `AppContext.BaseDirectory/logs` with error and fatal events mirrored to standard error. The host uses the .NET Generic Host and the SDK stdio transport. End-of-input performs graceful shutdown; the host cancels active long-running operations, drains them, then disposes project and assembly resident state.

The production host currently registers two maintenance tools:

- `get_server_health` reports process uptime, resident source and assembly sessions, active assembly accesses, handoff count, compilation-cache counters, and managed memory. An optional `targetPath` reports whether an existing supported solution or assembly is already resident. The check reads registry metadata only; it does not load or refresh a target. It does not return compiler or assembly diagnostic samples.
- `reload_config` reloads the active JSON settings file. The only currently supported runtime setting is `minimumLogLevel`, with exact values `Verbose`, `Debug`, `Information`, `Warning`, `Error`, or `Fatal`. A valid file publishes a new immutable version and changes the Serilog level switch. Invalid, unreadable, or missing files leave the active version and log level unchanged.

Both tools accept `maxResponseBytes` (512–65,536; default 16,384) and optional `maxResponseTokens` (positive integer). These limits apply to successful and recoverable error results. A token limit too small to represent the required result is returned as a sanitized `InvalidParams` error.

`--config <path>` selects an explicit JSON file. Without it, the host reads `%LOCALAPPDATA%/AiNetCodeNavigator/hostsettings.json`; if that default file is absent, startup uses built-in `Information` logging. Calling `reload_config` when no default file exists returns `CONFIG_NOT_FOUND`. Unknown fields and malformed JSON are rejected. The host does not watch the file automatically and never writes it.

The real-process integration test performs an MCP initialize handshake, lists the registered maintenance tools, calls health and configuration reload, closes stdin, and verifies a clean exit with no non-protocol stdout. The 20 navigation tools have not yet been registered; this host is not yet the complete navigation product.
