using System.Text;
using System.Text.Json;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed record PublishedAssembly(PlannedAssembly Item, string CompletionState);

internal static class DumpNavigationArtifacts
{
    private static readonly string[] Columns =
        ["name", "version", "culture", "publicKeyToken", "childRelativePath", "completionState"];

    internal static void WriteReadme(ExportDumpOwnership owner, string root)
    {
        const string readme = """
            # Assembly source dump

            Run commands from this root. If the assembly is unknown, search filenames first; use domain terms, synonyms, or likely type names. Replace placeholders before running commands:

            ```powershell
            rg --files -g '*.cs' | rg -i '<domain-term>|<synonym>'
            rg -l -i -g '*.cs' '<domain-term>|<synonym>' . | Select-Object -First 50
            rg -n -i '<assembly-term>' assemblies.json
            rg -n -F '<namespace-or-class>' namespace-map.md symbol-map.md
            rg -n '^(RUN |INPUT FAILURE|FAILURE |CLOSURE LIMITATION)' last-run.log
            ```

            `assemblies.json` has one row per published assembly. `columns` names each row position; `childRelativePath` is relative to this root. Read matching rows, not the whole catalog. Owner folders derive from safe filename prefixes (`_misc` for undotted names or unsafe owner prefixes), not verified vendors; variant subfolders distinguish duplicate filenames.

            `namespace-map.md` groups class symbols under `## <namespace>` headings; `symbol-map.md` sorts class symbols directly. Each entry maps a stable, fully qualified class name to a C# path relative to this root using `/`. Nested types use `+`, and generic types append a backtick and their own parameter count (for example, ``Outer`1+Inner`2``). Record classes are included; structs, interfaces, enums and delegates are excluded. Duplicate classes across assemblies and partial declarations retain all distinct paths. `<global>` denotes the global namespace. Maps index recognizable syntax even in partial source; they are written only after publication, and a missing map means finalization is unfinished. Check run and child status before assuming completeness.

            Owners with more than 64 assembly children can have further grouping folders from safe filename-stem segments. Homogeneous segments may be skipped; grouping continues recursively for larger groups, while singleton segments stay at the current level. Broad groups with no usable split can remain above the soft target, so directory sizes can vary. Group names are packaging hints only, not proof of vendor, product, or domain. Use the catalog path to navigate and directory listings to inspect the hierarchy; there are no per-folder maps.

            Check `last-run.log` for the final `RUN COMPLETE`, `RUN FAILED`, or `RUN INTERRUPTED` summary. The catalog's `runId` must match each child's `export-manifest.json`. `running`, a missing catalog, or a missing final log summary means the run is unfinished; do not assume the catalog is exhaustive. Failed and interrupted runs can still publish usable children. `complete` run status and exit code 0 can include `partial` assemblies.

            Select a catalog child from filename matches, then search and read relevant C# sections. The catalog path may include intermediate grouping directories:

            ```powershell
            $child = '<childRelativePath>'
            rg -l -i -g '*.cs' '<domain-term>|<symbol>' "$child"
            rg -n -g '*.cs' '<symbol>|<mapping>' '<narrow-directory-or-file>'
            ```

            Read the small `export-manifest.json` for identity, provenance, status, counts, and relative detail paths (schema version 2). `sourceFilesPath` references `source-files.json`, an array of source paths; filenames are usually cheaper to find with `rg --files`. `dependenciesPath` references `dependencies.json` with `dependencies` and `filteredEdges` arrays; open it only when tracing another assembly. `diagnosticsPath` references `diagnostics.json` with `referenceClosure` and `decompilation` arrays; inspect relevant entries for `partial` output or unresolved evidence. Optional paths are null when there are no corresponding details.

            ```powershell
            $manifest = Get-Content -LiteralPath "$child/export-manifest.json" -Raw | ConvertFrom-Json
            # Only when following a dependency:
            if ($manifest.dependenciesPath) {
                $links = Get-Content -LiteralPath "$child/$($manifest.dependenciesPath)" -Raw | ConvertFrom-Json
                $links.dependencies | Where-Object name -Match '<dependency-term>' | Select-Object -First 10 | ConvertTo-Json -Depth 6
            }
            # Only for affected files or reference limitations:
            if ($manifest.diagnosticsPath) {
                $issues = Get-Content -LiteralPath "$child/$($manifest.diagnosticsPath)" -Raw | ConvertFrom-Json
                @($issues.referenceClosure) + @($issues.decompilation) | Where-Object message -Match '<file-or-symbol>' | Select-Object -First 10 | ConvertTo-Json -Depth 6
            }
            ```

            The global content search is a fallback when filenames do not identify an owner; its first 50 paths are a sample. The first 10 detail matches are also a sample; refine filters or page through results when necessary. Use filename matches or `rg -l` before reading file sections. For `partial` assemblies, check diagnostics for the affected files. Dependency `childRelativePath` values describe planned exports: confirm catalog membership before following them.

            For storage questions, trace entities and called persistence methods to SQL or explicit mappings, including adapters, dependencies, and project resources. Type names alone do not prove table names. Cite file paths and lines; distinguish direct evidence from inference. Dynamic SQL or external configuration may require evidence outside this dump; missing matches do not prove absence. Source is decompiled; generated projects may not compile.

            This marked directory is disposable and fully replaced on every export. Temporary files and stages are not published exports. Keep your own files elsewhere.
            """;
        owner.ValidateRootPreflight();
        WriteNewText(Path.Combine(root, "README.md"), readme.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    internal static void WriteCatalog(ExportDumpOwnership owner, string root, string runId, string runState,
        int selected, int partial, int failed, IEnumerable<PublishedAssembly> published)
    {
        var entries = published.OrderBy(entry => entry.Item.ChildRelativePath, StringComparer.Ordinal).ToArray();
        var content = new StringBuilder();
        content.Append("{\n\"schemaVersion\":1,\"runId\":").Append(JsonSerializer.Serialize(runId))
            .Append(",\"runState\":").Append(JsonSerializer.Serialize(runState))
            .Append(",\"selected\":").Append(selected).Append(",\"exported\":").Append(entries.Length)
            .Append(",\"partial\":").Append(partial).Append(",\"failed\":").Append(failed)
            .Append(",\n\"columns\":").Append(JsonSerializer.Serialize(Columns)).Append(",\n\"rows\":[\n");
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var identity = entry.Item.Identity;
            string[] row = [identity.Name, identity.Version, identity.Culture, identity.PublicKeyToken,
                entry.Item.ChildRelativePath, entry.CompletionState];
            content.Append(JsonSerializer.Serialize(row));
            if (index < entries.Length - 1) content.Append(',');
            content.Append('\n');
        }
        content.Append("]\n}\n");
        owner.ValidateRootPreflight();
        var path = Path.Combine(root, "assemblies.json");
        var pendingPath = Path.Combine(root, ".assemblies.json.tmp");
        WriteNewText(pendingPath, content.ToString());
        ExportDumpOwnership.RejectReparseAncestors(path);
        File.Move(pendingPath, path, overwrite: true);
    }

    private static void WriteNewText(string path, string content)
    {
        ExportDumpOwnership.RejectReparseAncestors(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
