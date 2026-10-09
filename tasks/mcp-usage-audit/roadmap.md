# Active roadmap: exploratory cycle 2

The user authorized this cycle on 2026-10-09. The [first-round roadmap](archive/round1/roadmap.md) is historical; its unchecked B/G gates remain disclosed in [results.md](results.md), not active work here.

- [x] **A — Prepare.** All registered targets exist; original working trees were clean at preflight. Explorer listing/build and identity were recorded. R01/R04 build configuration was checked before loading. Two scouts have separate targets, evidence paths, and a serialized Explorer slot. See P-2 in `results.md`.
- [x] **B — Explore.** Two scouts inspected R01, R04, A01, and the isolated R03 copy through serialized Explorer runs. Independent review checked answers and costs, corrected agent summary errors, and accepted R03-F1 for triage. See X rows in `results.md`; no original external target changed.
- [x] **C — Triage.** One candidate selected: C2-E1 below. R01/R04/A01 did not establish a product fix. Do not turn output volume alone into a new feature.
- [ ] **D — Implement and review.** Complete selected slices one at a time. Verify a failing deterministic regression first, use official build and risk-selected checks, review privacy and contracts, then make a local commit. Reject a candidate when its benefit does not justify complexity.
- [ ] **E — Replay and transfer.** Run fresh Explorer processes against fixed snapshots and preselected questions. Independently judge answers, call/attempt/output costs, fallbacks, and omissions. Check a fresh agent question without implementation hints.
- [ ] **F — Close.** Run the final export safeguard when code changed, remove temporary scenarios, verify original targets and Navigator tree, publish sanitized results and limitations, and stop this cycle.

## Current state

- Baseline Navigator revision: `7dd26a41853d193823e55670a156893b7913a2d7`.
- Active stage: D. Implement C2-E1 after its neutral failing regression.
- Selected candidates: C2-E1 only (one attempted; no other candidate selected).
- Product writer: orchestrator only.
- Raw evidence root: ignored `temp/external-repos/mcp-usage-audit/round2/`.
- No direct MCP JSON calls, live server measurement, client or alternate call simulation.

## Selected slice

- [ ] **C2-E1 — Do not call an empty configured-framework list known.** R03-F1: three loaded project rows have unknown loaded contexts, `configuredFrameworksKnown=true`, empty configured values, and a 0/0 framework summary, while their old-style project files declare framework versions. `MSBuildStructureInputCollector` currently marks an empty `TargetFrameworks`/`TargetFramework` evaluation known. Add a neutral old-style fixture that fails before the change; mark the empty evaluation unknown, keep existing modern nonempty values known, and update the scope documentation. Do not infer legacy `TargetFrameworkVersion` normalization or missing analyzed contexts in this slice. Preserve public schema and existing consumer fields. Expected replay: the same isolated R03 snapshot reports three entries with unknown configured-framework coverage; relationship 2/2 and owner/body handoff remain usable. Verify official build, the narrow collector/Core regression, fresh Explorer replay, independent review, and final export safeguard. No direct MCP JSON simulation.

On interruption, update this block with completed stages, active assignments, owned processes, unresolved gates, evidence keys, and the exact next action. An unchecked item must not be reported as passed.
