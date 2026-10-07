# Navigation and assembly review evidence

Review date: 2026-10-07. Read-only product/architecture review; no code changed.

## Current implementation status

- The current product is a read-only MCP navigation server plus a separate offline assembly export CLI (`README.md:1-5`). `docs/` and current source/tests are the binding references under `AGENTS.md`; `tasks/ideen` had no existing task specifications to compare against.
- MCP source tools operate on loaded Roslyn solution documents. MCP assembly tools materialize decompiled source for a selected managed binary. The offline exporter instead emits a searchable tree/project per selected assembly and dependency closure (`docs/navigation/assembly-navigation.md:3`, `docs/navigation/assembly-decompilation.md:21`, `docs/assembly-export.md:3, 13-17`).

## Semantic relationships and tool overlap

- The relationship tools are distinct projections over static semantic evidence: `get_context.uses` gives direct incoming references, `find_references` can follow bounded caller chains and summarize sites, `get_call_tree` constructs bounded call graphs, and `get_type_relations` reports hierarchy or implementations (`docs/navigation/get-context.md:13`, `docs/navigation/relationship-contracts.md:9-19, 26`). This is useful for agents because the narrower query can be selected for the question; the overlap is intentional rather than duplicate API surface.
- Cross-feature identity is explicitly tested: `CrossFeatureRelationshipContractTests.RelationshipEngines_AgreeAcrossProjectsAndKeepHandoffsNavigable` and its scope/limit cases exercise shared project-aware handoffs and separate engine limits (`tests/AiNetCodeNavigator.FastTests/Symbols/CrossFeatureRelationshipContractTests.cs:22, 134, 214`). The shared evidence classifier lives in `RelationshipEvidence` (`src/AiNetCodeNavigator.Core/Symbols/RelationshipEvidence.cs:14, 25`), while the reference resolver retains traversal depth/node omissions (`src/AiNetCodeNavigator.Core/Symbols/FindReferencesResolver.cs:38, 57-80, 223-232`).
- Recommendation: for one symbol, begin with `get_context` and request only needed sections; use `find_references` for broader impact, `get_call_tree` for call paths, and `get_type_relations` for inheritance/implementation. Preserve each returned owner-bound reference and inspect its analysis/omission fields before claiming completeness. This follows the existing tool guide (`docs/tools/README.md:28, 49, 51`).

## Static test candidates

- `TestRecommendationBuilder` combines direct bound references, implementation expansion, optional bounded helper traversal, and name-derived fixture candidates (`src/AiNetCodeNavigator.Core/Symbols/TestRecommendationBuilder.cs:68-127, 165`). The payload marks its evidence mode `static-test-candidates-only` (`src/AiNetCodeNavigator.Core/Symbols/TestContextModels.cs:70-73`); source docs explain that attributes, runtime conditions, custom runners, and coverage are not evaluated (`docs/navigation/test-context.md:11, 15`).
- Integration contracts verify candidate activity metadata, helper-depth validation/path output, paged candidates, and that reached expansion limits remain partial (`tests/AiNetCodeNavigator.IntegrationTests/Mcp/SourceToolsContractTests.cs:31, 66, 1248`; see also helper-cap test at line 154).
- Recommendation: treat the section as a shortlist for source navigation. Open the returned fixture/method and verify the test actually exercises the changed behavior; run or inspect the actual test selection for runtime discovery, exclusions, generated tests, custom attributes, and dynamic dispatch. Name-derived fixture evidence alone is not a test-use claim.

## Assembly navigation and export

- MCP assembly navigation uses a decompiled Roslyn snapshot and does not execute the target (`docs/navigation/assembly-navigation.md:3`). Relationship APIs support selected-owner navigation and some bounded referenced-owner traversal; `get_context` can expand only direct uses across references, while transitive calls/references use their specialized tools (`docs/navigation/assembly-navigation.md:13, 19`). Cross-assembly implementation closure is explicitly absent for assembly `get_type_relations` (`docs/navigation/relationship-contracts.md:26`).
- The separate CLI is useful for broad/offline browsing across selected assemblies. It follows a bounded dependency closure and emits manifests, generated source trees, and diagnostics. A successful exit may still include partial exports; reconstructed projects are not guaranteed to compile (`docs/assembly-export.md:13-17, 60, 64, 68`). Its root dump is replaced on each run only when the ownership marker validates (`docs/assembly-export.md:23-25`).
- Recommendation: use MCP for targeted questions against one owner and supported relationship expansions; use the CLI when a broad local source tree is useful. Before relying on CLI output, check `last-run.log`, the catalog row, and the specific child manifest/completion state. Confirm dependencies actually appear in the current catalog; manifest dependency paths can describe planned edges whose exports failed (`docs/assembly-export.md:62, 64`).

## Static versus runtime boundary

All semantic relationships and test candidates are based on compiler/decompiled static evidence. They do not establish runtime dispatch, execution, or coverage. Assembly bodies are reconstructed text, and export projects are navigation aids rather than guaranteed buildable source. Keep that distinction explicit in agent answers; where runtime behavior matters, trace to and inspect the actual application/test execution path separately.
