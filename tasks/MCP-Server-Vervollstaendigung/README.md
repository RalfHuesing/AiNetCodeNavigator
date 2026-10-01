# MCP server completion

This task specifies a functional, read-only C# navigation MCP server with twenty navigation tools and two maintenance tools. Completion takes priority: reuse correct components, fix required behavior, focus per-commit integration gates, simplify Git fixtures, and keep evidence and documentation concise. Additional refactoring requires a bounded audit-defined task within the existing correction limit.

- [Concept](Konzept.md): intent, scope, implementation constraints, agent models, and completion criteria.
- [Server contracts](konzept/01-server.md): required navigation, runtime, ownership, budgets, recovery, and read-only behavior.
- [Verification and acceptance](konzept/02-verifikation.md): contract cases, component tests, required gates, documentation, and explicit E2E exclusion.
- [Implementation roadmap](roadmap.md): seven sequential implementation tasks followed by one independent completion audit with a bounded correction.

These three concept documents contain the complete specification. The user has approved the concept (`ready`), and the implementation roadmap is available. Implementation requires a separate workflow request.
