---
status: draft
---

# Assembly source dump

## Intention

A separate Windows console executable in the AiNetCodeNavigator solution creates a disposable, searchable C# source dump from explicitly selected managed DLLs and EXEs. Coding agents can read the resulting files directly. The existing MCP executable remains a read-only navigation server. This replaces the assembly-dump part of the owner's SourceToAI workflow; SourceToAI's Markdown feed features are not migrated.

## Scope

### Required

- Add `src/AiNetCodeNavigator.AssemblyExport/AiNetCodeNavigator.AssemblyExport.csproj` as a separate executable in `AiNetCodeNavigator.slnx`. It references a narrow Core export API, never the MCP host. Console progress and errors go to this executable's stdout/stderr; MCP stdout remains reserved for JSON-RPC.
- Invocation: `AiNetCodeNavigator.AssemblyExport.exe <output-directory> <source-directory> [<filename-pattern> ...]`. Thus two positional arguments are the minimum. One invocation accepts multiple absolute or relative source paths. A directory such as `C:\Vendor` recursively selects managed `.dll` and `.exe` files; bare filename patterns immediately following it filter that directory recursively and are combined as alternatives. For example, `"C:\Programme" "foo*.exe" "*bar*.dll"` selects matching files at any depth below `C:\Programme`. Path-qualified patterns such as `C:\Vendor\*.dll` remain independent inputs and match recursively below their parent directory. `*` and `?` are supported only in the final filename segment; the executable expands patterns received literally, so command examples quote them. Directory-segment wildcards and `**` remain outside this step. Native files found by directory or pattern searches are skipped, but each directory or pattern must select at least one managed file. An explicit file path must be a managed `.dll` or `.exe`. Normalize, deduplicate identical source paths, and sort deterministically.
- The output directory is an owned dump. After input and reference-closure preflight, if the directory is absent, create it and a regular UTF-8 marker file `.ainetcodenavigator-assembly-export` containing exactly `AiNetCodeNavigator.AssemblyExport:1` followed by a newline before producing content. If it exists without that regular marker and content, abort before deleting or writing anything in it. A user can delete the entire dump manually and rebuild it with the same command.
- For each selected DLL or EXE, write one child beneath `<output-directory>\<source-file-name>\` (for example `C:\Dump\Vendor.Core.dll\`). A unique filename can use that direct child; distinct remaining same-name variants use stable subdirectories named for their local/GAC origin and identity/content hash. The child contains the ILSpy whole-project output: generated `.csproj`, C# files and folders. Generate a small `.sln` that includes that project so the promised solution-level entry point exists; a successful decompilation is not a claim that this reconstructed project compiles.
- Before cleanup, discover the transitive closure of resolved, managed, non-system references for every explicit input. Automatically selected GAC or adjacent third-party DLLs become additional dump children. Explicit file inputs and proven referenced versions remain selected, even when their names match the automatic exclusion filter. For local files selected only through recursive source discovery, keep the highest assembly version per filename; keep equal-version files with different bytes. Deduplicate byte-identical aliases by identity and SHA-256 before assigning collision-safe output paths. Preserve sibling variants on reruns.
- The default automatic-export filter excludes simple names equal to `mscorlib`, `netstandard`, `System`, `Microsoft`, `WindowsBase`, `PresentationCore`, `PresentationFramework`, `Accessibility`, `UIAutomationClient`, `UIAutomationTypes`, or `UIAutomationProvider`, and names beginning `System.`, `Microsoft.`, or `Windows.` (case-insensitive). Resolve these references for decompilation where available, but do not export or traverse them for additional dump children. Do not classify an arbitrary DLL as system merely because it is in the GAC or the runtime's trusted-platform path list. Record each excluded edge and the matching rule in the referring DLL's manifest.
- For each selected or automatically included assembly, delete only its exact selected child before decompiling it, then stage and validate the generated project and C# documents and publish a fresh child. If decompilation fails, leave that child absent, report failure and continue independent assemblies; never leave an old tree that an agent could mistake for current. Do not retain version history. Never delete the output root, unselected children, sibling variants, or paths outside the validated root. Reject reparse-point targets that could redirect cleanup.
- Resolve assembly references using existing adjacent-file and trusted-platform lookup plus the installed .NET Framework GAC. Search the .NET Framework 4+ and legacy GAC locations when present. Select a GAC candidate by actual referenced identity (name, exact version, culture and public-key token), then check PE architecture against the referring assembly; prefer a compatible MSIL candidate and fail an unresolved ambiguity rather than guess. Do not use the highest installed version as a substitute. Traverse non-system references cycle-safely; a traversal limit must be reported as an incomplete closure and cause a nonzero run, never silently omit dump children. Pass proven dependency paths to the decompiler. Do not execute analyzed assemblies.
- Record the source path, assembly identity, content hash, decompiler version, resolved dependency paths/provenance, automatically exported children, filtered edges, and unresolved dependencies in a machine-readable manifest inside each published DLL directory. Write a root-level `last-run.json` with the exact inputs, selected children, exclusions, failures and completion state of the latest invocation so an agent can detect missing or failed exports. Report unresolved dependencies visibly in the console and manifest; publish a decompilation only if its generated project and source validation succeed. A per-DLL decompilation failure makes the process exit nonzero, while successful DLLs remain available. An unresolved dependency is a reported limitation, not a substituted or invented binding.
- Document the command, output ownership and cleanup contract, GAC limits, and how an agent finds the correct DLL dump. Include the new executable in a tested local deployment and release artifact, separately from MCP transport configuration.

### Not included

- Version history, incremental merge of generated source, or preservation of files manually placed inside a selected DLL child.
- Implicit machine-wide DLL discovery or a web/Markdown feed. Recursive source discovery remains confined to explicitly named directories and pattern roots; automatic export follows only proven non-system references reachable from selected inputs.
- Graph exports, symbol indexes or other derived sidecars in step one. They may be designed later against the same validated snapshot and explicit completeness information.
- A guarantee that decompiled C# equals original source or that generated projects build.

## CLI and filesystem contract

```text
AiNetCodeNavigator.AssemblyExport.exe "C:\asm-dump" "C:\Programme" "foo*.exe" "*bar*.dll"

C:\asm-dump\
  .ainetcodenavigator-assembly-export
  last-run.json
  fooTool.exe\
    fooTool.sln
    <decompiler-generated .csproj and .cs tree>
    export-manifest.json
  Sub.Dependency.dll\    # automatically found through a non-system reference
    ...
```

Validate argument count, output ownership, recursive source directories and all globs, managed images, canonical input/output paths, the dependency closure, output-name collisions and cleanup containment before creating the output root or deleting any existing assembly child. Report one line per selected assembly for start, dependency/filter decisions, success/failure and output path, followed by counts and an exit status. A no-match directory or pattern, invalid marker, or collision fails the whole invocation before publishing. Later per-assembly decompilation errors do not erase successful siblings.

The marker protects the output root, and only exact direct assembly children selected in the current invocation are replaceable. The tool does not sweep children whose input is absent from a later invocation. Such old children remain visible as prior exports; `last-run.json` distinguishes them from the latest selection. If root creation is interrupted before its marker is written, the next run refuses to touch that unmarked directory until the user removes it.

## Design and verified feasibility

- [`AssemblyDecompilationAdapter`](../../src/AiNetCodeNavigator.Core/Assemblies/AssemblyDecompilationAdapter.cs) already uses `WholeProjectDecompiler` to produce a project tree in staging and checks generated C# syntax. The exporter should reuse this underlying path through a supported Core facade rather than duplicate ILSpy logic or publish a navigation cache directory.
- [`AssemblyAnalysisSession`](../../src/AiNetCodeNavigator.Core/Assemblies/AssemblyAnalysisSession.cs) currently keeps cache generations and snapshot leases for navigation. Its cache has different lifetime and layout requirements from the user-owned dump; the export facade should stage and publish independently.
- [`AssemblyReferenceResolver`](../../src/AiNetCodeNavigator.Core/Assemblies/AssemblyReferenceResolver.cs) already traverses bounded metadata references and checks identity, but currently searches adjacent files and trusted-platform paths, not the classic GAC. Its current framework-name version tolerance and 8-depth/128-node limits cannot be mistaken for a complete, exact GAC export closure. Add an explicit, testable GAC candidate source and a closure result that identifies any limit or ambiguity; pass proven paths to the decompiler resolver. Keep the MCP navigation behavior and its provenance guarantees under test when modifying shared Core code.
- The current release workflow [publishes only the MCP project](../../.github/workflows/release.yml); build and deployment wiring must include the new executable.
- Microsoft's [GAC documentation](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/gac) distinguishes the .NET Framework 4+ and older default locations. [Modern .NET has no GAC](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/global-assembly-cache-apis-obsolete); adjacent files and runtime paths remain relevant for those targets.

## Product boundary

The current [product rule](../../.agents/rules/03-product-boundaries.mdc) calls AiNetCodeNavigator exclusively an MCP server. Implementation must update that rule and the current-state README/docs to describe a suite with two executables: a read-only MCP navigation server and an explicit offline export CLI that writes only to its marked output. Do not relax the MCP read-only or stdout guarantees.

## Verification and acceptance

- CLI tests cover minimum arguments, recursive directories and filename patterns, managed DLL/EXE selection, deterministic expansion, duplicate paths/names, no-match and invalid/nonmanaged inputs.
- Filesystem tests cover absent-root creation with exact marker; existing unmarked/invalid-marker hard failure without mutation; per-DLL deletion/rebuild; untouched unrelated children; path containment and reparse-point protection; failed decompilation leaving no selected DLL child.
- Resolver tests use temporary GAC-like trees with exact and mismatched identities, version/token/culture/architecture cases, local-versus-GAC precedence, missing references, transitive edges and cycles. Filter tests include `System.*` and `Microsoft.*` exclusions, a non-system GAC dependency, an adjacent third-party dependency, explicit-root override, and a non-system DLL in a trusted-platform path. No test depends on the host machine's GAC contents.
- End-to-end tests run the executable against generated managed test assemblies, confirm readable C# and project/solution output and manifests for both an explicit root and its automatically selected dependency, then rerun after an assembly change. Confirm the generated `.sln` references its emitted `.csproj` by relative path. Run the repository's required build and affected Fast/Integration tests; review the generated dump manually for agent usefulness. Release/deployment verification confirms both executables start independently and MCP stdout stays protocol-only.
