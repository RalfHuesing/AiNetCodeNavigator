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
