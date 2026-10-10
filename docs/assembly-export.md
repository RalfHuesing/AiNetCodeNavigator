# Assembly export CLI

`AiNetCodeNavigator.AssemblyExport.exe` is a separate offline command that decompiles selected managed .NET DLLs and EXEs into searchable C# project trees. It never executes analyzed assemblies. Use it when you need readable source from a runnable package; no repository checkout is needed.

The local deployment and Windows release archive place the CLI beside the MCP server executable. Run the CLI directly. MCP client process entries and `hostsettings.json` configure only the server.

## Discover the command offline

In PowerShell, set the executable path for your installed package, then inspect its usage and embedded topics:

```powershell
$exporter = 'C:\Tools\AiNetCodeNavigator\AiNetCodeNavigator.AssemblyExport.exe'
& $exporter --help
& $exporter --doc topics
& $exporter --version
```

Help, version, and documentation need no `--output` or `--source` arguments and do not export. `--doc guide` prints this complete guide. Documentation can be long; consider saving it to a file outside the output dump before reading or searching selected sections:

```powershell
& $exporter --doc guide > assembly-export-guide.md
Select-String -LiteralPath assembly-export-guide.md -Pattern '^##'
```

This guide is embedded from the canonical Markdown and describes the executable's built version. All operational commands below work without a source checkout. Run `--doc` separately from export options; combining them is an argument error with exit code `2`.

## Preview the selection, then export

Replace the example paths and filename patterns with your application. Choose a dedicated output directory outside every source directory. The directory will be disposable; read the ownership rule below before exporting.

```powershell
$dump = 'C:\asm-dump'
$exportArgs = @(
    '--output', $dump,
    '--source', 'C:\Programme\MyApplication',
    '--include', 'MyCompany*.dll',
    '--include', 'MyApplication.exe',
    '--exclude', 'DevExpress*.dll',
    '--dependencies', 'none'
)
& $exporter @exportArgs --dry-run
$previewExitCode = $LASTEXITCODE
$previewExitCode
```

The dry-run prints selected source and dependency paths, exclusions, filtered references, and plan issues. It reads assembly metadata and resolves references, but does not decompile, create, lock, reset, or write the output dump. Inspect both the selection and reported issues. If the preview fails, resolve those issues and repeat it before exporting.

This example uses `--dependencies none` to export only selected roots. Omit it or use `--dependencies all` to also export resolved non-system references. Repeat `--source` for more directories or explicit files. Include patterns filter recursively searched directories; an explicit file bypasses include patterns. Exclusions always win, including for dependencies.

After checking the preview and ownership rule, run the same arguments without `--dry-run`:

```powershell
& $exporter @exportArgs
$exportExitCode = $LASTEXITCODE
$exportExitCode
```

A normal run replaces the existing marked dump in full. Read the result checks below even when the exit code is `0`: a successful run can contain partial assemblies.

## Owned output directory

The output directory is **disposable**. On every export run, the exporter validates the exact ownership marker `.ainetcodenavigator-assembly-export`. It deletes the entire existing marked dump before creating a fresh one. Everything placed in that marked directory, including files from earlier runs, is removed. An existing directory without the exact regular marker is rejected without modification. The marker is UTF-8 text containing exactly `AiNetCodeNavigator.AssemblyExport:1` and an LF newline, without a BOM. An interrupted first creation that has no marker requires manual removal.

The exporter rejects a volume root, output/source overlap, and reparse-point redirects before destructive cleanup. A run lock prevents two exports from resetting the same output concurrently. Within the marked root it creates a separately marked temporary directory. Each assembly is decompiled into a unique UUID stage there; the temporary directory is removed after results are published. Cleanup validates ownership, containment, and reparse points. Do not put unrelated files in the dump.

## Check the exported result

First inspect `last-run.log` and the catalog at the dump root:

```powershell
Get-Content -LiteralPath (Join-Path $dump 'last-run.log') -Tail 20
Select-String -LiteralPath (Join-Path $dump 'last-run.log') -Pattern 'FAILURE|RUN ERROR|CLOSURE LIMITATION'
$catalog = Get-Content -LiteralPath (Join-Path $dump 'assemblies.json') -Raw | ConvertFrom-Json
$catalog | Select-Object runId, runState, selected, exported, partial, failed
$catalog.columns
```

The log should end with `RUN COMPLETE`, `RUN FAILED`, or `RUN INTERRUPTED` and counts. The catalog's `runState` is `complete`, `failed`, or `interrupted` after finalization. A missing final summary, missing catalog, or `running` state means the run is unfinished; do not treat its catalog or maps as exhaustive. Failed and interrupted runs can still publish usable children.

Find your assembly's row in `assemblies.json`; `columns` identifies each position in a row. Only published children have rows. Use the row's `childRelativePath` rather than guessing the directory from the filename. For example, select rows by an application filename fragment:

```powershell
$nameColumn = [Array]::IndexOf($catalog.columns, 'name')
$catalog.rows | Where-Object { $_[$nameColumn] -like '*MyApplication*' } | ConvertTo-Json -Depth 4
```

Then copy the chosen row's `childRelativePath` into this command and inspect its manifest:

```powershell
$child = Join-Path $dump '<childRelativePath>'
$manifest = Get-Content -LiteralPath (Join-Path $child 'export-manifest.json') -Raw | ConvertFrom-Json
$manifest | Select-Object runId, completionState, identity, sourcePath, sourceFilesPath, dependenciesPath, diagnosticsPath
if ($manifest.runId -ne $catalog.runId) { throw 'Catalog and manifest belong to different runs.' }
if ($manifest.diagnosticsPath) {
    Get-Content -LiteralPath (Join-Path $child $manifest.diagnosticsPath)
}
```

Check `completionState` (`complete` or `partial`), identity, source provenance, and relevant diagnostics before relying on the files. `complete` describes export completion; reconstructed projects are not guaranteed to compile. Dependency paths in manifests and detail files describe planned exports: confirm a catalog row before following one. Temporary stages are not published children.

Use the generated root `README.md` for navigation commands. Start with filenames or likely class names, narrow to a catalog child, then read the relevant source. `namespace-map.md` and `symbol-map.md` map class names to source paths. They index recognizable syntax, including partial source, rather than proving successful symbol binding. See the format reference below for their scope.

## Errors, streams, and exit codes

Help, version, documentation, dry-run selections, and export progress go to stdout. Argument errors, plan issues, and export failures go to stderr. During export, `last-run.log` also records progress and errors. A failure before the dump is created may only have a console error.

| Exit code | Meaning and next action |
| --- | --- |
| `0` | Normal export finished without counted input/export failures, or a dry-run has a nonempty selection and no plan issues. Check partial counts and manifests before using source. |
| `1` | Export encountered recoverable input, assembly, publication, cleanup, or interruption failures; independent work may have been published. Inspect the final log and catalog before using any child. |
| `2` | Invalid arguments, empty final selection, or a global planning/ownership preflight failure. A dry-run also returns `2` for any plan issue or an empty selection. Correct the console error and preview again. |

Missing dependencies are visible limitations and do not by themselves make a normal export's exit status nonzero. Some closure limitations do produce plan issues and a failed run. A failure occurring after planning, including a changed output directory or unavailable run lock, can return `1`; use the reported error to determine the cause.

An argument error or empty final selection leaves an existing dump intact. Once an export has reset a marked dump, a later failure does not restore its previous contents. Correct the cause and rerun the same export to replace the dump again.

## Option reference

| Option | Contract |
| --- | --- |
| `--output <directory>` | Required exactly once; dedicated disposable output dump. |
| `--source <directory-or-file>` | Required, repeatable; literal directories searched recursively or explicit managed DLL/EXE files. |
| `--include <filename-pattern>` | Repeatable; global OR selection across all source directories. Omitted selects all managed DLLs and EXEs. Does not restrict explicitly named files or dependency exports. |
| `--exclude <filename-pattern>` | Repeatable; excludes matching filenames from all export selection, including explicit files and dependencies. Always wins. |
| `--dependencies all\|none` | Default `all`; follows non-system references. `none` exports only selected roots. |
| `--dry-run` | Prints planned exports, exclusions, filtered references and input/closure issues without creating, locking, resetting or writing the output dump. |
| `--help` / `-h` | Prints usage without exporting. |
| `--version` | Prints the executable build version without exporting. |
| `--doc topics` | Lists embedded documentation topics without exporting. |
| `--doc guide` | Prints this complete guide without exporting. |

Option order does not affect selection. Patterns match the complete filename including its extension, case-insensitively, using `*` and `?`. Paths, directory wildcards and `**` are unsupported in include/exclude patterns. Quote patterns to pass them literally. Native files found by directory searches are skipped. Unmatched exclusions are allowed. If no assembly remains selected, the command reports failure before touching an existing dump.

## Selection and output format reference

The following details help interpret dependency selection, grouped child paths, and navigation artifacts. Use the catalog and manifests for the current run rather than inferring publication from the directory layout.

### Dependency selection and assembly variants

The planner expands the named source inputs, then follows resolved non-system assembly references with a global queue. It processes each canonical assembly path once. References may resolve to adjacent files, runtime assemblies, or installed .NET Framework GAC assemblies; modern .NET has no GAC. It does not search unrelated machine directories. Reference identity, including version, determines a GAC match; a newer unrelated GAC version is not substituted. Cycles are deduplicated, and an incomplete traversal at depth 128 or 4096 assemblies is reported.

Automatic dependency selection excludes the simple names `mscorlib`, `netstandard`, `System`, `Microsoft`, `WindowsBase`, `PresentationCore`, `PresentationFramework`, `Accessibility`, `UIAutomationClient`, `UIAutomationTypes`, and `UIAutomationProvider`, and names beginning `System.`, `Microsoft.`, or `Windows.` (case-insensitive). A selected root remains selected despite these automatic rules, unless a user exclusion matches its filename. GAC or runtime origin alone does not exclude a third-party assembly. Missing or ambiguous dependencies are recorded as limitations; independent assemblies continue.

User exclusions also apply to resolved dependency filenames. Excluded dependencies are not added to the export queue, and their own references are not traversed for export. `--dependencies none` stops dependency export traversal altogether. Both policies preserve direct resolved references for decompilation; dependency detail files retain the original reference metadata and export filtering reasons (`exclude:<pattern>` or `dependencies:none`). A dependency selected independently as a root can still be exported with `none`, but a user exclusion always prevents its export.

For recursively discovered local files with the same filename, the highest assembly version is selected. An explicitly named file or an older version proven by a reference is also retained. Byte-identical aliases with the same assembly identity are exported once. Equal-version files with different bytes are distinct variants. Output is grouped into an owner directory using the filename stem segment before the first dot, or `_misc` when the stem has no dot or its owner prefix is unsafe. Within an owner, groups with more than 64 assembly children are selectively split by further safe filename-stem segments. Homogeneous segments may be skipped, and singleton segment groups stay at the current level; larger groups can be split recursively while they remain above the soft target. A broad group with no further usable split can remain above the target. This is a soft grouping target and does not create numbered buckets. Group names are packaging hints derived from filenames, not verified vendor or product classifications. When multiple variants remain, they are grouped beneath `<owner>/<group-segments>/<filename>/<origin>-<identity-and-content-hash>`, with `local`, `gac32`, `gac64`, or `gacmsil` origin. A unique filename has a direct child beneath its owner or selected group path.

### Output layout and publication

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

Decompilation uses a bounded number of workers across available processors. Results are validated and published as children of the output root. One failing assembly does not stop independent work. A failed assembly has no published child in that run. The generated `.sln` references the emitted `.csproj`; reconstructed projects are not guaranteed to compile. If C# files were generated but the project file is empty or malformed, the exporter writes project XML listing those files and marks the child `partial` with a diagnostic. This replacement is a source-list placeholder without build targets or an inferred target framework. Decompiled `.cs` files with syntax errors or empty content remain available with diagnostics and a `partial` completion state. Source drift after planning fails the affected export.

### Navigation artifacts and manifest details

The root README, catalog, class maps, and file listings are the navigation aids; there are no per-folder maps. For a domain question, follow references to the actual implementation or mapping before drawing a conclusion from a class name. Group directory names are packaging hints only and do not prove a vendor, product, or domain classification.

There is no root `last-run.json`. Each published child's schema-v2 `export-manifest.json` is a small summary containing source path, assembly identity and hash, output and project paths, completion state, counts, and relative paths to detail files. `source-files.json` holds generated C# paths. `dependencies.json` holds resolved and filtered references when present. `diagnostics.json` holds closure and decompilation diagnostics when present. The large arrays are not repeated in the manifest. File-specific decompilation diagnostics use `/`-separated paths relative to that child, so identically named files can be distinguished. A `partial` child may still contain usable files; inspect diagnostics only for files or limitations relevant to the question.

### Class map format

`namespace-map.md` groups class declarations under `## <namespace>` headings; `symbol-map.md` sorts the same entries by class symbol. Each line has the form `- <symbol> -> <source-path>` with Markdown code spans around both values. Paths use `/` and are relative to the dump root. Symbol names come from parsed declarations, independently of folder names: fully qualified namespace, then class name; nested types use `+`, and each generic type appends a backtick followed by its own parameter count (for example, ``Outer`1+Inner`2``). These identities do not depend on generic parameter names, run IDs, absolute paths or timestamps. `<global>` denotes the global namespace. Class-like records are included; structs, interfaces, enums and delegates are excluded. Distinct source paths preserve partial declarations and duplicate classes across assemblies. Entries are deduplicated and sorted ordinally for repeatable output. Search both files with `rg -n -F '<namespace-or-class>' namespace-map.md symbol-map.md`.

The maps contain only successfully published children, including recognizable declarations in partial C# source. They are finalized before the catalog leaves `running`; a missing map or unfinished run is not an exhaustive index. A map write failure prevents a success summary. Check run state, child completion state and relevant diagnostics before relying on completeness. Syntax indexing does not prove that reconstructed projects compile or that symbols bind successfully.
