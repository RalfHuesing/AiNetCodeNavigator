# Reproduction and agent workflow verification

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
