# Find References and Implementations Core Engines

`FindReferencesResolver` uses Roslyn's solution-wide symbol reference search. Each source location includes its relative file path, one-based line and column, source snippet, project name, and enclosing symbol name. When a canonical source identity is available, the enclosing symbol includes an opaque `h:` handoff that resolves back to the declaration in the correct project.

`FindImplementationsResolver` behavior is provided by `FindReferencesResolver.FindImplementationsAsync`. For interface types it returns implementing types across the solution; for classes it returns direct and indirect derived types. For interface methods it returns implementing members, and for virtual, abstract, or override methods it returns overrides. Entries include a source location, signature, project name, and an opaque source handoff when available.

Both resolvers sort results deterministically and normalize `maxResults` to at least one. `TotalCount` reports the full number of results before limiting. `FindReferencesResult.IsTruncated` and `FindImplementationsResult.IsTruncated` indicate when results were limited. Null symbols and solutions throw `ArgumentNullException`; cancellation is passed through Roslyn searches and checked while collecting locations.

These Core APIs take an already-resolved Roslyn symbol, so unknown names, ambiguous identifiers, and invalid or stale handoffs are reported by the shared symbol-resolution layer before calling them. MCP transport formatting and public tool errors are covered by later tool-integration work.
