# Agentic navigation evolution

This directory is the discussion memory for improving navigation during agentic software development. Update the relevant topic files as the discussion develops. Keep findings, recommendations, user preferences, open decisions and implementation evidence distinct. These files do not claim that proposals are implemented or invoke the optional agent workflow.

Created: 2026-10-03. Initial source inspection: HEAD `a670e57d02654c04c833c61fcc15794f8eac7c1f`, with unrelated uncommitted traffic-capture and deployment changes present. Source findings describe the inspected working tree; the running MCP binary was not independently matched to this commit.

## Topics

| Topic | Current status / priority | Discussion file |
| --- | --- | --- |
| Symbol identity, snapshot binding and recovery | Approved direction; implementation pending | [01](01-symbol-identity-and-recovery.md) |
| Dependency graph collection and targeted traversal | Single collection per batch approved; implementation pending | [02](02-dependency-graph-analysis.md) |
| Long-running operations, polling and transport failure | Approved; actual-client verification required | [03](03-long-running-operations-and-transport.md) |
| Snapshot refresh, hashing and reusable analysis | Next discussion: reuse dependency collection across requests | [04](04-snapshot-refresh-and-analysis-cache.md) |
| Reproduction and complete agent workflows | P0 baseline | [05](05-agent-workflow-verification.md) |
| Optional analysis sessions | Exploration | [06](06-analysis-sessions.md) |
| Output selection, JSON/Markdown and token efficiency | Closed: preserve current output | [07](07-output-format-and-token-efficiency.md) |

The source evidence is the local [360-degree audit](../../temp/mcp-test-360/summary_findings.md), its request/response files, and the linked production sources and tests. The audit is a collection of observations rather than a complete acceptance result.

## Current discussion

- The user requests that this knowledge store be maintained as next steps are discussed in chat.
- The user has decided to remove the Base62 `h:` handle mechanism completely and replace ephemeral navigation handles with stable, self-describing symbol references. Do not retain an alternative `h:` mode or a parallel ephemeral identifier. The exact replacement wire contract remains to be specified; implementation is pending.
- The user has decided to preserve current output formats and presentation. Markdown conversion, JSON compaction and broader presentation optimization are not selected for this work.
- The user has approved eliminating repeated semantic scans in dependency-graph collection: collect each document batch once and derive all internal relationship pages from that result. Broader cross-request caching and targeted traversal remain unapproved proposals.
- The user has approved reliable status retrieval for long analyses on the condition that agents can handle it. The existing operationToken polling route and observed agent polls support this in principle. Diagnose the transport failure and implement shorter polls with real progress and an explicit next action; verify the intended clients. Do not rely exclusively on out-of-band notifications or claim universal agent compatibility.
- The next recommended discussion point is bounded reuse of dependency collection across requests against the same immutable snapshot and analysis scope. It remains a proposal; changed analysis inputs require fresh collection.
- Keep chat responses short and discuss one priority at a time; the user explicitly finds long explanations overwhelming.

See topics 01 and 07 for the decisions and supporting evidence. Current-state `docs/` remain unchanged because this work only records discussion and decisions under `tasks/`; no production behavior has changed.
