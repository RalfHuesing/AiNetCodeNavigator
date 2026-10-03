# Reproduction and agent workflow verification

## Current discussion priority, 2026-10-03

After approval of the identity and dependency optimizations, recommend automated checks of complete development sequences, not only isolated tool results. The core sequence is discovery, an external source edit, and navigation through the returned stable symbol reference against current analysis. Add unrelated edits, server restart and a large dependency query that follows running controls through to its actual final result.

This proposal is not yet approved. Existing required regressions for observable production changes are mandatory independently; the proposed addition is an explicit workflow-level acceptance set. Use deterministic fixtures, correct scope/owner selection, full paging/window reconstruction and a finite run plan. Record total elapsed time and tool requests for equivalent successful outcomes. Verify the selected polling behavior in the intended clients separately from transport-free tests. Do not infer that a tool-sequence replay establishes reliability across all LLMs.

Source edits in these scenarios belong to the external test harness; navigation tools remain read-only. Reuse existing fixture/test infrastructure and the official verification scripts. This point does not authorize an unbounded benchmark campaign, modifications to external audited repositories or the optional agent workflow.

## Existing evidence

The local 360-degree audit contains 55 request files and 55 response files covering the declared navigation tools and selected edge cases. Several responses are partial, and complete follow-up sequences are not retained for all body windows and cursors. It is useful exploratory evidence, not a complete production-readiness gate.

The earlier [bounded measurement task](../usage-audit-hardening/roadmap/M3-T2.md) already distinguishes complete workflows, cold/warm runs, semantic scope, request/poll/page counts and missing evidence. Its recorded execution claims remain task evidence; this discussion did not rerun its checks or expand its measurement mandate.

## Proposed workflow cases

1. Discover a declaration, read its body, change only the body, and navigate the same declaration again.
2. Change an unrelated loaded source document and reuse the intended declaration reference.
3. Restart the MCP process and reuse a declaration reference saved in agent context.
4. Exercise rename, signature change and deletion with explicit reconciliation or failure behavior.
5. Resolve equal DocCommentIds in different owning projects and overloads without choosing the first match.
6. Traverse dependencies on a synthetic broad solution and reconstruct all needed results/polls.
7. Hop to a referenced assembly owner and follow its declaration after reopening the session.
8. Exercise concurrent external edits and cursor consistency; the server itself remains read-only.

## Measurement requirements

Bind results to a known build, process and target/reference state. Record full response text, every relevant page and window, requests, polls, output tokens/bytes and total elapsed workflow time. Record client/schema/request token costs separately if available. A smaller first page or omitted evidence is not an efficiency improvement.

Include success rate and recovery requests. Token count per successful workflow is more meaningful than token count per identifier. Controlled edit logs are needed to explain snapshot drift.

## Acceptance and authorization

Production behavior changes require failing regressions where reproducible, appropriate contract/ownership/cancellation cases and official verification scripts. Transport-free correctness and actual stdio/client behavior are separate evidence categories. No broad performance campaign or optional workflow step is started by this knowledge-store request.
