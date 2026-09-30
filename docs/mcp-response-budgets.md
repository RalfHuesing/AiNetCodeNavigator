# MCP Response Budgets

The host-side `McpResponseFormatter` measures visible response text in UTF-8 bytes and with SharpToken's `cl100k_base` encoding. Public byte budgets are inclusive from 512 through 65,536 bytes; the default is 16,384 bytes. Core has no SharpToken or MCP transport dependency.

Formatting preserves complete newline-delimited units. A shortened response includes a continuation hint with the next UTF-16 offset and omitted UTF-8 byte count; callers can resume by formatting the same source from that offset. Input offsets must be zero, the end of the source, or immediately after a newline. The result also exposes its exact byte and token counts.

If the first complete unit and its continuation marker do not fit, formatting returns `RESPONSE_BUDGET_TOO_SMALL` with `minimumResponseBytes` and `minimumResponseTokens`. It offers a byte retry only when the required byte count is both larger than the current budget and within the supported 65,536-byte maximum. If a complete unit exceeds that maximum, the recovery hint asks the caller to narrow the query; it never recommends an invalid budget. If the byte budget is already sufficient but the token budget is not, the hint recommends raising the token budget. Error responses obey the requested token cap; when the cap cannot represent the error response, formatting throws `ArgumentOutOfRangeException` instead of returning an over-budget error.

This formatter is an internal host utility. Public MCP tool registration and stdio invocation are not established by this component.
