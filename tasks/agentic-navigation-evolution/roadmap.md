# Sequential implementation roadmap

Status: implementation execution started on 2026-10-03 by the explicitly invoked orchestrator. R01 is in progress; no point is complete. Specifications were consolidated on 2026-10-03 from the approved discussion.

Read the [shared execution contract, completion rules and verification policy](roadmap/execution.md) before starting any point. Then read that point's file and linked specifications. The orchestrator executes the points in order.

## Progress index

- [ ] R01 — [Reference primitives and exact resolvers](roadmap/R01-reference-primitives.md)
- [ ] R02 — [Switch every public route and remove handles](roadmap/R02-public-reference-migration.md)
- [ ] R03 — [Fresh snapshot identity once](roadmap/R03-snapshot-identity.md)
- [ ] R04 — [Collect each document batch once](roadmap/R04-single-collection.md)
- [ ] R05 — [Bounded dependency reuse](roadmap/R05-dependency-cache.md)
- [ ] R06 — [Targeted outgoing traversal](roadmap/R06-targeted-outgoing.md)
- [ ] R07 — [Short polls, progress and transport diagnosis](roadmap/R07-polling-and-transport.md)
- [ ] R08 — [Final gates and completed handoff](roadmap/R08-final-verification.md)

These eight checkboxes are the authoritative point completion state. Only the orchestrator changes them after reviewing every acceptance checkbox and the actual evidence in that point's file. All remain unchecked; see R01 for current execution evidence.

Each point file owns its scope, acceptance, verification, four detailed checkboxes and execution evidence. Record working state, implementation commits, executed commands/results, measurements and blockers there. The index remains a compact overview.

## Document-maintenance history

| Point | Commit(s) | Executed verification | Result / blocker |
| --- | --- | --- | --- |
| Specification consolidation | `033ac7a` | Six documents reviewed; 53 local links/anchors and R01–R08 order checked; diff whitespace check; no production gate | Documentation checks passed; no production implementation started |
| Orchestrator checklists and concept review | `7fe4fa5` | Three Luna/high read-only concept reviews and re-reviews; [finding disposition](reviews/luna-concept-review.md); seven documents reviewed; local links/anchors, R01–R08 order, 40 unchecked boxes and diff whitespace checked | All three re-reviews PASS; documentation checks passed; production gates not run |
| Roadmap split | See Git history for the commit adding this row | Point/checklist content preserved; links, 40 unchecked boxes and diff whitespace checked | Documentation-only restructure; implementation remains pending |
