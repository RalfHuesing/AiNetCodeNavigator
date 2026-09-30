# Find Symbol

`FindSymbolScanner` searches C# source declarations in a Roslyn solution. It supports case-insensitive substring matching, `*` and `?` wildcards, regular expressions recognized by the shared matcher, Markdown backticks around identifiers, and dot-separated type/member names. Results include symbol kind, signature, source path and line, project name, and a source handoff when the symbol has a canonical identity.

The kind filter accepts all symbols or one of class, struct, interface, enum, record, method, property, field, and event. A kind mismatch returns an explanatory message listing the kinds found for the name.

The scope filter accepts `all`, `production`, or `tests`. Classification is applied to each source location using its Roslyn project and document: test project metadata, recognized test project names or paths, and recognized test file paths identify test locations. `all` includes both groups. This keeps same-named declarations in separate production and test projects scoped to their own locations.

`maxResults` limits the returned entries and reports whether results were truncated. Name misses may include similar symbol suggestions. This page describes the Core scanner contract; MCP argument validation and transport formatting are documented with the server tools.
