# Cross-Feature Relationship Contract

The Core relationship engines operate on Roslyn symbols from the same solution snapshot. A source `h:` handoff emitted by one engine can be resolved with `SourceSymbolResolver` and the resulting symbol passed to another engine. Handoffs retain project identity when different projects contain same-named types or members.

`FindReferencesResolver` is the shared bounded breadth-first caller traversal for references and impact, with direct sites at depth 1 and each caller expansion advancing one level. Both projections retain reached-from provenance, source column, and the same evidence kind, so self-recursive sites and repeated calls remain aligned. Evidence kinds are `call`, `memberAccess`, `staticVirtualOrInterfaceTarget`, `possibleTarget`, and `unresolved`; these describe source binding evidence and do not claim runtime dispatch. Impact additionally reports direct and transitive counts and affected project/file summaries.

Relationship site identity also retains the owning project internally. Linked source documents shared by same-named projects remain distinct even when a caller cannot receive a handoff identifier.

`CallTreeBuilder` follows bounded caller/callee relationships in either direction. It retains direct and mutual recursive edges, while visited symbols stop further expansion around a cycle. Call sites on a shared edge preserve file, line, column, and evidence kind. A binding with multiple candidates is emitted as a possible-target site with its candidates; an unbound location is marked unresolved and never becomes a guessed graph edge. Graph nodes carry resolvable handoffs. Assembly relationship routes use the same source projection over their decompiled owners and preserve the evidence fields when owner graphs merge.

`FindReferencesResolver.FindImplementationsAsync` reports interface member implementations and virtual or abstract overrides. `TypeHierarchyScanner` reports source base/interface entries and transitive derived or implementing types across project references. Their source entries carry the same project-bound handoff contract.

Each payload keeps its own result limits and reports them in its own terms:

- Call trees set `Truncated`, `HiddenEdgeCount`, and `PendingNodeCount` for fan-out and node bounds.
- References and impact set display and node truncation fields; both mark `IsComplete` false when a requested traversal is cut short. Impact counts discovered direct and transitive sites before display truncation.
- Implementation results and type hierarchies report pre-limit `TotalCount` values and `IsTruncated` for their displayed implementation or subtype lists.

The Core APIs take resolved symbols rather than handoff strings. Identifier ambiguity and stale or invalid handoff errors are handled by shared symbol resolution before a relationship engine runs.

The public `resolve_type_origin` handler accepts one non-empty `symbolIdentifier` or `typeName`. In source mode it preserves the exact source-owning project and all declaration locations; if no source declaration matches, it searches resolved metadata references and reports unique or ambiguous origins. Assembly mode validates that symbol handoffs belong to the requested binary. See [Resolve Type Origin](resolve-type-origin.md) for the result shape and current evidence.
