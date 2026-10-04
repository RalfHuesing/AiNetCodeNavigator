# Deferred findings and evidence

[Roadmap](roadmap.md) · [Execution policy](roadmap/execution.md)

The user approved deferring additional evidence on 2026-10-04 so the complete implementation can finish. This file distinguishes unexecuted evidence from confirmed functional defects. Deferred checks are not claimed passed. Root owns disposition and progress; follow-up work does not authorize a push or deployment.

| ID | Point | Status | Missing evidence / observation | Reason for deferral | Follow-up / acceptance | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| F01 | R03 | Deferred evidence; no executed defect | Cross-platform and manifest path-replacement stress cases for captured analyzer dependency selection. Windows read-handle sharing was source-reviewed; no POSIX race was executed. | Expanded platform matrix is separate from current Windows behavioral gates. | Focused official Fast tests must prove the resolver consumes the captured manifest and reports/retries changing inputs without partial publication on each supported platform. | Future reviewer |
| F02 | R03 | Deferred evidence; no executed defect | Stronger byte-equivalence evidence for already loaded shared runtime/pinned-host assemblies versus physical images at the same location. Private images bind actual captured shadow bytes; shared images use the trusted host boundary. | Additional host mutation/provenance scenarios must not become an unbounded proof loop. | Exercise an actual supported shared-host replacement scenario through official scripts, or document the supported immutable-host lifetime boundary with concrete loaded/captured identities. | Future reviewer |
| F03 | R03–R06 | R03/R04 expanded matrix explicitly deferred; subsequent point disposition remains explicit | Expanded timing matrices and repeated performance runs beyond focused behavioral work-count assertions. R03's actual five-run identity/refresh measurements remain in its evidence; no unrun matrix is a result. | User prioritizes completed implementation; deterministic behavior/work-count regressions remain in the focused gates. | Run the existing/new controlled measurement selections through official scripts; record five samples, setup boundaries, semantic/work counts and median/range without universal performance claims. | Future reviewer |

## Confirmed findings

R03-P03-D01 is closed. Its original confirmed defect was successful identity despite consumption of an untracked Default-load-context external DLL. The approved actual-creator contract now rejects the unknown generator with a concrete workspace diagnosis before reuse. The actual regression passes in the final 92-test Fast selection; genuine supported creator/public-handler replacement and generated body 17→18 also pass. The separate Sol auditor reviewed the correction and actual gates. Original failing evidence and correction commands remain in [R03](roadmap/R03-snapshot-identity.md); this defect was corrected, not deferred.

## Structural scope

The verified R02/R03 checkpoints and [R04 structural review](roadmap/R04-single-collection.md#smallness-and-structural-checkpoint) remain in their point evidence. R04 centralizes collection/projection semantics, removes repeated host scans and adds a separately executable graph contract class. RelationshipTools still mixes route-specific recovery/formatting, and existing large contract classes still mix route scenarios/fixtures. Required roadmap extraction covers retained facts, outgoing traversal and operation progress. Blanket handler/registration rewrites, line-count-driven class splits, global fixture/parser/test-framework reorganization and E2E rewrites remain outside this task.

R04-A01–A06 are closed after actual failing baselines, minimal corrections, 22 Fast/9 Integration PASS and independent delta audit. They were functional corrections, not deferred evidence; complete commands/results remain in R04.
