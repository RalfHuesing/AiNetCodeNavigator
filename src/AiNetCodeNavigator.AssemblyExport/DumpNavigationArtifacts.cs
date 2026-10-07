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

            Start with `assemblies.json`. Its `columns` define the positional values in each `rows` entry; one row identifies one published assembly. Search rows by assembly name, then use `childRelativePath` relative to this root. Rows are sorted by path. Owner folders derive from the filename prefix before the first dot (`_misc` for undotted names); they do not identify vendors. Variant subfolders distinguish assemblies with the same filename.

            Check `last-run.log` for the final `RUN COMPLETE`, `RUN FAILED`, or `RUN INTERRUPTED` summary. The catalog's `runId` must match each child's `export-manifest.json`. `running`, a missing catalog, or a missing final log summary means the run is unfinished; do not assume the catalog is exhaustive. Failed and interrupted runs can still publish usable children. `complete` run status and exit code 0 can include `partial` assemblies.

            Read the selected manifest's `completionState` and diagnostics before relying on its source. It contains source paths, project path, full identity, dependencies, and limitations. Dependency `childRelativePath` values describe planned exports: confirm catalog membership before following them.

            Search within the selected child with `rg --files -g '*.cs'` or `rg -n -g '*.cs' 'TypeName|MemberName'`, then read only relevant files. Source is decompiled; generated projects may not compile.

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
