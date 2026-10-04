# Deferred findings and evidence

[Roadmap](roadmap.md) · [Execution policy](roadmap/execution.md)

The user approved deferring additional evidence on 2026-10-04 so the complete implementation can finish. This file distinguishes unexecuted evidence from confirmed functional defects. Deferred checks are not claimed passed. Root owns disposition and progress; follow-up work does not authorize a push or deployment.

| ID | Point | Status | Missing evidence / observation | Reason for deferral | Follow-up / acceptance | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| F01 | R03 | Deferred evidence; no executed defect | Cross-platform and manifest path-replacement stress cases for captured analyzer dependency selection. Windows read-handle sharing was source-reviewed; no POSIX race was executed. | Expanded platform matrix is separate from current Windows behavioral gates. | Focused official Fast tests must prove the resolver consumes the captured manifest and reports/retries changing inputs without partial publication on each supported platform. | Future reviewer |
| F02 | R03 | Deferred evidence; no executed defect | Stronger byte-equivalence evidence for already loaded shared runtime/pinned-host assemblies versus physical images at the same location. Private images bind actual captured shadow bytes; shared images use the trusted host boundary. | Additional host mutation/provenance scenarios must not become an unbounded proof loop. | Exercise an actual supported shared-host replacement scenario through official scripts, or document the supported immutable-host lifetime boundary with concrete loaded/captured identities. | Future reviewer |
| F03 | R03–R06 | Available for explicit deferral | Expanded timing matrices and repeated performance runs beyond focused behavioral work-count assertions. No unrun measurement is a result. | User prioritizes completed implementation; deterministic behavior/work-count regressions remain in the focused gates. | Run the existing/new controlled measurement selections through official scripts; record five samples, setup boundaries, semantic/work counts and median/range without universal performance claims. | Future reviewer |

## Confirmed findings

R03-P03-D01 is a confirmed functional defect, not deferred evidence: the Default-load-context generator consumes an external DLL absent from its captured inputs while identity succeeds. Its actual failing regression and independent Sol diagnosis are recorded in [R03](roadmap/R03-snapshot-identity.md). The approved creator contract correction must close it before R03 implementation completion.

## Structural scope

The prior verified R02 checkpoint and current R03 review remain in their point evidence. Required roadmap extraction covers identity, collection/projection, retained facts, outgoing traversal and operation progress. Blanket handler/registration rewrites, line-count-driven class splits, global fixture/parser/test-framework reorganization and E2E rewrites remain outside this task.
