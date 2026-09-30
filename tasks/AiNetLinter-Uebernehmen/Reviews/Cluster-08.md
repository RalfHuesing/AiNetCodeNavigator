# Cluster 8 Review

## Point 8.1: Budgeting and Truncation

- Implementation base: `c17efc44f1898cb905afd65f1b8dee66ac539a6c` (clean working tree); implementation commit is recorded after required gates.
- `McpResponseFormatter` is an internal host utility. It uses SharpToken `cl100k_base` for token counts and enforces inclusive public byte limits of 512 through 65,536, with a 16,384-byte default. SharpToken was moved from Core to the MCP host project.
- Responses preserve complete newline-delimited units. Truncated results carry a UTF-16 continuation offset and omitted UTF-8 byte count; callers can resume from that offset. A first unit that cannot fit returns `RESPONSE_BUDGET_TOO_SMALL` with the exact required byte and token counts. Invalid budgets, token caps, offsets, and malformed UTF-16 are rejected.
- Contract coverage is in `McpFormattingTests`: Unicode UTF-8 accounting, exact byte limits, token accounting and caps, undersized recovery, continuation, public boundaries, and invalid inputs. The new API tests were first run against the placeholder and failed to compile because `Format` and `CountTokens` did not exist; the final focused run passed 12/12.
- Required gates passed on the final implementation: build 0 warnings/0 errors; fast 462/462; integration 12/12; full 462 fast + 12 integration. `git diff --check` is run before commit.
- No public stdio or registered-tool invocation is claimed; that acceptance belongs to later host/registration work.
- Independent point audit: pending. No implementer audit was performed.
