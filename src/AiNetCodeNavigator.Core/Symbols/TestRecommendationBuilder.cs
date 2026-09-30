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
/// Finds static heuristic test-fixture candidates and attributed test methods for a production type or member.
/// Name and project evidence does not prove that a candidate tests, runs, or covers the target.
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
        CancellationToken ct = default,
        bool includeGenerated = false,
        SymbolScopeType scope = SymbolScopeType.All)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);

        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var targetType = targetSymbol as INamedTypeSymbol ?? targetSymbol.ContainingType;
        var targetTypeName = targetType?.Name ?? targetSymbol.Name;
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var candidateNames = BuildCandidateTestClassNames(targetTypeName);
        var fixtures = new List<TestFixtureMatch>();

        ct.ThrowIfCancellationRequested();
        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            solution,
            name => candidateNames.Contains(name),
            SymbolFilter.Type,
            ct).ConfigureAwait(false);
        var seenSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
        {
            ct.ThrowIfCancellationRequested();
            if (!seenSymbols.Add(symbol)) continue;

            var sourceDocument = symbol.DeclaringSyntaxReferences
                .Select(reference => solution.GetDocument(reference.SyntaxTree))
                .FirstOrDefault(document => document is not null);
            var sourceProject = sourceDocument?.Project;
            if (sourceProject is null ||
                (!TestDetector.IsTestClass(symbol) && !TestDetector.IsTestProject(sourceProject)))
            {
                continue;
            }
            var isTestDocument = TestDetector.IsTestProject(sourceProject) || TestDetector.IsTestFile(sourceDocument!.FilePath);
            if ((scope == SymbolScopeType.Production && isTestDocument) || (scope == SymbolScopeType.Tests && !isTestDocument)) continue;
            if (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(sourceDocument!, ct).ConfigureAwait(false)) continue;

            var fixture = CreateFixtureMatch(symbol, solution, solutionDir, handoffIdentity);
            if (fixture is not null) fixtures.Add(fixture);
        }

        fixtures = fixtures
            .OrderBy(fixture => PathNormalizer.NormalizeSeparators(fixture.FilePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(fixture => fixture.ClassName, StringComparer.Ordinal)
            .ToList();

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

    private static TestFixtureMatch? CreateFixtureMatch(INamedTypeSymbol testClass, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
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
                var memberHandoff = SourceHandoffFormatter.Format(member, solution, identity);

                methods.Add(new TestMethodMatch(member.Name, memberLine, memberHandoff));
            }
        }

        var classHandoff = SourceHandoffFormatter.Format(testClass, solution, identity);

        return new TestFixtureMatch(
            ClassName: testClass.Name,
            FilePath: filePath,
            Line: line,
            Framework: framework,
            Methods: methods,
            HandoffId: classHandoff,
            ProjectName: solution.GetDocument(syntaxRef.SyntaxTree)?.Project.Name)
        {
            SourceProjectId = solution.GetDocument(syntaxRef.SyntaxTree)?.Project.Id
        };
    }

    private static string DetectFramework(INamedTypeSymbol testClass)
    {
        foreach (var member in testClass.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var name = attr.AttributeClass?.Name ?? "";
                if (name.Equals("Fact", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("FactAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Theory", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TheoryAttribute", StringComparison.OrdinalIgnoreCase))
                {
                    return "xUnit";
                }

                if (name.Equals("TestMethod", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestMethodAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("DataTestMethod", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("DataTestMethodAttribute", StringComparison.OrdinalIgnoreCase))
                {
                    return "MSTest";
                }

                if (name.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCase", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCaseAttribute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCaseSource", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TestCaseSourceAttribute", StringComparison.OrdinalIgnoreCase))
                {
                    return "NUnit";
                }
            }
        }

        return "Unknown";
    }
}
