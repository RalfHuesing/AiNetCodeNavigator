# Snapshot refresh and reusable analysis

## Verified current behavior

[ResidentSolution](../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs) performs lazy refresh when a snapshot is requested. Refresh reads and hashes loaded source files, updates all linked documents sharing a path, and publishes changed text after collection. It preserves detection of edits with unchanged timestamps. Structure checks can trigger a full MSBuild reload.

[SolutionStructureFingerprint](../../src/AiNetCodeNavigator.Core/Workspace/SolutionStructureFingerprint.cs) checks solution/project/build inputs and candidate source membership. Its candidate discovery is not identical to an exact evaluated Compile-item list, so exclusion-sensitive reload behavior deserves separate investigation if reproduced. Plain audit text/JSON creation under `temp` is not established as the cause of source snapshot changes.

[AnalysisSymbolIdentity.ForSourceAsync](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs) computes document text hashes for the full loaded solution. Source route setup calls it, and handoff validation can call it again. This identifies potentially repeated work; its share of observed latency has not been measured.

`get_index_scope` uses `SourceAnalysisContext.CreateIndexScopeSnapshotHash`, adding configured-framework metadata to the source identity. Its `snapshotId` can therefore differ from ordinary source-tool IDs without a source edit. Cross-tool snapshot comparisons need a clearly defined identity domain.

## Proposed direction

- Reuse an identity computation for the same immutable Roslyn solution snapshot, with correct cancellation and lifetime behavior.
- Measure file refresh, structure checks, identity creation, compilation and scanner stages independently.
- Reuse dependency collection within an immutable snapshot before attempting incremental cache updates across snapshots.
- For cross-snapshot caches, account for referenced declaration and project changes that affect semantics in unchanged documents. Hashing only the query root or changed file is insufficient.
- Define whether a common analysis snapshot ID and separate inventory/configuration identity would make cross-tool evidence clearer.

## Constraints and open decisions

Do not replace content verification with timestamp-only checks. Watcher-based invalidation would require explicit lost-event recovery and must not silently stale results. Bound retained snapshots/graphs by memory and lifetime. Cache keys must include relevant ownership, source/reference context, scope and generated inclusion. Measure before selecting a refresh redesign.
