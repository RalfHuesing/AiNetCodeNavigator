# Assembly Navigation

Assembly tools analyze managed `.dll` and `.exe` targets through a read-only decompiled Roslyn snapshot. They do not load analyzed code for execution. `inspect_assembly` provides the library API inventory; use its type handoffs with `get_file_skeleton`, `get_class_structure`, or `get_symbol_body` for focused navigation.

Assembly handoffs bind to the canonical owner path, PE content hash, resolved-reference snapshot, generation, and declaration ID. Owner-aware consumers validate the target path and snapshot before returning source. Direct Root/Bridge/Leaf tests verify referenced-owner handoffs can be followed against their actual owner binaries; unsupported or unresolved metadata symbols do not receive handoffs.

`get_context` accepts a symbol and an explicit, duplicate-free selection of `body`, `members`, and `callers`. Assembly targets do not support `tests`. Its declaration head, identity, snapshot, and acquired owner lease are shared by the selected sections. The caller list contains direct incoming reference locations only; the `includeReferences` option expands only this direct relationship over the resolved assembly owners, while transitive call traversal remains `get_call_tree` or `get_impact`.

List sections use `maxResults` as a positive page size from 1 to 100, default 10. `members` follow stable file/declaration order. A per-section result cursor is bound to the original section selection, query, caller options, page size, and snapshot. After consuming outer response pages, replaying the cursor analyzes and returns only its section. A changed argument, owner, or snapshot is rejected. Body windows use `maxBodyLines` (default 80) and one-based `startLine`; body availability is separate from section delivery status.

An assembly caller section reports incompleteness from traversal limits, unresolved references, and degraded owners. A later section error preserves earlier results with partial statuses and retains the target snapshot in the error payload. Section-local cursors and response continuation pages remain distinct.

Other assembly routes remain purpose-specific: `search_assembly` finds declaration/text matches, `find_assembly_extensions` resolves extension methods with actual owner paths, `resolve_type_origin` identifies source or metadata ownership, and relationship routes expose hierarchy, implementations, references, call trees, impacts, or dependency graphs. See the [shared context contract](get-context.md) for the selected-section schema.
