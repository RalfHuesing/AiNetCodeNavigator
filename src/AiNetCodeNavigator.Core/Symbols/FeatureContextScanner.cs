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
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Aggregiert den semantischen Feature-Kontext für ein Symbol:
/// Deklaration/Signatur, eingehende Aufrufer und zugehörige Tests.
/// Rein lesende Navigation — strikt ohne Linter-Violations oder Qualitätsmetriken.
/// </summary>
public static class FeatureContextScanner
{
    public static async Task<FeatureContextPayload?> ScanAsync(
        FeatureContextRequest request,
        CancellationToken ct = default)
    {
        var identity = request.HandoffIdentity ?? await AnalysisSymbolIdentity.ForSourceAsync(request.Solution, ct).ConfigureAwait(false);
        var resolved = await ResolveSymbolResultAsync(request.Solution, request.SymbolIdentifier, identity, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return new FeatureContextPayload(
                new FeatureContextDeclaration(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, 0),
                Array.Empty<FeatureContextCallerEntry>(), Array.Empty<FeatureContextTestRecommendation>(), 0, 0, false, false, resolved.Error);
        }

        var symbol = resolved.Value;
        if (symbol is null) return null;

        var solutionDir = Path.GetDirectoryName(request.Solution.FilePath) ?? string.Empty;
        var declaration = ExtractDeclaration(symbol, solutionDir, identity, request.Solution);

        var allCallers = await CollectCallersAsync(symbol, request.Solution, solutionDir, identity, ct).ConfigureAwait(false);
        var scopedCallers = FilterCallersByScope(allCallers, request.Scope);
        var callersTruncated = scopedCallers.Count > request.MaxCallers;
        var shownCallers = scopedCallers.Take(request.MaxCallers).ToList();

        var testContext = await TestRecommendationBuilder.BuildAsync(symbol, request.Solution, ct).ConfigureAwait(false);
        var allTests = FlattenTestRecommendations(testContext);
        var testsTruncated = allTests.Count > request.MaxTests;
        var shownTests = allTests.Take(request.MaxTests).ToList();

        return new FeatureContextPayload(
            Declaration: declaration,
            Callers: shownCallers,
            Tests: shownTests,
            TotalCallers: scopedCallers.Count,
            TotalTests: allTests.Count,
            CallersTruncated: callersTruncated,
            TestsTruncated: testsTruncated);
    }

    public static async Task<ISymbol?> ResolveSymbolAsync(
        Solution solution,
        string symbolIdentifier,
        CancellationToken ct = default)
    {
        var identity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var result = await ResolveSymbolResultAsync(solution, symbolIdentifier, identity, ct).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    public static async Task<Result<ISymbol?>> ResolveSymbolResultAsync(
        Solution solution,
        string symbolIdentifier,
        AnalysisSymbolIdentity? identity,
        CancellationToken ct = default)
    {
        var cleanId = InputNormalizer.NormalizeSymbolIdentifier(symbolIdentifier);

        if (cleanId.StartsWith("h:", StringComparison.Ordinal) || cleanId.StartsWith("i:", StringComparison.Ordinal))
        {
            if (identity is null)
            {
                return Result<ISymbol?>.Failure(NavigationErrorCodes.InvalidHandoff, "A canonical source identity could not be created for this solution.");
            }

            return await SourceHandoffResolver.ResolveAsync(solution, cleanId, identity, ct).ConfigureAwait(false);
        }

        // 2. Exact metadata name lookup
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var type = compilation.GetTypeByMetadataName(cleanId);
            if (type != null) return Result<ISymbol?>.Success(type);
        }

        // 3. Search declarations
        var nameFilter = SymbolNameMatcher.CreateDeclarationNameFilter(cleanId);
        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            solution,
            nameFilter,
            SymbolFilter.TypeAndMember,
            ct).ConfigureAwait(false);

        return Result<ISymbol?>.Success(symbols.FirstOrDefault(s => string.Equals(s.Name, cleanId, StringComparison.OrdinalIgnoreCase))
            ?? symbols.FirstOrDefault(s => SymbolNameMatcher.MatchesSymbol(s, cleanId)));
    }

    private static FeatureContextDeclaration ExtractDeclaration(ISymbol symbol, string solutionDir, AnalysisSymbolIdentity? identity, Solution solution)
    {
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        var loc = syntaxRef?.GetSyntax().GetLocation() ?? symbol.Locations.FirstOrDefault(l => l.IsInSource);

        var filePath = loc?.SourceTree?.FilePath is not null
            ? PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath)
            : string.Empty;

        int startLine = 0;
        int endLine = 0;
        if (loc is not null && loc.IsInSource)
        {
            var span = loc.GetLineSpan();
            startLine = span.StartLinePosition.Line + 1;
            endLine = span.EndLinePosition.Line + 1;
        }

        var docCommentId = symbol.GetDocumentationCommentId();
        var internalId = identity?.FormatHandoff(symbol, solution);
        var handoffId = internalId is null ? null : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);

        var kind = symbol switch
        {
            INamedTypeSymbol { IsRecord: true } => "record",
            INamedTypeSymbol nts => nts.TypeKind.ToString().ToLowerInvariant(),
            IMethodSymbol ms => ms.MethodKind == MethodKind.Constructor ? "constructor" : "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            _ => symbol.Kind.ToString().ToLowerInvariant()
        };

        var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var modifiers = SymbolVisibilityResolver.ResolveVisibility(symbol);

        return new FeatureContextDeclaration(
            SymbolName: symbol.Name,
            Kind: kind,
            Signature: signature,
            Modifiers: modifiers,
            FilePath: filePath,
            Line: startLine,
            EndLine: endLine,
            DocCommentId: docCommentId,
            HandoffId: handoffId);
    }

    private static async Task<List<FeatureContextCallerEntry>> CollectCallersAsync(
        ISymbol symbol,
        Solution solution,
        string solutionDir,
        AnalysisSymbolIdentity? identity,
        CancellationToken ct)
    {
        var callers = new List<FeatureContextCallerEntry>();
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);

        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var loc in reference.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (loc.Document is not { } doc) continue;

                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var enclosingSymbol = semanticModel?.GetEnclosingSymbol(loc.Location.SourceSpan.Start);
                if (enclosingSymbol is null) continue;

                var callerName = enclosingSymbol switch
                {
                    IMethodSymbol m => $"{m.ContainingType?.Name}.{m.Name}",
                    IPropertySymbol p => $"{p.ContainingType?.Name}.{p.Name}",
                    _ => enclosingSymbol.Name
                };

                var lineSpan = loc.Location.GetLineSpan();
                var relPath = PathNormalizer.ToRelative(solutionDir, lineSpan.Path);
                var line = lineSpan.StartLinePosition.Line + 1;
                var projName = doc.Project.Name;

                var callerDocId = enclosingSymbol.GetDocumentationCommentId();
                string? callerHandoff = null;
                var callerInternalId = identity?.FormatHandoff(enclosingSymbol, solution);
                if (callerInternalId is not null)
                    callerHandoff = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(callerInternalId);

                var callerKind = enclosingSymbol switch
                {
                    IMethodSymbol => "method",
                    IPropertySymbol => "property",
                    _ => enclosingSymbol.Kind.ToString().ToLowerInvariant()
                };

                callers.Add(new FeatureContextCallerEntry(
                    CallerName: callerName,
                    CallerKind: callerKind,
                    CallerHandoffId: callerHandoff,
                    FilePath: relPath,
                    Line: line,
                    ProjectName: projName));
            }
        }

        return callers;
    }

    private static List<FeatureContextCallerEntry> FilterCallersByScope(
        List<FeatureContextCallerEntry> callers,
        SymbolScopeType scope)
    {
        if (scope == SymbolScopeType.All) return callers;

        return callers.Where(c =>
        {
            var isTest = TestDetector.IsTestFile(c.FilePath);
            return scope switch
            {
                SymbolScopeType.Production => !isTest,
                SymbolScopeType.Tests => isTest,
                _ => true
            };
        }).ToList();
    }

    private static List<FeatureContextTestRecommendation> FlattenTestRecommendations(TestContextPayload testContext)
    {
        var result = new List<FeatureContextTestRecommendation>();
        foreach (var fixture in testContext.TestFixtures)
        {
            if (fixture.Methods.Count == 0)
            {
                result.Add(new FeatureContextTestRecommendation(
                    FixtureName: fixture.ClassName,
                    TestMethod: "(all)",
                    FilePath: fixture.FilePath,
                    Line: fixture.Line,
                    Framework: fixture.Framework,
                    HandoffId: fixture.HandoffId));
            }
            else
            {
                foreach (var method in fixture.Methods)
                {
                    result.Add(new FeatureContextTestRecommendation(
                        FixtureName: fixture.ClassName,
                        TestMethod: method.MethodName,
                        FilePath: fixture.FilePath,
                        Line: method.Line,
                        Framework: fixture.Framework,
                        HandoffId: method.HandoffId));
                }
            }
        }

        return result;
    }

    public static string RenderMarkdown(FeatureContextPayload p)
    {
        var sb = new StringBuilder();
        if (p.Error is { } error)
        {
            sb.AppendLine("# Feature Context unavailable");
            sb.AppendLine($"- Error: `{error.Code}` — {error.Message}");
            return sb.ToString().TrimEnd();
        }

        var d = p.Declaration;

        sb.AppendLine($"# Feature Context: {d.SymbolName}");
        sb.AppendLine($"- Kind: {d.Kind}");
        sb.AppendLine($"- Modifiers: {d.Modifiers}");
        sb.AppendLine($"- Signature: `{d.Signature}`");
        sb.AppendLine($"- Defined: `{d.FilePath}:{d.Line}`");
        if (d.HandoffId != null)
        {
            sb.AppendLine($"- Handoff: `{d.HandoffId}`");
        }
        sb.AppendLine();

        sb.AppendLine($"## Incoming Callers ({p.TotalCallers})");
        if (p.Callers.Count == 0)
        {
            sb.AppendLine("Keine Aufrufer gefunden.");
        }
        else
        {
            foreach (var c in p.Callers)
            {
                var handoff = c.CallerHandoffId != null ? $" [handoff: `{c.CallerHandoffId}`]" : "";
                sb.AppendLine($"- `{c.CallerName}` in `{c.FilePath}:{c.Line}` ({c.ProjectName}){handoff}");
            }
            if (p.CallersTruncated)
            {
                sb.AppendLine($"... ({p.TotalCallers - p.Callers.Count} weitere Aufrufer abgeschnitten)");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"## Associated Tests ({p.TotalTests})");
        if (p.Tests.Count == 0)
        {
            sb.AppendLine("Keine zugehörigen Tests gefunden.");
        }
        else
        {
            foreach (var t in p.Tests)
            {
                var handoff = t.HandoffId != null ? $" [handoff: `{t.HandoffId}`]" : "";
                sb.AppendLine($"- `[{t.Framework}] {t.FixtureName}.{t.TestMethod}` in `{t.FilePath}:{t.Line}`{handoff}");
            }
            if (p.TestsTruncated)
            {
                sb.AppendLine($"... ({p.TotalTests - p.Tests.Count} weitere Tests abgeschnitten)");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
