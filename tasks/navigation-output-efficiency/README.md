# Navigation output efficiency

## Goal and decisions

Navigation responses must preserve usable evidence and follow-up coordinates while spending fewer tokens on presentation and repeated data. The local round-two output evaluation is a hypothesis source, not an implemented contract or a measured baseline.

- Use one compact JSON representation with structural newlines. Keep existing field names, values, meaningful false/zero values, and empty result collections. Newlines remain available to the response pager; do not turn a large response into one indivisible line. Public result-cursor registration must preserve this representation.
- Keep snapshot identity, normalized analyzed scope, and analysis completeness. Analysis completeness differs from outer response delivery completeness. Omit only the negative textual `resultContinuation=none` and `omissions=none` lines; absence means no known result continuation and no analysis omissions. Keep positive continuation and all omission reasons. Typed analysis metadata retains its existing shape.
- In `find_symbol`, omit `locations` only when its sole location exactly duplicates the primary location. Retain every site for multiple declarations and any nonidentical singleton. Keep documentation IDs, stable references, and exact per-entry owner paths: descriptive identity and executable routing remain useful, especially across referenced assemblies.
- In successful body items, remove the completed-window no-action sentence. When the selector equals its handoff exactly, print the handoff once; otherwise preserve both. Keep availability, content mode, line bounds, partial-window instructions, mixed failures, and recovery. Surface a nonempty body hint.
- For assembly class structure, anchor file paths to the absolute decompiled source root from the same acquired generation. Return that root on every domain page and root-relative file names that resolve to physical cached files. State the root once in assembly body batches and file skeletons as well. Preserve source-target path conventions and exact binary owner coordinates.
- Do not add a compact-mode switch, owner table, alias schema, blanket default suppression, or dependencies. Existing JSON serialization and token counting infrastructure suffice. Inventory flags and optional signature collections retain explicit semantics; deleting them globally would blur unavailable, absent, and unrequested information.

## Acceptance and verification

1. Record failing regressions before production fixes for cursor-format inflation and assembly class path anchoring; cover the other observable changes with focused contracts.
2. Check both single and multiple declaration locations, stable and raw body selectors, complete and partial bodies, positive analysis omissions, domain cursors, outer pages, exact budget recovery, and reconstructible assembly file paths.
3. Measure identical generic fixture responses with the existing `cl100k_base` counter and UTF-8 byte accounting. Report observed savings, without extrapolating the evaluation's percentage claim to all tools.
4. Run the official build, affected Fast/Integration selections, relevant extended contracts, and the complete eligible routine suite. Obtain an independent final review and correct concrete findings before committing. Do not release or deploy.

## Evidence

- The class-structure regression failed on the missing physical source root before the fix. The cursor-format regression failed because registration changed structural newline JSON into indented JSON. The singleton-location contract also failed before its projection changed. Local RED logs and TRX files are retained under ignored `temp/output-efficiency-proof/`.
- A generic 50-match source query measured 9,086 tokens for the reconstructed prior representation and 5,659 for the actual compact response: 3,427 tokens saved (37.7%). The routine run measured 9,288 versus 5,861 tokens (36.9%), and 36,421 versus 19,109 UTF-8 bytes; fixture paths and snapshot aliases vary between runs. Each comparison uses the same snapshot, selectors, declaration values, and complete visible response; it restores the prior pretty JSON, duplicated singleton locations, and negative header lines for its baseline. This is one representative fixture, not an estimate for all tools or a replay of historical traffic.
- The affected response-processing Fast selection passed all 79 tests. Assembly contracts verify the absolute generation root, physically reconstructible file paths on each member page, exact owner follow-ups, body/skeleton root visibility, and independent domain/outer paging. Source contracts preserve multiple declaration sites, raw selectors, unavailable-body explanations, partial windows, and mixed failure recovery.
- The official build passed with zero warnings/errors. The complete eligible routine gate passed 688 Fast and 104 Integration tests; its standard E2E/Extended exclusions remain in place. The two existing extended workspace-loading cases are unrelated to this response-formatting change. The independent final review found no actionable defects. No release or deployment was performed.
