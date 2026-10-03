# Symbol identity and recovery after edits

## Verified current behavior

- [HandoffHandleRegistry](../../src/AiNetCodeNavigator.Core/Symbols/HandoffHandleRegistry.cs) maps short public `h:` values to internal identifiers in process-local dictionaries. Host disposal clears the mappings. A persisted counter does not persist the mappings; a server restart can produce `HANDOFF_UNKNOWN` for previously issued handles.
- [SymbolHandoffIdentifier](../../src/AiNetCodeNavigator.Core/Symbols/SymbolHandoffIdentifier.cs) includes origin, target token, snapshot content token and declaration identity. Source declaration identities also contain a project marker.
- [SourceHandoffResolver](../../src/AiNetCodeNavigator.Core/Symbols/SourceHandoffResolver.cs) rejects a handle when its content token differs from the current solution content token, before resolving the declaration. A change to another loaded source document can invalidate a handle for an unchanged symbol.
- [AnalysisSymbolIdentity](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs) includes the loaded solution's document contents and project/reference context in source identity. Analysis filters do not restrict this identity to the queried symbol.
- [SourceSymbolResolver](../../src/AiNetCodeNavigator.Core/Symbols/SourceSymbolResolver.cs) can resolve raw documentation comment IDs against current source compilations. Equal declaration IDs across projects can return ambiguity candidates. Names, signatures and source positions are also supported, with their own ambiguity and edit-sensitivity limitations.
- The audit's stale class-structure request failed, while its subsequent DocCommentId request succeeded. During this discussion's initial analysis, a discovered handle also failed with `STALE_SNAPSHOT`; a method body was subsequently resolved through its documentation ID. Neither observation alone identifies the intervening file change.
- [NavigationToolSupport.Failure](../../src/AiNetCodeNavigator/Mcp/Tools/NavigationToolSupport.cs) currently uses a generic recovery message when no specific hint is supplied.

## User decision, 2026-10-03

The user has approved replacing the current ephemeral handle semantics and explicitly requires complete removal of the Base62 `h:` handle mechanism. Stable, self-describing symbol references are the selected direction, accepting additional tokens for reliable navigation after edits and restarts. Do not retain `h:` handles as an optional mode, compatibility route or second public navigation identity. This is an approved design decision; production implementation is pending and the exact replacement contract remains open.

## Assessment

The find/edit/follow-up loop is a central use case, so requiring rediscovery after unrelated edits is a significant usability problem. The short prefix and token count do not cause the instability: snapshot-bound identity and process-local indirection do. Merely exposing the existing long internal identifier would preserve snapshot invalidation.

Separate two questions:

1. Which declaration does the caller want to navigate now?
2. Against which analysis version was this particular body, relationship or page produced?

Stable declaration selection should survive body edits, unrelated edits and server restarts when owner and declaration identity remain the same. Snapshot identity should continue to bind analysis evidence and cursors. An explicit expected-snapshot constraint could express strict consistency when needed.

## Approved direction and remaining contract design

Prefer one primary public symbol reference that can be resolved without a process-local handle registry. For source, its conceptual identity is the selected solution plus an owning project selector and a Roslyn declaration ID. Return analysis snapshot information separately. Target-path repetition can be avoided where the request or response already establishes the owner, but cross-assembly results must preserve the actual owner.

The wire representation is undecided: a string with an explicitly defined escaping grammar, a typed selector object, or another self-contained representation. Examples discussed in chat describe this conceptual identity and are not existing accepted input syntax.

Keeping the `h:` spelling or adding a stable reference alongside the ephemeral handle was considered earlier and is now rejected by the user's decision. Removal must cover producers, consumers, registry/counter/alphabet infrastructure, runtime/configuration wiring, tests and documentation where they exist solely for this mechanism. Review shared uses before deleting any component; preserve independent snapshot and cursor consistency behavior.

Bare DocCommentIds are insufficient for exact ownership. File/line identifiers are not durable under inserted lines. A content hash changes on edits; a stateless hash of identity alone cannot resolve itself without an index or embedded identity. Random GUIDs require durable state or source annotations, which conflict with the server's read-only boundary if inserted into analyzed source.

## Stability boundaries and open decisions

- Renaming a declaration, moving it between owners, or changing a signature can change its semantic declaration identity. Body-edit stability must not be presented as universal refactoring stability.
- Decide whether rename/signature-change tracking is required. If so, define explicit reconciliation with ambiguity and deletion results; do not silently select a similar declaration.
- Define source owner/configuration identity, including duplicate namespaces/types, overloaded and generic members, linked files and loaded framework contexts.
- Define assembly owner selection and behavior when a binary is replaced with the same or a changed API.
- Define supported declarations without a canonical DocCommentId, including local functions; existing location fallbacks are not durable under arbitrary edits.
- Decide whether users need explicit expected-snapshot checks on navigation requests.
- Enumerate all producers and consumers, including graph nodes, body batches, context sections, ambiguity choices and assembly hopping, before changing the public contract.

## Proposed acceptance evidence

Discovery followed by body edit, unrelated source edit and server restart must reach the intended unchanged declaration through the returned public reference. Duplicate project declarations must remain distinct. Deleted or renamed declarations must never silently resolve to another symbol. Pages and cursors must preserve their own snapshot consistency. Existing strict `h:` behavior remains current until a new contract is implemented and verified.

Optional fixed analysis sessions are recorded in [topic 06](06-analysis-sessions.md).
