# Active results: exploratory cycle 2

Status: exploration in progress. No cycle-2 product candidate has been selected or implemented yet. Do not infer an optimum from an empty current ledger.

The [first-round report](archive/round1/results.md) remains the complete historical evidence, including two accepted fixes, partial overall acceptance, and its unresolved coverage limits. The [first-round roadmap](archive/round1/roadmap.md) preserves its historical B/G status. This cycle is separate and does not retroactively change that judgment.

## Run ledger

| ID | Stage | Alias | Evidence key | Observed result / limit | Decision |
| --- | --- | --- | --- | --- | --- |
| P-2 | Preflight | R01–R04 | `round2/preflight.local.json` | All registered source targets exist and their original worktrees were clean at preflight. Explorer listing/build succeeds; R01/R04 build configuration was checked before loading. Prior R03 original reference and framework limits remain until independently rechecked. | R01 and R04 scouts assigned with serialized Explorer access; no code candidate yet. |
| X-R01-2 | Exploration and independent review | R01 | `round2/scout-r01/`, `round2/review-r01/review.local.md` | Two questions, 9 logical calls, 43 physical attempts, 53,954 visible UTF-8 bytes. The cross-project handoff was supported. The contract question was incomplete because the scout omitted follow-up reads for contract members and helper behavior, although the output was readable. Namespace-depth recovery was actionable. A mislabeled local clean-state field was corrected; current target revision and clean state match. | Agent follow-up miss, not a proven product defect; no candidate selected from R01. |
| X-R04-2 | Exploration and independent review | R04 | `round2/scout-r04/`, `round2/review-r04/review.local.md` | Ten logical calls, 19 attempts, 201,572 visible bytes overall. The selected relationship query delivered 73 depth-specific rows at 50 unique locations across two pages with no analysis omission. The test section clearly distinguished 17 direct-use methods from four fixture-name-only suggestions; no execution or coverage was inferred. Preliminary and final query snapshots were kept separate after a local summary correction. | Passed bounded review; high output cost is observed, but no product defect or safe small change is established. |
| X-A01-2 | Exploration and independent review | A01 | `round2/scout-a01/`, `round2/review-a01/review.local.md` | Six calls, 25 attempts, 534,714 visible bytes overall. Exact body handoff and continuation completed a 164-line body. A selected depth-2 graph first returned 30/272 entries, then 272/272 with a larger result cap and 18 complete outer pages. The assembly-wide overview stayed partial; output truncation and separate diagnostics were initially conflated in the agent summary, then corrected from saved evidence. Fixed DLL hash and length match its manifest. | Bounded answer passed after summary correction. Large graph output is a single cost observation, not a proven product defect. |
| X-R03-2 | Exploration and independent review | R03 isolated copy | `round2/scout-r03/`, `round2/review-r03/review.local.md` | One question, 9 logical calls/attempts, 64,996 visible bytes. The selected relation returned two of two implementations with no result cursor; metadata owner and declaration handoff worked. Three solution projects reported unknown loaded framework contexts but empty configured-framework lists marked known and a 0/0 scope summary. A fourth project file found in preflight is outside the solution. The isolated copy has a preflight fingerprint; a post-load fingerprint comparison is still needed. | R03-F1 accepted for bounded C2-E1 correctness fix. |

Evidence keys are relative to ignored `temp/external-repos/mcp-usage-audit/`. Raw target paths, symbols, questions, answers, and response traces remain local. Product findings must be independently checked before entering the decision ledger.

## Candidate decisions

| ID | Finding and owner | Decision | Expected benefit / remaining limit |
| --- | --- | --- | --- |
| C2-E1 | R03-F1: `MSBuildStructureInputCollector` treats empty modern framework properties as known, which suppresses the scope's unknown-coverage count for old-style projects. | Select the conservative `IsKnown=false` correction for empty values. Keep the current public tool and fields; neutral regression, R03 replay, and review are required before acceptance. | Avoid false 0/0 framework reassurance. Legacy version extraction and mapping to loaded contexts remain unresolved. |

No other product candidate is selected. Output volume and agent follow-up mistakes remain observed costs, not automatic feature requests. Record before/after answers, attempts, bytes, omissions, verification, and disposition after replay.

## Limits

- A running MCP server is not used for measurement; every code revision requires a fresh Explorer build/process and recorded binary identity.
- Direct MCP JSON calls, live clients, alternate simulators, and transport checks are outside this cycle by user instruction.
- Current preflight does not establish complete semantic coverage. Original R03 dependencies, classic framework contexts, and assembly relationship closure were limited in round 1.
