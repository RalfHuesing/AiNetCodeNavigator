# MCP Tool Results

`McpToolResults` is an internal host utility for constructing `ModelContextProtocol.Protocol.CallToolResult` values. It emits exactly one text content block and sets `IsError` explicitly.

Successful text results use `Status: operation=ok, completeness=complete` and `IsError=false`; a shortened result uses `completeness=truncated` and retains its continuation hint. An optional `JsonElement` can be attached as `StructuredContent` for an untruncated success. If the text projection is truncated, combining it with structured content is rejected to avoid presenting a partial text view beside an apparently complete structured result.

`Error`, `Recoverable`, and `InvalidArgument` all use `IsError=true`, an `[ERROR]` code, and an actionable `nextAction` when supplied. The status, code/message, argument path, budget recovery details, and correction action form a required envelope: it is emitted whole or rejected with `ArgumentOutOfRangeException` when the requested budgets cannot represent it. Optional context is added only when the required envelope still fits; it may be shortened or omitted to preserve those fields. Recoverability describes the recommended correction; it does not change the MCP error flag.

`Loading` is the transient exception: it uses `Status: operation=retry, completeness=not_applicable` and `IsError=false`. It accepts byte and token budgets and keeps the retry action as a required field. Its display message is shortened to at most 64 Unicode scalar values before formatting so it cannot displace the retry action. If the action itself cannot fit, the builder returns an `IsError=true` budget error when that error envelope fits; otherwise it throws rather than returning an over-budget response.

Budget failures use `Status: operation=error, completeness=not_applicable`, retain `RESPONSE_BUDGET_TOO_SMALL` recovery details, and set `IsError=true`. Success retry minima and retry eligibility come from the formatter's success projection; the error status and recovery envelope are then formatted separately under the same byte and token limits. The error envelope is rechecked against its requested limits. Success, failure, loading, and budget-error status text is included in `McpResponseFormatter`'s UTF-8 byte and token accounting. An unrepresentable token cap is rejected instead of producing an over-budget result.

This is an internal construction layer. Tool registration and public stdio behavior are not established by it.
