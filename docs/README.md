# Documentation Index

The binding product references are these current-state pages and local Navigator code and tests. Task specifications describe acceptance requirements.

## Development

- [Build and Tests](development/build-and-tests.md): Solution structure, PowerShell scripts, and static log files in `temp/`.
- [MCP Response Budgets](mcp-response-budgets.md): Host-side UTF-8 limits, token accounting, explicit truncation, and recoverable undersized-budget results.
- [MCP Tool Results](mcp-tool-results.md): Internal `CallToolResult` builders, status text, structured success content, and error classification.
- [MCP Argument Validation](mcp-argument-validation.md): SDK input-schema validation, binder compatibility checks, safe field paths, and error-budget behavior.
- [MCP Long-Running Calls and Continuations](mcp-long-running-calls.md): Operation token lifecycle, polling, immutable text snapshots, continuation tokens, and their limits.
- [MCP Host](mcp-host.md): Stdio lifecycle, nineteen navigation and two maintenance registrations, health scope, and reloadable host settings.
- [MCP Tools](tools/README.md): All 21 tools, targets, wire arguments, defaults, paging, recovery, and usage examples.
- [Setup](setup/README.md): Windows executable, stdio process configuration, host settings, and current Claude Desktop, Cursor, and Antigravity setup references.
- [MCP Navigation Registration Status](navigation/mcp-registration-status.md): Current transport-free handler contracts and the boundary around retained end-to-end tests.

## Navigation

- [Index Scope](navigation/get-index-scope.md): Roslyn solution and project document inventory, bounds, completeness, errors, and read-only behavior.
- [Namespace Tree Core Scanner](navigation/get-namespace-tree.md): Source namespace hierarchy, project aggregation, depth and result bounds, truncation, and read-only behavior.
- [Shared Symbol Resolution](navigation/symbol-resolution.md): Identifier forms, ambiguity candidates, handoff roundtrips, and recoverable errors for follow-up scanners.
- [Find Symbol](navigation/find-symbol.md): Name and pattern matching, kind filters, and source scope behavior for the Core symbol scanner.
- [Get Symbol Body](navigation/get-symbol-body.md): AST declaration extraction, batching, line windows, and unavailable-source cases.
- [Get File Skeleton](navigation/get-file-skeleton.md): Top-level declarations without method bodies, structured Core results, Markdown rendering, and handoff IDs.
- [Get Class Structure](navigation/get-class-structure.md): Declared members, visibility, records and interfaces, filters, truncation, and handoffs.
- [Call Tree Core Engine](navigation/get-call-tree.md): Bounded Roslyn call graph traversal, source handoffs, and ASCII/Mermaid rendering.
- [Find References and Implementations Core Engines](navigation/find-references-and-implementations.md): Solution-wide reference locations, implementation and override discovery, result limits, and project-bound handoffs.
- [Symbol Impact Core Engine](navigation/impact-analysis.md): Transitive caller traversal, affected project summaries, limits, completeness, and handoffs.
- [Get Type Hierarchy Core Engine](navigation/get-type-hierarchy.md): Base chains, interfaces, transitive cross-project subtypes, source handoffs, and subtype limits.
- [Dependency Graph Core Scanner](navigation/dependency-graph.md): Project and source type dependencies, project-qualified edges, paging, scan bounds, and recoverable document errors.
- [Resolve Type Origin](navigation/resolve-type-origin.md): Source and metadata type origin results, exact source project ownership, and assembly-reference lookup.
- [Cross-Feature Relationship Contract](navigation/relationship-contracts.md): Shared source handoffs, caller-site identity, cross-project relationship behavior, and per-engine result limits.
- [Test Context](navigation/test-context.md): Test project/file/class detection and heuristic fixture and method recommendations.
- [Get Feature Context](navigation/get-feature-context.md): Combined declaration, caller and test candidate context, scope, handoffs, errors, and result limits.
- [Assembly Decompilation Core](navigation/assembly-decompilation.md): Read-only binary fingerprinting, decompilation cache generations, native/invalid image handling, and the virtual Roslyn snapshot.
- [Assembly Navigation Core Scanners](navigation/assembly-navigation.md): Assembly context, bounded text/data/external-call searches, extension method discovery, and referenced type origins.
