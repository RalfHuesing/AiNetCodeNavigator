# M0 — Baseline and implementation readiness

## Intent and scope

Establish the actual starting state and a small usable verification basis before editing production code. Covers concept P0 and [sections 3 and 8](../concept.md#3-findings-and-evidence). Historical live measurements are leads, not proof of the current deployed build.

## Executable points

- [ ] **M0.1 — Inspect owners, consumers, and substantive regressions.** Record current HEAD/status, the actual seventeen-tool catalog or any intervening changes, handler/scanner/formatter ownership, registrations, advertised follow-ups, and relevant test classes. Map removed tools' functional assertions to replacements. Inspect current docs and Exploration readiness. Acceptance: subsequent points have concrete owners/consumers and existing regression coverage to reuse; no unsupported assumption about another agent's work.
- [ ] **M0.2 — Record representative baseline flows and actionable runtime findings.** Use existing fixtures/repository targets for W1-W8, recording exact requests, loaded owner/snapshot/build, expected results, errors/omissions, and saved artifacts where available. Reuse existing tests for cases already proven there. Inspect reported slow paths; measure only useful reproduced candidates using section 8.2. Acceptance: fixed meaningful examples support later comparison, and each historical concern is reproduced, closed with evidence, or identified as a concrete missing check. No full timing matrix or LSP/model benchmark.
- [ ] **M0.R — Independent Sol review and readiness acceptance.** Confirm useful baseline, regression mapping, and no invented results. Resolve actionable findings under the [orchestrator policy](../orchestrator.md). If a stall or failure prevents useful verification, add and execute its correction point here before M1. Acceptance: implementation can proceed with the required checks available; remaining nonblocking findings have an owning milestone.

## Verification and non-goals

Read-only inventory requires no full build/test run. Build/run only what is necessary to identify the production baseline and inspect real calls; any corrective code gets official affected checks. The Exploration runner is completed and available, as recorded in [concept 8.3](../concept.md#83-required-final-exploration-inspection); verify its baseline flow here and extend task-specific scenarios during implementation. No additional runner preparation by the user is required. Do not repeatedly establish the same baseline, build another harness, implement speculative optimizations, or migrate production contracts in this milestone. Coordinate shared-file changes if another agent is actively editing them.

## Completion evidence

Not started. During execution, record baseline/build/target identity, concrete owner and test mappings, baseline artifact paths, runtime finding dispositions, review/fix outcomes, and relevant commits here. Keep raw logs in existing ignored directories.
