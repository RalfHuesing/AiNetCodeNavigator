# Long-running operations and transport failures

## Evidence and limits

The [large-target graph response](../../temp/mcp-test-360/raw_calls/02_source_san/10_dependency_graph_poll_eof_response.txt) records an initial running control, two approximately 15-second running polls and then an EOF/timeout description. The dump does not establish whether the client timed out, the server exited, or transport failed for another reason. It also does not preserve separate complete request/response frames for each poll or a server exit code.

[LongRunningToolCallStore](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs) already owns background work and operation tokens. Initial requests and subsequent polls currently use the same response window, configured to 15 seconds by the host. Cancelling a poll waiter differs from cancelling initial work. Operation retention, capacity, cancellation and continuation behavior already exist and must be preserved.

## Proposed next steps

- Reproduce through a known server build and a client with explicit timeout settings, recording process lifetime, request identifiers, complete frames and stderr/file logs.
- Correlate the existing uncommitted traffic-capture work with server lifecycle and actual analysis progress. Its presence in the working tree is not proof that the audited or live process uses it.
- Consider a short polling wait distinct from the initial response window, with elapsed time, analysis phase and a suggested polling interval.
- Report concrete progress such as scanned documents when available. Do not manufacture percentage completion or ETA from elapsed time.
- Decide how explicit operation cancellation is exposed if needed; avoid introducing an additional asynchronous subsystem merely because this audit contains a timeout.

## Proposed acceptance evidence

Long work remains owned by one operation across polls. Status calls return within their documented bound. Waiter cancellation does not corrupt the retained operation. Shutdown and operation expiry preserve resource cleanup. A reproduced disconnection is assigned a cause using client/server evidence. Longer timeouts alone are not a verified correction.
