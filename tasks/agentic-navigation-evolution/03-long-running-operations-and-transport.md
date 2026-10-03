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

Allowed phases, in order, are `loading`, `refreshing`, `identifying`, `analyzing`, `formatting`. Skip phases that do not apply. Do not regress the phase within an operation. Initialize to `loading`; before invoking analysis without a more precise event, advance to `analyzing`. `elapsedMilliseconds` is measured from operation admission and excludes no hidden stages.

Publish immutable, thread-safe progress records through a host adapter; Core may report typed phase/counter events without depending on MCP types. `processedDocuments` counts distinct semantic document needs successfully satisfied for this operation by new collection, shared in-flight collection or valid cached facts. It measures processed coverage, not the number of fresh scans. Keep actual scanner counts separate for performance evidence. Include `totalDocuments` only when the denominator for that operation's selected collection is fixed and known; omit it for growing outgoing frontiers or scans that cannot measure it. Counts must be monotonic and never exceed a published total. Failures remain analysis evidence in the final result.

Progress describes actual completed work. Do not derive percentage or ETA from elapsed time, fabricate progress during waits, or infer a stall solely from repeated running responses.

## Budget and lifecycle rules

A running control is atomic: status, usable operation token, elapsed time, phase, retry interval and complete next action must all fit. Optional document counters may be omitted to satisfy a budget; required fields may not be silently truncated.

Before admitting first-call work, reserve its actual operation-token value without starting/registering a job. Preflight the required control with that token, the maximum non-negative Int64 elapsed value, each allowed phase and the fixed action. Use the maximum measured UTF-8 bytes and maximum measured tokens across those phase variants, applying the existing public minimum-byte floor, as the admission minima. Decimal token width must have fixed measured tokenizer cost; phase cost is measured separately for every allowed value. If either budget is insufficient, return `RESPONSE_BUDGET_TOO_SMALL` with that exact measured admission pair, and instruct a fresh unchanged call with those budgets. The fresh token has the same cost, so the offered pair is executable. No job has started, so this retry cannot duplicate work. Preserve existing validation/error-envelope behavior for budgets too small even to represent an error; such a request must not admit work.

For a poll whose actual required control cannot fit its changed budgets, return the existing atomic budget error with exact measured minima and retain the operation and caller-supplied token. The next action repeats that same token/query with the offered budgets. Initial preflight must guarantee that an admitted job can always expose its usable token; budget failures must never orphan an operation.

Cancelling the initial request cancels its owned work. Cancelling a poll stops only that wait. If the operation awaits a shared cached computation, cancellation releases that operation's subscription according to specification 04; it must not cancel another subscriber's analysis. Expiry or shutdown cancels owned work, releases subscriptions and awaits cleanup. Exceptions remain recoverable errors, never running successes.

Retain current defaults: four active operations, 32 completed records and 30-minute inactivity retention. Valid polls reset running inactivity; disappearance of the client does not create infinite retention. Do not redesign operation capacity/expiry for this work.

## Agent/client behavior and verification

A client waits at least the supplied interval after a running response and repeats the exact bound query. It treats the final success/error separately from running status and follows outer pages before domain cursors. Server polling does not depend on the agent reading asynchronous notifications.

The audit agent already issued polls; this is evidence that the route is usable in principle, not proof about every model/client. Implementation acceptance requires:

1. Store/component and transport-free handler tests proving one retained job, both windows, progress, budget recovery and token/query invariants.
2. An SDK in-memory transport/client exchange with per-call timeout 5 seconds for polls and 20 seconds for the first call, exercising repeated running controls and final retrieval through the actual registered route. Do not relabel a complete product client flow merely to evade repository E2E exclusions.
3. A recorded manual agent round-trip through the then-connected AiNetCodeNavigator client used by the implementation task: unchanged query/token, observed waiting, final result/error and no false completion claim. Record client/version where exposed and server build identity. If this client is unavailable, leave R07 blocked for that concrete verification; do not substitute a claim of universal support.

Required slow-operation fixtures must be deterministic and bounded; do not rely on loading a user's large solution to produce a test delay.

## EOF/timeout investigation

The [audit trace](../../temp/mcp-test-360/raw_calls/02_source_san/10_dependency_graph_poll_eof_response.txt) reports an initial running response, two approximately 15-second polls and EOF/timeout. It lacks complete per-poll frames and a server exit code. The 15-second poll wait is not a proven disconnection cause.

Use the existing traffic-capture/lifecycle facilities, recording exact build, target/scope/document counts, client version and timeouts, request IDs, complete frames, monotonic timings, stderr/file logs, server PID/lifetime and exit code. Compare a controlled long-operation fixture with a generated large source fixture and, only if available, the original target. Diagnose client timeout, server exit/crash, cancellation and framing/transport failures separately.

A reproducible product defect requires a failing regression and cause-level correction within this point. If the historical EOF cannot be reproduced, the acceptable recorded outcome is: audited cause undetermined, bounded controlled/current-client exchanges completed without disconnection, and exact environment/attempts captured. Never claim the historical cause was fixed merely because polls got shorter.

## Inspected entry points

[LongRunningToolCallStore](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs), [runtime windows](../../src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs), [control formatter](../../src/AiNetCodeNavigator/Mcp/Formatting/McpToolResults.cs), [source routing](../../src/AiNetCodeNavigator/Mcp/Tools/NavigationToolSupport.cs), [store tests](../../tests/AiNetCodeNavigator.FastTests/Mcp/LongRunningToolCallStoreTests.cs) and [traffic-capture transport tests](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/TrafficCaptureTransportIntegrationTests.cs).
