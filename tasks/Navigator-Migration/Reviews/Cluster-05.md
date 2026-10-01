# Cluster 5 Review Log

Reference policy: external comparison material has been removed from this historical record. Local documentation, the public contract matrix, and Navigator code and tests are authoritative. Commit IDs, findings, and reported historical gate results below retain their original provenance; this cleanup does not rerun or reaccept them.

## Point 5.1 implementation

- Base commit: `76d1252519214e317e5f77d3472b5163c231d06d`; the working tree was clean before this implementation slice.

- Before-fix regressions failed for outgoing object creation/member access, `TopN=0`, Mermaid label newlines, and null public inputs (which leaked `NullReferenceException`). A high-fan-out regression was added with `TopN` above the 250-node cap.
- The Core builder now resolves explicit and target-typed object creation and non-invoked member access, displays constructor targets by containing type, clamps `TopN` to one, and refuses to add nodes beyond 250 while reporting hidden edges and truncation. The Mermaid renderer replaces CR/LF and quotes in labels. Builder and renderers validate null required inputs. The tests also verify source `h:` handoffs in call-tree output and that an override declaration is not emitted as an incoming caller.
- Added the current-state page [Call Tree Core Engine](../../../docs/navigation/get-call-tree.md) and indexed it in `docs/README.md`. It describes the Core engine only; public MCP tool transport remains for later integration work.
- The 5.1 audit checkbox remains `[ ]`. This implementation record is not an independent audit; the requested point audit is still pending.

### Verification

| Gate | Result |
|---|---|
| Focused CallTree FastTests | Passed, 9/9 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 337/337 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 349/349 across both test projects |
| `git diff --check` | Passed before commit |

## Point 5.1 audit 1 finding fixes

- Fix base: `d713e0d44351a68f9af2a8d93c2c0db7be16bf49`; the audit's review documentation was the only existing working-tree change.
- Reproduction tests failed before the fixes: a source method lost a third-party metadata call under default settings; `Both` with `TopN=1` emitted both an incoming and an outgoing edge; exactly 250 fully expanded nodes were marked truncated; and the graph exposed no count for queued nodes left unexpanded at the cap.
- Outgoing filtering now retains third-party metadata targets by default and filters framework assemblies/namespaces unless `IncludeBcl` is true. A compiled in-memory third-party assembly and `System.Console` verify both flag values.
- Incoming groups receive the first portion of the per-symbol fan-out budget in `Both` mode; outgoing groups use only the remaining budget. Hidden counts include discovered groups omitted in either direction.
- `CallGraphPayload.PendingNodeCount` reports queued nodes not expanded because the hard cap stopped traversal. `Truncated` is based on known omitted groups or pending work, so exactly 250 completed terminal nodes remain complete and a cap with queued work reports pending nodes without inventing an edge count. ASCII and Mermaid both render that pending count.
- Updated [Call Tree Core Engine](../../../docs/navigation/get-call-tree.md) for BCL filtering, combined fan-out, and the distinction between hidden edges and pending nodes. The point 5.1 audit checkbox stays `[ ]`; audit 1 has findings fixed but has not yet had its follow-up review.

### Audit 1 fix verification

| Gate | Result |
|---|---|
| Finding reproduction tests before fixes | Failed as expected, 4 findings reproduced |
| Focused CallTree FastTests after fixes | Passed, 13/13 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 341/341 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 353/353 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 5.1

- Audited commit: `05cdd1b8a63c89cffddbc88ac827c436243d28f4` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the implementation gates above are reported from the prior implementation record only.
- Outcome: Open findings below. The 5.1 audit checkbox stays unchecked. The implementation's constructor/member access coverage, source handoffs, null input handling, and basic Mermaid escaping have relevant tests, but the following contract gaps need fixes and regression coverage.

### Findings

1. **P1 — Third-party metadata calls disappear when `IncludeBcl` is false.** `src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs:167` rejects every target without a source location. A source method calling a non-BCL referenced assembly method therefore has no outgoing edge under the default request. Acceptance: distinguish BCL from other metadata assemblies, preserve the latter by default, and test a source caller with both a non-BCL metadata callee and a BCL callee under both `IncludeBcl` settings.
2. **P2 — `TopN` applies twice for `Both`.** `CallTreeBuilder.cs:118-122` and `:219-223` take `TopN` separately from incoming and outgoing groups. Acceptance: bound the combined expansion to `TopN` per expanded symbol, maintain deterministic direction/order choice, count hidden groups once, and cover a `Both` graph with callers and callees.
3. **P2 — The 250-node stop misreports completeness.** `CallTreeBuilder.cs:47` stops as soon as the node list reaches 250; `:347-353` then sets `Truncated` from that equality, even when the graph has exactly 250 complete terminal nodes. If queued nodes still await expansion, their undiscovered edges are absent but `HiddenEdgeCount` can remain zero. This contradicts `docs/navigation/get-call-tree.md:5`, which promises a positive hidden count for cap omissions. Acceptance: distinguish actual hard-cap omission from merely reaching 250 nodes, report remaining work without claiming a known edge count that was never scanned, and cover both an exactly complete 250-node graph and one with pending expansion at the cap.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Independent audit 2/3 of point 5.1

- Audited commit: `6b13f078b672f5575e4e4182d2d7477ccb18ca9b` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the gate results above belong to the implementation turn.
- The three audit-1 findings are resolved on this commit: metadata callees from a third-party assembly remain visible with default `IncludeBcl`; `Both` consumes one combined fan-out budget; and an exact, completed 250-node graph is not truncated while queued expansion is reported separately from known hidden edges. Each case has a targeted FastTest; the pending count is rendered in both formats.
- One new BCL-filter finding remains. The 5.1 audit checkbox stays unchecked pending the fix and the final allowed point audit.

### Finding

1. **P2 — Source symbols in framework-named namespaces are filtered as BCL.** `src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs:169` calls `IsBclSymbol` for every callee. `:248-269` classifies by namespace even when `target.Locations` has a source location. Thus a workspace method such as `System.Local.Call()` disappears from a default outgoing graph. Acceptance: retain every source-backed callee irrespective of its namespace/assembly name, while still excluding external BCL metadata and retaining non-BCL metadata by default; add a source-backed `System` namespace regression alongside the existing metadata test.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.1 audit 2 finding fix

- Fix base: `eb60637f2ef5b0ce52fa59c2d35458ee81e4b1e9`; the working tree was clean before this slice.
- The new reproduction initially failed: a source-backed call to `System.Local.Api.Call()` produced no outgoing callee node because the namespace-only BCL heuristic filtered it.
- Outgoing target filtering now checks whether a symbol is external before applying the BCL assembly/namespace rules. All source-backed targets remain visible; external BCL targets remain opt-in, and non-BCL metadata targets remain visible by default. The existing metadata/BCL test continues to cover both `IncludeBcl` values.
- Updated [Call Tree Core Engine](../../../docs/navigation/get-call-tree.md) to state that source-backed symbols in framework-named namespaces remain visible. The independent follow-up review is the third and final point audit; the 5.1 checkbox remains `[ ]` until that review is complete.

### Audit 2 fix verification

| Gate | Result |
|---|---|
| Source-backed `System` namespace reproduction before fix | Failed as expected: outgoing node omitted |
| Focused CallTree FastTests after fix | Passed, 14/14 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 342/342 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 354/354 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 3/3 of point 5.1

- Audited commit: `d11caa3d709a2a2e259370be5948328385e7955b` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. Scope was limited to the remaining audit-2 BCL-filter finding and its regression coverage; no broader point audit or build/test run was performed.
- **Accepted:** `CallTreeBuilder.cs:172-173` now applies the BCL classifier only to external symbols. The new FastTest at `CallTreeTests.cs:205-229` covers a source-backed `System.Local.Api.Call()` under the default setting. The pre-existing `CallTreeTests.cs:170-202` covers a third-party metadata callee retained by default and a framework metadata callee excluded by default but included with `IncludeBcl=true`. No point-5.1 findings remain open at the third-audit limit.
- The point 5.1 audit checkbox is complete. Public MCP transport and end-to-end contract verification remain assigned to later roadmap clusters and were not inferred from this Core audit.

### Review verification

- Inspected the committed source and regression tests; no production files were changed by this audit.
- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.2 implementation

- Base commit: `a028777123b51709134c3f9f3bb1af3f79c0cb69`; the working tree was clean before this slice.

- Before the fix, `maxResults=0` reproduced empty reference and implementation lists even when matches existed. Added tests also verify `-1` normalization, null argument failures, handoff roundtrips, same-name types in two implementing projects, and interface/abstract member implementations across project references. A BaseProcessor → FastProcessor → SafeProcessor contract test confirms indirect derived types are returned; this passed before a code change because Roslyn's current default is already transitive.
- Both Core resolver entry points now validate required Roslyn inputs and clamp nonpositive limits to one. `FindImplementationsResult` reports `IsTruncated` while preserving the full `TotalCount`. References and implementations remain ordered and retain their project-bound source handoffs.
- Added the current-state page [Find References and Implementations Core Engines](../../../docs/navigation/find-references-and-implementations.md) and indexed it in `docs/README.md`. It documents Core behavior separately from future MCP tool contracts.
- The 5.2 audit checkbox remains `[ ]`; this implementation record is not an independent audit.

### Verification

| Gate | Result |
|---|---|
| `maxResults=0` before-fix reproductions | Failed as expected for references and implementations (empty results) |
| Focused FindReferencesResolver FastTests after fix | Passed, 11/11 |
| Indirect derived class contract test | Passed, 1/1 (existing resolver behavior) |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 350/350 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 362/362 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 5.2

- Audited commit: `a75ec0910e3729666c4497689ab25f5094b3fdf4` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the implementation gate results above are from the prior turn.
- Direct cross-project references, two identically named implementation types in distinct projects, interface and abstract method implementations, indirect derived classes, handoff roundtrips, nonpositive result limits, and null inputs have relevant Core tests. Public identifier-resolution and MCP error formatting remain later integration work. The point 5.2 audit checkbox stays unchecked for the findings below.

### Findings

1. **P1 — `find_references` depth contract is absent from the Core engine.** `src/AiNetCodeNavigator.Core/Symbols/FindReferencesResolver.cs:20-24` exposes only a direct-reference query; `:32-77` processes references to the seed once and cannot follow callers. A future MCP wrapper cannot provide that contract through this engine. Acceptance: support bounded depth traversal (reference cap three) with origin/depth per call site, cycle and node limits, and explicit completeness/truncation; test a cross-project caller chain at depths one and two and a capped request. Keep the result a navigation query without linter metrics.
2. No Core test covers either property case. Acceptance: route supported property symbols by interface versus override semantics and cover a cross-project interface property implementation and an abstract/virtual property override; preserve distinct source handoffs.
3. The Core model has no status/error field and tests only null arguments. Acceptance: distinguish an unsupported target from a valid target with zero implementations using a typed result or documented exception that the MCP layer can map to a clear error; cover a struct and a non-virtual ordinary method without conflating them with empty interface results.

### Audit 1 remediation

- Added before-fix contract tests. The depth, node-limit, and typed-error tests failed to compile because the Core API had no depth/node-limit overload or recoverable implementation error fields. Re-running the property test with the former generic `FindImplementationsAsync` dispatch reproduced an empty abstract-property override list. The cycle case and remaining contracts run against real Roslyn projects.
- `FindReferencesResolver` now performs breadth-first caller traversal. Depth is clamped to 1–3, the hard expanded-symbol limit is 200 (lower per-call limits are supported), visited symbols prevent cycles, and each call site records its depth plus reached-from name and handoff. Result metadata distinguishes result-limit truncation, node-limit truncation, and a clamped requested depth.
- Implementation dispatch now supports interface and abstract/virtual/override properties, accepts only interface types and classes for type queries, and returns `FindImplementationsResult.IsSuccess`/`ErrorMessage` for unsupported targets. Valid targets with no matches remain successful empty results.
- Updated [Find References and Implementations Core Engines](../../../docs/navigation/find-references-and-implementations.md) with traversal limits, call-site origin metadata, completeness, property support, and recoverable target errors. The 5.2 audit checkbox remains open pending independent review.

### Remediation verification

| Gate | Result |
|---|---|
| Before-fix depth/node-limit and unsupported-target contract tests | Failed to compile as expected: depth/maxNodes overload and typed error fields were absent |
| Before-fix generic property dispatch reproduction | Failed as expected: abstract property override list was empty |
| Focused FindReferencesResolver FastTests after fix | Passed, 16/16 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 355/355 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 367/367 across both test projects |
| `git diff --check` | Passed before commit |

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Independent audit 2/3 of point 5.2

- Audited commit: `61195aa02695bc40df3592263d84680bad4391a6` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. Reviewed only audit-1 findings and the related source, models, tests, and documentation. No build or tests were run in this audit; the remediation gates above belong to the implementation turn.
- **Accepted — depth traversal:** `FindReferencesResolver.cs:20-168` retains the direct overload and adds bounded breadth-first traversal with depth 1–3, a 200-symbol ceiling, a visited set, call-site depth and reached-from provenance, and distinct display/node/depth completeness flags. `FindReferencesResolverTests.cs:43-133` covers a cross-project A ← B ← C chain, source handoff roundtrips, display and node caps, depth clamping, and a two-method cycle.
- **Accepted — properties:** `FindReferencesResolver.cs:217-234` routes interface properties through `FindImplementationsAsync` and virtual/abstract/override properties through `FindOverridesAsync`. `FindReferencesResolverTests.cs:297-322` checks both cases across projects and resolves their handoffs.
- **Accepted — unsupported targets:** `FindReferencesResolver.cs:182-248` distinguishes unsupported types and non-virtual methods with a typed `ErrorMessage`/`IsSuccess=false`; the test at `FindReferencesResolverTests.cs:325-351` also verifies that a valid interface with no implementations succeeds with an empty list.
- No point-5.2 findings remain open. The point audit checkbox is complete at audit 2/3. Public MCP formatting and identifier errors remain later integration work, not claims of this Core audit.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.3 implementation

- Base commit: `e8136f7d4798831b7421d52724ba69e8da116ba2`; the working tree was clean before this slice.

- Before-fix FastTests reproduced two defects: `maxResults=0` and `-1` returned no subtypes despite matches, and a null type failed with `NullReferenceException` rather than `ArgumentNullException`. Cross-project base/interface/derived searches and Handoff resolution passed before the fix; they are recorded as preserved contracts.
- `TypeHierarchyScanner` now explicitly searches transitive derived classes and implementers, normalizes subtype limits, guards malformed base cycles by original type definition, sorts interface and partial source locations deterministically, and checks cancellation during the base walk. It accepts classes, interfaces, and structs; other named type kinds return a typed unsuccessful payload. Source-backed cross-project entries retain project-bound handoffs; metadata-only entries stay visible without fake paths or handoffs.
- `TypeHierarchyService` and `GetTypeHierarchyFormatter` validate null inputs; formatter output reports unsupported-type errors and preserves the subtype count/truncation summary. Added [Get Type Hierarchy Core Engine](../../../docs/navigation/get-type-hierarchy.md) and its docs index entry. Point 5.3 audit remains open.

### Verification

| Gate | Result |
|---|---|
| Before-fix nonpositive-limit and null-type reproductions | Failed as expected: empty subtype lists; `NullReferenceException` |
| Cross-project base/interface/derived Handoff contracts before fix | Passed; preserved by post-fix tests |
| Focused TypeHierarchy FastTests after fix | Passed, 10/10 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 362/362 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 374/374 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 5.3

- Audited commit: `efcafcd8cc38848e9a117a18dd6aedf6a2db0996` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the gates above were reported by the implementation turn.
- Cross-project direct and indirect derived classes and interface implementers, source Handoff roundtrips, metadata-only base/interface display, nonpositive subtype limits, malformed base cycles, null arguments, and unsupported enum status are covered by the current Core tests. One source-location fidelity finding remains, so the point 5.3 audit checkbox stays unchecked.

### Finding

1. **P2 — Partial base/interface declarations lose all but one source location.** `src/AiNetCodeNavigator.Core/Hierarchy/TypeHierarchyScanner.cs:104-117` selects `FirstOrDefault()` from a type's source locations, and `CollectBaseTypes`/`CollectInterfaces` call that single-entry formatter (`:80-101`). For a partial base class or interface declared in two files, the Core payload exposes only one navigable declaration. Acceptance: emit each source declaration location for base and interface entries, keep metadata-only types as one locationless entry, preserve project-bound handoffs, and test a cross-project hierarchy with a partial base and partial interface in separate files.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

### Audit 1 remediation

- Added a before-fix regression using Contracts and App projects. The test failed as expected because a partial `Contracts.BaseType` declared in two files produced only one BaseTypes entry. It also covers a two-file partial interface, project-bound handoff roundtrips, and a partial subtype counted once.
- Base type and interface projections now emit one entry per source location with the same project-bound symbol handoff; metadata-only types still emit one pathless entry. Derived/implementing subtype projection continues to select one deterministic source location per type, so `TotalSubtypes` and `maxResults` stay type-based.
- Updated the hierarchy current-state page to document partial type location behavior. The point 5.3 audit checkbox remains unchecked pending independent review.

### Remediation verification

| Gate | Result |
|---|---|
| Before-fix partial base/interface source-location test | Failed as expected: 1 base location returned where 2 were declared |
| Focused TypeHierarchy FastTests after fix | Passed, 11/11 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 363/363 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 375/375 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 2/3 of point 5.3

- Audited commit: `4c7cd3f7e03387f9896753d6766c7b50ef59f7ea` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. Scope was limited to the audit-1 partial-location finding and its related Core changes/tests. No build or tests were run in this audit; the remediation gates above belong to the implementation turn.
- **Accepted:** `TypeHierarchyScanner.cs:86-101` now emits every source entry for bases and interfaces via `CreateEntries`; `:104-144` retains a single pathless entry for metadata, while subtype projection at `:59-65` still takes one entry per type before applying `maxResults`. `TypeHierarchyTests.cs:141-186` covers partial base and interface declarations in separate files across Contracts/App projects, resolves their project-bound handoffs, and asserts the partial derived class counts once. Existing metadata tests at `:247-270` verify pathless BCL base/interface entries and null handoffs.
- No point-5.3 findings remain open. The point audit checkbox is complete at audit 2/3. Public MCP transport remains later integration work.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.4 implementation

- Base commit: `c9a2675c03c836051e066526b042f5d7d9d762ea`; the working tree was clean before this slice.
- The symbol branch traverses callers breadth-first across the solution, distinguishes direct from deeper call sites, preserves project identity in call-site deduplication, counts projects from the complete result, normalizes `maxResults` to at least one, caps depth at three and nodes at 200, prevents cycles, emits caller handoffs, and reports depth and node-limit completeness separately.
- Before-fix tests reproduced four defects: same-path call sites in two projects collapsed into one; `maxResults=0` hid every result; pending BFS symbols at the node cap were reported as complete; and the existing impact total did not expose the indirect-only count separately. A cycle reproduction also showed that repeated source-line/member pairs at separate traversal depths were collapsed.
- `ImpactAnalyzer` now counts distinct direct and indirect call sites, preserves project/depth identity, keeps whole-solution project/file summaries independent of the display limit, and returns requested/effective depth, visited-node and effective-limit metadata, and `IsComplete`. The node cap is 200 by default, lower `maxNodes` values are supported, and larger values are clamped. Input errors are explicit and recursive graphs terminate by symbol identity. Existing `TransitiveImpactCount` remains the total impact-site count; `TransitiveCallSitesCount` reports only sites beyond depth one.
- Added [Symbol Impact Core Engine](../../../docs/navigation/impact-analysis.md) and indexed it in `docs/README.md`. The 5.4 audit checkbox remains open pending independent review.

### Verification

| Gate | Result |
|---|---|
| Before-fix cross-project path/dedup, nonpositive-result-limit, pending-node, and direct/transitive-count reproductions | Failed as expected |
| Focused ImpactAnalyzer FastTests after fix | Passed, 8/8 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 370/370 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 382/382 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 5.4

- Audited commit: `6d877374c1809483d8e6656d5eb19c929a9163a0` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the gates above belong to the implementation turn.
- Cross-project two-level callers and caller handoff roundtrips, same relative path in distinct projects, direct versus indirect totals, display/depth/node bounds, cycle termination, and null/node-limit errors have relevant Core tests. One provenance/deduplication finding remains; the point 5.4 audit checkbox stays unchecked.

### Finding

1. **P1 — Reconverging impact paths on one line are collapsed.** `src/AiNetCodeNavigator.Core/Symbols/ImpactModels.cs:7-13` has no reached-from symbol, and `ImpactAnalyzer.cs:113-115` deduplicates only by project/path/line/calling member/depth. Consider `Target.A()` called by `B()` and `C()`, then `Top()` calls `B(); C();` on the same source line. At depth two, the two distinct `Top()` relationships get the same key, so `TransitiveCallSitesCount`, `TransitiveImpactCount`, and shown call sites undercount and the caller cannot tell which branch each site reached. Acceptance: retain the reached-from source symbol identity/handoff or equivalent stable provenance in each impact site, include it in deduplication and deterministic sorting, and cover a cross-project branching/reconverging graph with two calls on one line. Assert both depth-two relationships and their individually resolvable provenance, plus direct/indirect totals and affected-project summaries under `maxResults` truncation.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

### Audit 1 remediation

- Added a before-fix cross-project convergence regression: `Top.Go` calls `B.Call(); C.Call();` on one source line, while the branch methods both call `Contracts.Api.Run()`. The impact result expected four relationships but returned three because the two depth-two entries shared a de-duplication key.
- Each `ImpactCallSiteEntry` now records `ReachedFromSymbolId` and a project-bound `ReachedFromSymbolHandoffId` for the symbol whose references produced the site. Stable origin identity participates in de-duplication and deterministic ordering, preserving converging branches even when the caller, file, and line are the same. The test resolves both origin handoffs separately to BranchB and BranchC, checks direct/indirect totals, and verifies affected-project summaries remain complete when `maxResults` truncates the displayed entries.
- Updated [Symbol Impact Core Engine](../../../docs/navigation/impact-analysis.md) with reached-from provenance and its role in call-site identity. The 5.4 audit checkbox remains open pending independent follow-up.

### Audit remediation verification

| Gate | Result |
|---|---|
| Before-fix cross-project convergence regression | Failed as expected: 3 impact sites returned where 4 were expected |
| Focused ImpactAnalyzer FastTests after fix | Passed, 9/9 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 371/371 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 383/383 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 2/3 of point 5.4

- Audited commit: `4dcaf08e3a91591b53f471600b19dd49845893cd` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. Scope was limited to the audit-1 convergence/provenance finding and its related code, test, and documentation. No build or tests were run in this audit; the remediation gates above belong to the implementation turn.
- **Accepted:** `ImpactAnalyzer.cs:63-66` captures the reached-from symbol identity and source handoff for each BFS expansion; `:99-107` puts both on the call-site entry; `:116-139` includes caller and reached-from identity in deduplication and stable sorting before result limits and totals are computed. The regression at `ImpactAnalyzerTests.cs:186-232` exercises BranchB and BranchC calling the target, with Top invoking both branch methods on one line. It asserts two distinct depth-two sites and origin handoffs resolving to the correct projects, direct and transitive counts, and all affected projects under `maxResults=3` truncation.
- No point-5.4 findings remain open. The point audit checkbox is complete at audit 2/3; public MCP formatting remains later integration work.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.5 implementation

- The Core scanner remains a solution-wide snapshot without tool-level file/type direction, scope filters, or handoffs.
- Added a before-fix regression with two project assemblies declaring the same `Shared.Models.Widget`: both consumer edges previously resolved to `ContractsTwo/WidgetTwo.cs` because the scanner keyed declarations by display string. The fixed scanner keys by Roslyn symbol identity and carries source/target project names on namespace and file edges. Partial source types use their lexically first declaration file as their representative.
- Added coverage for external framework type filtering, independent relationship paging and totals, document limits and completeness, page-size clamping, and invalid offsets. The result now exposes per-collection totals/continuation state, scanned/total document counts, clamp metadata, and recoverable document errors. Hard bounds are 500 relationships per page and 1000 documents per scan.
- Updated [Dependency Graph Core Scanner](../../../docs/navigation/dependency-graph.md). The 5.5 audit checkbox remains open for independent review.

### Implementation verification

| Gate | Result |
|---|---|
| Before-fix same-FQN cross-project regression | Failed as expected: both consumers pointed to ContractsTwo |
| Focused DependencyGraphScanner FastTests after fix | Passed, 6/6 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 376/376 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 388/388 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 5.5

- Audited commit: `1295b465d3e87b20c5684b87486d9fbabe6e6a3e` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the implementation gates above belong to the prior turn.
- Project references, same full type names in different assemblies, external BCL exclusion, independent relationship paging, limit metadata, and basic invalid offsets have relevant Core tests. The following gaps remain; point 5.5's audit checkbox stays unchecked.

### Findings

1. **P1 — The target-centered dependency graph contract cannot be reconstructed from the snapshot.** `src/AiNetCodeNavigator.Core/Dependencies/DependencyGraphScanner.cs:23-26` accepts only a solution and paging options; `:110-145` aggregates usages into namespace/file pairs and loses which declaring type produced each edge. Filtering the current aggregate by file cannot produce a correct type-scoped first hop when a file has two types. Acceptance: provide a bounded Core query for file and type targets with direction and depth, preserving edge provenance, cycles, and truncation metadata; test two types in one file with different dependencies, plus incoming and multi-hop paths. The MCP layer may resolve identifiers and format the result later, but cannot infer discarded type provenance.
2. **P1 — The 1000-document cap is not resumable.** `DependencyGraphScanner.cs:37-45` always takes the first `maxDocuments` documents; `:147-165` applies `Offset` only to completed relationship collections. Repeating calls with another relationship offset rescans the same first 1000 documents, so dependencies in document 1001+ cannot be queried through this Core API. `DocumentLimitReached` correctly reports incompleteness but supplies no document cursor. Acceptance: provide a deterministic document continuation/window or targeted bounded scan that can reach later documents, keep counts/completeness scoped accurately, and test a dependency whose declaring or consuming file lies beyond a small test document limit across continuation calls.
3. **P2 — Generic type targets are absent from source dependency edges.** `DependencyGraphScanner.cs:110-113` inspects only `IdentifierNameSyntax` and uses `GetTypeInfo`; a reference to `Contracts.Box<int>` has a `GenericNameSyntax` for `Box`, so the target generic type is never considered. Acceptance: collect source-backed named types for generic and qualified forms without adding external metadata noise or duplicate edges; test a cross-project field or parameter of `Contracts.Box<int>` and a non-generic control.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

### Audit 1 remediation

- At the audited baseline `a83f28f`, the Core API exposed no target, direction, depth, or document continuation fields. The before-fix executable generic repro also failed: the dependency edge for `Contracts.Box<Contracts.Item>` contained Item but omitted Box.
- Added symbol-backed `DependencyTypeReference` edges with assembly-qualified IDs, fully qualified names, source/target type provenance, project, file, namespace, and depth. Type syntax collection includes identifier, generic, and qualified forms, while source-location checks continue to exclude BCL/package metadata.
- Added `TargetFilePath`/`TargetTypeName`, optional project restriction, `Outgoing`/`Incoming`/`Both`, depth clamped to 1–3, and deterministic type-edge paging. Tests cover a two-type file with distinct outgoing edges, incoming traversal, a two-hop caller-to-target-to-dependency chain, file targets, and generic target matching.
- Added `DocumentOffset` and `NextDocumentOffset`; continuation starts at the next stable-sorted document rather than repeating earlier documents. Results expose their starting offset and remain explicitly incomplete when a response covers only a later batch. The continuation regression checks offsets 0 then 1 and a null final cursor.
- Continuation is stateless: target traversals operate on the current document batch. For a complete target traversal over more than one batch, callers must first gather unfiltered `TypeDependencies` from every document page and then traverse the combined edges. This limitation is documented; `IsComplete` does not claim the later page alone is a complete solution graph.
- Updated [Dependency Graph Core Scanner](../../../docs/navigation/dependency-graph.md) and the 5.5 acceptance rows. The 5.5 audit checkbox remains open for independent follow-up.

### Audit 1 remediation verification

| Gate | Result |
|---|---|
| Before-fix generic/qualified type regression | Failed as expected: generic Box edge missing |
| Focused DependencyGraphScanner FastTests after remediation | Passed, 10/10 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 380/380 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 392/392 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 2/3 of point 5.5

- Audited commit: `a1cc7068ce559d4b6b55b1c119209cbca269d41d` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. No build or tests were run in this audit; the remediation gate results above belong to the implementation turn.
- **Accepted — generic/qualified type edges:** `DependencyGraphScanner.cs:101-143` now resolves `TypeSyntax` through Roslyn and preserves `DependencyTypeReference` source/target type identity. `DependencyGraphScannerTests.cs:121-142` covers `Contracts.Box<Contracts.Item>` across projects and a type target query. External metadata remains gated by source locations.
- **Partial — document continuation:** `DocumentOffset`/`NextDocumentOffset` at `DependencyGraphScanner.cs:52-61` can advance the deterministic document window, and later pages correctly remain incomplete. The current test at `DependencyGraphScannerTests.cs:171-197` only advances across two empty-edge documents; it does not prove an edge is found after continuation or that document 1001+ is reachable.
- **Open — complete target traversal:** `DependencyGraphScanner.cs:143-148` calls `Traverse` only on `rawEdges` collected from the current document batch. The target BFS at `:197-244` has no persisted frontier or public traversal over an accumulated edge set. A first-hop edge in batch one followed by the second hop in batch two cannot be returned by any targeted call, despite `NextDocumentOffset`; the documentation explicitly instructs callers to aggregate unfiltered pages but provides no Core operation to traverse them. This does not meet the audit-1 bounded full-solution target-query acceptance.

### Remaining acceptance

1. **P1:** Make type/file targeted traversal cover the full solution across document windows through a resumable frontier or a public bounded traversal of accumulated edges. Preserve direction, depth, visited-cycle behavior, deterministic ordering, and explicit node-cap truncation. Test a target-to-middle-to-leaf path split across two document batches in both outgoing and incoming directions.
2. **P2:** Add a continuation regression with a real source dependency in a later window, including a document beyond the default 1000-document cap; assert project/type/file identity, cursor progression, and completeness on partial versus final results.

The point 5.5 audit checkbox remains unchecked for the final allowed point audit.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

### Audit 2 remediation

- Added the public `DependencyGraphTraversal.MergeAndTraverse` path. It accepts unfiltered scanner payload pages, validates both document-window and relationship-page continuity, merges source/target type edges across document batches, then performs target-file/type traversal over the combined graph. It retains direction, depth, type provenance, and deterministic ordering.
- Added a hard 200-type visited-node budget (lower limits are supported), plus `VisitedTypeCount`, `NodeLimitReached`, and `HiddenTypeDependencyCount`. `IsComplete` stays false when a document or relationship page is missing, when the merged input is not complete, when depth is clamped, or when the node budget cuts traversal short.
- Added a real 1002-document fixture: the Caller→Target edge is in the first window; Target→Dependency is in the continued window at offset 1000. The test asserts the resumed window contains that second dependency, a first-window-only merge stays incomplete, and `MergeAndTraverse` reconstructs the two-hop path with depths 1 and 2 for outgoing type, incoming type, and outgoing file targets. The same graph verifies the node cap and hidden-edge count.
- Clarified the documentation: bounded scan pages are stateless; callers gather all unfiltered document and relationship pages, then call the public merger/traverser. Incomplete or discontinuous batches cannot be reported complete. The final point-5.5 audit checkbox remains open.

### Audit 2 remediation verification

| Gate | Result |
|---|---|
| Before-fix generic/qualified type regression | Failed as expected: generic Box edge missing |
| Before-fix 1002-document path / merge capability | Baseline had no merge/traversal API; scan pages could not yield the cross-window two-hop result |
| Focused DependencyGraphScanner FastTests | Passed, 11/11 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 381/381 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 393/393 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 3/3 of point 5.5

- Audited commit: `5816da597c37d38a76bf84415e40f21b75093c89` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. Scope was limited to the two open audit-2 findings: cross-window targeted traversal, document 1001+ continuation, node limit, and completeness. No build or tests were run in this audit; the remediation gates above belong to the implementation turn.
- **Accepted — cross-window traversal and conservative incompleteness:** the public `DependencyGraphTraversal.MergeAndTraverse` (`DependencyGraphTraversal.cs:14-169`) rejects targeted input pages, combines direct type edges across document windows, and uses the same direction/depth traversal as the scanner. Missing pages set `ContinuationInputIncomplete`; document errors, nonzero result offset, depth clamp, or node cap also prevent `IsComplete` (`DependencyGraphModels.cs:111-112`). The multi-page completeness defect below remains.
- **Accepted — document continuation and cap:** `DependencyGraphScannerTests.cs:222-304` creates 1002 documents. The first 1000-document window contains Caller→Target, the resumed window at offset 1000 contains Target→Dependency, and the merged result returns both hops for outgoing type/file and incoming type queries. A first-window-only merge stays incomplete. The test also checks a one-node budget, `NodeLimitReached`, one hidden downstream edge, and false completeness. Scanner traversal itself now applies the same 200-type hard cap (`DependencyGraphScanner.cs:220-291`).
- **P2 open technical debt — complete relationship pages can falsely report incomplete.** `DependencyGraphTraversal.cs:174-190` advances `ArePagesComplete` by each collection's returned item count, while the scanner applies the same `Offset`/`PageSize` to all four independent collections (`DependencyGraphScanner.cs:172-175`). With `PageSize=100`, 101 type edges, and one project edge, passing pages at offsets 0 and 100 makes the project collection advance its expected offset to 1, so it rejects the valid second page at 100. `MergeAndTraverse` sets `ContinuationInputIncomplete=true` and `IsComplete=false` although every relationship page was supplied. The 1002-document regression uses only one relationship page per window and does not catch this. Acceptance: validate each collection's coverage against shared page windows while allowing shorter collections to be exhausted; preserve detection of skipped/duplicate pages. Add a regression with more than one type-edge page and fewer project/namespace/file edges, asserting the complete merged query reports `IsComplete=true` and a missing type page reports false.
- The point audit checkbox is complete at the third-audit limit, with this finding retained as technical debt. Public MCP transport, production/test scope, generated-file filtering, and handoff rendering remain later integration work and are not claimed here.

### Review verification

- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.6 — shared cross-project relationship contract

- Implementation baseline: `013d2b0` with a clean working tree. The shared scenario below extends that precedent across all Core relationship engines in one solution.
- Before-fix repro: the new three-project test reached the same five reference/impact sites but returned same-line sites in different orders: references depended on Roslyn enumeration, while impact sorted by caller and reached-from identity. The test also showed outgoing call-tree traversal omitted `Dispatcher.Dispatch`'s expression-bodied `handler.Handle()` call; the graph stopped before `Handler.Handle` and `Api.Record`.
- `FindReferencesResolver` now sorts sites by the shared depth/project/path/caller/reached-from identity keys used by `ImpactAnalyzer`, including a stable source-symbol key rather than opaque handle order. The same-line converging reference and impact sequences now compare exactly, including each reached-from handoff.
- `CallTreeBuilder.GetBodyNode` now returns expression-body syntax nodes for methods, constructors, properties, and accessors, so descendant traversal includes a root invocation or member access in an expression body.
- Added `CrossFeatureRelationshipContractTests` over Contracts → Middle → App. It checks outgoing and incoming call trees, a five-site transitive reference/impact chain (two direct override sites and three transitive sites, including both reached-from paths at one dispatcher line), interface method implementation discovery, abstract overrides, hierarchy edges, and successful project-specific handoff resolution. The limit test asserts display truncation and counts for references, impact, overrides, and hierarchy plus fan-out truncation and hidden edges for a call tree.
- Updated the references/implementations, impact, and call-tree current-state pages, and added the shared contract page to the docs index. The 5.6 independent point audit remains open.

### Point 5.6 verification

| Gate | Result |
|---|---|
| Before-fix cross-feature ordering and expression-body repro | Failed as expected: ordered caller sites differed and the outgoing graph stopped at the expression-bodied caller |
| Focused `CrossFeatureRelationshipContractTests` after fixes | Passed, 2/2 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 383/383 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 395/395 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 5.6

- Audited commit: `85a836f736fe202cb1d4b60665c736b38b19cbb4` (clean working tree before review edits). Reviewer: `gpt-6-sol`, medium reasoning effort. This was a read-only code and test audit; no independent build or test gate was run.
- **Accepted in the covered three-project scenario:** `CrossFeatureRelationshipContractTests.cs:19-124` compares all five ordered reference and impact sites, checks the two same-line converging origins and their project-bound handoffs, and follows outgoing/incoming call trees through the expression-bodied Middle methods. It also covers interface implementations, abstract overrides, base/interface/subtype handoffs, and result truncation in the shared fixture (`:126-165`). `CallTreeBuilder.GetBodyNode` includes arrow-expression nodes, so root invocations are visible to descendant traversal.
- **P2 open — repeated calls on one line diverge between references and impact.** `FindReferencesResolver.cs:84-87,115-126,143-156` records the source column and retains two locations that differ only by column. `ImpactModels.cs:7-15` has no column, and `ImpactAnalyzer.cs:86-107,117-137` groups by project/path/line/caller/depth/origin, collapsing those same two calls into one entry. For example, in one caller line, `Contracts.Api.Record(); Contracts.Api.Record();` yields two reference sites but one impact site and a smaller direct/transitive impact count. The new cross-feature equality test (`CrossFeatureRelationshipContractTests.cs:52-58,167-176`) has same-line *different* reached-from symbols, but never repeats one target on a line. Acceptance: choose and document a consistent call-site identity for both APIs, preferably retaining each source span/column in impact; add a shared cross-project regression with two calls to the same target on one line that compares ordered sites, totals, and display truncation. Keep distinct reached-from branches intact.
- Point 5.6 audit checkbox remains open while this contract mismatch is unresolved. The separate 5.5 multi-page completeness technical debt is unchanged.

### Review verification

- No production files were changed.
- The documentation diff was inspected and `git diff --check` passed before commit.

### Point 5.6 audit 1 remediation

- Before-fix reproduction: the shared Contracts → Middle → App test added two `Contracts.Api.Record()` calls on the same line in `Handler.Handle`. `FindReferencesResolver` returned six sites while `ImpactAnalyzer` returned five, so the new equality assertion failed as expected (`Expected: 6`, `Actual: 5`).
- `ImpactCallSiteEntry` now carries the one-based source column. Impact grouping includes column, and ordering uses the same location/provenance keys as references, preserving distinct same-line calls while retaining separate reached-from branches.
- The shared scenario now asserts six matching reference/impact sites, three direct and three transitive impact sites, unique columns for the two repeated calls, and identical ordered prefixes under `maxResults` truncation.
- Updated navigation and relationship contract documentation to describe source-column identity. The separate 5.5 multi-page completeness technical debt remains unchanged. The point 5.6 audit checkbox remains open for the final independent audit.

#### Verification

| Gate | Result |
|---|---|
| Before-fix focused repro | Failed as expected: reference count 6, impact count 5 |
| Focused `CrossFeatureRelationshipContractTests` after fix | Passed, 2/2 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 383/383 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 395/395 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 2/3 of point 5.6

- Audited commit: `dbd43f24fbc4a3a7a8dc83ab4ba5aa791b9b9bb3` (clean working tree before review edits). Reviewer: `gpt-6-sol`, medium reasoning effort. Scope was the audit-1 same-line call-site finding; no independent build or test gate was run.
- **Accepted — call-site identity and ordering:** `ImpactAnalyzer.cs:86-89,99-108,117-140` now captures the one-based source column, includes it in the de-duplication key, and sorts by the same project/path/line/caller/origin/column sequence as `FindReferencesResolver.cs:143-156`. `ImpactCallSiteEntry` exposes the column. Two calls to the same target from one caller on one line therefore remain separate, while distinct reached-from branches also remain separate.
- **Accepted — cross-project regression and limits:** `CrossFeatureRelationshipContractTests.cs:38-66,146-173,175-183` uses Contracts → Middle → App, adds two `Api.Record()` calls on one `Handler.Handle` line, and compares all six ordered reference/impact sites including column, caller and origin handoffs. It asserts two distinct columns, three direct and three transitive impact sites, matching total counts, and equal displayed entries with `maxResults: 1` plus truncation/incompleteness. The implementation's `Take(effectiveResultLimit)` applies to the already matched full ordering. The before-fix failure and the passing focused, build, fast, integration, and full gates are recorded above in the implementation remediation; this audit did not rerun them.
- The audit-1 P2 finding is closed. Point 5.6 meets its audited Core contract and its checklist checkbox is complete at audit 2/3. The unrelated point 5.5 multi-page completeness technical debt remains open.

### Review verification

- Inspected the committed source, tests, and current-state contract documentation; no production files were changed.
- The documentation diff was inspected and `git diff --check` passed before commit.

## Cluster 5 integration review 1 — fix round 0

- Reviewed commit: `76d620f722c8d56b4599959942c6cac7d9f786eb` (clean working tree before review edits). Reviewer: `gpt-6-sol`, medium reasoning effort. This first cluster integration review does not reopen or increment any point audit, especially the completed 5.5 limit of three audits.
- The shared Contracts → Middle → App scenario exercises CallTree, References, Impact, Implementations, and Hierarchy on the same Roslyn solution. References and Impact now retain the same ordered site identity (project, path, line, caller, reached-from symbol, column), including the two equal-line calls and same-line converging origins. Their Core handoffs resolve through the common source resolver. The incoming/outgoing call trees reach the expression-bodied chain, while each engine exposes its own depth, display, or node-limit state. The separate dependency scanner regressions cover same-named types in distinct projects, relationship paging, a real edge after document 1000, cross-window two-hop traversal, and a visited-node cap (`DependencyGraphScannerTests.cs:31-87,222-304`). The symbol-engine shared test does not claim dependency-graph handoff interoperability; the graph Core API accepts type/file/project target fields rather than source handoff strings.
- **P2 priority for cluster fix round 0 — merged relationship-page completeness:** the existing point 5.5 finding remains reproducible from `DependencyGraphTraversal.cs:174-190`: `ArePagesComplete` requires every collection's next `Offset` to equal the count returned by that collection. Scanner pages instead share one requested offset across independently sized collections (`DependencyGraphScanner.cs:172-175`). When type edges require a second page but project edges end on the first, complete input is falsely reported as `ContinuationInputIncomplete`, and `IsComplete` becomes false. Correct validation of shared page windows, including exhausted short collections; add a regression with more than one type-edge page and fewer project/namespace/file edges, and a missing-page control that must remain incomplete. This is the existing 5.5 technical debt promoted for the cluster fix round, not a fourth point audit.
- **Finding disposition:** no additional cluster-wide defect was established in the inspected Core interfaces. Cluster completion remains pending the P2 fix and its independent cluster-level review. Public MCP transport, generated-file/scope filtering, and graph handoff rendering remain later integration work.
- **Gates:** this documentation-only integration review ran no build or tests. The most recent recorded Cluster 5 implementation gates are the 5.6 remediation: focused tests 2/2, build with 0 warnings/errors, FastTests 383/383, integration 12/12, and full suite 395/395. `git diff --check` for this review passed before commit.

## Cluster 5 integration fix round 1 — shared relationship-page completeness

- Scope: close the existing 5.5 `ArePagesComplete` technical debt promoted by cluster integration review 1. This is a fix round, not a fourth point audit.
- Before-fix repro: synthetic shared-cursor pages contain 101 type edges at offsets 0 and 100 (`PageSize=100`) and one project edge only on offset 0. The focused contract test failed as expected because the merge marked the complete input `IsComplete=false`.
- `ArePagesComplete` now validates the shared page cursor using each page's `PageSize`, checks the expected number of items for that collection at each shared offset, and considers a short collection exhausted once its declared total is covered. A missing type continuation remains incomplete because type coverage stops before its declared total; skipped shared offsets are rejected.
- Added `MergeAndTraverse_AcceptsSharedOffsetPagesWhenShortCollectionsAreExhausted`, which checks the complete 101-type/1-project case and a missing-page control. Updated the dependency graph contract documentation with shared-cursor semantics.
- The 5.5 point audit checkbox and audit count remain unchanged; this fix round does not claim or perform another point audit.

### Verification

| Gate | Result |
|---|---|
| Before-fix focused repro | Failed as expected: complete shared pages reported incomplete |
| Focused shared-page contract test after fix | Passed, 1/1 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 384/384 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 396/396 across both test projects |
| `git diff --check` | Passed before commit |

## Cluster 5 integration review 2 — fix round 1

- Reviewed commit: `1a23aaf4fec1eaa5645fc38b851491648be24d21` (clean working tree before review edits). Reviewer: `gpt-6-sol`, medium reasoning effort. Scope was only the promoted shared-offset completeness finding and its interaction with merged traversal; this is not a fourth point 5.5 audit. No independent build or tests were run.
- **P2 closed — complete 101/1 shared pages:** `DependencyGraphTraversal.cs:174-199` now advances by each supplied page's shared `PageSize`, checks the expected collection count at that offset, and allows an exhausted short collection to return zero on later pages. The new regression (`DependencyGraphScannerTests.cs:307-356`) supplies 100 then one type edge at offsets 0 and 100, with a single project edge only at offset 0. `MergeAndTraverse` returns all 101 traversed type edges and the project edge, with `ContinuationInputIncomplete=false` and `IsComplete=true`. Omitting the offset-100 page leaves the declared type total uncovered and yields `ContinuationInputIncomplete=true` and `IsComplete=false`. Existing document-window continuation and node-cap paths remain separate and unchanged.
- **Cluster status:** no open Cluster 5 Core finding remains after this fix. The point 5.5 audit count stays at 3/3; its separate shared-offset checklist item is now complete. Public MCP transport, production/test scope and generated-file filtering, and graph handoff rendering remain later integration work.
- **Gates:** this documentation-only review did not rerun gates. The fix-round record above reports its before-fix failure, focused test 1/1, build with 0 warnings/errors, FastTests 384/384, integration 12/12, and full suite 396/396. `git diff --check` for this review passed before commit.
