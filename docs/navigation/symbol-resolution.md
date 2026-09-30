# Shared Symbol Resolution

`SourceSymbolResolver.ResolveAsync` is the common Core resolver used by `SourceSymbolBodyResolver.ResolveAsync`, `FeatureContextScanner`, and `ClassStructureScanner`. It resolves source declarations from these identifier forms:

- An opaque `h:` handoff, or an internal `i:` handoff when a matching source identity is supplied.
- A Roslyn documentation comment ID such as `M:Demo.Greeter.Greet` or `T:Demo.Greeter`.
- A source position in `path:line:column` or `path:line` form. Paths may be absolute, solution-relative, or match a source document's normalized relative path. Lines and columns are one-based.
- An exact simple or qualified declaration name. A method signature can disambiguate overloads; a short name that identifies multiple declarations is ambiguous.

Resolution is source-oriented. Metadata-only symbols are not returned by name or documentation ID. A unique match is passed to the requested follow-up scanner. Multiple matches return `AMBIGUOUS_SYMBOL` and `ResolutionCandidates`; each candidate reports its name, kind, signature, file, source lines, project, documentation ID, and reusable `h:` handoff when available. Passing a candidate handoff back to any of the three follow-up scanners selects that declaration.

No matching source declaration returns `SYMBOL_NOT_FOUND`. Numeric positions outside a document's one-based line/column bounds return `INVALID_ARGUMENT`; paths that match no source document return `SYMBOL_NOT_FOUND`. Invalid and stale handoffs retain the handoff resolver's structured error codes. Null solutions and blank identifiers throw argument exceptions. The body resolver returns a `SymbolBodyResolutionResult` for identifier-based resolution, while Feature Context and Class Structure carry errors and candidate choices in their payloads.
