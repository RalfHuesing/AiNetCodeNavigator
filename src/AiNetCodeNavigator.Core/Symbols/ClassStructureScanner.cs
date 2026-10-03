#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Extracts a complete overview of all members of a C# type
/// (kind, name, visibility, start/end line, line count, signature, handoff IDs).
/// Supports partial classes across multiple source files.
/// </summary>
public static class ClassStructureScanner
{
    private const string PrimaryConstructorParameterKind = "PrimaryCtor-Param";

    public const int DefaultMaxMembers = 50;
    public const int MaxMembersCap = 200;

    public static async Task<ClassStructurePayload?> ScanAsync(
        ClassStructureScanRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SymbolIdentifier);

        var resolution = await SourceSymbolResolver.ResolveAsync(request.Solution, request.SymbolIdentifier,
            request.HandoffIdentity, ct).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return new ClassStructurePayload(
                string.Empty, string.Empty, Array.Empty<string>(), 0, 0, 0, false,
                Array.Empty<ClassStructureMemberEntry>(), Array.Empty<string>(), resolution.Error)
            {
                ResolutionCandidates = resolution.Candidates
            };
        }
        var namedType = resolution.Symbol as INamedTypeSymbol ?? resolution.Symbol?.ContainingType as INamedTypeSymbol;
        if (namedType is null) return null;

        var identity = await AnalysisSymbolIdentity.ForSourceAsync(request.Solution, ct).ConfigureAwait(false);

        var solutionDir = Path.GetDirectoryName(request.Solution.FilePath) ?? string.Empty;
        var typeDeclarations = await GetEligibleDeclarationsAsync(namedType, request.Solution, request.ScopeType,
            request.IncludeGenerated, ct).ConfigureAwait(false);
        var (files, totalLines) = CollectDeclarationFiles(typeDeclarations, solutionDir);
        var extractedMembers = await ExtractMembersAsync(namedType, request.Solution, solutionDir, identity,
            request.ScopeType, request.IncludeGenerated, ct).ConfigureAwait(false);

        var filteredMembers = FilterMembers(extractedMembers, request.KindFilter, request.NameFilter);
        var sortedMembers = SortMembers(filteredMembers, request.SortBy);

        var maxMembers = request.CollectAllMembers ? int.MaxValue : Math.Clamp(request.MaxMembers, 1, MaxMembersCap);
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
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolIdentifier);

        var result = await ResolveTypeSymbolResultAsync(solution, symbolIdentifier, identity: null, ct: ct).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    public static async Task<Result<INamedTypeSymbol?>> ResolveTypeSymbolResultAsync(
        Solution solution,
        string symbolIdentifier,
        AnalysisSymbolIdentity? identity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolIdentifier);

        var resolution = await SourceSymbolResolver.ResolveAsync(solution, symbolIdentifier, identity, ct).ConfigureAwait(false);
        if (!resolution.IsSuccess) return Result<INamedTypeSymbol?>.Failure(resolution.Error!.Value);
        var symbol = resolution.Symbol;
        return Result<INamedTypeSymbol?>.Success(symbol as INamedTypeSymbol ?? symbol?.ContainingType as INamedTypeSymbol);
    }

    private static (List<string> Files, int TotalLines) CollectDeclarationFiles(
        IReadOnlyList<SyntaxReference> declarations,
        string solutionDir)
    {
        var files = new List<string>();
        int totalLines = 0;

        foreach (var syntaxRef in declarations)
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

    private static async Task<List<ClassStructureMemberEntry>> ExtractMembersAsync(
        INamedTypeSymbol namedType,
        Solution solution,
        string solutionDir,
        AnalysisSymbolIdentity? handoffIdentity,
        SymbolScopeType scopeType,
        bool includeGenerated,
        CancellationToken cancellationToken)
    {
        var result = new List<ClassStructureMemberEntry>();
        if (namedType.IsRecord)
        {
            result.AddRange(await ExtractRecordPrimaryConstructorParametersAsync(namedType, solution, solutionDir,
                scopeType, includeGenerated, cancellationToken).ConfigureAwait(false));
        }

        foreach (var m in namedType.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipMember(m)) continue;
            var declaration = await FirstEligibleDeclarationAsync(m, solution, scopeType, includeGenerated, cancellationToken).ConfigureAwait(false);
            if (declaration is null) continue;
            result.Add(CreateMemberEntry(m, declaration, solution, solutionDir, handoffIdentity));
        }

        return result;
    }

    private static async Task<IReadOnlyList<ClassStructureMemberEntry>> ExtractRecordPrimaryConstructorParametersAsync(
        INamedTypeSymbol namedType,
        Solution solution,
        string solutionDir,
        SymbolScopeType scopeType,
        bool includeGenerated,
        CancellationToken cancellationToken)
    {
        var result = new List<ClassStructureMemberEntry>();
        var primaryConstructor = namedType.InstanceConstructors
            .FirstOrDefault(constructor => constructor.DeclaringSyntaxReferences
                .Any(reference => reference.GetSyntax() is RecordDeclarationSyntax { ParameterList: not null }));
        if (primaryConstructor is null || primaryConstructor.Parameters.Length == 0)
        {
            return result;
        }

        foreach (var parameter in primaryConstructor.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = await FirstEligibleDeclarationAsync(parameter, solution, scopeType, includeGenerated, cancellationToken).ConfigureAwait(false);
            if (declaration is null) continue;
            var syntax = await declaration.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
            var location = syntax?.GetLocation() ?? parameter.Locations.FirstOrDefault(location => location.IsInSource);
            var startLine = 0;
            var endLine = 0;
            var filePath = string.Empty;
            if (location is not null && location.IsInSource)
            {
                var span = location.GetLineSpan();
                startLine = span.StartLinePosition.Line + 1;
                endLine = span.EndLinePosition.Line + 1;
                if (location.SourceTree?.FilePath is { } sourcePath)
                {
                    filePath = PathNormalizer.ToRelative(solutionDir, sourcePath);
                }
            }

            result.Add(new ClassStructureMemberEntry(
                Kind: PrimaryConstructorParameterKind,
                Name: parameter.Name,
                Visibility: "public",
                StartLine: startLine,
                EndLine: endLine,
                LineCount: 0,
                Signature: $"{parameter.Name} : {parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
                FilePath: filePath,
                HandoffId: null));
        }
        return result;
    }

    private static async Task<IReadOnlyList<SyntaxReference>> GetEligibleDeclarationsAsync(
        ISymbol symbol, Solution solution, SymbolScopeType scopeType, bool includeGenerated, CancellationToken cancellationToken)
    {
        var result = new List<SyntaxReference>();
        foreach (var declaration in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = solution.GetDocument(declaration.SyntaxTree);
            if (document is null || !MatchesScope(document, scopeType)) continue;
            if (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false)) continue;
            result.Add(declaration);
        }
        return result;
    }

    private static async Task<SyntaxReference?> FirstEligibleDeclarationAsync(
        ISymbol symbol, Solution solution, SymbolScopeType scopeType, bool includeGenerated, CancellationToken cancellationToken)
    {
        var declarations = await GetEligibleDeclarationsAsync(symbol, solution, scopeType, includeGenerated, cancellationToken).ConfigureAwait(false);
        return declarations.FirstOrDefault();
    }

    private static bool MatchesScope(Document document, SymbolScopeType scopeType)
    {
        if (scopeType == SymbolScopeType.All) return true;
        var isTest = TestDetector.IsTestProject(document.Project) || TestDetector.IsTestFile(document.FilePath ?? document.Name);
        return scopeType switch
        {
            SymbolScopeType.Production => !isTest,
            SymbolScopeType.Tests => isTest,
            _ => true
        };
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
        SyntaxReference declaration,
        Solution solution,
        string solutionDir,
        AnalysisSymbolIdentity? handoffIdentity)
    {
        var syntaxNode = declaration.GetSyntax();
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

        var signature = m is IFieldSymbol { HasConstantValue: true } constantField
            ? $"{m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} = {FormatLiteral(constantField.ConstantValue)}"
            : m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
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

        return handoffIdentity?.FormatHandoff(symbol, solution);
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
            return nts.TypeKind == TypeKind.Struct ? "Record Struct" : "Record Class";
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
            "constructor" or "constructors" => string.Equals(memberKind, "Constructor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(memberKind, PrimaryConstructorParameterKind, StringComparison.OrdinalIgnoreCase),
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
        ArgumentNullException.ThrowIfNull(p);

        var sb = new StringBuilder();
        if (p.Error is { } error)
        {
            sb.AppendLine("# Class Structure unavailable");
            sb.AppendLine($"- Error: `{error.Code}` — {error.Message}");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"# Type: {p.TypeName}");
        sb.AppendLine($"- Kind: {p.Kind}");
        var filesStr = p.Files.Count == 0 ? "unknown" : string.Join(", ", p.Files);
        var fileCountStr = p.Files.Count == 1 ? "1 file" : $"{p.Files.Count} files";
        sb.AppendLine($"- Files: {filesStr} ({fileCountStr})");
        sb.AppendLine($"- Total Lines: {p.TotalLines}");
        sb.AppendLine($"- Member Count: {p.ShownMemberCount} of {p.TotalMemberCount}");
        sb.AppendLine();

        if (p.Members.Count == 0)
        {
            sb.AppendLine("No members found.");
            return sb.ToString().TrimEnd();
        }

        var isMultiFile = p.Files.Count > 1;
        sb.AppendLine(isMultiFile
            ? "| Kind | Name | Visibility | File | Lines | Signature | Handoff |"
            : "| Kind | Name | Visibility | Lines | Signature | Handoff |");
        sb.AppendLine(isMultiFile
            ? "| :--- | :--- | :--- | :--- | :---: | :--- | :--- |"
            : "| :--- | :--- | :--- | :---: | :--- | :--- |");

        foreach (var m in p.Members)
        {
            var handoffText = m.HandoffId != null ? $"`{EscapeTableCell(m.HandoffId)}`" : "-";
            var linesText = m.LineCount > 0 ? $"{m.StartLine}-{m.EndLine} ({m.LineCount})" : "-";
            var fileText = isMultiFile ? $"{EscapeTableCell(Path.GetFileName(m.FilePath))} | " : string.Empty;
            sb.AppendLine($"| {EscapeTableCell(m.Kind)} | {EscapeTableCell(m.Name)} | {EscapeTableCell(m.Visibility)} | {fileText}{linesText} | `{EscapeTableCell(m.Signature)}` | {handoffText} |");
        }

        if (p.Truncated)
        {
            sb.AppendLine();
            sb.AppendLine($"[{p.TotalMemberCount} members total, {p.ShownMemberCount} shown — increase maxMembers or refine the filter]");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatLiteral(object? value) => value is null
        ? "null"
        : SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)
            ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;

    private static string EscapeTableCell(string value) => value
        .Replace("\r\n", " ", StringComparison.Ordinal)
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Replace("|", "\\|", StringComparison.Ordinal);
}
