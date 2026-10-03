# Stable symbol references

Status: specified for implementation. Execution: [R01 and R02](roadmap.md#r01--reference-primitives-and-exact-resolvers).

## Required behavior

An unchanged declaration can be followed after body edits, unrelated edits, project-option changes and server restart, provided its owner coordinate and Roslyn declaration ID still identify exactly one supported declaration in the selected current target. Snapshot evidence is separate from declaration selection.

Renames, signature changes that alter the Roslyn ID, and moves between projects produce a different reference. An absent old declaration fails explicitly; never infer a replacement from names, locations, similar bodies or earlier registry state. Deleting and recreating the same exact declaration identity is indistinguishable from the original; this contract deliberately identifies declarations, not historical entities.

## Public string contract

| Origin | Canonical form | Example |
| --- | --- | --- |
| Source | `src:<project-path>\|<declaration-id>` | `src:src/App/App.csproj\|M:App.Service.Run(System.String)` |
| Assembly | `asm:<simple-name>\|<declaration-id>` | `asm:App.Library\|T:App.Service` |

The backslashes in the Markdown table only escape its column delimiter. Actual wire strings have a plain `|`:

```text
src:src/App/App.csproj|M:App.Service.Run(System.String)
asm:App.Library|T:App.Service
```

There is no embedded snapshot hash, generation, assembly version, MVID, absolute path, target fingerprint or randomly allocated identity. The existing absolute `targetPath` argument supplies solution or binary context. Follow-ups retain the exact returned `ownerTargetPath`/`targetPath` when an item belongs to a different binary owner.

Serialization rules:

1. Prefixes are exactly lowercase `src:` and `asm:`. Recognize these prefixes case-insensitively before routing so a wrong-case reference fails validation instead of falling through to name lookup. Each reference contains exactly one unescaped `|` separating two non-empty components.
2. Escape literal `%`, `|` and ASCII space as `%25`, `%7C`, `%20` in either component. Reject control characters, including tabs/newlines, and invalid Unicode. Leave other Unicode and Roslyn punctuation readable.
3. Decode once. Only those three uppercase escape sequences are valid. Do not URI-decode a second time or normalize the declaration ID's case, punctuation, generic arity or parameter types.
4. Producers emit canonical strings. Consumers may trim whitespace around the whole argument as existing input normalization does; the remaining prefixed reference must round-trip byte-for-byte through the codec. Noncanonical escaping, separators, paths or prefixes return `INVALID_SYMBOL_REFERENCE`.
5. Malformed input recognized as a reference never falls through to fuzzy/name/location discovery. Existing raw documentation IDs, supported names/signatures and source-location selectors remain available with their existing ambiguity semantics; they are discovery inputs, not additional generated stable IDs.

Apply the same Core codec and ownership rules to every producer and consumer. Do not duplicate parsers in tools or renderers.

## Source owner coordinate

Starting at the canonical solution directory, walk upward to the nearest Git repository marker containing that solution. Support both a `.git` directory and a worktree/git-directory pointer file containing one `gitdir: <path>` line, with optional final newline. Resolve a relative pointer against the marker's directory and require an existing destination directory. Do not invoke Git, scan an unrelated checkout or use the process working directory. A present but unreadable or malformed marker fails owner-reference creation rather than silently changing bases. Without a Git marker, use the selected solution directory. A missing/unstable project path or failed owner-base discovery yields no producer reference; consumers report `UNSUPPORTED_IDENTIFIER` with the exact owner-path limitation and recovery reason.

Create the path relative to that base from the owning loaded project's canonical absolute `.csproj` path. Use `/`, remove redundant dot segments and separators, and preserve the loaded owner's path spelling. Compare physical owner paths ordinal-ignore-case on Windows and ordinal on other platforms; compare declaration IDs ordinal on all platforms. Case variations of discovery input are permitted by existing discovery rules, but generated references always use the loaded owner's canonical spelling.

External loaded projects on the same filesystem volume use a normalized `../...` relative path. Such paths are allowed only when they match a project already loaded in the selected solution; reference parsing must not open arbitrary project files. If relative conversion yields a rooted path (for example another Windows drive), no stable reference is available. Never embed the absolute path as a fallback. Moving the repository without changing relative layout preserves its source references; moving an external project or changing the selected non-Git base may change them.

Project names are display metadata, not identity. Linked files in different projects receive different project coordinates. Partial declarations in the same project share one declaration reference.

Resolve the path to the loaded project set before looking up the declaration. If several loaded Roslyn contexts have the same physical project path, do not choose the first context or add an invented TFM suffix. Return `AMBIGUOUS_SYMBOL` with existing candidate metadata and explicit loaded-context information; producers for that ambiguous owner return no usable reference. The existing workspace's framework selection remains unchanged. Recover by using a solution/load configuration with one intended loaded context; no new MCP configuration-selection argument is added.

Normalize constructed generic symbols to their original declaration and reduced extension methods to their underlying original declaration. Resolve with Roslyn declaration-ID lookup in the exact owner's current compilation, restrict matches to declarations proven to belong to that project, deduplicate with Roslyn symbol equality, and require exactly one match. Project-reference/metadata declarations found by the lookup must not masquerade as declarations owned by that project. An unavailable compilation is a workspace failure, not proof that a declaration is absent.

## Assembly owner coordinate

Use the simple name read from the selected owner assembly metadata, preserving its spelling. Compare it ordinal against the selected binary's simple name. The filename is not a substitute for metadata identity. Resolve in one current leased owner generation and require exactly one matching declaration owned by that assembly; referenced declarations must use their own returned owner target.

A different simple name or a source/assembly origin mismatch yields `TARGET_MISMATCH`. Equal simple names in different DLLs cannot be distinguished by this string alone: the returned reference and exact owner target form the navigation coordinate. Deliberately changing that target to a same-name binary selects that binary; no stateless prior-path validation is promised. Never search all resident DLLs and pick the first matching name.

Reopen an evicted owner through normal target loading. Restart or eviction does not itself invalidate a reference. A rebuilt owner with the same exact declaration can resolve; unreadable/deleted/invalid binaries retain normal target-loading failures. During a batch or cross-owner traversal, retain existing generation leases and reference-closure checks so one analysis never combines replaced binaries. Such evidence checks may still return `STALE_SNAPSHOT`; declaration references themselves are not content-bound.

## Declarations without a stable reference

Emit a stable reference only when the supported type/member declaration has a non-empty canonical Roslyn declaration ID, proven unique owner, and a successful exact round-trip. Include overloaded/generic members, constructors, operators, explicit interface implementations and nested types where that round-trip succeeds.

Local functions, anonymous/synthesized declarations, file-local or decompiler-only declarations without a unique round-tripping ID, and owners without representable paths receive no stable reference. Preserve their available display/location/body information and existing raw-location navigation; never describe location selectors as durable after edits. Existing availability flags, null identifiers and follow-up-tool lists express the absence. Do not invent GUIDs or write identifiers into source.

## Output and input integration

Preserve existing output structure, casing policy, field names and renderer formats. Existing `handoffId`, `FromHandoffId`, `ToHandoffId` and corresponding rendered labels carry the new stable string. Existing `Handoff` availability flags and follow-up tool lists indicate exact reference availability. `docCommentId` stays descriptive metadata. No second stable-ID field or compatibility handle is added.

Cover discovery, namespace/type/member outlines, skeletons, body batches and body windows, context sections, resolution candidates, references, implementations, calls, hierarchy, impact, dependency nodes, type origins, assembly inspection/search/extensions and cross-owner hopping. A symbol reference accepted in `filePaths` resolves to all declaring documents in its exact owner, including partial declarations, before existing tool filters are applied. Physical path inputs retain their existing solution/decompiler-relative semantics; their base does not change to the symbol-reference base.

Body-window follow-ups select the current declaration text using the stable reference and declaration-relative range. After edits, callers may need to restart their range to read the whole new body. They must not concatenate windows from different reported snapshots as one consistent body.

## Errors and recovery

| Condition | Code | Required next action |
| --- | --- | --- |
| Malformed new reference or obsolete `h:`/`i:` wire input | `INVALID_SYMBOL_REFERENCE` | Repeat discovery on current target and use a returned reference |
| Wrong origin, missing loaded source owner or wrong assembly simple name | `TARGET_MISMATCH` | Use returned owner target; otherwise rediscover on intended target |
| Exact owner exists but declaration is absent | `SYMBOL_NOT_FOUND` | Rediscover the renamed/moved/changed declaration |
| Several exact owner contexts or declaration matches | `AMBIGUOUS_SYMBOL` | Select a uniquely loaded owner/context; do not guess |
| Owner coordinate cannot be constructed or represented | `UNSUPPORTED_IDENTIFIER` | Correct repository/owner metadata or use available raw-location navigation; no absolute-ID fallback |
| Required source compilation unavailable | `WORKSPACE_DIAGNOSTIC` | Correct the reported workspace loading/configuration issue and retry |
| Target unavailable or unsupported | Existing target-loading code | Correct the target/access according to the reported failure |
| Stale cursor or replaced binary during retained evidence analysis | `STALE_SNAPSHOT` | Restart that analysis/page query on the current target |

Treat `h:`/`i:` as obsolete reference prefixes only when the input is not a valid physical drive-path selector such as `H:\repo\File.cs`. Batch failures retain successful items and per-item codes/actions; all-failed batches remain errors. No snapshot error may be raised solely because an unchanged declaration's previous reference predates an edit.

## Complete removal

Remove registry lookup/emission, Base62 alphabet, counter persistence, lock-file handling, registrations, configuration, disposal hooks and handle-only errors/tests/docs. Remove the old snapshot-bound `i:` symbol wire parser/formatter; it is not a replacement public identity. Retain independently used hashing, path normalization, relationship identities, owner leases and cursor consistency in appropriately named owners. Never delete shared behavior only because its existing class name includes handoff terminology.

No active producer emits `h:`, no consumer resolves one, no optional mode restores one, and no runtime counter file is created or updated. Negative rejection tests may contain legacy literals. Historical task artifacts need not be rewritten as current implementation. Current `docs/`, tool descriptions, [.agents/rules/08](../../.agents/rules/08-ainetcodenavigator-mcp-navigation.mdc) and test helpers must describe the new references when R02 ships.

## Acceptance

Verify canonical codec round-trips and rejection, reserved characters, Unicode paths, nested/non-Git/worktree roots, same-name projects, linked/partial declarations, external same-volume and cross-drive owners, ambiguous loaded contexts, original generic/reduced declarations and unavailable-ID cases.

Discover a reference, edit its body, edit another loaded file, change project options, reload/restart the runtime, and follow the same exact declaration each time. Verify rename/delete/signature change returns the specified failure and never redirects. Assembly tests cover restart/eviction, rebuilt same API, changed API, different-name targets, same-name binaries with distinct owner targets and cross-owner follow-ups.

Run existing consumer contracts under byte/token budgets, body/result/outer paging, mixed batches and owner replacement. Stable references must fit or return an executable budget recovery, never be cut into malformed identifiers.

## Inspected entry points

[Registry](../../src/AiNetCodeNavigator.Core/Symbols/HandoffHandleRegistry.cs), [old source resolver](../../src/AiNetCodeNavigator.Core/Symbols/SourceHandoffResolver.cs), [raw resolver](../../src/AiNetCodeNavigator.Core/Symbols/SourceSymbolResolver.cs), [analysis identity](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs), [assembly resolver](../../src/AiNetCodeNavigator.Core/Assemblies/AssemblySymbolHandoffResolver.cs), [structure consumers](../../src/AiNetCodeNavigator/Mcp/Tools/StructureTools.cs) and [relationship consumers](../../src/AiNetCodeNavigator/Mcp/Tools/Relationships/RelationshipTools.cs). These links describe pre-implementation owners and must be updated if those files are removed.
