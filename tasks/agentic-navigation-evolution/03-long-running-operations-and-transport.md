# Long-running operations and transport failures

## User decision and agent compatibility, 2026-10-03

The user conditionally approved this point if agents can handle it. The existing result-based polling route supports that condition in principle: the audit agent already issued two polls, current schemas expose operationToken, and the repository navigation instructions tell agents to repeat the same tool/target/query with the returned token. Record diagnosis of the EOF/timeout, shorter status polls and concrete progress as approved, with implementation and actual-client verification pending. The observed 15-second poll wait is not a proven cause of disconnection. Preserve existing output presentation.

Keep the existing ordinary tool-result route: operation=running, the same operationToken, useful progress when measurable, and an explicit next action including a bounded suggested wait. Agents must preserve query identity; a running result is not a successful analysis. Short response time must not create uncontrolled tight polling loops. The exact wait/progress fields remain contract design work.

The inspected PendingOperationReturnsFinalResultBySameTokenWithoutRestarting test covers completion through the same token without restarting work. It was read, not run during this discussion. This establishes an implemented protocol route and test intent, not universal compatibility across clients or models. Verification must include the actual clients intended for use, correct final-result retrieval, bounded retries and no premature success report.

Native MCP progress notifications are a separate optional integration path: the [official schema](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/schema/2025-11-25/schema.json) describes a caller-supplied progressToken and does not require the receiver to provide notifications. Do not make agent-visible status depend exclusively on out-of-band notifications or introduce native task support as part of this approval.

## Evidence and limits

The [large-target graph response](../../temp/mcp-test-360/raw_calls/02_source_san/10_dependency_graph_poll_eof_response.txt) records an initial running control, two approximately 15-second running polls and then an EOF/timeout description. The dump does not establish whether the client timed out, the server exited, or transport failed for another reason. It also does not preserve separate complete request/response frames for each poll or a server exit code.

[LongRunningToolCallStore](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs) already owns background work and operation tokens. Initial requests and subsequent polls currently use the same response window, configured to 15 seconds by the host. Cancelling a poll waiter differs from cancelling initial work. Operation retention, capacity, cancellation and continuation behavior already exist and must be preserved.

## Proposed next steps

- Reproduce through a known server build and a client with explicit timeout settings, recording process lifetime, request identifiers, complete frames and stderr/file logs.
- Correlate the existing uncommitted traffic-capture work with server lifecycle and actual analysis progress. Its presence in the working tree is not proof that the audited or live process uses it.
- Approved: use a short polling wait distinct from the initial response window, with elapsed time, analysis phase and a suggested polling interval; specify the exact bounds during contract design.
- Report concrete progress such as scanned documents when available. Do not manufacture percentage completion or ETA from elapsed time.
- Still open: decide how explicit operation cancellation is exposed if needed; avoid introducing an additional asynchronous subsystem merely because this audit contains a timeout.

## Proposed acceptance evidence

Long work remains owned by one operation across polls. Status calls return within their documented bound. Waiter cancellation does not corrupt the retained operation. Shutdown and operation expiry preserve resource cleanup. A reproduced disconnection is assigned a cause using client/server evidence. Longer timeouts alone are not a verified correction.
