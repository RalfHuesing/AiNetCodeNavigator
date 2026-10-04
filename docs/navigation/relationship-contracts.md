# Cross-Feature Relationship Contract

The Core relationship engines operate on Roslyn symbols from the same solution snapshot. A source reference emitted by one engine can be resolved with `SourceSymbolResolver` and the resulting symbol passed to another engine. References retain project identity when different projects contain same-named types or members. See [Shared Symbol Resolution](symbol-resolution.md) for the reference contract.

`FindReferencesResolver` is the shared bounded breadth-first caller traversal for references, with direct sites at depth 1 and each caller expansion advancing one level. Reference entries retain reached-from provenance, source column, and the same evidence kind, so self-recursive sites and repeated calls remain aligned. Evidence kinds are `call`, `memberAccess`, `staticVirtualOrInterfaceTarget`, `possibleTarget`, and `unresolved`; these describe source binding evidence and do not claim runtime dispatch. Bound virtual/interface invocations use `staticVirtualOrInterfaceTarget`; non-call method-group and property references use `memberAccess`. Possible-target sites retain their candidate names in references and graphs. Optional [reference summaries](reference-summary.md) report discovered direct/deeper site counts and owner-qualified project/file identities.

Relationship site identity also retains the owning project internally. Linked source documents shared by same-named projects remain distinct even when a declaration has no stable reference.

`CallTreeBuilder` follows bounded caller/callee relationships in either direction. It retains direct and mutual recursive edges, while visited symbols stop further expansion around a cycle. Call sites on a shared edge preserve file, line, column, and evidence kind. A binding with multiple candidates is emitted as a possible-target site with its candidates; an unbound location is marked unresolved and never becomes a guessed graph edge. Graph nodes carry stable references when representable. Assembly relationship routes use the same source projection over their decompiled owners and preserve the evidence fields when owner graphs merge.

`FindReferencesResolver.FindImplementationsAsync` reports interface member implementations and virtual or abstract overrides. `TypeHierarchyScanner` reports source base/interface entries and transitive derived or implementing types across project references. Their source entries carry the same project-bound reference contract.

Each payload keeps its own result limits and reports them in its own terms:

- Call trees report separate node, edge, edge-site and candidate/unresolved site counts; repeated sites on one edge do not add nodes. They set `Truncated`, `HiddenEdgeCount`, and `PendingNodeCount` for fan-out and node bounds.
- References set display and node truncation fields and mark `IsComplete` false when a requested traversal is cut short. Reference summaries count discovered direct and deeper sites before display truncation.
- Implementation results and type hierarchies report pre-limit `TotalCount` values and `IsTruncated` for their displayed implementation or subtype lists.

The Core APIs take resolved symbols rather than reference strings. Identifier ambiguity and invalid reference errors are handled by shared symbol resolution before a relationship engine runs.

The public `resolve_type_origin` handler accepts one non-empty `symbolIdentifier` or `typeName`. In source mode it preserves the exact source-owning project and all declaration locations; if no source declaration matches, it searches resolved metadata references and reports unique or ambiguous origins. Assembly mode resolves symbol references only in the supplied target. See [Resolve Type Origin](resolve-type-origin.md) for the result shape and current evidence.
