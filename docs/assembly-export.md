# Assembly export CLI

`AiNetCodeNavigator.AssemblyExport.exe` is a separate offline command in the solution. It accepts an output directory and one or more DLL paths or quoted patterns:

```powershell
AiNetCodeNavigator.AssemblyExport.exe "C:\Dump" "C:\Vendor\*.dll" "C:\Other\Product.Core.dll"
```

The positional parser requires at least two arguments. Preflight expands `*` and `?` only in the final filename segment; directory wildcards and recursive `**` are unsupported. Every pattern must match managed DLL files. Paths are normalized, deduplicated and sorted. `--help` prints usage and exits with status `0`; argument or preflight failures return `2`. The current executable returns `1` after successful preflight because project export execution is still being implemented.

## Input planning and ownership

Preflight resolves the non-system reference closure through [Core reference resolution](navigation/assembly-navigation.md), retaining unresolved edges and dependency provenance. An incomplete traversal or ambiguous resolution aborts. Automatic selection excludes simple names `mscorlib`, `netstandard`, `System`, `Microsoft`, `WindowsBase`, `PresentationCore`, `PresentationFramework`, `Accessibility`, `UIAutomationClient`, `UIAutomationTypes`, and `UIAutomationProvider`, and prefixes `System.`, `Microsoft.`, and `Windows.` (case-insensitive). Filtered edges retain their matching rule. Explicit inputs are selected regardless of the filter; their non-system references are traversed. GAC and runtime provenance alone do not exclude a dependency.

The planner deduplicates identical paths and identical assembly identities with identical bytes. Different content with the same identity and different files mapping to the same DLL filename fail preflight. Proven dependency aliases are normalized for decompilation while the original closure edges retain their paths and provenance. Source files and resolved dependencies inside the output root are rejected.

The filesystem owner validates the root and every selected direct child before creating or deleting anything. An existing root must contain the regular UTF-8 marker `.ainetcodenavigator-assembly-export` with exactly `AiNetCodeNavigator.AssemblyExport:1` and an LF newline, without a BOM. Root creation writes this marker before content. An interrupted creation leaving an unmarked root requires manual removal before another run.

Cleanup accepts only the selected direct `<dll-file-name>` children. It preserves unrelated children and rejects reparse points in root ancestors, selected children and their descendants. Ownership is revalidated before cleanup. Staging paths must be direct children prefixed `.assembly-export-stage-`; they are checked for reparse redirects. These filesystem operations are available to the export pipeline; the current preflight-only CLI does not create a dump.
