#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class AssemblySearchScanner
{
    internal readonly record struct SearchLineMatch(int LineNumber, TextSpan? DeclarationNameSpan);
    private readonly record struct DeclarationHeader(TextSpan Span, TextSpan NameSpan);
    private readonly record struct SearchHitCandidate(AssemblySearchHit Hit, ISymbol? DeclaredSymbol);

    public const int DefaultMaxResults = 100;
    public const int MaxResults = 1000;
    public const int DefaultMaxFiles = 0;
    public const int MaxFiles = 2000;
    public const int MaxContextLines = 5;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly IReadOnlyDictionary<string, string> BuiltInPatterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["data_access"] = @"\b(DbContext|DbSet|IDbConnection|DbCommand|SqlConnection|NpgsqlConnection|MySqlConnection|SqliteConnection|Execute(?:Reader|NonQuery|Scalar|Sql|SqlRaw|Interpolated|Async)?|FromSql(?:Raw|Interpolated)?|SaveChanges(?:Async)?|BeginTransaction(?:Async)?|TransactionScope|Dapper|DataContext|SELECT\s+.*?\s+FROM|INSERT\s+INTO|UPDATE\s+.*?\s+SET|DELETE\s+FROM|EXEC(?:UTE)?\s+[a-zA-Z0-9_#]+|File\.(?:Read|Write|Open)\w*|Directory\.\w+)\b",
        ["external_calls"] = @"\b(HttpClient|HttpRequestMessage|WebClient|RestClient|GrpcChannel|ChannelBase|Socket|TcpClient|Process\.Start|Assembly\.Load)\b",
    };

    public static Task<Result<AssemblySearchPayload>> SearchAsync(
        AssemblySearchRequest request,
        CancellationToken cancellationToken = default) =>
        SearchAsync(request, HandoffHandleRegistry.Default, cancellationToken);

    internal static async Task<Result<AssemblySearchPayload>> SearchAsync(
        AssemblySearchRequest request,
        HandoffHandleRegistry handoffRegistry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handoffRegistry);
        var kind = string.IsNullOrWhiteSpace(request.SearchKind)
            ? "text"
            : request.SearchKind.Trim().ToLowerInvariant();
        if (kind is not ("text" or "external_calls" or "data_access"))
        {
            return Result<AssemblySearchPayload>.Failure(
                NavigationErrorCodes.InvalidArgument,
                "searchKind must be text, external_calls, or data_access.");
        }

        if (kind == "text" && string.IsNullOrWhiteSpace(request.Query))
        {
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument, "query must not be empty for text search.");
        }

        if (request.ContextLines < 0)
        {
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument, "contextLines must be zero or greater.");
        }

        if (request.Kind is not (null or "method" or "type" or "property"))
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument, "kind must be method, type, or property.");

        if (request.MaxFiles < 0)
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument, "maxFiles must be zero or greater.");

        var pattern = string.IsNullOrWhiteSpace(request.Query) ? BuiltInPatterns[kind] : request.Query;
        var qualifiedTypeName = TryGetQualifiedTypeName(pattern, request) ? pattern.Trim() : null;
        var searchPattern = qualifiedTypeName is null ? pattern : pattern[(pattern.LastIndexOf('.') + 1)..];
        var opened = await AssemblyNavigationSessionScope.OpenAsync(request.AssemblyPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<AssemblySearchPayload>.Failure(opened.Error);
        await using var scope = opened.Value!;
        var context = scope.Context;
        var binding = AssemblyPaging.CreateSearchBinding(context.Origin.CanonicalPath, context.Origin.ContentHash,
            context.ReferenceSnapshotHash, request);
        var handoffIdentity = AnalysisSymbolIdentity.ForAssembly(context.Origin.CanonicalPath, context.Origin.ContentHash,
            context.Generation, context.ReferenceSnapshotHash);
        var cursorStatus = AssemblyPaging.ReadBoundOffset(request.Cursor, binding, out var offset);
        if (cursorStatus != AssemblyPaging.BoundCursorStatus.Valid)
            return Result<AssemblySearchPayload>.Failure(cursorStatus == AssemblyPaging.BoundCursorStatus.StaleBinding
                    ? NavigationErrorCodes.StaleSnapshot
                    : NavigationErrorCodes.InvalidArgument,
                "resultCursor is not bound to this target snapshot and search query.",
                "Repeat the same search against the same assembly snapshot using its most recent resultCursor.");
        Regex? fileMatcher = null;
        var negateFileMatcher = false;
        if (!RegexAutoDetector.TryCreateFilterRegex(request.FileFilter, out fileMatcher, out negateFileMatcher, out var filterError))
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument, filterError ?? "fileFilter is invalid.");
        List<SyntaxTree> matchingTrees;
        try
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            matchingTrees = context.Compilation.SyntaxTrees
                .Where(tree => MatchesFile(tree.FilePath, context.Origin.CanonicalPath, fileMatcher, negateFileMatcher))
                .GroupBy(tree => tree.FilePath, comparer)
                .Select(group => group.First())
                .OrderBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tree => tree.FilePath, StringComparer.Ordinal)
                .ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument,
                "The fileFilter exceeded the evaluation time limit.", "Simplify the filter or use a glob.");
        }
        var maxFiles = request.MaxFiles == 0 ? matchingTrees.Count : Math.Min(request.MaxFiles, MaxFiles);
        var limit = InspectAssemblyScanner.NormalizeLimit(request.MaxResults, DefaultMaxResults, MaxResults);
        var contextLineLimit = Math.Clamp(request.ContextLines, 0, MaxContextLines);
        var declarationOnly = request.DeclarationOnly || !string.IsNullOrWhiteSpace(request.Kind);
        var initialRegex = qualifiedTypeName is not null
            ? false
            : string.IsNullOrWhiteSpace(request.Query) || (request.UseRegex ?? RegexAutoDetector.IsLikelyRegex(searchPattern));
        var scan = await ScanAsync(initialRegex).ConfigureAwait(false);
        if (scan.Error is not null) return Result<AssemblySearchPayload>.Failure(scan.Error.Value);
        if (qualifiedTypeName is null && request.UseRegex is null && !string.IsNullOrWhiteSpace(request.Query) && !initialRegex
            && scan.TotalCount == 0 && RegexAutoDetector.HasRegexMetaCharacters(searchPattern)
            && (RegexAutoDetector.IsValidRegex(searchPattern, out _) || searchPattern.Contains('*') || searchPattern.Contains('?')))
        {
            var promotedPattern = RegexAutoDetector.IsValidRegex(searchPattern, out _)
                ? searchPattern
                : RegexAutoDetector.ConvertWildcardToRegex(searchPattern);
            var promoted = await ScanAsync(useRegex: true, promotedPattern).ConfigureAwait(false);
            if (promoted.Error is not null) return Result<AssemblySearchPayload>.Failure(promoted.Error.Value);
            if (promoted.TotalCount > 0) scan = promoted;
        }
        if (request.Cursor is not null && offset >= scan.TotalCount)
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.StaleSnapshot,
                "resultCursor is beyond the remaining search results.",
                "Use a resultCursor from a nonfinal result page.");

        async Task<(List<SearchHitCandidate> Results, int TotalCount, int HitFileCount, ResultError? Error)> ScanAsync(bool useRegex, string? overridePattern = null)
        {
            var filesWithHits = new List<(string Path, List<SearchHitCandidate> Hits)>();
            try
            {
                var matcher = new Regex(useRegex ? overridePattern ?? searchPattern : Regex.Escape(searchPattern),
                    RegexOptions.CultureInvariant | (request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase), RegexTimeout);
                foreach (var tree in matchingTrees)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var filePath = tree.FilePath;
                    var sourceText = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                    var semanticModel = context.Compilation.GetSemanticModel(tree);
                    var matchingLines = FindTextLines(sourceText, root, matcher, declarationOnly);
                    var fileHits = new List<SearchHitCandidate>();
                    foreach (var match in matchingLines)
                    {
                        if (!MatchesKind(root, match.DeclarationNameSpan, request.Kind)) continue;
                        var declaredSymbol = match.DeclarationNameSpan is { } declarationSpan
                            ? GetDeclaredSymbol(root, semanticModel, declarationSpan, cancellationToken)
                            : null;
                        if (qualifiedTypeName is not null
                            && (declaredSymbol is not INamedTypeSymbol typeSymbol
                                || !string.Equals(typeSymbol.ToDisplayString(), qualifiedTypeName, StringComparison.Ordinal))) continue;
                        var line = sourceText.Lines[match.LineNumber];
                        var surrounding = contextLineLimit == 0
                            ? Array.Empty<string>()
                            : Enumerable.Range(Math.Max(0, match.LineNumber - contextLineLimit),
                                    Math.Min(sourceText.Lines.Count - 1, match.LineNumber + contextLineLimit) - Math.Max(0, match.LineNumber - contextLineLimit) + 1)
                                .Select(index => sourceText.Lines[index].ToString())
                                .ToArray();
                        var symbol = GetContainingSymbolName(root, sourceText, line, match.DeclarationNameSpan);
                        var hit = new AssemblySearchHit(filePath, match.LineNumber + 1, line.ToString(),
                            symbol ?? declaredSymbol?.Name, surrounding);
                        fileHits.Add(new SearchHitCandidate(hit, declaredSymbol));
                    }
                    if (fileHits.Count > 0) filesWithHits.Add((filePath, fileHits));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return ([], 0, 0, new ResultError(NavigationErrorCodes.InvalidArgument,
                    "The search pattern exceeded the evaluation time limit.", "Simplify the regular expression or use literal matching."));
            }
            catch (ArgumentException exception)
            {
                return ([], 0, 0, new ResultError(NavigationErrorCodes.InvalidArgument, $"Invalid search pattern: {exception.Message}"));
            }

            var selected = filesWithHits.Take(maxFiles).ToList();
            var selectedCount = selected.Sum(file => file.Hits.Count);
            return (selected.SelectMany(file => file.Hits).ToList(), selectedCount, filesWithHits.Count, null);
        }

        var truncatedBy = new List<string>(2);
        if (request.MaxFiles > 0 && scan.HitFileCount > maxFiles) truncatedBy.Add("maxFiles");
        var pageResults = scan.Results.Skip(offset).Take(limit).Select(candidate =>
        {
            if (request.Kind is not ("type" or "method") || candidate.DeclaredSymbol is null)
                return candidate.Hit;

            var internalHandoff = handoffIdentity.FormatHandoff(candidate.DeclaredSymbol);
            var publicHandoff = internalHandoff is null
                ? null
                : handoffRegistry.GetOpaqueHandleForOutputOrThrow(internalHandoff);
            return candidate.Hit with
            {
                HandoffId = publicHandoff,
                OwnerTargetPath = publicHandoff is null ? null : context.Origin.CanonicalPath,
            };
        }).ToList();
        var hasMorePages = offset + pageResults.Count < scan.TotalCount;
        if (hasMorePages) truncatedBy.Add("maxResults");
        var resultCursor = hasMorePages ? AssemblyPaging.CreateToken(offset + pageResults.Count, binding) : null;
        return Result<AssemblySearchPayload>.Success(new AssemblySearchPayload(
            context.Origin.CanonicalPath,
            kind,
            string.IsNullOrWhiteSpace(request.Query) ? pattern : request.Query,
            pageResults,
            scan.TotalCount,
            truncatedBy.Count > 0,
            context.Diagnostics,
            truncatedBy,
            resultCursor,
            new NavigationAnalysisMetadata(
                NavigationAnalysisMetadata.CreateSnapshotId("assembly", handoffIdentity.ContentHash),
                $"search(kind={kind}, query={request.Query ?? request.SearchKind}, fileFilter={request.FileFilter ?? "*"}, declarationOnly={declarationOnly}, kindFilter={request.Kind ?? "*"}, maxFiles={maxFiles}, maxResults={limit})",
                truncatedBy.Where(static reason => reason != "maxResults").ToArray(),
                truncatedBy.Contains("maxFiles", StringComparer.Ordinal) ? "partial" : "complete",
                hasMorePages)));
    }

    internal static IEnumerable<SearchLineMatch> FindTextLines(
        Microsoft.CodeAnalysis.Text.SourceText sourceText,
        Microsoft.CodeAnalysis.SyntaxNode root,
        Regex matcher,
        bool declarationOnly)
    {
        var declarationHeaders = declarationOnly
            ? root.DescendantNodesAndSelf().OfType<MemberDeclarationSyntax>()
                .SelectMany(member => GetDeclarationNameSpans(member)
                    .Select(nameSpan => new DeclarationHeader(GetDeclarationHeaderSpan(member), nameSpan)))
                .Concat(root.DescendantNodesAndSelf().OfType<EnumMemberDeclarationSyntax>()
                    .Select(member => new DeclarationHeader(member.Span, member.Identifier.Span)))
                .ToArray()
            : Array.Empty<DeclarationHeader>();
        for (var i = 0; i < sourceText.Lines.Count; i++)
        {
            var line = sourceText.Lines[i];
            if (!matcher.IsMatch(line.ToString())) continue;
            if (!declarationOnly)
            {
                yield return new SearchLineMatch(i, null);
                continue;
            }

            foreach (Match match in matcher.Matches(line.ToString()))
            {
                var span = new TextSpan(line.Start + match.Index, match.Length);
                var trivia = root.FindTrivia(span.Start, findInsideTrivia: true);
                var token = root.FindToken(span.Start, findInsideTrivia: true);
                if (IsCommentOrDocTrivia(trivia) || token.Parent is StructuredTriviaSyntax || IsStringLiteral(token)) continue;
                var candidates = declarationHeaders.Where(header => header.Span.Contains(span)).ToArray();
                var declaration = candidates.FirstOrDefault(header => header.NameSpan.Contains(span));
                if (declaration.NameSpan.Length == 0) declaration = candidates.FirstOrDefault();
                if (declaration.NameSpan.Length > 0)
                {
                    yield return new SearchLineMatch(i, declaration.NameSpan);
                    break;
                }
            }
        }
    }

    private static IEnumerable<TextSpan> GetDeclarationNameSpans(MemberDeclarationSyntax member)
    {
        if (member is FieldDeclarationSyntax field)
        {
            return field.Declaration.Variables.Select(variable => variable.Identifier.Span);
        }
        if (member is EventFieldDeclarationSyntax eventField)
        {
            return eventField.Declaration.Variables.Select(variable => variable.Identifier.Span);
        }

        var span = GetDeclarationNameSpan(member);
        return span is null ? Array.Empty<TextSpan>() : [span.Value];
    }

    private static TextSpan GetDeclarationHeaderSpan(MemberDeclarationSyntax member)
    {
        var end = member switch
        {
            TypeDeclarationSyntax type when type.OpenBraceToken.RawKind != 0 => type.OpenBraceToken.Span.End,
            EnumDeclarationSyntax enumeration when enumeration.OpenBraceToken.RawKind != 0 => enumeration.OpenBraceToken.Span.End,
            MethodDeclarationSyntax method => GetHeaderEnd(method.Body?.OpenBraceToken, method.ExpressionBody?.ArrowToken, method.SemicolonToken),
            ConstructorDeclarationSyntax constructor => GetHeaderEnd(constructor.Body?.OpenBraceToken, constructor.ExpressionBody?.ArrowToken, constructor.SemicolonToken),
            DestructorDeclarationSyntax destructor => GetHeaderEnd(destructor.Body?.OpenBraceToken, destructor.ExpressionBody?.ArrowToken, destructor.SemicolonToken),
            OperatorDeclarationSyntax @operator => GetHeaderEnd(@operator.Body?.OpenBraceToken, @operator.ExpressionBody?.ArrowToken, @operator.SemicolonToken),
            ConversionOperatorDeclarationSyntax conversion => GetHeaderEnd(conversion.Body?.OpenBraceToken, conversion.ExpressionBody?.ArrowToken, conversion.SemicolonToken),
            PropertyDeclarationSyntax property => GetHeaderEnd(property.AccessorList?.OpenBraceToken, property.ExpressionBody?.ArrowToken, property.SemicolonToken),
            IndexerDeclarationSyntax indexer => GetHeaderEnd(indexer.AccessorList?.OpenBraceToken, indexer.ExpressionBody?.ArrowToken, indexer.SemicolonToken),
            EventDeclarationSyntax @event when @event.AccessorList is not null => @event.AccessorList.OpenBraceToken.Span.End,
            _ => member.Span.End,
        };
        return TextSpan.FromBounds(member.SpanStart, Math.Clamp(end, member.SpanStart, member.Span.End));
    }

    private static int GetHeaderEnd(SyntaxToken? openBrace, SyntaxToken? arrow, SyntaxToken semicolon)
    {
        if (openBrace is { RawKind: not 0 } brace) return brace.Span.End;
        if (arrow is { RawKind: not 0 } arrowToken) return arrowToken.Span.End;
        return semicolon.RawKind == 0 ? 0 : semicolon.Span.End;
    }

    private static TextSpan? GetDeclarationNameSpan(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.Span,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.Span,
        MethodDeclarationSyntax method => method.Identifier.Span,
        PropertyDeclarationSyntax property => property.Identifier.Span,
        EventDeclarationSyntax @event => @event.Identifier.Span,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.Span,
        DestructorDeclarationSyntax destructor => destructor.Identifier.Span,
        OperatorDeclarationSyntax @operator => @operator.OperatorToken.Span,
        ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.Span,
        IndexerDeclarationSyntax indexer => indexer.ThisKeyword.Span,
        _ => null,
    };

    private static bool IsStringLiteral(SyntaxToken token) => token.Kind() is
        Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken
        or Microsoft.CodeAnalysis.CSharp.SyntaxKind.CharacterLiteralToken
        or Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineRawStringLiteralToken
        or Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineRawStringLiteralToken;

    private static bool IsCommentOrDocTrivia(SyntaxTrivia trivia) => trivia.Kind() is
        SyntaxKind.SingleLineCommentTrivia
        or SyntaxKind.MultiLineCommentTrivia
        or SyntaxKind.SingleLineDocumentationCommentTrivia
        or SyntaxKind.MultiLineDocumentationCommentTrivia
        or SyntaxKind.DocumentationCommentExteriorTrivia;

    internal static string? GetContainingSymbolName(
        SyntaxNode root,
        Microsoft.CodeAnalysis.Text.SourceText sourceText,
        Microsoft.CodeAnalysis.Text.TextLine line,
        TextSpan? declarationNameSpan = null)
    {
        if (declarationNameSpan is TextSpan matchedDeclarationNameSpan)
        {
            return sourceText.ToString(matchedDeclarationNameSpan);
        }

        var member = root.FindNode(line.Span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault();
        var nameSpan = member is null
            ? null
            : GetDeclarationNameSpans(member)
                .Select(span => (TextSpan?)span)
                .FirstOrDefault(span => span!.Value.IntersectsWith(line.Span));
        return nameSpan is null ? null : sourceText.ToString(nameSpan.Value);
    }

    private static bool MatchesFile(string path, string assemblyPath, Regex? matcher, bool negated)
    {
        if (matcher is null) return true;
        var normalizedPath = path.Replace('\\', '/');
        if (System.IO.Path.IsPathRooted(path))
        {
            try
            {
                var relative = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(assemblyPath)!, path);
                if (relative != ".." && !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !System.IO.Path.IsPathRooted(relative))
                    normalizedPath = relative.Replace('\\', '/');
            }
            catch (ArgumentException) { }
        }

        var fileName = System.IO.Path.GetFileName(path).Replace('\\', '/');
        var matches = matcher.IsMatch(normalizedPath) || matcher.IsMatch(fileName);
        return matches != negated;
    }

    private static bool MatchesKind(SyntaxNode root, TextSpan? declarationNameSpan, string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return true;
        if (declarationNameSpan is not TextSpan declarationSpan) return false;
        var node = root.FindNode(declarationSpan, getInnermostNodeForTie: true);
        foreach (var declaration in node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>())
        {
            var declarationKind = declaration switch
            {
                BaseMethodDeclarationSyntax => "method",
                PropertyDeclarationSyntax => "property",
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => "type",
                _ => null,
            };
            if (declarationKind is not null) return string.Equals(declarationKind, kind, StringComparison.Ordinal);
        }
        return false;
    }

    private static bool TryGetQualifiedTypeName(string pattern, AssemblySearchRequest request)
    {
        if (!string.Equals(request.Kind, "type", StringComparison.Ordinal)
            || request.UseRegex is true
            || string.IsNullOrWhiteSpace(request.Query)
            || pattern.IndexOf('.') <= 0
            || pattern.IndexOfAny(['*', '?', '[', ']', '(', ')', '{', '}', '+', '\\']) >= 0)
            return false;

        return true;
    }

    private static ISymbol? GetDeclaredSymbol(
        SyntaxNode root,
        SemanticModel semanticModel,
        TextSpan declarationNameSpan,
        CancellationToken cancellationToken)
    {
        var node = root.FindNode(declarationNameSpan, getInnermostNodeForTie: true);
        foreach (var declaration in node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>())
        {
            var nameSpan = GetDeclarationNameSpan(declaration);
            if (nameSpan is null || nameSpan.Value != declarationNameSpan) continue;
            return semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        }

        return null;
    }
}
