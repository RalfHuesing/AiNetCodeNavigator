# Shared Navigation Context

`get_context` answers a bounded question about one source or managed-assembly symbol. The caller selects a non-empty, duplicate-free list of `body`, `members`, `callers`, and `tests`. The response includes one compact declaration head and only the requested sections. Unselected scanners do not run.

The handler resolves the symbol once and shares its owner identity, snapshot, and lease across the selected sections. Source members retain declaration order; callers are direct incoming references with source locations, not transitive impact. Tests are static recommendations from the full source solution, independent of `callerScope`. On assemblies, `tests` is unsupported and `includeReferences` expands only direct caller discovery across resolved owners.

Each section reports its own status, analyzed scope, omissions, and optional cursor. `maxResults` is the page size for list sections (default 10, maximum 100); `maxBodyLines` controls the separate one-based body window (default 80). A body continuation uses `startLine` and returns its next position. A list cursor belongs to one section and the original complete selection; after reading outer response pages, continuing it analyzes and returns only that section. Changed target, query, selected sections, explicit argument presence, page size, or snapshot is rejected.

Invalid target/section combinations and explicitly supplied ineffective arguments fail validation. A later section failure is returned as an error while preserving earlier sections marked partial and a single head snapshot. This is section-level failure evidence, not a complete success response. Body availability is reported separately from section delivery status.

Test results are static candidates, not executed tests or measured coverage. See [Static Test Candidates](test-context.md) for their evidence and bounded discovery rules.
