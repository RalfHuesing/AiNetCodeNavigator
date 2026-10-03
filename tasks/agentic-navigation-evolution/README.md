# Agentic navigation evolution

This directory is the discussion memory for improving navigation during agentic software development. Update the relevant topic files as the discussion develops. Keep findings, recommendations, user preferences, open decisions and implementation evidence distinct. These files do not claim that proposals are implemented or invoke the optional agent workflow.

Created: 2026-10-03. Initial source inspection: HEAD `a670e57d02654c04c833c61fcc15794f8eac7c1f`, with unrelated uncommitted traffic-capture and deployment changes present. Source findings describe the inspected working tree; the running MCP binary was not independently matched to this commit.

## Topics

| Topic | Current status / priority | Discussion file |
| --- | --- | --- |
| Symbol identity, snapshot binding and recovery | Source/assembly references, rename behavior and relative-path base approved | [01](01-symbol-identity-and-recovery.md) |
| Dependency graph collection and targeted traversal | Collection, reuse and targeted outgoing analysis approved | [02](02-dependency-graph-analysis.md) |
| Long-running operations, polling and transport failure | Approved; actual-client verification required | [03](03-long-running-operations-and-transport.md) |
| Snapshot refresh, hashing and reusable analysis | Dependency and source-identity reuse approved | [04](04-snapshot-refresh-and-analysis-cache.md) |

The source evidence is the local [360-degree audit](../../temp/mcp-test-360/summary_findings.md), its request/response files, and the linked production sources and tests. The audit is a collection of observations rather than a complete acceptance result.

## Current discussion

- The user requests that this knowledge store be maintained as next steps are discussed in chat.
- The user has decided to remove the Base62 `h:` handle mechanism completely and replace ephemeral navigation handles with stable, self-describing symbol references. Do not retain an alternative `h:` mode or a parallel ephemeral identifier. They have approved readable string IDs containing the source project path relative to the repository and the Roslyn declaration ID, with analysis snapshot identity separate. No absolute path is embedded in the symbol ID. Exact serialization grammar and detailed edge cases remain to be specified; implementation is pending.
- Existing output presentation remains unchanged.
- The user has approved eliminating repeated semantic scans in dependency-graph collection: collect each document batch once and derive all internal relationship pages from that result.
- The user has approved reliable status retrieval for long analyses on the condition that agents can handle it. The existing operationToken polling route and observed agent polls support this in principle. Diagnose the transport failure and implement shorter polls with real progress and an explicit next action; verify the intended clients. Do not rely exclusively on out-of-band notifications or claim universal agent compatibility.
- The user has approved bounded reuse of dependency collection across requests against the same immutable snapshot and analysis scope. Changed analysis inputs require fresh collection. Incremental updates across snapshots remain an unapproved proposal.
- The user has approved targeted outgoing dependency analysis, starting from the selected type's declaration documents and expanding only as needed for the requested depth. This addresses initial queries when no reusable full graph exists. Full incoming/both coverage and completeness must be preserved; implementation is pending.
- The user has approved computing source analysis identity once per unchanged immutable Roslyn snapshot and reusing it across navigation calls. Fresh workspace/content checks remain required; implementation is pending.
- The user has removed the dedicated workflow-verification proposal, output-format discussion and optional fixed analysis sessions completely from this knowledge store. Do not recreate these topics unless requested. Existing repository verification rules still apply to implementation.
- The user has approved rename/signature-change behavior: a changed declaration identity gets a new symbol ID; an old ID whose exact declaration no longer exists returns an explicit failure and a discovery action. Do not redirect based on similarity. Body/unrelated edits preserve unchanged declaration IDs.
- The user has approved assembly symbol references consisting of a readable assembly name plus Roslyn declaration ID, with exact DLL owner target kept separate. No absolute path, content hash, generation or version is embedded in the reusable declaration reference. Implementation is pending.
- The user has approved the base for relative source project paths: use the Git repository root containing the selected solution, or the solution directory when no Git repository exists. Handle worktree .git files and nested solutions deterministically, independently of process current directory.
- The major discussion points are now selected. The next recommended step is a concise implementation plan covering the approved scope and remaining technical edge cases. Begin with the replacement symbol-reference contract and complete handle removal, then dependency collection/reuse/targeted traversal and polling. Creating that plan is the next proposal; do not start production implementation or optional agent-workflow steps from this approval-recording turn.
- Keep chat responses short and discuss one priority at a time; the user explicitly finds long explanations overwhelming.

See the remaining topic files for the decisions and supporting evidence. Current-state `docs/` remain unchanged because this work only records discussion and decisions under `tasks/`; no production behavior has changed.
