# Member Structure

`get_context` with `sections=[members]` provides the declared member inventory described in [Shared Navigation Context](get-context.md). Member references follow directly to `get_symbol_body` using the returned owner target. Context requires a type declaration; a member reference is not a member-inventory root.

The Core `ClassStructureScanner` owns source extraction, filtering and ordering. Its resolved-type entry point accepts the common validated context root. It includes fields, constants (with invariant literal values), events, properties, methods and constructors across eligible partial declarations, separating accessibility from signature. Type kinds distinguish `Class`, `Record Class`, `Record Struct`, `Interface`, `Struct`, and `Enum`. Record positional parameters appear as `PrimaryCtor-Param` rows with zero member line count and no stable reference. Other declarations without exact canonical references retain null reference fields.

The scanner's standalone `ScanAsync` remains a Core API, resolving a type or a member's containing type through [shared symbol resolution](symbol-resolution.md). Its optional bounded `MaxMembers` and Markdown renderer remain available to Core consumers. The MCP context collects the complete filtered inventory before applying its independent section pages.
