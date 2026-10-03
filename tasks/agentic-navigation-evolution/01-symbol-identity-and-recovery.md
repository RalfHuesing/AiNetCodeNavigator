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

The user has approved replacing the current ephemeral handle semantics and explicitly requires complete removal of the Base62 `h:` handle mechanism. Stable, self-describing symbol references are the selected direction, accepting additional tokens for reliable navigation after edits and restarts. Do not retain `h:` handles as an optional mode, compatibility route or second public navigation identity. They have also approved a readable string composed of the source project path and Roslyn declaration ID, with the explicit constraint that the path is relative to the repository, not absolute. Snapshot identity remains separate. Production implementation is pending; exact grammar, repository-base selection and remaining identity boundaries are open.

## Assessment

The find/edit/follow-up loop is a central use case, so requiring rediscovery after unrelated edits is a significant usability problem. The short prefix and token count do not cause the instability: snapshot-bound identity and process-local indirection do. Merely exposing the existing long internal identifier would preserve snapshot invalidation.

Separate two questions:

1. Which declaration does the caller want to navigate now?
2. Against which analysis version was this particular body, relationship or page produced?

Stable declaration selection should survive body edits, unrelated edits and server restarts when owner and declaration identity remain the same. Snapshot identity should continue to bind analysis evidence and cursors. An explicit expected-snapshot constraint could express strict consistency when needed.

## Approved direction and remaining contract design

Use one primary public symbol reference that can be resolved without a process-local handle registry. For source, its conceptual identity is the selected target context plus a repository-relative owning project path and a Roslyn declaration ID. Return analysis snapshot information separately. Target-path repetition can be avoided where the request or response already establishes the owner, but cross-assembly results must preserve the actual owner.

The user has selected a readable string carrying the repository-relative owning-project path and the Roslyn declaration ID for source. Preserve string-based symbolIdentifier inputs. The selected target remains request context and assembly follow-ups retain their actual owner target. This decision does not change the existing absolute targetPath request parameter; it forbids absolute paths embedded in the proposed symbol ID. Examples discussed in chat describe conceptual identity and are not existing accepted input syntax.

Define the string grammar, escaping and canonicalization before implementation. Source project paths must distinguish equally named projects and be relative to the repository root, including when the selected solution is nested below that root. The user has approved Git-root selection with a solution-directory fallback for non-Git targets, independently of the caller's current directory. Define remaining behavior for projects outside that base, multiple framework contexts and declarations without DocCommentIds. The approved assembly counterpart uses assembly name and declaration ID; do not invent a source project owner for a binary. Keep analysis content hashes outside the reusable declaration identity. The readable-string and repository-relative path decisions are approved.

Keeping the `h:` spelling or adding a stable reference alongside the ephemeral handle was considered earlier and is now rejected by the user's decision. Removal must cover producers, consumers, registry/counter/alphabet infrastructure, runtime/configuration wiring, tests and documentation where they exist solely for this mechanism. Review shared uses before deleting any component; preserve independent snapshot and cursor consistency behavior.

Bare DocCommentIds are insufficient for exact ownership. File/line identifiers are not durable under inserted lines. A content hash changes on edits; a stateless hash of identity alone cannot resolve itself without an index or embedded identity. Random GUIDs require durable state or source annotations, which conflict with the server's read-only boundary if inserted into analyzed source.

## Approved rename and signature-change behavior

The user has approved that a declaration whose semantic identity changes receive a new symbol ID. Resolve the old ID exactly against current source; if its declaration no longer exists, return a clear not-found result with an action to rediscover the intended declaration. Do not automatically redirect based on similar names, file positions or bodies. Body edits and unrelated edits preserve unchanged declaration IDs. This does not create a guarantee of persistent entity identity through arbitrary refactorings, nor does it prove continuity if a declaration is deleted and recreated under the same exact identity. Implementation is pending.

## Approved stable assembly reference

The user has approved a readable assembly simple name plus Roslyn declaration ID, with the exact owner DLL retained as separate target context in follow-up requests and owner fields. Assemblies lack a source project path, so this is the counterpart to the approved source representation. Do not embed an absolute path, binary content hash, generation, MVID or assembly version in the reusable declaration reference. Current analysis must still validate the selected owner and use current binary/reference contents; unchanged declared API can resolve after reopening or rebuilding, while a missing declaration receives the approved exact-resolution error.

Implementation is pending. A simple assembly name does not select one physical binary by itself: equal names must remain disambiguated through the selected exact owner target, including cross-assembly hopping. Never resolve a reference to the first matching name across loaded owners. Define assembly-reference grammar and same-name/wrong-owner behavior before implementation. Local-function/decompiler identity limitations remain separate contract work.

## Approved base for relative source project paths

The user has approved resolving the Git repository root that contains the selected source solution, including worktree .git files. When the target has no Git repository, use the selected solution's directory as the base so extracted/archive source targets remain supported. Both choices are deterministic from the target and independent of process current directory. Nested solutions in a Git repository use the repository root, not the solution directory. Implementation is pending. Define normalized relative paths and explicit handling for project owners outside the chosen base; avoid embedding absolute paths or silently selecting a different owner.

## Stability boundaries and open decisions

- Renaming a declaration, moving it between owners, or changing a signature can change its semantic declaration identity. Body-edit stability must not be presented as universal refactoring stability.
- Approved: no automatic rename/signature tracking or similarity-based reassignment. A changed declaration uses its new ID; an absent exact declaration returns an actionable failure.
- Define source owner/configuration identity, including duplicate namespaces/types, overloaded and generic members, linked files and loaded framework contexts.
- Approved: assembly name plus declaration ID within the exact selected DLL owner; unchanged declared API can resolve against the current rebuilt binary. Missing exact declarations return an actionable failure.
- Define supported declarations without a canonical DocCommentId, including local functions; existing location fallbacks are not durable under arbitrary edits.
- Decide whether users need explicit expected-snapshot checks on navigation requests.
- Enumerate all producers and consumers, including graph nodes, body batches, context sections, ambiguity choices and assembly hopping, before changing the public contract.

## Proposed acceptance evidence

Discovery followed by body edit, unrelated source edit and server restart must reach the intended unchanged declaration through the returned public reference. Duplicate project declarations must remain distinct. Deleted or renamed declarations must never silently resolve to another symbol. Pages and cursors must preserve their own snapshot consistency. Existing strict `h:` behavior remains current until a new contract is implemented and verified.
