# Product decisions and unresolved choices

This file is the continuation point for decisions during subsequent chats. Recommendation does not mean approval.

## Confirmed user constraints

| ID | Decision | Authority/consequence |
| --- | --- | --- |
| D-001 | Review independently; the information-efficiency premise is a hypothesis. | User request, 2026-10-07. Alternative product cuts remain open. |
| D-002 | No production code changes in this pass. | User request. Only review artifacts are written under `tasks/ideen/`. |
| D-003 | Persist evidence, ideas and later decisions here. | User request. Keep current-state docs separate from proposals. |
| D-004 | Cover technical C# environment, including net48, net10, Blazor and WPF; business domain knowledge is separate. | User scope. Runtime completeness must not be implied. |

## Reviewer proposals — not yet approved

| ID | Proposed decision | Next evidence needed |
| --- | --- | --- |
| P-001 | Position Navigator as a read-only technical evidence service for preparing changes. | Task comparison versus shell/file tools; source-backend comparator if worthwhile. |
| P-002 | Retain all 12 current tools and the separate exporter initially. Consolidate internals and delivery, not all query intents. | Tool-use/cost traces and failure analysis. |
| P-003 | Prioritize environment coverage and simplified result delivery over adding further graph types. | Representative net48/multi-TFM/Razor/WPF cases; page usability comparison. |
| P-004 | Pilot bounded change-context composition using existing engines. | Measurable workflow benefit without increased omissions or overfetch. |
| P-005 | Keep builds/tests/edits external; consider links to verified artifacts later. | Concrete verification workflow and snapshot/configuration provenance requirements. |

## Open product decisions

1. Which success matters most initially: legacy-solution reliability, unfamiliar-library investigation, or preparing cross-project changes? Reviewer default: prioritize framework/environment reliability, then change preparation; keep assembly investigation as a differentiator.
2. Is the supported truth disk state, IDE unsaved buffers, or both? Current inspection refreshes disk-backed source. Reviewer default: document disk state; add an IDE adapter only for a demonstrated requirement.
3. Which exact net48/project systems/toolchains must be supported? Need at least one representative old-style solution, one SDK-style/multi-TFM case, a Blazor component and WPF view. No universal legacy-support promise before fixtures.
4. Should clients support a new response/continuation contract, or must all existing clients work unchanged? Reviewer default: additive prototype/compatibility path, measured adoption; verify native MCP Tasks support per client.
5. How much uncertainty is acceptable? Reviewer default: resolved facts, attributed heuristic candidates and unknown/unanalysed areas remain distinguishable; no confidence percentage masquerading as correctness.
6. What cold/warm latency and memory budgets are acceptable on the user's machine and solutions? Reviewer default: collect distributions first; set explicit budgets before optimizing caches or adding selective loading.
7. Does a current-snapshot change packet suffice, or is baseline/deleted-symbol impact essential? Reviewer default: current snapshot and explicit missing baseline evidence first; historical semantics are a separate feature.

## Subsequent user direction — 2026-10-07

The user selected four bounded improvements for an implementation roadmap: discoverable existing tool semantics, explicit skeleton scope, prominent framework-coverage summaries, and shared simple symbol-name matching. Their sole executable specification is [Navigation Contract Clarity](../../navigation-contract-clarity/roadmap.md). The user requested `gpt-6.1-sol` with reasoning `medium` for implementation and audit, an overall closing audit with finding/correction rounds, and a commit of the planning artifacts. This request creates the implementation basis; production implementation has not started. Broader review proposals remain unapproved.

## Resumption procedure

Read this file, the [assessment](review.md), and [experiment plan](experiment.md). Ask only for input needed for the next chosen slice. Record each explicit user decision here with date, scope, rationale and superseded proposal. Do not convert the review into an implementation backlog merely because recommendations exist.
