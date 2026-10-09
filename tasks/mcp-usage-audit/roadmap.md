# Active roadmap: exploratory cycle 2

The user authorized this cycle on 2026-10-09. The [first-round roadmap](archive/round1/roadmap.md) is historical; its unchecked B/G gates remain disclosed in [results.md](results.md), not active work here.

- [x] **A — Prepare.** All registered targets exist; original working trees were clean at preflight. Explorer listing/build and identity were recorded. R01/R04 build configuration was checked before loading. Two scouts have separate targets, evidence paths, and a serialized Explorer slot. See P-2 in `results.md`.
- [ ] **B — Explore.** Send up to two independent scouts to different target snapshots. They use only the Explorer, choose adaptive agent questions, inspect actual outputs, and submit bounded answers and friction evidence. Have an independent reviewer check ground truth and costs.
- [ ] **C — Triage.** Select at most three attempted candidates, with at most two implementation slices. Require a concrete agent benefit, isolated cause, smallest owner, neutral reproduction where feasible, consumer decision, replay task, and narrow tests. Record other observations without inventing work.
- [ ] **D — Implement and review.** Complete selected slices one at a time. Verify a failing deterministic regression first, use official build and risk-selected checks, review privacy and contracts, then make a local commit. Reject a candidate when its benefit does not justify complexity.
- [ ] **E — Replay and transfer.** Run fresh Explorer processes against fixed snapshots and preselected questions. Independently judge answers, call/attempt/output costs, fallbacks, and omissions. Check a fresh agent question without implementation hints.
- [ ] **F — Close.** Run the final export safeguard when code changed, remove temporary scenarios, verify original targets and Navigator tree, publish sanitized results and limitations, and stop this cycle.

## Current state

- Baseline Navigator revision: `7dd26a41853d193823e55670a156893b7913a2d7`.
- Active stage: B. R01 has the Explorer slot; R04 waits for release.
- Selected candidates: none.
- Product writer: orchestrator only.
- Raw evidence root: ignored `temp/external-repos/mcp-usage-audit/round2/`.
- No direct MCP JSON calls, live server measurement, client or alternate call simulation.

On interruption, update this block with completed stages, active assignments, owned processes, unresolved gates, evidence keys, and the exact next action. An unchecked item must not be reported as passed.
