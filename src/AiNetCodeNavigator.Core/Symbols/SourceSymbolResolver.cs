#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>A source declaration that can be selected from an ambiguous identifier.</summary>
public sealed record SymbolResolutionCandidate(
    string Name,
    string Kind,
    string Signature,
    string FilePath,
    int Line,
    int EndLine,
    string ProjectName,
    string? DocCommentId,
    string? HandoffId);

/// <summary>The result of resolving a source identifier; ambiguous results include selectable candidates.</summary>
public sealed record SourceSymbolResolution(
    ISymbol? Symbol,
    IReadOnlyList<SymbolResolutionCandidate> Candidates,
    ResultError? Error)
{
    public bool IsSuccess => Symbol is not null && Error is null;
}

/// <summary>
/// Resolves source symbols by source handoff, documentation comment ID, file position, or an
/// exact simple/qualified name. Ambiguous names return candidate metadata with reusable handoffs.
/// </summary>
public static class SourceSymbolResolver
{
    private static readonly HashSet<char> DocumentationIdPrefixes = ['T', 'M', 'P', 'F', 'E', 'N', '!', 'A'];

    public static async Task<SourceSymbolResolution> ResolveAsync(
        Solution solution,
        string identifier,
        AnalysisSymbolIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var clean = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        AnalysisSymbolIdentity? effectiveIdentity;
        if (identity is null)
        {
            effectiveIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var validatedIdentity = await SourceHandoffResolver.ValidateIdentityAsync(solution, identity, cancellationToken).ConfigureAwait(false);
            if (!validatedIdentity.IsSuccess) return Failure(validatedIdentity.Error!.Value);
            effectiveIdentity = validatedIdentity.Value;
        }

        if (InputNormalizer.HasOpaqueHandoffPrefix(clean) || clean.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
        {
            if (effectiveIdentity is null)
            {
                return Failure(NavigationErrorCodes.InvalidHandoff, "A canonical source identity could not be created for this solution.");
            }

            var handoff = await SourceHandoffResolver.ResolveAsync(solution, clean, effectiveIdentity, cancellationToken).ConfigureAwait(false);
            if (!handoff.IsSuccess) return Failure(handoff.Error!.Value);
            return Success(handoff.Value, solution, effectiveIdentity);
        }

        if (IsDocumentationId(clean))
        {
            var stableMatches = await ResolveDocumentationIdAsync(solution, clean, cancellationToken).ConfigureAwait(false);
            return ResolveCandidates(stableMatches, solution, effectiveIdentity, identifier);
        }

        if (TryParsePosition(clean, out var path, out var line, out var column))
        {
            var positioned = await ResolvePositionAsync(solution, path, line, column, cancellationToken).ConfigureAwait(false);
            return ResolveCandidates(positioned.Symbols, solution, effectiveIdentity, identifier, positioned.Error);
        }

        if (TryParseLineOnlyPosition(clean, out path, out line))
        {
            var positioned = await ResolveLineAsync(solution, path, line, cancellationToken).ConfigureAwait(false);
            return ResolveCandidates(positioned.Symbols, solution, effectiveIdentity, identifier, positioned.Error);
        }

        var names = await ResolveByNameAsync(solution, clean, cancellationToken).ConfigureAwait(false);
        return ResolveCandidates(names, solution, effectiveIdentity, identifier);
    }

    private static SourceSymbolResolution Success(ISymbol? symbol, Solution solution, AnalysisSymbolIdentity? identity)
    {
        if (symbol is null) return Failure(NavigationErrorCodes.SymbolNotFound, "The source symbol could not be resolved.");
        var candidate = CreateCandidate(symbol, solution, identity);
        return new SourceSymbolResolution(symbol, candidate is null ? Array.Empty<SymbolResolutionCandidate>() : [candidate], null);
    }

    private static SourceSymbolResolution ResolveCandidates(
        IReadOnlyList<ISymbol> symbols,
        Solution solution,
        AnalysisSymbolIdentity? identity,
        string identifier,
        ResultError? resolutionError = null)
    {
        if (resolutionError is { } error) return Failure(error);

        var distinct = symbols
            .Select(NormalizeOwningSymbol)
            .Where(symbol => symbol is not null)
            .Cast<ISymbol>()
            .Distinct(SymbolEqualityComparer.Default)
            .ToList();
        var candidates = distinct
            .Select(symbol => CreateCandidate(symbol, solution, identity))
            .Where(candidate => candidate is not null)
            .Cast<SymbolResolutionCandidate>()
            .OrderBy(candidate => PathNormalizer.NormalizeSeparators(candidate.FilePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Line)
            .ThenBy(candidate => candidate.ProjectName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();

        if (distinct.Count == 1) return new SourceSymbolResolution(distinct[0], candidates, null);
        if (distinct.Count == 0)
        {
            return Failure(
                NavigationErrorCodes.SymbolNotFound,
                $"No source symbol matched '{identifier}'.");
        }

        var choices = string.Join(", ", candidates.Select(candidate =>
            $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} ({candidate.ProjectName}; {candidate.HandoffId ?? "no handoff"})"));
        return new SourceSymbolResolution(
            null,
            candidates,
            new ResultError(
                NavigationErrorCodes.AmbiguousSymbol,
                $"'{identifier}' matches multiple source symbols. Select a candidate using its handoff ID. {choices}"));
    }

    private static SourceSymbolResolution Failure(ResultError error) =>
        new(null, Array.Empty<SymbolResolutionCandidate>(), error);

    private static SourceSymbolResolution Failure(string code, string message) =>
        Failure(new ResultError(code, message));

    private static async Task<IReadOnlyList<ISymbol>> ResolveDocumentationIdAsync(
        Solution solution,
        string declarationId,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;

            IEnumerable<ISymbol> resolved;
            try
            {
                resolved = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation);
            }
            catch (ArgumentException)
            {
                continue;
            }

            foreach (var symbol in resolved)
            {
                if (!symbol.Locations.Any(location =>
                        location.IsInSource &&
                        location.SourceTree is not null &&
                        solution.GetDocument(location.SourceTree)?.Project.Id == project.Id))
                {
                    continue;
                }

                symbols.Add(symbol);
            }
        }

        return symbols;
    }

    private static async Task<IReadOnlyList<ISymbol>> ResolveByNameAsync(
        Solution solution,
        string identifier,
        CancellationToken cancellationToken)
    {
        var normalized = identifier.Replace("global::", string.Empty, StringComparison.Ordinal);
        var requestedName = NamePart(normalized);
        if (requestedName.Length == 0) return Array.Empty<ISymbol>();

        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            solution,
            name => string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase),
            SymbolFilter.TypeAndMember,
            cancellationToken).ConfigureAwait(false);

        var matches = symbols.Where(symbol => MatchesQualifiedName(symbol, normalized)).ToList();
        return matches;
    }

    private static string NamePart(string identifier)
    {
        var name = identifier;
        var parameterStart = name.IndexOf('(');
        if (parameterStart >= 0) name = name[..parameterStart];

        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0) name = name[(lastDot + 1)..];

        var genericStart = name.IndexOf('<');
        if (genericStart >= 0) name = name[..genericStart];
        return name.Trim();
    }

    private static bool MatchesQualifiedName(ISymbol symbol, string identifier)
    {
        var requested = identifier.Trim();
        var fullSignature = StripGlobalQualifier(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        if (string.Equals(fullSignature, requested, StringComparison.Ordinal)) return true;
        if (requested.Contains('('))
        {
            return fullSignature.EndsWith(requested, StringComparison.Ordinal);
        }

        var qualifiedName = symbol.ContainingSymbol is INamespaceOrTypeSymbol container
            ? $"{StripGlobalQualifier(container.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))}.{symbol.Name}"
            : symbol.Name;
        return identifier.Contains('.', StringComparison.Ordinal)
            ? string.Equals(qualifiedName, requested, StringComparison.Ordinal)
              || qualifiedName.EndsWith($".{requested}", StringComparison.Ordinal)
            : string.Equals(symbol.Name, requested, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(IReadOnlyList<ISymbol> Symbols, ResultError? Error)> ResolvePositionAsync(
        Solution solution,
        string path,
        int line,
        int column,
        CancellationToken cancellationToken)
    {
        if (line < 1 || column < 1)
        {
            return (Array.Empty<ISymbol>(), new ResultError(NavigationErrorCodes.InvalidArgument, "Position line and column must be at least 1."));
        }

        var documents = FindDocuments(solution, path);
        if (documents.Count == 0)
        {
            return (Array.Empty<ISymbol>(), new ResultError(NavigationErrorCodes.SymbolNotFound, $"No source document matched path '{path}'."));
        }

        var symbols = new List<ISymbol>();
        var hasValidPosition = false;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (line > text.Lines.Count)
            {
                continue;
            }

            if (column > text.Lines[line - 1].Span.Length) continue;
            hasValidPosition = true;

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null) continue;

            var position = text.Lines[line - 1].Start + column - 1;
            var token = root.FindToken(position, findInsideTrivia: true);
            var symbol = ResolveSymbolAtToken(token, semanticModel);
            if (symbol is not null && IsSourceSymbol(symbol)) symbols.Add(symbol);
        }

        return hasValidPosition
            ? (symbols, null)
            : (Array.Empty<ISymbol>(), new ResultError(NavigationErrorCodes.InvalidArgument, "The requested line or column is outside the source document."));
    }

    private static async Task<(IReadOnlyList<ISymbol> Symbols, ResultError? Error)> ResolveLineAsync(
        Solution solution,
        string path,
        int line,
        CancellationToken cancellationToken)
    {
        if (line < 1)
        {
            return (Array.Empty<ISymbol>(), new ResultError(NavigationErrorCodes.InvalidArgument, "Position line must be at least 1."));
        }

        var documents = FindDocuments(solution, path);
        if (documents.Count == 0)
        {
            return (Array.Empty<ISymbol>(), new ResultError(NavigationErrorCodes.SymbolNotFound, $"No source document matched path '{path}'."));
        }

        var symbols = new List<ISymbol>();
        var hasValidLine = false;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (line > text.Lines.Count)
            {
                continue;
            }
            hasValidLine = true;

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null) continue;

            var span = text.Lines[line - 1].Span;
            foreach (var token in root.DescendantTokens(span))
            {
                if (!span.Contains(token.Span)) continue;
                var symbol = ResolveSymbolAtToken(token, semanticModel);
                if (symbol is not null && IsSourceSymbol(symbol)) symbols.Add(symbol);
            }
        }

        return hasValidLine
            ? (symbols, null)
            : (Array.Empty<ISymbol>(), new ResultError(NavigationErrorCodes.InvalidArgument, "The requested line is outside the source document."));
    }

    private static List<Document> FindDocuments(Solution solution, string path)
    {
        var normalizedPath = PathNormalizer.NormalizeSeparators(path);
        var solutionDirectory = Path.GetDirectoryName(solution.FilePath);
        string? absolutePath = null;
        try
        {
            if (Path.IsPathFullyQualified(path))
            {
                absolutePath = PathNormalizer.NormalizeSeparators(Path.GetFullPath(path));
            }
            else if (!string.IsNullOrWhiteSpace(solutionDirectory))
            {
                absolutePath = PathNormalizer.NormalizeSeparators(Path.GetFullPath(Path.Combine(solutionDirectory, path)));
            }
        }
        catch (ArgumentException)
        {
            return [];
        }

        return solution.Projects
            .SelectMany(project => project.Documents)
            .Where(document =>
            {
                if (string.IsNullOrWhiteSpace(document.FilePath)) return false;
                var documentPath = PathNormalizer.NormalizeSeparators(document.FilePath);
                if (string.Equals(documentPath, normalizedPath, StringComparison.OrdinalIgnoreCase)) return true;
                if (absolutePath is not null &&
                    string.Equals(PathNormalizer.NormalizeSeparators(Path.GetFullPath(document.FilePath)), absolutePath, StringComparison.OrdinalIgnoreCase)) return true;

                var relativePath = string.IsNullOrWhiteSpace(solutionDirectory)
                    ? documentPath
                    : PathNormalizer.NormalizeSeparators(Path.GetRelativePath(solutionDirectory, document.FilePath));
                return string.Equals(relativePath, normalizedPath, StringComparison.OrdinalIgnoreCase)
                       || relativePath.EndsWith($"/{normalizedPath}", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static ISymbol? ResolveSymbolAtToken(SyntaxToken token, SemanticModel semanticModel)
    {
        if (!token.IsKind(SyntaxKind.IdentifierToken) && !IsAccessorKeyword(token)) return null;

        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (IsDeclarationName(node, token))
            {
                var declared = semanticModel.GetDeclaredSymbol(node);
                if (declared is not null) return NormalizeOwningSymbol(declared);
            }

            if (node is SimpleNameSyntax simpleName && simpleName.Identifier == token)
            {
                var referenced = semanticModel.GetSymbolInfo(node).Symbol;
                if (referenced is not null) return NormalizeOwningSymbol(referenced);
            }
        }

        return null;
    }

    private static bool IsDeclarationName(SyntaxNode node, SyntaxToken token) => node switch
    {
        BaseTypeDeclarationSyntax declaration => declaration.Identifier == token,
        DelegateDeclarationSyntax declaration => declaration.Identifier == token,
        MethodDeclarationSyntax declaration => declaration.Identifier == token,
        ConstructorDeclarationSyntax declaration => declaration.Identifier == token,
        DestructorDeclarationSyntax declaration => declaration.Identifier == token,
        PropertyDeclarationSyntax declaration => declaration.Identifier == token,
        EventDeclarationSyntax declaration => declaration.Identifier == token,
        VariableDeclaratorSyntax declaration => declaration.Identifier == token,
        ParameterSyntax declaration => declaration.Identifier == token,
        TypeParameterSyntax declaration => declaration.Identifier == token,
        LocalFunctionStatementSyntax declaration => declaration.Identifier == token,
        EnumMemberDeclarationSyntax declaration => declaration.Identifier == token,
        AccessorDeclarationSyntax declaration => declaration.Keyword == token,
        _ => false
    };

    private static bool IsAccessorKeyword(SyntaxToken token) =>
        token.IsKind(SyntaxKind.GetKeyword) ||
        token.IsKind(SyntaxKind.SetKeyword) ||
        token.IsKind(SyntaxKind.InitKeyword) ||
        token.IsKind(SyntaxKind.AddKeyword) ||
        token.IsKind(SyntaxKind.RemoveKeyword);

    private static bool IsSourceSymbol(ISymbol symbol) =>
        NormalizeOwningSymbol(symbol)?.Locations.Any(location => location.IsInSource && location.SourceTree is not null) == true;

    private static ISymbol? NormalizeOwningSymbol(ISymbol? symbol) => symbol switch
    {
        IMethodSymbol { AssociatedSymbol: { } owner, MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove } => owner,
        _ => symbol
    };

    private static bool TryParsePosition(string identifier, out string path, out int line, out int column)
    {
        path = string.Empty;
        line = 0;
        column = 0;
        var segments = identifier.Split(':');
        if (segments.Length < 3 ||
            !int.TryParse(segments[^1], out column) ||
            !int.TryParse(segments[^2], out line)) return false;

        path = string.Join(":", segments[..^2]);
        return path.Length > 0;
    }

    private static bool TryParseLineOnlyPosition(string identifier, out string path, out int line)
    {
        path = string.Empty;
        line = 0;
        var segments = identifier.Split(':');
        if (segments.Length < 2 || !int.TryParse(segments[^1], out line)) return false;

        path = string.Join(":", segments[..^1]);
        return path.Length > 0;
    }

    private static bool IsDocumentationId(string identifier) =>
        identifier.Length > 2 && identifier[1] == ':' && DocumentationIdPrefixes.Contains(char.ToUpperInvariant(identifier[0]));

    private static SymbolResolutionCandidate? CreateCandidate(ISymbol symbol, Solution solution, AnalysisSymbolIdentity? identity)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource && candidate.SourceTree is not null);
        if (location is null) return null;

        var filePath = location.SourceTree!.FilePath;
        var lineSpan = location.GetLineSpan();
        var document = solution.GetDocument(location.SourceTree);
        var solutionDirectory = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        return new SymbolResolutionCandidate(
            Name: symbol.Name,
            Kind: symbol.Kind.ToString().ToLowerInvariant(),
            Signature: symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            FilePath: PathNormalizer.ToRelative(solutionDirectory, filePath),
            Line: lineSpan.StartLinePosition.Line + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            ProjectName: document?.Project.Name ?? symbol.ContainingAssembly?.Name ?? string.Empty,
            DocCommentId: symbol.GetDocumentationCommentId(),
            HandoffId: SourceHandoffFormatter.Format(symbol, solution, identity));
    }

    private static string StripGlobalQualifier(string value) =>
        value.StartsWith("global::", StringComparison.Ordinal) ? value[8..] : value;
}
