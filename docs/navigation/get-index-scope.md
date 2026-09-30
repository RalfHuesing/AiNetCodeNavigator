# Index Scope

`IndexScopeScanner.ScanAsync` reports the Roslyn document inventory for a loaded solution or one named project. It reads project and document metadata; it does not read source text, access the filesystem, or change workspace contents. A document with compile errors is still part of the inventory because this scan does not compile documents.

The scope includes each project's Roslyn `Project.Documents` entries, including documents from non-C# projects. `TotalDocumentCount` and file-type counts describe these document entries, not unique physical files or every asset under a project directory. `CSharpFileCount` counts `.cs` document entries belonging to C# projects. Each project entry reports its document count, C# document count, language classification, and test-project classification. File-type entries indicate symbol navigation coverage only when all `.cs` entries of that extension belong to C# projects.

Project names are matched case-insensitively. An unknown project returns `ScanCompleted=false` and an `Error`; cancellation is propagated to the caller. The default response shows at most 100 projects and 64 file types. Requested limits are clamped to at least 1 and at most 500 projects or 128 file types. The scanner still counts the full selected scope before applying these presentation limits, so total counts remain complete when `IsTruncated` is true. `TruncatedBy`, shown counts, and `NextAction` identify presentation truncation. The scanner does not provide cursor or offset pagination.

The Core inventory is intentionally narrower than AiNetLinter's `get_index_scope`, which also reports physical project assets and generated/build file categories. This scanner describes the loaded Roslyn document scope used by navigation.
