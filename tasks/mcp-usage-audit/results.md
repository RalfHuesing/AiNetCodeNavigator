# Active results: focused cycle 3

Status: stopped at the user's request after two bounded explorations and independent reviews. No cycle-3 product candidate or code change was accepted. This is not an optimum claim.

[Round 1](archive/round1/results.md) and [round 2](archive/round2/results.md) remain the historical evidence. Round 2's late-frozen R02 inventory answered scope and ownership but stopped before a declaration/body handoff. This cycle begins with fresh answer-free questions and does not treat that omission as a server defect.

## Ledger

| ID | Stage | Alias | Evidence key | Observed result and limit | Decision |
| --- | --- | --- | --- | --- | --- |
| P-3 | Preflight | R02, R04 | `round3/preflight.local.json`, `round3/explorer-list.local.log`, frozen questions | Both targets existed and were clean at global preflight; Explorer listing succeeded and runner/Core hashes were recorded. The R02 self-target checkout became dirty during navigation because its temporary scenario was in the same checkout; HEAD and Core output stayed stable. R04 remained at its preflight revision and clean. | Inputs accepted with the R02 scenario-contamination limit. |
| X-R02-3 | Exploration and review | R02 | `round3/scout-r02/`, `round3/review-r02/review.local.md` | Twelve calls/attempts, 66,051 visible UTF-8 response bytes. Static output supported the public entry, local validation and route, owning resolver, and a focused Core test. No actual request to the selected operation was issued; host binding remains inferred. Two result cursors and 51 call-tree edges were not followed. A final runner hash was saved, not per-call runner hashes. | Bounded static answer passed; agent/question-budget limit, no product defect. |
| X-R04-3 | Exploration and review | R04 | `round3/scout-r04/`, `round3/review-r04/review.local.md` | Nine calls, ten attempts, 48,848 visible UTF-8 bytes. Static output supported one production caller. No direct test use appeared; one indirect test candidate had a helper-chain path, while a same-name suggestion had no evidence. One malformed argument was corrected; one outer page was completed. The chosen operation was frozen after extra navigation, and the ignored scenario source embedded a target-specific handoff. | Answer content passed within its limits; probe process requires rework, no product defect. |

No product code was changed in this cycle. The R02 summary's initial clean-state and prompt-location bookkeeping was corrected in ignored evidence; its raw pre-navigation record remains available. R04's ignored scenario copy retains the procedure issue for review, while its temporary scenario was removed from the repository. The user stopped further work before any correction/replay stage.

The scouts' omitted calls and one schema error are agent/probe behavior. The saved outputs exposed the relevant cursors, depth bounds, evidence kinds, and argument recovery. These observations do not justify a new public tool or a server change. Future work would need a separately authorized, cleaner probe; none is scheduled.

Raw target identities, questions, source excerpts, and responses remain only in ignored temp/external-repos/mcp-usage-audit/round3/ under the privacy boundary. The tracked result contains aliases and measured visible UTF-8 bytes only. No direct MCP JSON calls, alternate simulator, broad test suite, or export safeguard ran in this documentation-only cycle.
