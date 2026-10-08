# Assembly export CLI

`AiNetCodeNavigator.AssemblyExport.exe` is a separate offline command. Use named options to select sources and a dedicated output directory:

```powershell
AiNetCodeNavigator.AssemblyExport.exe --output "C:\asm-dump" --source "C:\Programme" --include "foo*.exe" --include "*bar*.dll" --exclude "DevExpress*.dll"
```

| Option | Contract |
| --- | --- |
| `--output <directory>` | Required exactly once; dedicated disposable output dump. |
| `--source <directory-or-file>` | Required, repeatable; literal directories searched recursively or explicit managed DLL/EXE files. |
| `--include <filename-pattern>` | Repeatable; global OR selection across all source directories. Omitted selects all managed DLLs and EXEs. Does not restrict explicitly named files or dependency exports. |
| `--exclude <filename-pattern>` | Repeatable; excludes matching filenames from all export selection, including explicit files and dependencies. Always wins. |
| `--dependencies all\|none` | Default `all`; follows non-system references. `none` exports only selected roots. |
| `--dry-run` | Prints planned exports, exclusions, filtered references and input/closure issues without creating, locking, resetting or writing the output dump. |
| `--help` / `-h` | Prints usage without exporting. |

Option order does not affect selection. Patterns match the complete filename including its extension, case-insensitively, using `*` and `?`. Paths, directory wildcards and `**` are unsupported in include/exclude patterns. Quote patterns to pass them literally. Native files found by directory searches are skipped. Unmatched exclusions are allowed. If no assembly remains selected, the command reports failure before touching an existing dump.

To export only your selected application files, suppress dependency exports:

```powershell
AiNetCodeNavigator.AssemblyExport.exe --output "C:\asm-dump" --source "C:\Programme\MyApplication" --include "MyCompany*.dll" --include "MyApplication.exe" --dependencies none
```

Add `--dry-run` to inspect the selection before exporting. The inspection resolves references and reads assembly metadata; it does not decompile or publish source artifacts.

The local deployment and Windows release archive place the CLI beside the MCP server executable. Run the CLI directly; MCP client process entries and `hostsettings.json` configure only the server. The exporter never executes analyzed assemblies.

## Selection and dependencies

The planner expands the named source inputs, then follows resolved non-system assembly references with a global queue. It processes each canonical assembly path once. References may resolve to adjacent files, runtime assemblies, or installed .NET Framework GAC assemblies; modern .NET has no GAC. It does not search unrelated machine directories. Reference identity, including version, determines a GAC match; a newer unrelated GAC version is not substituted. Cycles are deduplicated, and an incomplete traversal at depth 128 or 4096 assemblies is reported.

Automatic dependency selection excludes the simple names `mscorlib`, `netstandard`, `System`, `Microsoft`, `WindowsBase`, `PresentationCore`, `PresentationFramework`, `Accessibility`, `UIAutomationClient`, `UIAutomationTypes`, and `UIAutomationProvider`, and names beginning `System.`, `Microsoft.`, or `Windows.` (case-insensitive). A selected root remains selected despite these automatic rules, unless a user exclusion matches its filename. GAC or runtime origin alone does not exclude a third-party assembly. Missing or ambiguous dependencies are recorded as limitations; independent assemblies continue.

User exclusions also apply to resolved dependency filenames. Excluded dependencies are not added to the export queue, and their own references are not traversed for export. `--dependencies none` stops dependency export traversal altogether. Both policies preserve direct resolved references for decompilation; dependency detail files retain the original reference metadata and export filtering reasons (`exclude:<pattern>` or `dependencies:none`). A dependency selected independently as a root can still be exported with `none`, but a user exclusion always prevents its export.

For recursively discovered local files with the same filename, the highest assembly version is selected. An explicitly named file or an older version proven by a reference is also retained. Byte-identical aliases with the same assembly identity are exported once. Equal-version files with different bytes are distinct variants. Output is grouped into an owner directory using the filename stem segment before the first dot, or `_misc` when the stem has no dot or its owner prefix is unsafe. Within an owner, groups with more than 64 assembly children are selectively split by further safe filename-stem segments. Homogeneous segments may be skipped, and singleton segment groups stay at the current level; larger groups can be split recursively while they remain above the soft target. A broad group with no further usable split can remain above the target. This is a soft grouping target and does not create numbered buckets. Group names are packaging hints derived from filenames, not verified vendor or product classifications. When multiple variants remain, they are grouped beneath `<owner>/<group-segments>/<filename>/<origin>-<identity-and-content-hash>`, with `local`, `gac32`, `gac64`, or `gacmsil` origin. A unique filename has a direct child beneath its owner or selected group path.

## Owned output directory

The output directory is **disposable**. On every run, the exporter validates the exact ownership marker `.ainetcodenavigator-assembly-export`. It deletes the entire existing marked dump before creating a fresh one. Everything placed in that marked directory, including files from earlier runs, is removed. An existing directory without the exact regular marker is rejected without modification. The marker is UTF-8 text containing exactly `AiNetCodeNavigator.AssemblyExport:1` and an LF newline, without a BOM. An interrupted first creation that has no marker requires manual removal.

The exporter rejects a volume root, output/source overlap, and reparse-point redirects before destructive cleanup. A run lock prevents two exports from resetting the same output concurrently. Within the marked root it creates a separately marked temporary directory. Each assembly is decompiled into a unique UUID stage there; the temporary directory is removed after results are published. Cleanup validates ownership, containment, and reparse points. Do not put unrelated files in the dump.

```text
C:\asm-dump\
  .ainetcodenavigator-assembly-export
  README.md
  assemblies.json
  namespace-map.md
  symbol-map.md
  last-run.log
  Example\
    Example.Core.dll\
      Example.Core.sln
      Example.Core.csproj
      <generated C# tree and project resources>
      export-manifest.json
      source-files.json
      dependencies.json        # when references or filtered edges exist
      diagnostics.json         # when diagnostics exist
    Example.Dependency.dll\
      <another complete generated project>
  _misc\
    Standalone.dll\
      Standalone.sln
      Standalone.csproj
      export-manifest.json
```

When an owner has more than 64 assembly children, eligible larger groups can add levels such as the following; omitted children are required for this example to cross the threshold:

```text
Example\
  <safe-stem-segment>\
    <assembly-child directories>
  <other children omitted>
```

Decompilation uses a bounded number of workers across available processors. Results are validated and published as children of the output root. One failing assembly does not stop independent work. A failed assembly has no published child in that run. The generated `.sln` references the emitted `.csproj`; reconstructed projects are not guaranteed to compile. Decompiled `.cs` files with syntax errors or empty content remain available with diagnostics and a `partial` completion state. Source drift after planning fails the affected export.

The generated root `README.md` gives an agent a short navigation procedure, including searches when the owning assembly is unknown. `assemblies.json` is a compact catalog of published assembly children in the current dump. Its `columns` array names the positions in each `rows` array; use a row's relative child path to open the corresponding directory, including any intermediate grouping directories. The root README, catalog, class maps, and file listings are the navigation aids; there are no per-folder maps. For a domain question, narrow by filenames and search terms before opening source files; follow references to the actual implementation or mapping before drawing a conclusion from a class name. Group directory names are packaging hints only and do not prove a vendor, product, or domain classification. Check the catalog's run state and the child's `completionState` before relying on an export. A planned dependency path in a child manifest or its dependencies file does not prove that dependency was published; confirm it has a catalog row.

`last-run.log` contains short progress, errors, and a final run summary; it is also printed in the console. On a failed run, search it for `FAILURE` lines to identify omitted children; the catalog lists only published children. There is no root `last-run.json`. Each published child's schema-v2 `export-manifest.json` is a small summary containing source path, assembly identity and hash, output and project paths, `complete` or `partial` state, counts, and relative paths to detail files. `source-files.json` holds generated C# paths. `dependencies.json` holds resolved and filtered references when present. `diagnostics.json` holds closure and decompilation diagnostics when present. The large arrays are not repeated in the manifest. File-specific decompilation diagnostics use `/`-separated paths relative to that child, so identically named files can be distinguished. A `partial` child may still contain usable files; inspect diagnostics only for files or limitations relevant to the question. An abruptly terminated run may have no final summary; temporary stages are not published exports.

`namespace-map.md` groups class declarations under `## <namespace>` headings; `symbol-map.md` sorts the same entries by class symbol. Each line has the form `- <symbol> -> <source-path>` with Markdown code spans around both values. Paths use `/` and are relative to the dump root. Symbol names come from parsed declarations, independently of folder names: fully qualified namespace, then class name; nested types use `+`, and each generic type appends a backtick followed by its own parameter count (for example, ``Outer`1+Inner`2``). These identities do not depend on generic parameter names, run IDs, absolute paths or timestamps. `<global>` denotes the global namespace. Class-like records are included; structs, interfaces, enums and delegates are excluded. Distinct source paths preserve partial declarations and duplicate classes across assemblies. Entries are deduplicated and sorted ordinally for repeatable output. Search both files with `rg -n -F '<namespace-or-class>' namespace-map.md symbol-map.md`.

The maps contain only successfully published children, including recognizable declarations in partial C# source. They are finalized before the catalog leaves `running`; a missing map or unfinished run is not an exhaustive index. A map write failure prevents a success summary. Check run state, child completion state and relevant diagnostics before relying on completeness. Syntax indexing does not prove that reconstructed projects compile or that symbols bind successfully.

For a repeatable local inspection, run `pwsh -File ./scripts/export-assembly-smoke.ps1`. It builds the solution, creates a small managed probe DLL and an excluded alias, exports the selected probe to `temp/assembly-export-smoke-dump/`, and checks the navigation artifacts, both class maps and their source links, and generated C# tree. It also checks named include/exclude selection and verifies that a dry-run leaves the existing dump's file hashes unchanged. This separate, disposable test dump does not replace an existing `temp/asm-dump/`.

A run with no failed inputs or exports exits `0`, even if some published assemblies are `partial`; inspect the catalog's `partial` count and the corresponding manifests. Recoverable input or assembly failures return `1` after other work is attempted. Invalid arguments, empty final selection or global ownership/preflight failures return `2`. A dry-run returns `2` when the plan contains issues or has no selected assemblies, otherwise `0`. Missing dependencies are visible limitations and do not by themselves make the normal export's exit status nonzero.
