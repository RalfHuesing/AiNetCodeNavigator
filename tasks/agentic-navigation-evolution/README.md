# Agentic navigation evolution

This directory is the implementation basis and maintained knowledge store for the improvements approved on 2026-10-03. The user authorized consolidation and a sequential roadmap; production implementation has not started.

Start at [roadmap.md](roadmap.md). It owns execution order, status and evidence. The four specifications below own their contracts; the roadmap links to them instead of duplicating them.

| Specification | Authoritative subject |
| --- | --- |
| [01 — Symbol references](01-symbol-identity-and-recovery.md) | Wire grammar, exact ownership, resolution, errors and complete handle removal |
| [02 — Dependencies](02-dependency-graph-analysis.md) | Single collection, projection, coverage and targeted outgoing traversal |
| [03 — Long operations](03-long-running-operations-and-transport.md) | Response windows, progress, polling, cancellation and transport evidence |
| [04 — Snapshots and caches](04-snapshot-refresh-and-analysis-cache.md) | Freshness, identity memoization, dependency retention and invalidation |

## Authority and scope

These are planned acceptance requirements, not evidence of current behavior. All four specifications and the roadmap are binding together. Repository rules in [.agents/rules](../../.agents/rules/README.md) continue to apply. Invoking an orchestrator later does not implicitly invoke optional steps in [.agents/agent-workflow](../../.agents/agent-workflow/README.md).

The selected scope is complete removal of Base62 `h:` navigation handles; readable source and assembly declaration references; dependency collection once, bounded reuse and targeted outgoing analysis; source identity once per immutable snapshot; and reliable short status polls with real progress. Existing output presentation and tool purposes remain unchanged.

Navigation remains read-only. No source annotations, refactoring, linting, persistent symbol registry, alternate handle mode, native MCP task subsystem, new explicit cancellation tool, new expected-snapshot argument, cross-snapshot incremental cache or separate incoming-edge index is included. The existing broad graph supplies incoming/both analysis. No new configuration options or dependencies are required by this plan.

## Evidence boundary

The [360-degree audit](../../temp/mcp-test-360/summary_findings.md) and raw calls under `temp/mcp-test-360` motivated the work. They establish stale-handle observations and a long dependency analysis followed by an EOF/timeout description, not its cause or a measured speedup.

The contracts were reconciled with local source at HEAD `1d18f0bcd38d752146bc3d03bc96b1ab47b721a9`. Earlier inspection began at `a670e57d02654c04c833c61fcc15794f8eac7c1f`. A live discovery/poll completed during consolidation and still emitted `h:` handles; the live process was not independently matched to either commit. Source links identify implementation entry points, not proof that this plan is implemented.

The subsequent [Luna concept review](reviews/luna-concept-review.md) records independent comprehension checks, corrected findings and re-review outcomes. It is documentation evidence; implementation completion still requires the roadmap's executed gates and deliberate orchestrator checkmarks.

## Maintenance

Keep each requirement in its owning specification. Record implementation status, commits, executed checks, measurements and concrete blockers in the roadmap. Update current-state `docs/` and affected agent navigation rules only in the implementation commits that establish and verify their new behavior. Keep chat updates short.
