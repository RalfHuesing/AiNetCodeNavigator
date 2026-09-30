# MCP Navigation Registration Status

The production catalog exposes all twenty navigation tool names and two maintenance tools. The current real-stdio integration test exercises the thirteen handlers added for Cluster 9.2 on a deterministic source solution and a small managed assembly. That is implementation evidence for the listed paths, not acceptance of the full public reference contract.

## Exercised handler paths

The source fixture exercises `get_call_tree`, `find_references`, `get_type_hierarchy`, `find_implementations`, symbol-mode `get_impact`, Git `change-context`, file-mode `dependency_graph`, `get_feature_context`, and `get_test_context`. It uses production, test, and generated documents in Core tests to verify filter-before-limit behavior and that filtered source handoffs resolve to bodies against the original solution identity.

The managed fixture exercises `get_assembly_context` with a symbol and body, `inspect_assembly`, `search_assembly`, `find_assembly_extensions`, assembly `resolve_type_origin`, assembly symbol `get_impact`, and the established `get_symbol_body` follow-up. The source Git fixture is a linked worktree whose root contains a `.git` file. The Assembly inspection consumer follows response-window pages and retries a line-safe page with its advertised minimum byte budget when 512 bytes cannot contain the next complete line.

## Current unverified contracts

- Assembly relationship reference closure is unverified beyond the exercised single managed fixture. Source scope tests verify filtered handoffs against the original full solution identity. Assembly handoff owner-path, content-hash, and generation behavior is not established by these fixture paths.
- Core domain cursors and outer MCP response-page tokens are separate today. For example, an `inspect_assembly` call limited to one type can return a Core cursor inside its serialized payload, but that token does not currently continue through the public `continuationToken` route. Do not treat response-window paging as proof of domain-cursor completeness.
- The full public parameter matrix remains open: exact defaults and caps, generated and production/test scopes on every applicable handler, file and symbol targeting rules, stable pattern deduplication, source retry/error behavior, and the seven existing tools' reference default parity.
- Git change-context evidence covers a changed tracked source file in a temporary `.git`-file worktree. Statuses for malicious and missing refs, staged/unstaged/untracked/deleted files, non-Git and clean repositories, incomplete symbol mapping, and child-process cancellation have not been verified.
- Public byte/token recovery, domain truncation, continuation replay, and stale target/hash/generation behavior do not yet have a complete per-tool matrix. Larger body and find results have not been verified for line-safe rendering without escaped monolithic JSON.
- A cold source fixture verifies no workspace writes. Same-project-name isolation across two solutions and external MSBuild target redirection remain unverified.

The current registration and fixture evidence is recorded in the [Cluster 9.2 implementation record](../../tasks/AiNetLinter-Uebernehmen/Reviews/Cluster-09.md#point-92-registered-handler-slice--contract-work-open). Point 9.2 and dependent point 8.5 remain open until the remaining public contracts are implemented and tested.
