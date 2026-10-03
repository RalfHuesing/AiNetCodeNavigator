# MCP Navigation Registration Status

The production catalog exposes exactly 17 read-only navigation tools. `get_context` is the single shared context route for Source and Assembly targets; the catalog has no separate feature, test, or assembly context route. SDK definitions are the authority for wire names, input schemas, defaults, and validation. The [tool reference](../tools/README.md) documents the current contract.

Transport-free handler tests verify source and assembly routing, owner-bound handoffs, per-tool cursor and response-budget behavior, snapshot validation, and read-only target access. The `get_context` tests cover selected Source and Assembly sections, shared declaration identity, caller/test scope independence, one-section result cursors, invalid combinations, error aggregation, generated-source filtering, and direct assembly-owner callers. Full E2EIntegration is excluded from routine gates and was not run for this implementation.

`get_context` resolves one target symbol and shares the resulting owner identity, snapshot, and lease across only the selected sections. It exposes direct callers, while transitive impact remains the separate `get_impact` workflow. Static tests are recommendations from the reusable `TestRecommendationBuilder`; they do not measure execution or coverage. Assembly reference-owner expansion applies only to direct callers.
