# Roadmap

Preparation is complete. Execution has not started. Resume at the first unchecked item; check items only with linked sanitized evidence in `results.md` and local raw evidence retained.

- [x] Prepare charter, protocol, prompts, blank evidence templates, and ignored target registry.
- [ ] **A — Preflight and calibration.** Verify all alias targets, prerequisites, external dirty states, ignore boundaries, current catalog, and transport-free runner behavior. Record load limitations and run identities. Acceptance: usable targets and explicit blockers; no assumed framework/LOC verification. No baseline test suite or export smoke required.
- [ ] **B — Freeze questions and independent ground truth.** At least six source questions per target, including two held-out questions; at least three assembly questions. Assign a reviewer to ground truth and seal holdouts. Acceptance: applicable families, independent evidence, budgets and measurement plan recorded locally.
- [ ] **C — Baseline usage audit.** Run adaptive explorations on R01–R04, inspect all current tools, score outputs, count recovery attempts/fallbacks, and classify findings. Acceptance: actual saved outputs and per-question judgments; transport boundary explicit; temporary scenarios cleaned up.
- [ ] **D — Select evidence-backed changes.** Rank findings, reject unsupported ideas, record contract decisions, and append bounded executable implementation children here. Acceptance: each child has finding IDs, owner, neutral reproduction, acceptance criteria, verification commands, and cross-target replay selection. Do not preselect a new tool catalog.
- [ ] **E — Implement and verify selected slices.** Execute the children sequentially with one writer. Each child includes failing regression where feasible, implementation, affected docs/consumers, appropriate official gates, independent review, and a verified local commit. Parent remains open until all children pass. Insert stall diagnosis before the next feature slice whenever required.
- [ ] **F — Paired replay and fresh holdouts.** Re-run frozen baseline tasks and use a fresh explorer for holdouts; compare per-question correctness, cost, output, and usability across all targets. Acceptance: benefits supported by comparable measurements; regressions resolved or changes rejected; no hidden fallback or partial load.
- [ ] **G — Final acceptance.** Independent review of boundaries, privacy, tool coherence, docs, risk-selected test evidence, remaining coverage gaps, final export safeguard/manifests, scenario cleanup, and external working-tree changes. No full-suite or transport gate. Reuse still-valid checks under the protocol. Acceptance: completion criteria in the charter met and sanitized final report committed. Otherwise retain open in-scope blockers and report partial status.

## Implementation child form

Copy under E after triage:

```markdown
- [ ] E<n> — <neutral outcome>
  - Finding IDs and evidence:
  - Owner / allowed files:
  - Reproduction and expected failing check:
  - Contract / consumer decision:
  - Acceptance and cross-target replay:
  - Necessary verification: risk, actual filter, expected duration, reusable evidence, remaining gaps:
  - Independent review / correction state:
  - Result and commit:
```

## Resume state

- Last completed stage: preparation only.
- Active assignment: none.
- Next action after execution is requested: A.
- Required pending user decision: none for preparation.
- Evidence location: local registry prepared; no run evidence yet.

On interruption update this section with neutral IDs, active owned processes, completed gates, pending checks, local handoff location, and the exact next action. Never mark an interrupted or unrun gate as passed.
