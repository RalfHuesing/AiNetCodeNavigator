# Long-running operations and transport

Status: specified for implementation. Execution: [R07](roadmap.md#r07--short-polls-progress-and-transport-diagnosis).

## One operation and ordinary tool results

Retain the existing `operationToken` route. A running response has `operation=running`, `completeness=not_applicable` and the same bound operation token. It is not a navigation result. Polling never starts a second analysis, changes the snapshot already chosen by the operation or reinterprets its query.

Issue new operation tokens using the existing cryptographically random, fixed-width 39-digit decimal token generator already used for continuations. This gives repeatable `cl100k_base` token cost for fresh-call admission/recovery; it does not change the opaque-token API or its binding/expiry semantics. Reuse that generator rather than adding a second token framework, and check issuance collisions in the operation store. Token kinds remain distinguished by their request fields and stores, never by parsing their digits.

Preserve query binding to tool, canonical target, semantic arguments, explicit argument-presence rules and active result cursor. Response budgets may change. Running polls preserve an active `resultCursor`; `continuationToken` remains mutually exclusive with operation/result tokens. Completed results, errors and outer/domain paging retain their existing replay semantics.

No new MCP task subsystem, progress-notification dependency, cancel tool or public poll-window argument is introduced. Existing shutdown, initial-request cancellation, poll-waiter cancellation, operation idle expiry and capacity limits remain authoritative.

## Response windows

| Call | Maximum operation wait before returning a running control |
| --- | --- |
| First call | 15 seconds |
| Poll with `operationToken` | 1 second |

These are server wait bounds, not network end-to-end guarantees. Use monotonic time and injectable internal durations/time source for tests. Formatting, scheduling and transport overhead must be measured separately. If work completes within the applicable window, return its completed result immediately.

The existing first-window override in tests must not accidentally override the poll window. Do not increase either window to hide a transport failure. A synchronous delegate must remain off the transport thread and outside the operation-store lock.

## Running-control fields

Preserve the existing status preamble and `operationToken=<token>` text convention. Add these newline-delimited control fields in the ordinary text result:

```text
elapsedMilliseconds: <non-negative integer>
phase: <phase>
retryAfterMilliseconds: 1000
processedDocuments: <non-negative integer, only when measured>
totalDocuments: <non-negative integer, only when known>
nextAction: Wait at least 1000 ms, then repeat the same tool, target and query with this operationToken; preserve an active resultCursor and omit continuationToken.
```

Phases have this fixed order/mapping:

| Phase | Boundary |
| --- | --- |
| loading | Target/owner acquisition and initial materialization |
| refreshing | Source freshness verification and reload |
| identifying | Snapshot/reference fingerprint creation or memo lookup |
| analyzing | Symbol resolution, semantic collection and traversal |
| formatting | Final result projection, serialization and response formatting |

The published phase is the highest boundary reached, including when later cross-owner work re-enters loading. Skip inapplicable boundaries; initialize to loading and advance to analyzing before an analysis delegate without more detailed events. Never regress. elapsedMilliseconds is monotonic elapsed time since admission, represented as a non-negative Int64 with saturation at Int64.MaxValue.

Publish immutable thread-safe records through a host adapter; Core reports typed events without MCP dependencies. For source dependency collection, processedDocuments counts each (snapshot ticket, ProjectId, DocumentId) need once when satisfied by new/shared collection or valid cached facts. It describes covered needs, not fresh scans. Do not reset it at phase transitions. Other tools may omit document counters entirely.

Publish totalDocuments only when the final required need set is known and cannot grow (for example broad eligible coverage or known depth-1 roots). Once published, it never changes. If frontier discovery can grow needs, omit totalDocuments throughout that operation. Counts never decrease or exceed a published total. Failed needs do not increase processedDocuments; final omissions remain separate.

Progress describes actual completed work. Do not derive percentage or ETA from elapsed time, fabricate progress during waits, or infer a stall solely from repeated running responses.

## Budget and lifecycle rules

A running control is atomic: status, usable operation token, elapsed time, phase, retry interval and complete next action must all fit. Optional document counters may be omitted to satisfy a budget; required fields may not be silently truncated.

Before admitting first-call work, reserve its actual operation-token value without starting/registering a job. Preflight the required control with that token, the maximum non-negative Int64 elapsed value, each allowed phase and the fixed action. Use the maximum measured UTF-8 bytes and maximum measured tokens across those phase variants, applying the existing public minimum-byte floor, as the admission minima. Decimal token width must have fixed measured tokenizer cost; phase cost is measured separately for every allowed value. If either budget is insufficient, return `RESPONSE_BUDGET_TOO_SMALL` with that exact measured admission pair, and instruct a fresh unchanged call with those budgets. The fresh token has the same cost, so the offered pair is executable. No job has started, so this retry cannot duplicate work. Preserve existing validation/error-envelope behavior for budgets too small even to represent an error; such a request must not admit work.

For a poll whose actual required control cannot fit changed budgets, offer the same fixed admission pair, not a temporary smaller elapsed/phase minimum that might fail on the next poll. Return the atomic budget error and retain the operation and caller-supplied token. Every admission/poll budget error includes both minimumResponseBytes and minimumResponseTokens plus the complete next action. The retry uses exactly that offered pair and unchanged query; first-call retry creates one job, poll retry stays on the same job. Optional counters may disappear at tight budgets. Verify the required control and error envelope independently; budgets unable even to carry the existing error envelope never admit work. Initial preflight guarantees that an admitted job can expose its usable token.

Cancelling the initial request cancels its owned work. Cancelling a poll stops only that wait. If the operation awaits a shared cached computation, cancellation releases that operation's subscription according to specification 04; it must not cancel another subscriber's analysis. Expiry or shutdown cancels owned work, releases subscriptions and awaits cleanup. Exceptions remain recoverable errors, never running successes.

Retain current defaults: four active operations, 32 completed records and 30-minute inactivity retention. Valid polls reset running inactivity; disappearance of the client does not create infinite retention. Do not redesign operation capacity/expiry for this work.

## Agent/client behavior and verification

A client waits at least the supplied interval after a running response and repeats the exact bound query. It treats the final success/error separately from running status and follows outer pages before domain cursors. Server polling does not depend on the agent reading asynchronous notifications.

The audit agent already issued polls; this is evidence that the route is usable in principle, not proof about every model/client. Implementation acceptance requires:

1. Store/component and transport-free handler tests proving one retained job, both windows, progress, budget recovery and token/query invariants.
2. A bounded SDK transport component in the new IntegrationTests class `AiNetCodeNavigator.IntegrationTests.Mcp.LongRunningTransportComponentTests`, with no ExtendedIntegration/E2EIntegration trait. Follow the existing traffic-capture component boundary: in-process SDK host, explicitly written/read in-memory JSON-RPC frames and a registered fixture tool delegating to the real operation store/control formatter. No SDK client object, child stdio server, MSBuild workspace load or complete navigation product flow. First-call frame timeout is 20 seconds; poll-frame timeout is 5 seconds. Exercise repeated running controls and final retrieval. Actual Navigator tool definitions/validators/handlers are verified separately without transport under item 1. Select this component with `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AiNetCodeNavigator.IntegrationTests.Mcp.LongRunningTransportComponentTests'`. It is not a claim that an excluded E2E suite passed.
3. A recorded agent round-trip through the connected AiNetCodeNavigator client used by the implementation task, on the new verified server build. The client waits at least the announced interval, retains the exact query/token, retrieves final result/error and follows outer pages before domain cursors. Record client identity and version (`unavailable` with reason if not exposed), exact server build identity and every running response/poll's monotonic timestamps. The measured interval from client receiving a running response to starting its next poll is at least retryAfterMilliseconds. Record normalized tool/target/semantic arguments and explicit argument presence, operationToken and active resultCursor equality; continuationToken is absent on polls. An unavailable connected client or build identity prevents completion of R07, not just a universal compatibility claim.

Required slow-operation fixtures are deterministic and bounded; a user's large solution is not the timing mechanism. Preserve existing official exclusions for complete E2E/client handshake tests. Required checks are the bounded fixture transport, transport-free actual routes and recorded connected-agent round-trip above.

Store the tracked evidence summary at `tasks/agentic-navigation-evolution/evidence/R07.md`; create it during R07, not during planning. Store ignored raw frames, monotonic timing records and stderr/process evidence at `temp/agentic-navigation-evolution/R07/<run-id>/`, where run-id is UTC yyyyMMddTHHmmssfffZ plus a GUID suffix. The summary links to those actual artifacts and states their ignored/nonportable nature. The R07 roadmap evidence row links to the tracked summary and records executed commands/results. Manual product evidence is separate from automated eligible gates.

## EOF/timeout investigation

The [audit trace](../../temp/mcp-test-360/raw_calls/02_source_san/10_dependency_graph_poll_eof_response.txt) reports an initial running response, two approximately 15-second polls and EOF/timeout. It lacks complete per-poll frames and a server exit code. The 15-second poll wait is not a proven disconnection cause.

Use the existing traffic-capture/lifecycle facilities, recording exact build, target/scope/document counts, client version and timeouts, request IDs, complete frames, monotonic timings, stderr/file logs, server PID/lifetime and exit code. Compare a controlled long-operation fixture with a generated large source fixture and, only if available, the original target. Diagnose client timeout, server exit/crash, cancellation and framing/transport failures separately.

A reproducible defect requires a failing regression and cause-level correction. The evidence summary records original-target availability (`unavailable` plus reason when missing), each controlled/generated/current-client attempt, complete request/response frames, timing and process/exit outcome. If historical EOF cannot be reproduced, the accepted outcome is explicitly `historicalCause=undetermined`, with all required controlled/current-client checks successful and environment/attempts/artifact paths recorded. Never call the historical cause fixed merely because polls got shorter.

## Inspected entry points

[LongRunningToolCallStore](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs), [runtime windows](../../src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs), [control formatter](../../src/AiNetCodeNavigator/Mcp/Formatting/McpToolResults.cs), [source routing](../../src/AiNetCodeNavigator/Mcp/Tools/NavigationToolSupport.cs), [store tests](../../tests/AiNetCodeNavigator.FastTests/Mcp/LongRunningToolCallStoreTests.cs) and [traffic-capture transport tests](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/TrafficCaptureTransportIntegrationTests.cs).
