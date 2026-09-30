# Get Type Hierarchy Core Engine

`TypeHierarchyScanner.ScanAsync` accepts a Roslyn class, interface, or struct symbol and reports its full base type chain and transitive implemented interfaces. Source declarations include relative paths, one-based lines, and project-aware `h:` handoffs when a source identity is available. Metadata-only bases and interfaces remain visible with no source path or handoff.

For classes, the scanner returns direct and indirect derived classes across the solution. For interfaces, it returns direct and indirect implementing types across the solution, including classes that inherit an implementation. Structs retain their base and interface information and have no derived-class search. Subtypes and source locations are ordered deterministically.

`maxResults` applies only to derived or implementing subtypes and is normalized to at least one. `TotalSubtypes` reports the count before limiting and `IsTruncated` reports whether the subtype list was shortened. Base types and interfaces are not limited. Inheritance cycles in malformed source are stopped by tracking each base type's original definition; cancellation is checked during base-chain traversal and Roslyn subtype searches.

Null symbols and solutions throw `ArgumentNullException`. Enums and other unsupported named type kinds return a payload with `IsSuccess == false` and an explanatory `ErrorMessage`; the formatter returns that error text. A supported type with no subtypes is a successful result with an empty list. The text formatter renders the base chain with indentation, interfaces as a list, and derived or implementing types in the bounded final section.
