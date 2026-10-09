# Roadmap

The first round is active. Resume at the first unchecked item; check items only with linked sanitized evidence in `results.md` and local raw evidence retained.

- [x] Prepare charter, protocol, prompts, blank evidence templates, and ignored target registry.
- [x] **A — Preflight and calibration.** Verify all alias targets, prerequisites, external dirty states, ignore boundaries, current catalog, and transport-free runner behavior. Record load limitations and run identities. Acceptance: usable targets and explicit blockers; no assumed framework/LOC verification. No baseline test suite or export smoke required. See A-001 in `results.md`.
- [x] **B — Freeze questions and independent ground truth.** Fixed initial sample: six source questions per target, including two held-out questions, and three assembly questions across the campaign. Assign a reviewer to ground truth and seal holdouts. Acceptance: applicable families, independent evidence, budgets and measurement plan recorded locally. See B-001 in `results.md`.
- [x] **C — Baseline usage audit.** Run adaptive explorations on R01–R04, inspect all current tools, score outputs, count recovery attempts/fallbacks, and classify findings. Acceptance: actual saved outputs and per-question judgments; transport boundary explicit; temporary scenarios cleaned up. See C-001–C-006 and the findings table in `results.md`.
- [x] **D — Select evidence-backed changes.** Two candidates are frozen below: E1 source metadata-origin completeness, then E2 exact assembly member handoff. No new public tool. See D-001 and the decision ledger in `results.md`; do not replace a rejected candidate.
- [ ] **E — Implement and verify selected slices.** Execute the children sequentially with one writer. Each child includes failing regression where feasible, implementation, affected docs/consumers, appropriate official gates, independent review, and a verified local commit. Parent remains open until all children pass. Insert stall diagnosis before the next feature slice whenever required.
- [ ] **F — Paired replay and fresh holdouts.** Re-run frozen baseline tasks and use a fresh explorer for holdouts; compare per-question correctness, cost, output, and usability across all targets. Acceptance: benefits supported by comparable measurements; regressions resolved or changes rejected; no hidden fallback or partial load.
- [ ] **G — Final acceptance.** Independent review of boundaries, privacy, tool coherence, docs, risk-selected test evidence, remaining coverage gaps, final export safeguard/manifests, scenario cleanup, and external working-tree changes. No full-suite or transport gate. Reuse still-valid checks under the protocol. Acceptance: completion criteria in the charter met and sanitized final report committed. Otherwise retain open in-scope blockers and report partial status.

### Frozen E children

- [x] **E1 — Bound source metadata-origin claims to loaded references.** Finding F-R03-02. Neutral missing-HintPath regression failed before the fix; the existing tool now labels source metadata searches as loaded-reference scope with unknown declared coverage and recovery, while direct source ownership remains precise. Existing owner/candidate/inventory fields remain; no new tool or input. Official build, two exact transport-free handler tests, and three focused Core tests passed; independent review passed with no correction attempt. Replay remains at F. Evidence `round1/E1/`, `round1/review-E1/`; commit `3b9b4ec`.
- [ ] **E2 — Restore exact assembly member handoff when uniquely resolvable.** Finding CSELF-02; owner: Core exact assembly symbol resolver, corresponding transport-free tests and symbol/assembly navigation docs. First construct a neutral managed fixture with distinct overload bodies and degraded semantic binding that reproduces an ID accepted at discovery but not at direct body lookup. If this cannot reproduce the defect, investigate within this candidate's bound and reject an unsupported resolver change. Permit an exact, unique declaration-ID fallback within the supplied assembly owner only; retain owner checks, canonical encoding and ambiguity failures. Do not rewrite signatures, guess overloads, expand reference closure, or add a public tool. Acceptance: direct method handoff retrieves the selected body; wrong owner, missing and colliding declarations still fail precisely; partial relationship analysis remains visible. Build with `pwsh -File ./scripts/build.ps1`; start with one exact `ExactAssemblySymbolResolverTests` case through `pwsh -File ./scripts/test-fast.ps1 -Filter '<exact case>'`, then only a risk-justified transport-free assembly handler case through `scripts/test-integration.ps1 -Filter '<exact case>'` if needed. Record actual filters and durations; select under five minutes each. Independent review required. Replay fixed A01/Q025–Q027 with the same binary snapshot and budgets; cross-check source-target owner behavior. Correction limit: two focused attempts after first review. Result/commit pending.

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

- Last completed slice: E1 — source metadata-origin completeness, independently reviewed and committed (`3b9b4ec`).
- Active assignment: E2 — exact assembly member handoff.
- Next action: create a neutral failing assembly member-reference regression before any resolver change.
- Required pending user decision: none. R03 uses an isolated local evaluation copy with supplied references; unknown framework contexts and partial assembly relationships remain explicit limits.
- Evidence location: ignored `temp/external-repos/mcp-usage-audit/round1/` and the calibration raw run referenced in `results.md`.

On interruption update this section with neutral IDs, active owned processes, completed gates, pending checks, local handoff location, and the exact next action. Never mark an interrupted or unrun gate as passed.
