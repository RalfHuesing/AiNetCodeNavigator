# R02 — Switch all public routes and remove handles

[Index](../roadmap.md) · [Shared execution rules](execution.md)

Prerequisite: [R01](R01-reference-primitives.md). Next: [R03](R03-snapshot-identity.md).

Contract: [01 — output/input integration and complete removal](../01-symbol-identity-and-recovery.md).

Use R01 references in every inventoried output/input and owner hop. Preserve existing field names, renderer formats and raw discovery selectors. Preserve snapshot-bound pages, analysis evidence and assembly generation leases. Replace handle-only recovery with the specified reference errors/actions.

Delete Base62 registry/counter/alphabet/persistence/locks, runtime setup/disposal, handle-only settings, old `i:` symbol serialization and obsolete tests/errors. Extract independent hashing/path/cursor behavior before removing shared classes. No compatibility flag or old-handle resolution path survives. Update the inventory with each path's completed migration/removal.

Acceptance: discovery → body/unrelated edit → follow-up → runtime restart succeeds for an unchanged exact declaration. Rename/delete/changed-ID cases fail clearly. Source/assembly/skeleton/context/graph/body/candidate paths use only new references. Byte/token budgets and outer/domain continuations remain executable; mixed batches preserve successes. Active handle machinery and counter-file I/O are absent.

Verification: official build; affected FastTests across symbols, assemblies, structures, dependencies, calls, hierarchy, formatting and schemas; source/assembly/relationship/index-scope transport-free handler contracts; narrowly selected relevant ExtendedIntegration tests under repository rules. Search active source/config/tests/docs for legacy machinery and classify each remaining legacy literal (negative test or historical artifact). Do not remove arbitrary files outside this repository, including a user's old counter file.

Update affected current-state docs, tool descriptions, navigation rule 08 and test helper assumptions in this point. Final public schemas must expose the specified input semantics without a second ID.

Completion checklist:

- [ ] R02.1 — Every R01 inventory route emits/consumes the new references; handle-only runtime/configuration/persistence paths are removed.
- [ ] R02.2 — Edit/restart/error/owner/budget/paging acceptance and required build/test selections passed; remaining legacy literals are classified.
- [ ] R02.3 — Current-state docs, schemas/descriptions, navigation rule 08 and test helpers match the public switch.
- [ ] R02.4 — Inventory disposition, executed evidence and implementation commit(s) are recorded; the orchestrator reviewed R02.1–R02.3.

## Execution evidence

The orchestrator records this point's implementation evidence here under the [shared evidence policy](execution.md#verification-commands-and-evidence-policy). Its completion checkbox is in the [index](../roadmap.md#progress-index).

| Field | Recorded evidence |
| --- | --- |
| Working state | In progress; started from clean verified R01 HEAD `19e9dd128375d13cea4bf15cd13d9f1916a91e75` |
| Implementation commit(s) | — |
| Executed verification | Not run |
| Measurements / artifacts | — |
| Blocker / next action | Complete the production and test/documentation migration, then run official gates and an independent read-only audit; no blocker established |

### Assignment and prerequisite evidence

R01 production commit `538291c9bd6671d3164caa48b80923fafa7db34a` and completion/evidence commit `19e9dd128375d13cea4bf15cd13d9f1916a91e75` are present. R01's four acceptance boxes and index box are checked after official gates and independent audit round 3 PASS. The working tree was clean before R02's production assignment; later edits belong to the assigned workers.

- `/root/r02_implementation`: explicitly configured `gpt-6-luna`, reasoning `high`; owns production `src/**` and the complete producer/consumer/runtime removal disposition.
- `/root/r02_tests_docs`: explicitly configured `gpt-6-luna`, reasoning `high`; owns `tests/**`, current-state `docs/**`, root `README.md` and navigation rule 08. Coordinates exact public APIs with the production worker.
- The orchestrator owns all verification runs, task/specification evidence, checkbox changes and commits. Both workers must pause before any build/test; no concurrent verification is permitted in this checkout. The separate Sol audit starts against the actual completed candidate.

Both assignments include the full R02 contract, specification 01, R01's verified inventory, all linked repository rules, preserved snapshot/evidence/lease boundaries, complete legacy removal, negative reference-input coverage, public follow-up acceptance and required eligible test categories. R02 remains unchecked until actual verification and independent audit pass.
