# MCP server completion

This task specifies a functional, read-only C# navigation MCP server with twenty navigation tools and two maintenance tools. Completion takes priority: reuse correct components, fix required behavior, focus per-commit integration gates, simplify Git fixtures, and keep evidence and documentation concise. Additional refactoring requires a bounded audit-defined task within the existing correction limit.

- [Concept](Konzept.md): intent, scope, implementation constraints, agent models, and completion criteria.
- [Server contracts](konzept/01-server.md): required navigation, runtime, ownership, budgets, recovery, and read-only behavior.
- [Verification and acceptance](konzept/02-verifikation.md): contract cases, component tests, required gates, documentation, and explicit E2E exclusion.

These three concept documents contain the complete specification. The concept is a draft. Review and approval, roadmap creation, and implementation require their respective workflow requests.
