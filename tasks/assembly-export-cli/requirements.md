# Assembly export CLI selection

## Approved scope

Replace the offline exporter's positional arguments with explicit named options:

- `--output <directory>`: required once.
- `--source <directory-or-file>`: required, repeatable; recursively search directories for managed DLLs and EXEs.
- `--include <filename-pattern>`: repeatable, global OR selection for directory sources only; omitted means all managed DLLs and EXEs.
- `--exclude <filename-pattern>`: repeatable, applies to all selected files and dependencies, including explicitly named files. Exclusion always wins.
- `--dependencies all|none`: default `all`; retain current automatic system filters. `none` exports only selected roots.
- `--dry-run`: inspect the selection and exclusion reasons without creating, resetting, locking or writing an output dump.

Filename patterns match names including extensions, case-insensitively, with `*` and `?`. Reject paths, directory wildcards and `**` in patterns. Sources are literal directories or files. Option ordering does not affect selection. Includes apply only to roots; dependencies can add files outside the include patterns. Excluded dependencies remain usable for decompilation resolution, with their original reference metadata and a recorded export exclusion reason; do not traverse them for further export selection.

Unmatched exclusions are allowed. Empty final selection must report a failure before touching an existing dump. Preserve ownership, overlap, reparse-point, version-selection, content-deduplication, bounded traversal and independent-failure behavior. Help and invalid arguments must not write output artifacts. The MCP server and Core implementation remain unchanged.

## Dependency choice

Use `System.CommandLine` for parsing. Reuse centrally pinned stable version `2.0.11`, already used by the MCP server; updating the shared version to newer `2.0.12` is outside this task's server-preservation boundary. No new general-purpose parser implementation or separate package version is needed.

## Verification

Cover argument contracts and errors, recursive selection, global include OR semantics, explicit source behavior, root/dependency exclusion precedence, traversal boundaries, dependency suppression with retained decompilation references, and no-write/empty-selection behavior through focused unit and executable integration tests. Run the official build, focused fast and integration selections, the generated-assembly smoke exploration, and the complete eligible routine solution selection. Finish with an independent audit subagent before committing verified changes.
