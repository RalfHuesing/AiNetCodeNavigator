#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Extrahiert eine vollständige Übersicht aller Member eines C#-Typs
/// (Kind, Name, Visibility, Start-/Endzeile, Zeilenanzahl, Signatur, Handoff-IDs).
/// Unterstützt partial classes über mehrere Quelldateien.
/// </summary>
public static class ClassStructureScanner
{
    public const int DefaultMaxMembers = 50;
    public const int MaxMembersCap = 200;

    public static async Task<ClassStructurePayload?> ScanAsync(
        ClassStructureScanRequest request,
        CancellationToken ct = default)
    {
        var namedType = await ResolveTypeSymbolAsync(request.Solution, request.SymbolIdentifier, ct).ConfigureAwait(false);
        if (namedType is null) return null;

        var solutionDir = Path.GetDirectoryName(request.Solution.FilePath) ?? string.Empty;
        var (files, totalLines) = CollectDeclarationFiles(namedType, solutionDir);
        var extractedMembers = ExtractMembers(namedType, request.Solution, solutionDir, request.HandoffIdentity);

        var filteredMembers = FilterMembers(extractedMembers, request.KindFilter, request.NameFilter);
        var sortedMembers = SortMembers(filteredMembers, request.SortBy);

        var maxMembers = Math.Clamp(request.MaxMembers, 1, MaxMembersCap);
        var shownMembers = sortedMembers.Take(maxMembers).ToList();
        var isTruncated = sortedMembers.Count > shownMembers.Count;
        var truncatedBy = isTruncated ? new List<string> { "maxMembers" } : new List<string>();

        return new ClassStructurePayload(
            TypeName: namedType.ToDisplayString(),
            Kind: DescribeTypeKind(namedType),
            Files: files,
            TotalLines: totalLines,
            TotalMemberCount: sortedMembers.Count,
            ShownMemberCount: shownMembers.Count,
            Truncated: isTruncated,
            Members: shownMembers,
            TruncatedBy: truncatedBy);
    }

    public static async Task<INamedTypeSymbol?> ResolveTypeSymbolAsync(
        Solution solution,
        string symbolIdentifier,
        CancellationToken ct = default)
    {
        var cleanId = InputNormalizer.NormalizeSymbolIdentifier(symbolIdentifier);

        // 1. Try resolve through handoff handle registry if it starts with h: or is an internal id
        if (cleanId.StartsWith("h:", StringComparison.Ordinal) || cleanId.StartsWith("i:", StringComparison.Ordinal))
        {
            var restoreResult = HandoffHandleRegistry.Default.RestoreInternalHandoffForInput(cleanId);
            var internalId = restoreResult.IsSuccess ? restoreResult.Value! : cleanId;

            if (SymbolHandoffIdentifier.TryParse(internalId, out var parsed))
            {
                var targetDocId = parsed.DocumentationCommentId;
                foreach (var project in solution.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                    if (compilation is null) continue;

                    var symbol = Microsoft.CodeAnalysis.DocumentationCommentId.GetFirstSymbolForDeclarationId(targetDocId, compilation);
                    if (symbol != null)
                    {
                        if (symbol is INamedTypeSymbol nts) return nts;
                        if (symbol.ContainingType is not null) return symbol.ContainingType;
                    }
                }
            }
        }

        // 2. Exact metadata name lookup across project compilations
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var type = compilation.GetTypeByMetadataName(cleanId);
            if (type != null) return type;
        }

        // 3. Search declarations via SymbolFinder
        var nameFilter = SymbolNameMatcher.CreateDeclarationNameFilter(cleanId);
        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            solution,
            nameFilter,
            SymbolFilter.Type,
            ct).ConfigureAwait(false);

        var match = symbols
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(s => string.Equals(s.Name, cleanId, StringComparison.OrdinalIgnoreCase))
            ?? symbols
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(s => SymbolNameMatcher.MatchesSymbol(s, cleanId));

        if (match != null) return match;

        // 4. Try searching for member and resolving its containing type
        var memberSymbols = await SymbolFinder.FindSourceDeclarationsAsync(
            solution,
            nameFilter,
            SymbolFilter.Member,
            ct).ConfigureAwait(false);

        var memberMatch = memberSymbols
            .FirstOrDefault(s => string.Equals(s.Name, cleanId, StringComparison.OrdinalIgnoreCase))
            ?? memberSymbols
            .FirstOrDefault(s => SymbolNameMatcher.MatchesSymbol(s, cleanId));

        return memberMatch?.ContainingType;
    }

    private static (List<string> Files, int TotalLines) CollectDeclarationFiles(
        INamedTypeSymbol namedType,
        string solutionDir)
    {
        var files = new List<string>();
        int totalLines = 0;

        foreach (var syntaxRef in namedType.DeclaringSyntaxReferences)
        {
            var path = syntaxRef.SyntaxTree.FilePath;
            var relPath = PathNormalizer.ToRelative(solutionDir, path);
            if (!files.Contains(relPath, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(relPath);
            }

            var span = syntaxRef.GetSyntax().GetLocation().GetLineSpan();
            var lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            totalLines += Math.Max(lines, 1);
        }

        return (files, totalLines);
    }

    private static List<ClassStructureMemberEntry> ExtractMembers(
        INamedTypeSymbol namedType,
        Solution solution,
        string solutionDir,
        AnalysisSymbolIdentity? handoffIdentity)
    {
        var result = new List<ClassStructureMemberEntry>();

        foreach (var m in namedType.GetMembers())
        {
            if (ShouldSkipMember(m)) continue;
            result.Add(CreateMemberEntry(m, solution, solutionDir, handoffIdentity));
        }

        return result;
    }

    private static bool ShouldSkipMember(ISymbol m)
    {
        if (m.IsImplicitlyDeclared && m is not IMethodSymbol { MethodKind: MethodKind.Constructor })
        {
            return true;
        }

        if (m is IMethodSymbol method)
        {
            if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise)
            {
                return true;
            }
            if (method.Name.StartsWith('<') || method.Name.EndsWith('$'))
            {
                return true;
            }
        }

        if (m is IFieldSymbol field && (field.Name.StartsWith('<') || field.Name.EndsWith('$')))
        {
            return true;
        }

        return false;
    }

    private static ClassStructureMemberEntry CreateMemberEntry(
        ISymbol m,
        Solution solution,
        string solutionDir,
        AnalysisSymbolIdentity? handoffIdentity)
    {
        var syntaxNode = m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var loc = syntaxNode?.GetLocation() ?? m.Locations.FirstOrDefault(l => l.IsInSource) ?? m.Locations.FirstOrDefault();
        var memberFilePath = loc?.SourceTree?.FilePath is not null
            ? PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath)
            : string.Empty;

        int startLine = 0;
        int endLine = 0;
        int lineCount = 0;
        if (loc is not null && loc.IsInSource)
        {
            var span = loc.GetLineSpan();
            startLine = span.StartLinePosition.Line + 1;
            endLine = span.EndLinePosition.Line + 1;
            lineCount = endLine - startLine + 1;
        }

        var signature = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var handoffId = FormatMemberHandoff(m, handoffIdentity, solution);

        return new ClassStructureMemberEntry(
            Kind: ResolveMemberKind(m),
            Name: m.Name,
            Visibility: SymbolVisibilityResolver.ResolveVisibility(m),
            StartLine: startLine,
            EndLine: endLine,
            LineCount: lineCount,
            Signature: signature,
            FilePath: memberFilePath,
            HandoffId: handoffId);
    }

    private static string? FormatMemberHandoff(
        ISymbol symbol,
        AnalysisSymbolIdentity? handoffIdentity,
        Solution solution)
    {
        if (symbol.IsImplicitlyDeclared || !symbol.Locations.Any(location => location.IsInSource))
        {
            return null;
        }

        var docCommentId = symbol.GetDocumentationCommentId();
        if (docCommentId is null) return null;

        try
        {
            return HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(docCommentId);
        }
        catch
        {
            return docCommentId;
        }
    }

    private static string ResolveMemberKind(ISymbol m)
    {
        if (m is IMethodSymbol method)
        {
            return method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ? "Constructor" : "Method";
        }
        if (m is IPropertySymbol) return "Property";
        if (m is IFieldSymbol field) return field.IsConst ? "Constant" : "Field";
        if (m is IEventSymbol) return "Event";
        if (m is INamedTypeSymbol nts)
        {
            return nts.TypeKind switch
            {
                TypeKind.Enum => "Enum",
                TypeKind.Interface => "Interface",
                TypeKind.Struct => "Struct",
                _ => "Class",
            };
        }
        return m.Kind.ToString();
    }

    private static string DescribeTypeKind(INamedTypeSymbol nts)
    {
        if (nts.IsRecord)
        {
            return nts.TypeKind == TypeKind.Struct ? "Record Struct" : "Record";
        }

        return nts.TypeKind switch
        {
            TypeKind.Interface => "Interface",
            TypeKind.Struct => "Struct",
            TypeKind.Enum => "Enum",
            _ => "Class"
        };
    }

    private static List<ClassStructureMemberEntry> FilterMembers(
        List<ClassStructureMemberEntry> members,
        string? kindFilter,
        string? nameFilter)
    {
        var result = (IEnumerable<ClassStructureMemberEntry>)members;
        if (!string.IsNullOrWhiteSpace(kindFilter) && !kindFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedKind = kindFilter.Trim();
            result = result.Where(m => MatchesKind(m.Kind, normalizedKind));
        }

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var normalizedName = nameFilter.Trim();
            result = result.Where(m => m.Name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }

    private static bool MatchesKind(string memberKind, string filter)
    {
        if (string.Equals(memberKind, filter, StringComparison.OrdinalIgnoreCase)) return true;
        return filter.ToLowerInvariant() switch
        {
            "method" or "methods" => string.Equals(memberKind, "Method", StringComparison.OrdinalIgnoreCase),
            "property" or "properties" => string.Equals(memberKind, "Property", StringComparison.OrdinalIgnoreCase),
            "field" or "fields" => string.Equals(memberKind, "Field", StringComparison.OrdinalIgnoreCase),
            "constructor" or "constructors" => string.Equals(memberKind, "Constructor", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static List<ClassStructureMemberEntry> SortMembers(
        List<ClassStructureMemberEntry> members,
        string? sortBy)
    {
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "name" => members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "kind" => members.OrderBy(m => m.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => members.OrderBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.StartLine).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    public static string RenderMarkdown(ClassStructurePayload p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Typ: {p.TypeName}");
        sb.AppendLine($"- Kind: {p.Kind}");
        var filesStr = p.Files.Count == 0 ? "unbekannt" : string.Join(", ", p.Files);
        var fileCountStr = p.Files.Count == 1 ? "1 Datei" : $"{p.Files.Count} Dateien";
        sb.AppendLine($"- Files: {filesStr} ({fileCountStr})");
        sb.AppendLine($"- Total Lines: {p.TotalLines}");
        sb.AppendLine($"- Member Count: {p.ShownMemberCount} von {p.TotalMemberCount}");
        sb.AppendLine();

        if (p.Members.Count == 0)
        {
            sb.AppendLine("Keine Member gefunden.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("| Kind | Name | Visibility | Lines | Signature | Handoff |");
        sb.AppendLine("| :--- | :--- | :--- | :---: | :--- | :--- |");

        foreach (var m in p.Members)
        {
            var handoffText = m.HandoffId != null ? $"`{m.HandoffId}`" : "-";
            var linesText = m.LineCount > 0 ? $"{m.StartLine}-{m.EndLine} ({m.LineCount})" : "-";
            sb.AppendLine($"| {m.Kind} | {m.Name} | {m.Visibility} | {linesText} | `{m.Signature}` | {handoffText} |");
        }

        if (p.Truncated)
        {
            sb.AppendLine();
            sb.AppendLine($"[{p.TotalMemberCount} Member gesamt, {p.ShownMemberCount} gezeigt — maxMembers erhöhen oder Filter verfeinern]");
        }

        return sb.ToString().TrimEnd();
    }
}
