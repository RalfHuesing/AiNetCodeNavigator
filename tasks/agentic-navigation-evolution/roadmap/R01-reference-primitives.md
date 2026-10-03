# R01 — Reference primitives and exact resolvers

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: None. Next: [R02](R02-public-reference-migration.md).

Contract: [01 — grammar, ownership, unavailable references and errors](../01-symbol-identity-and-recovery.md).

Implement the shared typed source/assembly reference model and codec, deterministic Git/worktree/non-Git base selection, relative project matching, exact Roslyn declaration lookup and owner-bound assembly lookup. Keep declaration selection separate from snapshot hashing. Reuse existing metadata/provenance/session owners rather than introducing another assembly-loading subsystem.

Inventory every current handoff producer/consumer and handle-only component. Record the actual inventory in this point's evidence so R02 covers every path. Include physical-file inputs that also accept handles, candidate/body/context routes, text/Mermaid renderers, test helpers, configuration and docs.

Acceptance: all reference-level cases in specification 01 pass, including malformed escaping, no-ID declarations, exact duplicate-owner ambiguity, multi-context behavior, partial/linked sources and same-name binary owner pairing. Resolution never chooses a first match or invalidates an unchanged declaration due to content changes.

Verification: official build and affected FastTests for the new codec/source/assembly resolvers and path ownership. Add focused integration coverage where exact assembly provenance requires it. Tests for the new resolver do not depend on the old registry.

Boundary: no new public emission or dual public mode yet. The old public behavior remains until the atomic R02 switch; this preparatory commit must not be advertised as the completed migration.

Completion checklist:

- [ ] R01.1 — Shared codec, owner-base selection and exact source/assembly resolvers implement specification 01; no public dual mode is enabled.
- [ ] R01.2 — Complete producer/consumer/removal inventory is recorded from current local sources.
- [ ] R01.3 — Point acceptance and its required build/test selections passed; exact commands and results are recorded.
- [ ] R01.4 — Implementation commit(s) and the internal-only boundary are recorded; the orchestrator reviewed R01.1–R01.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | In progress; implementation round 1 assigned to a separate `gpt-6-luna` / high agent on 2026-10-03 |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | No blocker identified. Complete internal primitives and inventory, execute official gates, then independent `gpt-6.1-sol` / medium read-only audit |

### Initial inspection

- Starting HEAD: `4729bb8f27816af0f5244284bacbd0a88b407f85` (`main`); working tree and index clean.
- R01 is the first unchecked point; no implementation completion evidence exists for R01–R08.
- Read AGENTS.md, all eight linked repository rules, the task README/index/shared execution contract, R01 and specifications 01/04.
- The existing public source/assembly paths still use `HandoffHandleRegistry`, snapshot-bound identifiers and their existing resolvers. R01 preserves that public behavior; R02 owns the switch.
- Only the orchestrator runs verification scripts, stages/commits or edits checkboxes. The implementation agent has explicit internal-only file boundaries and must report a complete removal inventory and actual test selections.
