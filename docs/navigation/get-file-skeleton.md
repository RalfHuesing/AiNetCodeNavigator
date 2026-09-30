# Get File Skeleton

`FileSkeletonBuilder.BuildMarkdownForDocumentAsync` renders the top-level type declarations in one C# source document. The underlying `SkeletonMapBuilder.BuildForDocumentAsync` returns structured `SkeletonTypeInfo` and `SkeletonMemberInfo` records for classes, records, interfaces, structs, and enums. Nested type declarations are intentionally omitted, matching the navigation reference.

Type and member entries include their declaration kind, modifiers, signature, relative source path, and an optional handoff ID. The syntax walker reads declarations from Roslyn syntax and semantic models; method and constructor bodies are not included in the skeleton. Record primary-constructor parameters are represented as synthesized properties, and enum values are represented as fields.

The Markdown renderer groups types by namespace and includes an `h:...` handoff ID when one is available. `FileSkeletonBuilder` creates opaque source handoffs by default. A caller may provide `formatSymbolId` to control how raw documentation IDs are exposed. Null documents, paths, projects, type lists, or semantic models are rejected with `ArgumentNullException`. A document without a syntax tree or semantic model produces an empty type list; cancellation is propagated.
