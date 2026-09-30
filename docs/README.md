# Documentation Index

## Development

- [Build and Tests](development/build-and-tests.md): Solution structure, PowerShell scripts, and static log files in `temp/`.

## Navigation

- [Index Scope](navigation/get-index-scope.md): Roslyn solution and project document inventory, bounds, completeness, errors, and read-only behavior.
- [Get File Tree Core Scanner](navigation/get-file-tree.md): Physical tree traversal, summary aggregates, filters, bounds, and read-only behavior.
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
- [Cross-Feature Relationship Contract](navigation/relationship-contracts.md): Shared source handoffs, caller-site identity, cross-project relationship behavior, and per-engine result limits.
- [Test Context](navigation/test-context.md): Test project/file/class detection and heuristic fixture and method recommendations.
- [Get Feature Context](navigation/get-feature-context.md): Combined declaration, caller and test candidate context, scope, handoffs, errors, and result limits.
- [Assembly Decompilation Core](navigation/assembly-decompilation.md): Read-only binary fingerprinting, decompilation cache generations, native/invalid image handling, and the virtual Roslyn snapshot.
