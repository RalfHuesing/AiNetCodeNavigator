# Luna concept review

Review date: 2026-10-03. Starting HEAD: `033ac7aa57f5fec87088a4dcca5a2bd1709d2d26`. This records a documentation review, not implementation acceptance.

Three independent read-only subagents used `gpt-6-luna` with high reasoning. Their assignment was to identify missing decisions, contradictory contracts and wording that a later implementation agent could interpret differently. The primary agent checked their findings against local sources, corrected the owning specifications and requested another review of the corrected documents. The reviewers changed no files and ran no product gates.

## Review disposition

| Reviewer | Contracts | First findings | Re-review |
| --- | --- | --- | --- |
| review_identity | [01](../01-symbol-identity-and-recovery.md), related ownership contracts | 7 | PASS: all seven closed |
| review_dependencies | [02](../02-dependency-graph-analysis.md), [04](../04-snapshot-refresh-and-analysis-cache.md), related roadmap steps | 10 | PASS: ten original findings and four follow-up subjects closed |
| review_polling_roadmap | [03](../03-long-running-operations-and-transport.md), [roadmap](../roadmap.md) | 5 | PASS: all five closed |

## Corrections retained in the specifications

- Reference preprocessing, canonical escaping/path validation, Git/worktree base selection, exact Roslyn symbol round-trip, duplicate owner contexts, unavailable-reference output and drive-path routing are defined in [01](../01-symbol-identity-and-recovery.md).
- Coverage versus fresh work, scope filtering, file seeds, representative evidence, deterministic ordering and common depth/node/hidden-edge semantics are defined in [02](../02-dependency-graph-analysis.md).
- Progress phases/counters, fixed control-budget recovery, transport-component classification, actual-client evidence and historical EOF disposition are defined in [03](../03-long-running-operations-and-transport.md).
- Fresh-request versus retained-operation boundaries, captured metadata bytes, typed fingerprint inputs, exact cache keys, overlapping document subscribers, cancellation quiescence and deterministic retention accounting are defined in [04](../04-snapshot-refresh-and-analysis-cache.md).
- The [roadmap index](../roadmap.md) has eight point checkboxes; each linked file under `roadmap/` has four acceptance checkboxes. Only the orchestrator may tick them, after checking actual acceptance, executed evidence and verified implementation commits. All 40 remain unchecked.

The dependency review's second pass found four remaining subjects: option-surface/language coverage, absent/type-free file roots, fingerprint byte framing and cache-key accounting. Those were clarified before its final re-review. One premise was corrected using an isolated reflection inspection of the pinned local Roslyn 5.9.0 assemblies: CompilationOptions.Features, ReferencesSupersedeLowerVersions, CurrentLocalTime and CSharpCompilationOptions.TopLevelBinderFlags are non-public, although their XML documentation contains members. ParseOptions.Features is public. Specification 04 therefore defines supported public option construction/provenance and excludes private reflection rather than requiring access to those internal members.

All three reviewers reported PASS after the corrections. No known blocking contract finding remained at review completion. This report is evidence only. The four specifications remain the authoritative behavioral contracts; this report must not introduce additional acceptance requirements.

After that review, the roadmap was split into a compact index, shared execution rules and eight point files. Step requirements and all 40 checkbox items were preserved; links and evidence locations were adjusted. This structural change does not claim another Luna review or production verification.

## Verification boundary

Reviewed documentation diffs, local Markdown targets/anchors, code-fence balance, R01–R08 ordering and all 40 unchecked roadmap boxes; `git diff --check` passed before the documentation commit. No production code changed; no product build/test gate was run or claimed to pass. The later orchestrator must execute the implementation gates in the roadmap.
