# Optional analysis sessions

## Idea under discussion

An optional session could hold one immutable source snapshot for a connected sequence of body, relationship and context requests. A caller would explicitly switch to current analysis after editing. This could support consistent comparisons and concurrent investigation.

This is a new proposal, not an exposed production tool or parameter. Current snapshot-bound handles do not by themselves provide a user-controlled pinned-session contract.

## Relationship to other topics

Fixed sessions preserve an evidence version; [stable symbol references](01-symbol-identity-and-recovery.md) identify declarations for current navigation. A pinned session alone does not solve an agent's need to inspect its latest edits. The caller must be able to distinguish old evidence from current code.

## Open decisions

- Is there demonstrated demand beyond the current get_context shared-snapshot behavior?
- Which tools support the same pinned analysis scope and owner/reference closure?
- How are retained snapshots, memory, session expiry, currentness and explicit release bounded?
- Can agents compare old/current results without assuming that old line positions still locate current source?
- What happens to a long-running operation when the caller selects a new current snapshot?

Prefer resolving ordinary current-navigation stability and repeated analysis costs before adding a public session lifecycle. This topic remains optional exploration.
