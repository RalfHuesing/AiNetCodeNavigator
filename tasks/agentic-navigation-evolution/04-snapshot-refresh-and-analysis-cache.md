# Snapshot refresh and reusable analysis

## User decision, 2026-10-03

The user has approved reuse of collected dependency relationships across navigation requests against the same immutable snapshot. Another graph query on the same target, snapshot, scope and generated inclusion should reuse the collection and apply its own traversal/projection. Targeted outgoing traversal is also approved in topic 02. The user has now also approved computing source identity once per immutable unchanged snapshot and reusing it. Implementation is pending. Incremental updates across snapshots remain an unapproved proposal.

Retain a fresh workspace check at the analysis boundary. When relevant content or project/reference context changes, old collected relationships must not be used as current analysis. Bound cache retention and memory; a cache miss, expiry or eviction recomputes rather than degrading correctness.

## Approved source identity once per immutable snapshot

The user has approved reusing the computed source identity across navigation calls against the same immutable Roslyn Solution snapshot instead of repeatedly hashing its complete document texts. Fresh disk/structure checks still select the current snapshot; any relevant content or project/reference context change requires identity computation for the new snapshot. Do not substitute timestamp-only detection or reuse identity across changed semantic inputs. Preserve the separate configured-framework binding of index-scope metadata until its contract is separately decided.

Implementation is pending. During implementation, inspect remaining identity call sites after the approved handle removal so superseded handoff validation is not optimized or retained unnecessarily. Verify equal identity/results for unchanged inputs, changed-text detection with unchanged timestamps, project/reference changes, shared-file behavior, cancellation and bounded cache lifetime. Potential latency savings require measurement; no speedup is claimed from source inspection alone.

## Verified current behavior

[ResidentSolution](../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs) performs lazy refresh when a snapshot is requested. Refresh reads and hashes loaded source files, updates all linked documents sharing a path, and publishes changed text after collection. It preserves detection of edits with unchanged timestamps. Structure checks can trigger a full MSBuild reload.

[SolutionStructureFingerprint](../../src/AiNetCodeNavigator.Core/Workspace/SolutionStructureFingerprint.cs) checks solution/project/build inputs and candidate source membership. Its candidate discovery is not identical to an exact evaluated Compile-item list, so exclusion-sensitive reload behavior deserves separate investigation if reproduced. Plain audit text/JSON creation under `temp` is not established as the cause of source snapshot changes.

[AnalysisSymbolIdentity.ForSourceAsync](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs) computes document text hashes for the full loaded solution. Source route setup calls it, and handoff validation can call it again. This identifies potentially repeated work; its share of observed latency has not been measured.

`get_index_scope` uses `SourceAnalysisContext.CreateIndexScopeSnapshotHash`, adding configured-framework metadata to the source identity. Its `snapshotId` can therefore differ from ordinary source-tool IDs without a source edit. Cross-tool snapshot comparisons need a clearly defined identity domain.

## Proposed direction

- Approved: reuse an identity computation for the same immutable Roslyn solution snapshot, with correct cancellation and lifetime behavior.
- Measure file refresh, structure checks, identity creation, compilation and scanner stages independently.
- Approved: reuse dependency collection within an immutable snapshot. Incremental cache updates across snapshots are not covered by this approval.
- For cross-snapshot caches, account for referenced declaration and project changes that affect semantics in unchanged documents. Hashing only the query root or changed file is insufficient.
- Define whether a common analysis snapshot ID and separate inventory/configuration identity would make cross-tool evidence clearer.

## Constraints and open decisions

Do not replace content verification with timestamp-only checks. Watcher-based invalidation would require explicit lost-event recovery and must not silently stale results. Bound retained snapshots/graphs by memory and lifetime. Cache keys must include relevant ownership, source/reference context, scope and generated inclusion. Measure before selecting a refresh redesign.
