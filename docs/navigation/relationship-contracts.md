# Cross-Feature Relationship Contract

The Core relationship engines operate on Roslyn symbols from the same solution snapshot. A source `h:` handoff emitted by one engine can be resolved with `SourceSymbolResolver` and the resulting symbol passed to another engine. Handoffs retain project identity when different projects contain same-named types or members.

`FindReferencesResolver` and `ImpactAnalyzer` use breadth-first caller traversal with the same depth convention: direct call sites are depth 1, and each caller expansion advances one level. Both retain reached-from symbol provenance in site identity. If a virtual call site can reach multiple source overrides, the same file and line can appear once per reached-from symbol. Both APIs use stable project, path, caller, and reached-from ordering, so matching results remain aligned. Impact additionally reports direct and transitive counts and affected project/file summaries.

`CallTreeBuilder` can follow the same source chain in either direction. Outgoing traversal reads block-bodied and expression-bodied members; incoming traversal groups source references by caller. Graph nodes carry resolvable handoffs, and call sites between the same pair of nodes are combined.

`FindReferencesResolver.FindImplementationsAsync` reports interface member implementations and virtual or abstract overrides. `TypeHierarchyScanner` reports source base/interface entries and transitive derived or implementing types across project references. Their source entries carry the same project-bound handoff contract.

Each payload keeps its own result limits and reports them in its own terms:

- Call trees set `Truncated`, `HiddenEdgeCount`, and `PendingNodeCount` for fan-out and node bounds.
- References and impact set display and node truncation fields; both mark `IsComplete` false when a requested traversal is cut short. Impact counts discovered direct and transitive sites before display truncation.
- Implementation results and type hierarchies report pre-limit `TotalCount` values and `IsTruncated` for their displayed implementation or subtype lists.

The Core APIs take resolved symbols rather than handoff strings. Identifier ambiguity and stale or invalid handoff errors are handled by shared symbol resolution before a relationship engine runs.
