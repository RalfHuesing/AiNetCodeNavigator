# MCP Traffic Capture

Traffic capture records local MCP `tools/call` requests and their correlated responses for inspection and offline analysis. It is disabled by default and independent of `minimumLogLevel`. Enable `trafficCapture.enabled` in the selected host settings file, then restart the server. See [Setup](setup/README.md#local-process-configuration) for the complete settings JSON and supported ranges.

## Files and ordering

Capture files live under `<log-directory>/traffic`, using the same log-directory selection as normal server logging. Each server process creates a UUID session directory. The layout is:

```text
traffic/
  <session-uuid>/
    session.json
    calls.jsonl
    <tool-directory>/
      <call-uuid>/
        input.json
        output.json
        summary.json
```

`input.json` and `output.json` preserve the original UTF-8 JSON-RPC frames, including their line terminators. They are tapped at the transport streams, without reformatting or reserializing the messages. Output is captured after SDK serialization and includes the actual budgeted response, tool errors, and JSON-RPC errors. Files can contain source code and local paths exactly as transmitted.

Tool names are sanitized for directory use; summaries retain the original tool name and record the corresponding `toolDirectory`. Calls with invalid or missing tool names use a fallback directory. `session.json` records the server build identity and client name/version when supplied in `initialize`.

`calls.jsonl` appends one summary per completed call in completion order. Its `sequence` identifies request arrival order within the session; concurrent calls can finish out of order. Each line contains the same record as that call's `summary.json`. Raw input is written when the request arrives, and the output and summary are written when its response is observed, so files can be inspected during a session.

## Measurements and correlation

Summaries include session/call IDs, the JSON-RPC request ID, sequence, tool name, UTC start/completion timestamps, elapsed milliseconds, and these measurements:

| Field | Meaning |
| --- | --- |
| `inputBytes`, `outputBytes` | Original framed UTF-8 message lengths, including line terminators and JSON-RPC envelopes. |
| `visibleTextBytes` | UTF-8 size of response text content blocks joined by a newline. |
| `visibleTextTokens` | Token count of that same joined text with `cl100k_base`. |
| `tokenEncoding` | Encoding used for the text token measurement. |
| `outcome`, `errorCode`, `errorMessage` | Response classification and available error details. |
| `cancellationRequested` | Whether a cancellation notification was observed for the request. This does not imply that a successful response became a cancellation. |

Incoming `operationToken`, `continuationToken`, and `resultCursor`, plus their outgoing counterparts, allow an analyst to follow polling and paging. `operation` records available response status metadata. Tokens remain opaque. The capture does not combine calls into one logical operation or change their navigation semantics.

The text token count is a comparison measure, not the client's billed model usage. Structured content, JSON envelopes, tool schemas, the user's task, and the client's broader model context are outside that text count. Raw response files preserve structured content for separate analysis. Only tool calls are stored as raw capture files; initialization supplies session metadata, and cancellation notifications supply cancellation markers. Other protocol traffic and unparseable frames are not a complete network trace.

## Limits and failures

`retentionDays` defaults to 7 and `maxTotalBytes` defaults to 536,870,912 (512 MiB). Storage checks account for capture data across sessions under the same traffic directory; lock files are excluded. Cleanup removes expired inactive sessions and uses the oldest inactive sessions when reducing excess stored data. Active sessions are protected by an exclusive session lock.

The stream tap buffers at most the smaller of `maxTotalBytes` and 1 MiB for one frame. An oversized frame stops capture rather than truncating a raw capture file; the original stream still passes through. Pending calls are bounded. Storage limits and capture I/O failures also stop capture and report the reason through normal logging. Capture startup failure falls back to ordinary stdio.

On normal shutdown, calls that have no observed response receive an incomplete summary when storage remains available; no output is fabricated. If capture stops because of a limit or I/O failure, or the process is killed, a call may have only some of its files. A session stop reason is recorded when it can still be written. Capture failures do not turn successful navigation into tool failures.

With capture disabled, the host uses its ordinary stdio transport without capture directories, frame parsing, or token measurement.
