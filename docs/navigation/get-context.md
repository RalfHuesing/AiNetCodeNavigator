# Shared Navigation Context

`get_context` answers a bounded question about one source or managed-assembly symbol. The caller selects a non-empty, duplicate-free list of `body`, `members`, `uses`, and `tests`. The response includes one compact declaration head and only the requested sections. Unselected scanners do not run.

`usageScope=all|production|tests` defaults to `all`, applies only to source `uses`, and is rejected when `uses` is unselected or the target is an assembly. The old `callers` section and `callerScope` argument are rejected. Explicit ineffective options remain errors even when equal to their defaults.

`RelationshipTools.Context.cs` owns the context entry point, validation, selected sections and cursors. The handler resolves the symbol once and shares its owner identity, snapshot, and lease across the selected sections.

Members reuse the class-structure scanner and retain complete visibility, signatures, source ranges, line counts, kinds, primary-constructor parameters, files, and stable references. `memberNameFilter` retains case-insensitive name substring matching. `memberKindFilter` retains source exact-kind matching and the methods/properties/fields/constructors aliases (constructors include positional record parameters); assembly kinds use case-insensitive substring matching. `memberSortBy=lines|kind|name` defaults to `lines` (file, declaration line, then name). Source-only `memberScope=all|production|tests` defaults to `all` and affects only this section. All member options require `members`, including explicitly supplied defaults; assembly targets reject `memberScope`. `members` requires a type, and an empty filtered list is a successful result.

Section `structure` repeats type kind/name, owner `targetPath`, declaring `files`, and `totalLines` on each page. Assembly `structure.decompiledSourceRoot` is the acquired physical generation root; member paths and files within it are relative, while unusual external files remain absolute. See [Member Structure](get-class-structure.md).

Uses are direct incoming symbol references with source locations, including method groups and member access. Tests are static recommendations from the full source solution, independent of `usageScope`. On assemblies, `tests` is unsupported and `includeReferences` expands only direct use discovery across resolved owners.

Each section reports its own status, analyzed scope, omissions, and optional cursor. `maxResults` is the page size for list sections (default 10, maximum 100); `maxBodyLines` controls the separate one-based body window (default 80). A body continuation uses `startLine` and returns its next position. A list cursor belongs to one section and the original complete selection; after reading outer response pages, continuing it analyzes and returns only that section. Changed target, query, selected sections, explicit argument presence, page size, or snapshot is rejected.

Invalid target/section combinations and explicitly supplied ineffective arguments fail validation. A later section failure is returned as an error while preserving earlier sections marked partial and a single head snapshot. This is section-level failure evidence, not a complete success response. Body availability is reported separately from section delivery status.

Test results are static candidates, not executed tests or measured coverage. See [Static Test Candidates](test-context.md) for their evidence and bounded discovery rules.

Source tests accept `testHelperDepth=0..2` (default 1), counting intermediate helpers. Explicit depth requires `tests`, even at the default value. Tests scan the loaded source solution independently of `usageScope`; `includeGenerated` controls generated declarations. Returned fixture/method identities and shortest static path steps support body navigation. The tests-section `analysis` reports helper depth/count and helper, implementation, fixture and reference limit flags. Reached limits remain partial with omissions on the last fixture page. Depth and its explicit presence bind both domain cursors and outer delivery.
