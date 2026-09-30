# Call Tree Core Engine

The Core call tree engine builds a bounded graph around a Roslyn symbol. Incoming traversal groups source references by caller. Outgoing traversal resolves invocations, explicit and target-typed object creation, and non-invoked member access from the symbol's declaration bodies. A type seed returns the type and up to five method hints without expanding a graph.

`RequestedDepth` is clamped to 1–5. `TopN` is clamped to at least one edge per expanded symbol; in `Both` mode the incoming groups are considered first, then outgoing groups use the remaining budget. The builder stops at 250 distinct nodes. `Truncated` is true when known edges were omitted or queued nodes remain unexpanded. `HiddenEdgeCount` counts only discovered groups omitted by `TopN` or the hard node cap; `PendingNodeCount` counts queued nodes whose outgoing or incoming relationships were not scanned. Reaching exactly 250 fully expanded terminal nodes does not by itself mark the graph truncated. Symbols are deduplicated with Roslyn symbol equality, call sites for the same edge are combined, and node IDs are local `n1`, `n2`, ... labels.

Outgoing source targets are always retained, including targets in `System` or other framework-named namespaces. Metadata targets are retained by default unless they resolve to BCL assemblies or namespaces. Set `IncludeBcl` to include those framework targets too.

Source-backed nodes carry opaque `h:` handoff IDs when a canonical source identity can be produced. ASCII output lists graph edges, handoffs, known omitted edges, and pending expansions; Mermaid output uses the same nodes and edges, places handoffs in comments, and annotates pending expansions. Mermaid labels replace quotes and line breaks so symbol names and paths do not break the flowchart syntax.

The Core entry point throws `ArgumentNullException` for a missing request, solution, or seed symbol. Both renderers throw `ArgumentNullException` for a missing payload. Cancellation is honored during traversal. The MCP `get_call_tree` tool contract and its transport behavior are documented with the later MCP tool integration.
