#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Ermittelt passende Test-Fixtures und Testmethoden für eine gegebene Produktionsklasse oder Methode.
/// </summary>
public static class TestRecommendationBuilder
{
    private static readonly string[] TestAffixes =
    [
        "Tests", "Test", "Specs", "Spec", "IntegrationTests", "IntegrationTest", "UnitTests", "UnitTest", "FastTests"
    ];

    public static async Task<TestContextPayload> BuildAsync(
        ISymbol targetSymbol,
        Solution solution,
        CancellationToken ct = default)
    {
        var targetType = targetSymbol as INamedTypeSymbol ?? targetSymbol.ContainingType;
        var targetTypeName = targetType?.Name ?? targetSymbol.Name;
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var candidateNames = BuildCandidateTestClassNames(targetTypeName);
        var fixtures = new List<TestFixtureMatch>();

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Search by candidate names
            foreach (var candidate in candidateNames)
            {
                ct.ThrowIfCancellationRequested();
                var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
                    solution,
                    name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase),
                    SymbolFilter.Type,
                    ct).ConfigureAwait(false);

                foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
                {
                    if (fixtures.Any(f => f.ClassName == symbol.Name)) continue;
                    if (!TestDetector.IsTestClass(symbol) && !TestDetector.IsTestProject(symbol.ContainingAssembly is null ? project : solution.GetProject(symbol.ContainingAssembly) ?? project))
                    {
                        continue;
                    }

                    var fixture = CreateFixtureMatch(symbol, solutionDir);
                    if (fixture != null)
                    {
                        fixtures.Add(fixture);
                    }
                }
            }
        }

        var totalMethods = fixtures.Sum(f => f.Methods.Count);
        return new TestContextPayload(
            TargetSymbol: targetSymbol.Name,
            TargetKind: targetSymbol.Kind.ToString().ToLowerInvariant(),
            TestFixtures: fixtures,
            TotalTestFixtures: fixtures.Count,
            TotalTestMethods: totalMethods);
    }

    private static HashSet<string> BuildCandidateTestClassNames(string typeName)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var affix in TestAffixes)
        {
            candidates.Add($"{typeName}{affix}");
            candidates.Add($"{affix}{typeName}");
        }

        return candidates;
    }

    private static TestFixtureMatch? CreateFixtureMatch(INamedTypeSymbol testClass, string solutionDir)
    {
        var syntaxRef = testClass.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null) return null;

        var loc = syntaxRef.GetSyntax().GetLocation();
        var lineSpan = loc.GetLineSpan();
        var filePath = loc.SourceTree?.FilePath is not null
            ? PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath)
            : string.Empty;
        var line = lineSpan.StartLinePosition.Line + 1;

        var framework = DetectFramework(testClass);
        var methods = new List<TestMethodMatch>();

        foreach (var member in testClass.GetMembers().OfType<IMethodSymbol>())
        {
            if (TestDetector.IsTestMethod(member))
            {
                var memberLoc = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
                var memberLine = memberLoc?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
                var memberDocId = member.GetDocumentationCommentId();
                string? memberHandoff = null;
                if (memberDocId != null)
                {
                    try { memberHandoff = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(memberDocId); }
                    catch { memberHandoff = memberDocId; }
                }

                methods.Add(new TestMethodMatch(member.Name, memberLine, memberHandoff));
            }
        }

        var docCommentId = testClass.GetDocumentationCommentId();
        string? classHandoff = null;
        if (docCommentId != null)
        {
            try { classHandoff = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(docCommentId); }
            catch { classHandoff = docCommentId; }
        }

        return new TestFixtureMatch(
            ClassName: testClass.Name,
            FilePath: filePath,
            Line: line,
            Framework: framework,
            Methods: methods,
            HandoffId: classHandoff);
    }

    private static string DetectFramework(INamedTypeSymbol testClass)
    {
        foreach (var member in testClass.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var name = attr.AttributeClass?.Name ?? "";
                if (name.StartsWith("Fact", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Theory", StringComparison.OrdinalIgnoreCase))
                {
                    return "xUnit";
                }
                if (name.StartsWith("Test", StringComparison.OrdinalIgnoreCase) || name.StartsWith("TestCase", StringComparison.OrdinalIgnoreCase))
                {
                    return "NUnit";
                }
                if (name.StartsWith("TestMethod", StringComparison.OrdinalIgnoreCase))
                {
                    return "MSTest";
                }
            }
        }

        return "xUnit";
    }
}
