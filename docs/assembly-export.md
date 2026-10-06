# Assembly export CLI

`AiNetCodeNavigator.AssemblyExport.exe` is a separate offline command in the solution. Give it an output directory, a source directory, and one or more quoted filename patterns to select matching managed DLL/EXE files recursively:

```powershell
AiNetCodeNavigator.AssemblyExport.exe "C:\asm-dump" "C:\Programme" "foo*.exe" "*bar*.dll"
```

The local deployment places the executable and its dependencies directly beside the MCP server package in the deployment directory root. The self-contained Windows release archive uses the same layout. This CLI is invoked directly; MCP client process entries and `hostsettings.json` configure the server only. When an agent needs whole readable source for selected assemblies, it should run this command and check `last-run.json` and each selected child's `export-manifest.json` before treating output as current or complete.

The positional parser requires at least two arguments. Bare filename patterns immediately following a source directory filter that directory and all its subdirectories; multiple patterns are combined, so either pattern can select a file. A directory without following bare patterns selects every managed `.dll` and `.exe` beneath it. A path-qualified pattern such as `C:\Vendor\*.dll` or `C:\Vendor\Tool?.exe` also matches recursively below its parent directory. `*` and `?` are supported only in the final filename segment; directory-segment wildcards and `**` are unsupported. Native files and other extensions found during a directory or pattern search are skipped; each directory or pattern must yield at least one managed file. An explicit file path must be a managed DLL or EXE. Reparse-point source directories are rejected rather than traversed. Paths are normalized, deduplicated and sorted. Recoverable input, closure, and per-child errors are recorded and do not prevent independent inputs or children from running. `--help` prints usage and exits with status `0`; argument or global ownership/preflight failures return `2`. A run with any input or export failure returns `1`; a complete run returns `0`. Missing dependencies are reported as a partial limitation and do not alone make the exit status nonzero.

## Input planning and ownership

Preflight resolves the non-system reference closure through [Core reference resolution](navigation/assembly-navigation.md), retaining unresolved edges and dependency provenance. If traversal is incomplete but establishes the root identity, the CLI exports that root and every proven dependency it found, marks the child partial, records closure diagnostics, and returns nonzero. Ambiguous resolution is recorded against the referring assembly; that child is preserved and independent assemblies continue. Automatic selection excludes simple names `mscorlib`, `netstandard`, `System`, `Microsoft`, `WindowsBase`, `PresentationCore`, `PresentationFramework`, `Accessibility`, `UIAutomationClient`, `UIAutomationTypes`, and `UIAutomationProvider`, and prefixes `System.`, `Microsoft.`, and `Windows.` (case-insensitive). Filtered edges retain their matching rule. Explicit inputs are selected regardless of the filter; their non-system references are traversed. GAC and runtime provenance alone do not exclude a dependency.

GAC lookup is limited to installed .NET Framework GAC directories; modern .NET has no GAC. Adjacent files and the runtime trusted-platform list remain dependency candidates. Dependency resolution does not search other machine directories; explicit source directories are searched recursively. The reference closure stops with an explicit error at depth 128 or 4096 nodes rather than silently omitting children.

The planner deduplicates identical paths and aliases with the same assembly identity and full SHA-256. When recursive directory or filename-pattern expansion selects several local files with the same basename, it keeps the highest `AssemblyName.Version`; explicitly named files and older versions proven by references remain selected. Different remaining variants, including equal-version files with different bytes, are stored beneath `<filename>/<origin>-<identity-and-content-hash>`, where origin is `gac32`, `gac64`, `gacmsil`, or `local`. Manifests and run reports record these paths relative to the dump. A conflict with a selected child or variant layout is reported against the affected assembly, and independent children continue. Proven dependency aliases are normalized for decompilation while original closure edges retain their paths and provenance. Source files and resolved dependencies inside the output root are rejected.

The filesystem owner validates root ownership and root-level report/log paths before creating output. It validates each exact selected child immediately before its cleanup or publication; a child conflict is reported for that assembly while independent children continue. An existing root must contain the regular UTF-8 marker `.ainetcodenavigator-assembly-export` with exactly `AiNetCodeNavigator.AssemblyExport:1` and an LF newline, without a BOM. Root creation writes this marker before content. An interrupted creation leaving an unmarked root requires manual removal before another run.

Cleanup accepts only the exact selected `<source-file-name>` child or its selected variant subdirectory. It preserves unrelated children and sibling variants, and rejects reparse points in root ancestors, selected children and their descendants. Ownership is revalidated before cleanup. Staging paths must be direct children prefixed `.assembly-export-stage-`; they are checked for reparse redirects. The CLI writes its marker before run reports or generated content. It also rejects reparse redirects and directories at the root-level `last-run.json` and `last-run.log` paths. Root ownership and reparse failures remain global because the exporter cannot safely write a report outside a validated root.


## Project output and run reports

For each selected DLL or EXE the tool removes its existing selected child, creates a fresh staging directory inside the marked root, validates the generated project and C# files, and publishes the stage at the planned relative child path. A unique file uses `<output-directory>\<source-file-name>`; remaining same-name variants use `<output-directory>\<source-file-name>\<variant-key>`. A failed export leaves that child absent and reports the failure while independent assemblies continue. Staging cleanup checks containment and reparse points. Unselected children and sibling variants remain from earlier runs.

```text
C:\Dump\
  .ainetcodenavigator-assembly-export
  last-run.json
  last-run.log
  Product.Core.dll\
    Product.Core.sln
    Product.Core.csproj
    <generated C# tree and project resources>
    export-manifest.json
  Vendor.Dependency.dll\
    <another complete generated project>
```

The generated `.sln` references its emitted `.csproj` by relative path. Its name follows the emitted project name. Export validates project XML, generated file paths, and the source/dependency snapshot. Decompiled `.cs` files with syntax errors or empty content are retained with diagnostics and a `partial` completion state so usable output is not discarded; reconstructed projects are not guaranteed to compile. Source drift after planning fails that DLL's export.

Each `export-manifest.json` records the source path, relative child path, identity, SHA-256, decompiler version, project/source paths, resolved dependency paths and provenance, links from dependency indexes to exported child paths, automatically exported children, filtered edges with referring DLL and matching rule, unresolved edges and decompilation diagnostics. Manifest `completionState` is `complete` or `partial`, where partial means usable output with reported dependency or decompilation limitations.

`last-run.json` records literal input patterns, expanded explicit paths, every selected child (including failures and pending work), input/closure failures, exclusions and failures. A selected item blocked by a filesystem conflict has `existingChildPreserved: true` when its previous path remains in place. It is not a current export for that run. The report is atomically replaced first with a `running` state before any selected child is removed, after each result, and finally with `complete`, `partial`, `failed` or `interrupted`. `last-run.log` mirrors the console's stdout and stderr lines from the export run. An abruptly terminated process can leave `running`/`pending` states and a staging directory; those are not published exports. A graceful Ctrl+C records interruption and cleans its current stage. Check this report before using a child: a child absent from the latest selection belongs to an earlier run, and a failed or pending selection does not prove a current export. Use the selected child's `.sln` or `.csproj` as the solution-level entry point and its `.cs` tree for direct agent reading.
