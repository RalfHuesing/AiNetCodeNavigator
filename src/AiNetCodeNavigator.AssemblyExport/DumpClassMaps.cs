using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed record DumpClassEntry(string Namespace, string Symbol, string SourcePath);

internal static class DumpClassMaps
{
    internal static IReadOnlyList<DumpClassEntry> Read(string stage, string childRelativePath,
        IEnumerable<string> sourcePaths, CancellationToken cancellationToken)
    {
        var entries = new HashSet<DumpClassEntry>();
        foreach (var relativePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(Path.Combine(stage, relativePath));
            if (Path.IsPathRooted(relativePath) || !ExportDumpOwnership.IsWithin(source, stage))
                throw new InvalidDataException("Class map source escaped staging.");
            ExportDumpOwnership.RejectReparseAncestors(source);
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(source),
                new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: cancellationToken);
            var path = Path.Combine(childRelativePath, relativePath).Replace('\\', '/');
            foreach (var declaration in tree.GetRoot(cancellationToken).DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (declaration is not ClassDeclarationSyntax
                    && (declaration is not RecordDeclarationSyntax record
                        || record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))) continue;
                var ancestors = declaration.Ancestors().Reverse().ToArray();
                var ns = string.Join('.', ancestors.OfType<BaseNamespaceDeclarationSyntax>()
                    .Select(node => string.Join('.', node.Name.DescendantTokens()
                        .Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText))));
                var types = ancestors.OfType<TypeDeclarationSyntax>().Append(declaration).ToArray();
                if (types.Any(type => type.Identifier.IsMissing)) continue;
                var name = string.Join('+', types.Select(type => type.Identifier.ValueText
                    + (type.TypeParameterList is { Parameters.Count: > 0 } parameters
                        ? "`" + parameters.Parameters.Count : "")));
                entries.Add(new(ns, string.IsNullOrEmpty(ns) ? name : ns + "." + name, path));
            }
        }
        return entries.ToArray();
    }

    internal static void Write(ExportDumpOwnership owner, string root, IEnumerable<DumpClassEntry> entries)
    {
        var ordered = entries.Distinct().OrderBy(entry => entry.Namespace, StringComparer.Ordinal)
            .ThenBy(entry => entry.Symbol, StringComparer.Ordinal).ThenBy(entry => entry.SourcePath, StringComparer.Ordinal).ToArray();
        owner.ValidateRootPreflight();
        WriteMap(Path.Combine(root, "namespace-map.md"), "Namespace map", writer =>
        {
            foreach (var group in ordered.GroupBy(entry => entry.Namespace))
            {
                writer.WriteLine();
                writer.WriteLine("## " + (group.Key.Length == 0 ? "<global>" : group.Key));
                writer.WriteLine();
                foreach (var entry in group) WriteEntry(writer, entry);
            }
        });
        owner.ValidateRootPreflight();
        WriteMap(Path.Combine(root, "symbol-map.md"), "Class symbol map", writer =>
        {
            foreach (var entry in ordered.OrderBy(entry => entry.Symbol, StringComparer.Ordinal)
                .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)) WriteEntry(writer, entry);
        });
    }

    private static void WriteMap(string path, string title, Action<TextWriter> writeEntries)
    {
        ExportDumpOwnership.RejectReparseAncestors(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine("# " + title);
        writer.WriteLine();
        writer.WriteLine("Class and record-class declarations from published C# files. Paths are relative to this dump root and use `/`. Nested types use `+`; generic types use a backtick followed by their own parameter count. Multiple paths preserve partial declarations and assembly variants. `<global>` is the global namespace. These syntax indexes include recognizable declarations in partial source; consult `assemblies.json`, `last-run.log`, and the child manifest for completeness.");
        writer.WriteLine();
        writeEntries(writer);
    }

    private static void WriteEntry(TextWriter writer, DumpClassEntry entry) =>
        writer.WriteLine($"- ``{entry.Symbol}`` -> ``{entry.SourcePath}``");
}
