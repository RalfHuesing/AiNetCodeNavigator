# Roadmap

The bounded first round has ended with partial acceptance. Check items only with linked sanitized evidence in `results.md` and local raw evidence retained. B remains unchecked because its missed preimplementation freeze cannot be repaired retroactively. G's independent review is complete, but its full acceptance criteria remain unmet; no automatic next round follows.

- [x] Prepare charter, protocol, prompts, blank evidence templates, and ignored target registry.
- [x] **A — Preflight and calibration.** Verify all alias targets, prerequisites, external dirty states, ignore boundaries, current catalog, and transport-free runner behavior. Record load limitations and run identities. Acceptance: usable targets and explicit blockers; no assumed framework/LOC verification. No baseline test suite or export smoke required. See A-001 in `results.md`.
- [ ] **B — Freeze questions and independent ground truth.** IDs, budgets and baseline questions were allocated before implementation, but actual R01 Q005/Q006 definitions and answer-free handoff were absent. They were independently sealed only after E1/E2 and before their fresh explorer; this cannot retroactively meet the preimplementation freeze. R02–R04 holdouts and all baseline questions were frozen as intended. See B-001 correction and F-007 in `results.md`. This historical gate remains open.
- [x] **C — Baseline usage audit.** Run adaptive explorations on R01–R04, inspect all current tools, score outputs, count recovery attempts/fallbacks, and classify findings. Acceptance: actual saved outputs and per-question judgments; transport boundary explicit; temporary scenarios cleaned up. See C-001–C-006 and the findings table in `results.md`.
- [x] **D — Select evidence-backed changes.** Two candidates are frozen below: E1 source metadata-origin completeness, then E2 exact assembly member handoff. No new public tool. See D-001 and the decision ledger in `results.md`; do not replace a rejected candidate.
- [x] **E — Implement and verify selected slices.** E1 and E2 each have a neutral failing regression, focused implementation, official build and filtered checks, independent review, and a local commit. No verification stall or correction-after-review occurred. See E-001/E-002 in `results.md`.
- [x] **F — Paired replay and fresh transfer questions.** All baseline questions were replayed on identified snapshots and eight fresh explorer questions were independently reviewed. Six holdouts were sealed before implementation; R01 Q005/Q006 are late-frozen transfer probes. Benefits are bounded to E1 completeness visibility and E2 direct member handoff; no speed claim. See F-001–F-009 in `results.md`. The historical B gate remains open.
- [ ] **G — Final acceptance.** Independent review of boundaries, privacy, tool coherence, docs, risk-selected test evidence, remaining coverage gaps, final export safeguard/manifests, scenario cleanup, and external working-tree changes is complete (G-001). E1/E2 and safeguards passed in scope. Full charter acceptance remains open because B's timely R01 freeze is missing and serious semantic coverage limits remain. No full-suite or transport gate applies. Final result is partial acceptance, not a pending review.

### Frozen E children

- [x] **E1 — Bound source metadata-origin claims to loaded references.** Finding F-R03-02. Neutral missing-HintPath regression failed before the fix; the existing tool now labels source metadata searches as loaded-reference scope with unknown declared coverage and recovery, while direct source ownership remains precise. Existing owner/candidate/inventory fields remain; no new tool or input. Official build, two exact transport-free handler tests, and three focused Core tests passed; independent review passed with no correction attempt. Replay remains at F. Evidence `round1/E1/`, `round1/review-E1/`; commit `3b9b4ec`.
- [x] **E2 — Restore exact assembly member handoff when uniquely resolvable.** Finding CSELF-02. A neutral managed fixture reproduced the failed declaration-ID roundtrip before the fix. A selected-owner exact-ID fallback now returns only unique explicit declarations; collisions, wrong owner and missing declarations retain errors. Official build, eight focused Resolver FastTests and one transport-free handler test passed; independent review passed without correction. Fixed A01 replay remains at F. Evidence `round1/E2/`, `round1/review-E2/`; commit `0dbc071`.

Stop after G or at an unresolved bounded blocker. Do not automatically start another improvement round. Unresolved acceptance stays unchecked even when the round has ended with a partial report. Apply the charter's correction/diagnosis limits and preserve user changes when rejecting a candidate.

## Implementation child form

Copy under E after triage:

```markdown
- [ ] E<n> — <neutral outcome>
  - Finding IDs and evidence:
  - Owner / allowed files:
  - Smallest necessary change / excluded adjacent work / complexity cost:
  - Reproduction and expected failing check:
  - Contract / consumer decision:
  - Acceptance and cross-target replay:
  - Necessary verification: risk, actual filter, expected duration, reusable evidence, remaining gaps:
  - Independent review / correction state:
  - Result and commit:
```

## Resume state

- Last completed review: G — independent boundary, privacy, verification and export review, with partial overall acceptance (G-001).
- Active assignment: none; the bounded first round is closed. Historical B and full G acceptance remain unchecked.
- Next action: no automatic next round. Any further remediation or a new preimplementation holdout campaign requires a new user instruction.
- Required pending user decision: none for this round. R03's isolated evaluation copy has supplied references; unknown framework contexts and partial assembly relationships remain explicit limits.
- Evidence location: ignored `temp/external-repos/mcp-usage-audit/round1/` and the calibration raw run referenced in `results.md`.

On interruption update this section with neutral IDs, active owned processes, completed gates, pending checks, local handoff location, and the exact next action. Never mark an interrupted or unrun gate as passed.
