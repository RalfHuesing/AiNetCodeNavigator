# Cluster 5 Review Log

## Point 5.1 implementation

- Base commit: `76d1252519214e317e5f77d3472b5163c231d06d`; the working tree was clean before this implementation slice.
- Read-only AiNetLinter comparison covered `CallGraphTreeBuilder`, `OutgoingCallScanner`, the override-declaration filter, the ASCII/Mermaid renderers, and `CallGraphTraversalTests`. AiNetLinter includes constructor/object-creation and member-access targets, clamps `topN` to at least one, enforces its graph node cap, excludes BCL symbols by default, excludes override declarations from incoming edges, and sanitizes Mermaid line breaks and quotes.
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
- Reviewer: `gpt-6-sol`, medium reasoning effort. Read-only comparison used AiNetLinter's `CallGraphTreeBuilder`, `OutgoingCallScanner`, and call graph contracts. No build or tests were run in this audit; the implementation gates above are reported from the prior implementation record only.
- Outcome: Open findings below. The 5.1 audit checkbox stays unchecked. The implementation's constructor/member access coverage, source handoffs, null input handling, and basic Mermaid escaping have relevant tests, but the following contract gaps need fixes and regression coverage.

### Findings

1. **P1 — Third-party metadata calls disappear when `IncludeBcl` is false.** `src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs:167` rejects every target without a source location. A source method calling a non-BCL referenced assembly method therefore has no outgoing edge under the default request. AiNetLinter's `OutgoingCallScanner.AddOutgoingSymbol` excludes external targets only when `IsBclSymbol` also holds. Acceptance: distinguish BCL from other metadata assemblies, preserve the latter by default, and test a source caller with both a non-BCL metadata callee and a BCL callee under both `IncludeBcl` settings.
2. **P2 — `TopN` applies twice for `Both`.** `CallTreeBuilder.cs:118-122` and `:219-223` take `TopN` separately from incoming and outgoing groups. With `TopN=1`, a seed with at least one caller and one callee emits two incident edges, whereas AiNetLinter's graph builder combines direction groups and applies one `Take(TopN)`. Acceptance: bound the combined expansion to `TopN` per expanded symbol, maintain deterministic direction/order choice, count hidden groups once, and cover a `Both` graph with callers and callees.
3. **P2 — The 250-node stop misreports completeness.** `CallTreeBuilder.cs:47` stops as soon as the node list reaches 250; `:347-353` then sets `Truncated` from that equality, even when the graph has exactly 250 complete terminal nodes. If queued nodes still await expansion, their undiscovered edges are absent but `HiddenEdgeCount` can remain zero. This contradicts `docs/navigation/get-call-tree.md:5`, which promises a positive hidden count for cap omissions. Acceptance: distinguish actual hard-cap omission from merely reaching 250 nodes, report remaining work without claiming a known edge count that was never scanned, and cover both an exactly complete 250-node graph and one with pending expansion at the cap.

### Review verification

- Inspected the fixed commit and AiNetLinter source; no production files were changed.
- The documentation diff was inspected and `git diff --check` passed before commit.

## Independent audit 2/3 of point 5.1

- Audited commit: `6b13f078b672f5575e4e4182d2d7477ccb18ca9b` (clean working tree before review edits).
- Reviewer: `gpt-6-sol`, medium reasoning effort. Reviewed the implementation diff, the four new call-tree regression tests, the ASCII/Mermaid pending-node output, and AiNetLinter's `OutgoingCallScanner.AddOutgoingSymbol` and `IsBclSymbol` behavior. No build or tests were run in this audit; the gate results above belong to the implementation turn.
- The three audit-1 findings are resolved on this commit: metadata callees from a third-party assembly remain visible with default `IncludeBcl`; `Both` consumes one combined fan-out budget; and an exact, completed 250-node graph is not truncated while queued expansion is reported separately from known hidden edges. Each case has a targeted FastTest; the pending count is rendered in both formats.
- One new BCL-filter finding remains. The 5.1 audit checkbox stays unchecked pending the fix and the final allowed point audit.

### Finding

1. **P2 — Source symbols in framework-named namespaces are filtered as BCL.** `src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs:169` calls `IsBclSymbol` for every callee. `:248-269` classifies by namespace even when `target.Locations` has a source location. Thus a workspace method such as `System.Local.Call()` disappears from a default outgoing graph. AiNetLinter's `OutgoingCallScanner.AddOutgoingSymbol` first checks `isExternal` and only excludes an external symbol that is BCL. Acceptance: retain every source-backed callee irrespective of its namespace/assembly name, while still excluding external BCL metadata and retaining non-BCL metadata by default; add a source-backed `System` namespace regression alongside the existing metadata test.

### Review verification

- Inspected only committed source/tests and the AiNetLinter read-only reference; no production files were changed.
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
- **Accepted:** `CallTreeBuilder.cs:172-173` now applies the BCL classifier only to external symbols. The new FastTest at `CallTreeTests.cs:205-229` covers a source-backed `System.Local.Api.Call()` under the default setting. The pre-existing `CallTreeTests.cs:170-202` covers a third-party metadata callee retained by default and a framework metadata callee excluded by default but included with `IncludeBcl=true`. This matches AiNetLinter's external-only filter. No point-5.1 findings remain open at the third-audit limit.
- The point 5.1 audit checkbox is complete. Public MCP transport and end-to-end contract verification remain assigned to later roadmap clusters and were not inferred from this Core audit.

### Review verification

- Inspected the committed source and regression tests; no production files were changed by this audit.
- The documentation diff was inspected and `git diff --check` passed before commit.

## Point 5.2 implementation

- Base commit: `a028777123b51709134c3f9f3bb1af3f79c0cb69`; the working tree was clean before this slice.
- Read-only AiNetLinter comparison covered `FindReferencesTool`, `FindImplementationsTool`, and their FastTests. The reference clamps result limits to at least one, reports total and truncation for implementations, searches interface type implementations transitively across the solution, finds interface method implementations and virtual/abstract method overrides, and emits opaque follow-up handles.
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
- Reviewer: `gpt-6-sol`, medium reasoning effort. Read-only comparison covered AiNetLinter's `FindReferencesTool`, `CallGraphTraversal`, and `FindImplementationsTool`, including its type, method, and property dispatch. No build or tests were run in this audit; the implementation gate results above are from the prior turn.
- Direct cross-project references, two identically named implementation types in distinct projects, interface and abstract method implementations, indirect derived classes, handoff roundtrips, nonpositive result limits, and null inputs have relevant Core tests. Public identifier-resolution and MCP error formatting remain later integration work. The point 5.2 audit checkbox stays unchecked for the findings below.

### Findings

1. **P1 — `find_references` depth contract is absent from the Core engine.** `src/AiNetCodeNavigator.Core/Symbols/FindReferencesResolver.cs:20-24` exposes only a direct-reference query; `:32-77` processes references to the seed once and cannot follow callers. AiNetLinter's `FindReferencesTool.ExecuteResolvedAsync` invokes `CallGraphTraversal.ExpandAsync` with the requested depth, and its `FindReferencesToolTests.cs:398-424` verifies a real A ← B ← C chain at depth two. A future MCP wrapper cannot provide that contract through this engine. Acceptance: support bounded depth traversal (reference cap three) with origin/depth per call site, cycle and node limits, and explicit completeness/truncation; test a cross-project caller chain at depths one and two and a capped request. Keep the result a navigation query without linter metrics.
2. **P2 — Property implementations/overrides lack the reference's explicit dispatch.** `FindReferencesResolver.cs:123-133` applies `SymbolFinder.FindImplementationsAsync` to all non-type symbols and adds `FindOverridesAsync` only for `IMethodSymbol`. AiNetLinter's `FindImplementationsTool.FindPropertyImplementationsAsync` uses `FindOverridesAsync` for virtual/abstract/override properties and `FindImplementationsAsync` for interface properties. No Core test covers either property case. Acceptance: route supported property symbols by interface versus override semantics and cover a cross-project interface property implementation and an abstract/virtual property override; preserve distinct source handoffs.
3. **P2 — Unsupported implementation targets have no recoverable result.** `FindReferencesResolver.cs:110-133` sends every non-interface named type to `FindDerivedClassesAsync` and every other symbol to `FindImplementationsAsync`, whereas AiNetLinter's `FindRawImplementationsAsync` rejects unsupported symbol kinds and non-virtual concrete methods with an explanatory error. The Core model has no status/error field and tests only null arguments. Acceptance: distinguish an unsupported target from a valid target with zero implementations using a typed result or documented exception that the MCP layer can map to a clear error; cover a struct and a non-virtual ordinary method without conflating them with empty interface results.

### Review verification

- Inspected committed Core source/tests and the AiNetLinter read-only reference; no production files were changed.
- The documentation diff was inspected and `git diff --check` passed before commit.
