#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class AssemblySearchScanner
{
    internal readonly record struct SearchLineMatch(int LineNumber, TextSpan? DeclarationNameSpan);

    public const int DefaultMaxResults = 100;
    public const int MaxResults = 1000;
    public const int MaxContextLines = 5;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly IReadOnlyDictionary<string, string> BuiltInPatterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["data_access"] = @"\b(DbContext|DbSet|IDbConnection|DbCommand|SqlConnection|NpgsqlConnection|MySqlConnection|SqliteConnection|Execute(?:Reader|NonQuery|Scalar|Sql|SqlRaw|Interpolated|Async)?|FromSql(?:Raw|Interpolated)?|SaveChanges(?:Async)?|BeginTransaction(?:Async)?|TransactionScope|Dapper|DataContext|SELECT\s+.*?\s+FROM|INSERT\s+INTO|UPDATE\s+.*?\s+SET|DELETE\s+FROM|EXEC(?:UTE)?\s+[a-zA-Z0-9_#]+|File\.(?:Read|Write|Open)\w*|Directory\.\w+)\b",
        ["external_calls"] = @"\b(HttpClient|HttpRequestMessage|WebClient|RestClient|GrpcChannel|ChannelBase|Socket|TcpClient|Process\.Start|Assembly\.Load)\b",
    };

    public static async Task<Result<AssemblySearchPayload>> SearchAsync(
        AssemblySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        var pattern = string.IsNullOrWhiteSpace(request.Query) ? BuiltInPatterns[kind] : request.Query;
        var useRegex = string.IsNullOrWhiteSpace(request.Query) || request.UseRegex;
        Regex matcher;
        try
        {
            matcher = new Regex(useRegex ? pattern : Regex.Escape(pattern),
                RegexOptions.CultureInvariant | (request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            return Result<AssemblySearchPayload>.Failure(NavigationErrorCodes.InvalidArgument, $"Invalid search pattern: {exception.Message}");
        }

        var opened = await AssemblyNavigationSessionScope.OpenAsync(request.AssemblyPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return Result<AssemblySearchPayload>.Failure(opened.Error);
        await using var scope = opened.Value!;
        var context = scope.Context;
        var limit = InspectAssemblyScanner.NormalizeLimit(request.MaxResults, DefaultMaxResults, MaxResults);
        var contextLineLimit = Math.Clamp(request.ContextLines, 0, MaxContextLines);
        var results = new List<AssemblySearchHit>(Math.Min(limit, 128));
        var totalCount = 0;

        try
        {
            foreach (var document in context.Compilation.SyntaxTrees.Select(tree => context.Compilation.GetSemanticModel(tree)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tree = document.SyntaxTree;
                var filePath = tree.FilePath;
                if (!MatchesFile(filePath, request.FileFilter)) continue;
                var sourceText = await tree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var matchingLines = FindTextLines(sourceText, root, matcher, request.DeclarationOnly);

                foreach (var match in matchingLines)
                {
                    totalCount++;
                    if (results.Count >= limit) continue;
                    var line = sourceText.Lines[match.LineNumber];
                    var surrounding = contextLineLimit == 0
                        ? Array.Empty<string>()
                        : Enumerable.Range(Math.Max(0, match.LineNumber - contextLineLimit),
                                Math.Min(sourceText.Lines.Count - 1, match.LineNumber + contextLineLimit) - Math.Max(0, match.LineNumber - contextLineLimit) + 1)
                            .Select(index => sourceText.Lines[index].ToString())
                            .ToArray();
                    var symbol = GetContainingSymbolName(root, sourceText, line, match.DeclarationNameSpan);
                    results.Add(new AssemblySearchHit(filePath, match.LineNumber + 1, line.ToString(), symbol, surrounding));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return Result<AssemblySearchPayload>.Failure(
                NavigationErrorCodes.InvalidArgument,
                "The search pattern exceeded the evaluation time limit.",
                "Simplify the regular expression or use literal matching.");
        }

        return Result<AssemblySearchPayload>.Success(new AssemblySearchPayload(
            context.Origin.CanonicalPath,
            kind,
            string.IsNullOrWhiteSpace(request.Query) ? pattern : request.Query,
            results,
            totalCount,
            totalCount > limit,
            context.Diagnostics));
    }

    internal static IEnumerable<SearchLineMatch> FindTextLines(
        Microsoft.CodeAnalysis.Text.SourceText sourceText,
        Microsoft.CodeAnalysis.SyntaxNode root,
        Regex matcher,
        bool declarationOnly)
    {
        var declarationHeaders = declarationOnly
            ? root.DescendantNodesAndSelf().OfType<MemberDeclarationSyntax>()
                .SelectMany(GetDeclarationNameSpans)
                .Concat(root.DescendantNodesAndSelf().OfType<EnumMemberDeclarationSyntax>().Select(member => member.Identifier.Span))
                .ToArray()
            : Array.Empty<TextSpan>();
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
                var declarationNameSpan = declarationHeaders.FirstOrDefault(header => header.Contains(span));
                if (declarationNameSpan.Length > 0)
                {
                    yield return new SearchLineMatch(i, declarationNameSpan);
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

    private static bool MatchesFile(string path, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || path.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
