# MCP Tool Results

`McpToolResults` is an internal host utility for constructing `ModelContextProtocol.Protocol.CallToolResult` values. It emits exactly one text content block and sets `IsError` explicitly.

Successful text results use `Status: operation=ok, completeness=complete` and `IsError=false`; a shortened result uses `completeness=truncated` and retains its continuation hint. An optional `JsonElement` can be attached as `StructuredContent` for an untruncated success. If the text projection is truncated, combining it with structured content is rejected to avoid presenting a partial text view beside an apparently complete structured result.

`Error`, `Recoverable`, and `InvalidArgument` all use `IsError=true`, an `[ERROR]` code, and an actionable `nextAction` when supplied. Recoverability describes the recommended correction; it does not change the MCP error flag. `Loading` is the transient exception: it uses `Status: operation=retry, completeness=not_applicable`, an `[INFO]` message, and `IsError=false`.

Budget failures use `Status: operation=error, completeness=not_applicable`, retain `RESPONSE_BUDGET_TOO_SMALL` recovery details, and set `IsError=true`. Success and failure builders include their status text in `McpResponseFormatter`'s UTF-8 byte and token accounting. An unrepresentable token cap is rejected by the formatter instead of producing an over-budget result.

This is an internal construction layer. Tool registration and public stdio behavior are not established by it.
