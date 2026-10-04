# Roadmap: agentic navigation consolidation

## Goal and execution

Implement the [concept](concept.md), preserving substantive navigation regressions and improving actual agent workflows. This roadmap prepares execution; no production milestone has been completed by creating it.

Start the later execution task with: `Implement tasks/agentic-navigation-tool-consolidation using orchestrator.md and roadmap.md.` The [orchestrator prompt](orchestrator.md) defines delegation, reviews, fixes, verification, and resumption. This task-specific process uses the repository rules without invoking the generic four-step workflow.

## Ordered milestones

Execute the first open child point, in order. A parent closes only after its implementation points, independent review, required fixes, and verification are complete.

- [x] **M0 — [Baseline and implementation readiness](roadmap/00-baseline.md)**: concept P0; identify current code, useful reproductions, consumers, and existing regression coverage.
- [x] **M1 — [References, context uses, and evidence](roadmap/01-references-and-evidence.md)**: concept P1.
- [ ] **M2 — [Consolidated navigation entry points](roadmap/02-entry-points.md)**: concept P2; intermediate catalog of thirteen tools.
- [ ] **M3 — [Discovery and metadata ownership](roadmap/03-discovery-and-metadata.md)**: concept P3; final catalog of twelve tools.
- [ ] **M4 — [Focused dependencies and assembly output](roadmap/04-focused-output.md)**: concept P4.
- [ ] **M5 — [Helper-based test navigation](roadmap/05-test-navigation.md)**: concept P5.
- [ ] **M6 — [Runtime findings and capacity recovery](roadmap/06-runtime.md)**: concept P6; implement demonstrated corrections without speculative infrastructure.
- [ ] **M7 — [Final agent-flow verification and release readiness](roadmap/07-final-verification.md)**: concept P7 and sections 8.2–8.4.

The concept owns product requirements; milestone files own progress and completion evidence; the orchestrator prompt owns execution policy. Use links instead of copying the full contracts into each file. A conflict is resolved in the owning document before dependent work continues; do not silently change a product contract.

## Working record

Use each milestone's completion evidence for commands/results, relevant commits, review findings and resolution, Exploration artifact paths, and the next open point. Keep raw logs under existing ignored artifact directories. No separate execution log, task database, per-test approval process, or automatically generated leaf-file tree is needed.

A point too large for a reliable agent assignment may be split inside its existing milestone before dispatch, without changing its acceptance. Add a small correction point only for a real failure, stall, or blocker. A baseline finding that blocks useful verification is corrected before dependent feature work, even if it belongs conceptually to M6; link that evidence from M6 instead of repeating the fix.

Preparation was independently reviewed by a Sol 6.1/medium subagent. Its two findings were resolved: project-level dependency navigation is explicitly source-only, and final JSON-RPC polling/paging/reference follow-ups are mandatory. The focused re-review passed. A Luna/high inventory identified existing test owners linked from M1–M6; this is source inspection, not a claim that tests passed. Local Markdown links/anchors and the documentation diff were checked. Production execution remains unstarted.

## Completion

Finish when the twelve-tool contract, preserved navigation behavior, new capabilities, targeted checks, required final solution check, Exploration inspection, transport smoke, and independent final review pass. Report any unrun or unresolved requirement accurately. Do not add new milestones merely for theoretical stress cases or nonessential polish.
