# Call Tree Core Engine

The Core call tree engine builds a bounded graph around a Roslyn symbol. Incoming traversal groups source references by caller. Outgoing traversal resolves invocations, explicit and target-typed object creation, and non-invoked member access from the symbol's declaration bodies. A type seed returns the type and up to five method hints without expanding a graph.

`RequestedDepth` is clamped to 1–5. `TopN` is clamped to at least one edge per expanded symbol. The builder stops at 250 distinct nodes and reports `Truncated` with a positive `HiddenEdgeCount` when the fan-out or node cap omits edges. Symbols are deduplicated with Roslyn symbol equality, call sites for the same edge are combined, and node IDs are local `n1`, `n2`, ... labels.

Source-backed nodes carry opaque `h:` handoff IDs when a canonical source identity can be produced. ASCII output lists graph edges and handoffs; Mermaid output uses the same nodes and edges and places handoffs in comments. Mermaid labels replace quotes and line breaks so symbol names and paths do not break the flowchart syntax.

The Core entry point throws `ArgumentNullException` for a missing request, solution, or seed symbol. Both renderers throw `ArgumentNullException` for a missing payload. Cancellation is honored during traversal. The MCP `get_call_tree` tool contract and its transport behavior are documented with the later MCP tool integration.
