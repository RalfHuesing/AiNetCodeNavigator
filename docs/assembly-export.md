# Assembly export CLI

`AiNetCodeNavigator.AssemblyExport.exe` is a separate offline command. Give it a dedicated output directory, a source directory, and optional quoted filename patterns. It searches source directories recursively:

```powershell
AiNetCodeNavigator.AssemblyExport.exe "C:\asm-dump" "C:\Programme" "foo*.exe" "*bar*.dll"
```

The example exports managed files matching either pattern at any depth below `C:\Programme`. A source directory without patterns selects all managed `.dll` and `.exe` files below it. Multiple source paths can be passed in one invocation. A path-qualified pattern such as `C:\Vendor\Tool?.exe` searches recursively below its parent. `*` and `?` work in the final filename segment; directory wildcards and `**` are unsupported. Native files found by a directory search are skipped. Each directory or pattern must match at least one managed file, and an explicit file must be managed. `--help` prints usage.

The local deployment and Windows release archive place the CLI beside the MCP server executable. Run the CLI directly; MCP client process entries and `hostsettings.json` configure only the server. The exporter never executes analyzed assemblies.

## Selection and dependencies

The planner expands the named source inputs, then follows resolved non-system assembly references with a global queue. It processes each canonical assembly path once. References may resolve to adjacent files, runtime assemblies, or installed .NET Framework GAC assemblies; modern .NET has no GAC. It does not search unrelated machine directories. Reference identity, including version, determines a GAC match; a newer unrelated GAC version is not substituted. Cycles are deduplicated, and an incomplete traversal at depth 128 or 4096 assemblies is reported.

Automatic selection excludes the simple names `mscorlib`, `netstandard`, `System`, `Microsoft`, `WindowsBase`, `PresentationCore`, `PresentationFramework`, `Accessibility`, `UIAutomationClient`, `UIAutomationTypes`, and `UIAutomationProvider`, and names beginning `System.`, `Microsoft.`, or `Windows.` (case-insensitive). An explicitly named assembly remains selected. GAC or runtime origin alone does not exclude a third-party assembly. Missing or ambiguous dependencies are recorded as limitations; independent assemblies continue.

For recursively discovered local files with the same filename, the highest assembly version is selected. An explicitly named file or an older version proven by a reference is also retained. Byte-identical aliases with the same assembly identity are exported once. Equal-version files with different bytes are distinct variants. Output is grouped into an owner directory using the filename stem segment before the first dot, or `_misc` when the stem contains no dot. When multiple variants remain, they are grouped beneath `<owner>/<filename>/<origin>-<identity-and-content-hash>`, with `local`, `gac32`, `gac64`, or `gacmsil` origin. A unique filename has a direct `<owner>/<filename>` child.

## Owned output directory

The output directory is **disposable**. On every run, the exporter validates the exact ownership marker `.ainetcodenavigator-assembly-export`. It deletes the entire existing marked dump before creating a fresh one. Everything placed in that marked directory, including files from earlier runs, is removed. An existing directory without the exact regular marker is rejected without modification. The marker is UTF-8 text containing exactly `AiNetCodeNavigator.AssemblyExport:1` and an LF newline, without a BOM. An interrupted first creation that has no marker requires manual removal.

The exporter rejects a volume root, output/source overlap, and reparse-point redirects before destructive cleanup. A run lock prevents two exports from resetting the same output concurrently. Within the marked root it creates a separately marked temporary directory. Each assembly is decompiled into a unique UUID stage there; the temporary directory is removed after results are published. Cleanup validates ownership, containment, and reparse points. Do not put unrelated files in the dump.

```text
C:\asm-dump\
  .ainetcodenavigator-assembly-export
  README.md
  assemblies.json
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

Decompilation uses a bounded number of workers across available processors. Results are validated and published as children of the output root. One failing assembly does not stop independent work. A failed assembly has no published child in that run. The generated `.sln` references the emitted `.csproj`; reconstructed projects are not guaranteed to compile. Decompiled `.cs` files with syntax errors or empty content remain available with diagnostics and a `partial` completion state. Source drift after planning fails the affected export.

The generated root `README.md` gives an agent a short navigation procedure, including searches when the owning assembly is unknown. `assemblies.json` is a compact catalog of published assembly children in the current dump. Its `columns` array names the positions in each `rows` array; use a row's relative child path to open the corresponding directory. For a domain question, narrow by filenames and search terms before opening source files; follow references to the actual implementation or mapping before drawing a conclusion from a class name. Check the catalog's run state and the child's `completionState` before relying on an export. A planned dependency path in a child manifest or its dependencies file does not prove that dependency was published; confirm it has a catalog row.

`last-run.log` contains short progress, errors, and a final run summary; it is also printed in the console. On a failed run, search it for `FAILURE` lines to identify omitted children; the catalog lists only published children. There is no root `last-run.json`. Each published child's schema-v2 `export-manifest.json` is a small summary containing source path, assembly identity and hash, output and project paths, `complete` or `partial` state, counts, and relative paths to detail files. `source-files.json` holds generated C# paths. `dependencies.json` holds resolved and filtered references when present. `diagnostics.json` holds closure and decompilation diagnostics when present. The large arrays are not repeated in the manifest. File-specific decompilation diagnostics use `/`-separated paths relative to that child, so identically named files can be distinguished. A `partial` child may still contain usable files; inspect diagnostics only for files or limitations relevant to the question. An abruptly terminated run may have no final summary; temporary stages are not published exports.

For a repeatable local inspection, run `pwsh -File ./scripts/export-assembly-smoke.ps1`. It builds the solution, creates a small managed probe DLL, exports it to `temp/assembly-export-smoke-dump/`, and checks the navigation artifacts and generated C# tree. This separate, disposable test dump does not replace an existing `temp/asm-dump/`.

A run with no failed inputs or exports exits `0`, even if some published assemblies are `partial`; inspect the catalog's `partial` count and the corresponding manifests. Recoverable input or assembly failures return `1` after other work is attempted. Invalid arguments or global ownership/preflight failures return `2`. Missing dependencies are visible limitations and do not by themselves make the exit status nonzero.
