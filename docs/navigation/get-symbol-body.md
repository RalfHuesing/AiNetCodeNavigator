# Get Symbol Body

`SourceSymbolBodyResolver.Resolve` extracts the source declaration associated with a Roslyn symbol. The identifier-based `ResolveAsync` first uses the [shared symbol resolution contract](symbol-resolution.md); unresolved or ambiguous identifiers return a structured error and, for ambiguity, selectable candidates. The body result contains declaration text, source availability, total line count, displayed one-based line bounds, truncation state, documentation ID, and an optional source handoff. A partial method definition with an implementation resolves to the implementation declaration. Default interface methods with executable bodies remain available; abstract methods and properties, abstract interface members, and interface declarations report unavailable bodies.

`maxBodyLines` limits the output window and `startLine` selects its one-based starting line. Values below one are normalized to one. A window that exceeds the declaration returns a message with the total line count and does not throw. Truncated windows include a continuation marker and set `HasMore`.

`ResolveBatch` applies the same window to each input symbol, preserves input order, and returns an empty result for an empty sequence. Metadata symbols without source syntax are retained as unavailable results with zero source lines, so they do not prevent other batch items from being returned. Passing a null symbol or null batch sequence throws `ArgumentNullException`.

The MCP host currently registers `get_symbol_body` for source and managed-assembly targets. It is the tested follow-up consumer for symbol handoffs from the initial navigation slice; see [MCP Host](../mcp-host.md) for the scope of the public integration evidence.
