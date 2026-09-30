#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Zentraler Service zur Erkennung und Klassifizierung von Test-Artefakten (Projekte, Dateien, Klassen, Methoden).
/// </summary>
public static class TestDetector
{
    private static readonly string[] TestPathSegments =
    [
        ".Tests/", ".UnitTests/", ".FastTests/", ".IntegrationTests/", ".ComponentTests/",
        ".TestKit/", ".Specs/", "/tests/", "/test/", "/specs/"
    ];

    private static readonly string[] TestFileSuffixes =
    [
        "Tests.cs", "Test.cs", "Spec.cs", "Specs.cs"
    ];

    private static readonly string[] TestKeywords =
    [
        "xunit", "nunit", "testplatform", "unittesting", "mstest"
    ];

    private static readonly string[] DefaultTestProjectNameSuffixes =
    [
        "Tests", "Test", "UnitTests", "UnitTest", "IntegrationTests", "IntegrationTest",
        "FastTests", "ComponentTests", "TestKit", "Specs", "Spec"
    ];

    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fact", "FactAttribute",
        "Theory", "TheoryAttribute",
        "Test", "TestAttribute",
        "TestMethod", "TestMethodAttribute",
        "DataTestMethod", "DataTestMethodAttribute",
        "TestCase", "TestCaseAttribute",
        "TestCaseSource", "TestCaseSourceAttribute"
    };

    private static readonly string[] ClassNameAffixes =
    [
        "Tests", "Test", "Specs", "Spec", "IntegrationTests", "UnitTests", "ComponentTests", "FastTests"
    ];

    private static readonly string[] IntegrationPathMarkers =
    [
        "/Integration/", ".IntegrationTests/", "/IntegrationTests/", "/E2E/",
        "/EndToEnd/", "/Functional/", "/Performance/", "/Stress/"
    ];

    private static readonly string[] ComponentPathMarkers =
    [
        "/Component/", ".ComponentTests/", "/ComponentTests/"
    ];

    public static bool IsTestProject(Project project, IReadOnlyList<string>? testProjectNameSuffixes = null)
    {
        foreach (var reference in project.MetadataReferences)
        {
            if (IsTestReference(reference.Display))
            {
                return true;
            }
        }

        var suffixes = testProjectNameSuffixes ?? DefaultTestProjectNameSuffixes;
        if (HasTestProjectNameSuffix(project.Name, suffixes))
        {
            return true;
        }

        if (project.FilePath is { } path)
        {
            var fileName = Path.GetFileName(path);
            if (IsTestFile(fileName) || IsTestFile(path))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTestProjectOrHasTestFiles(Project project, IReadOnlyList<string>? testProjectNameSuffixes = null)
    {
        if (IsTestProject(project, testProjectNameSuffixes)) return true;
        return project.Documents.Any(d => d.FilePath != null && IsTestFile(d.FilePath));
    }

    public static IReadOnlyList<Project> FindTestProjects(Solution solution, IReadOnlyList<string>? testProjectNameSuffixes = null)
    {
        return solution.Projects
            .Where(p => IsTestProject(p, testProjectNameSuffixes))
            .ToList();
    }

    public static bool IsTestFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalized = PathNormalizer.NormalizeSeparators(path);

        foreach (var suffix in TestFileSuffixes)
        {
            if (EndsWithNamedAffix(normalized, suffix))
            {
                return true;
            }
        }

        foreach (var segment in TestPathSegments)
        {
            if (normalized.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTestClass(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Class) return false;

        if (ClassNameAffixes.Any(affix => EndsWithNamedAffix(typeSymbol.Name, affix)))
        {
            return true;
        }

        return typeSymbol.GetMembers().OfType<IMethodSymbol>().Any(IsTestMethod);
    }

    public static bool IsTestMethod(IMethodSymbol methodSymbol)
    {
        foreach (var attribute in methodSymbol.GetAttributes())
        {
            var attrName = attribute.AttributeClass?.Name;
            if (attrName != null && TestAttributeNames.Contains(attrName))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTestSymbol(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method && IsTestMethod(method)) return true;
        if (symbol is INamedTypeSymbol type && IsTestClass(type)) return true;

        if (symbol.ContainingType is not null && IsTestClass(symbol.ContainingType)) return true;

        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource && location.SourceTree?.FilePath != null)
            {
                if (IsTestFile(location.SourceTree.FilePath)) return true;
            }
        }

        return false;
    }

    public static string DetermineTestCategory(ISymbol symbol)
    {
        var locations = symbol.Locations.Where(l => l.IsInSource && l.SourceTree?.FilePath != null);
        foreach (var loc in locations)
        {
            var normalized = PathNormalizer.NormalizeSeparators(loc.SourceTree!.FilePath);
            if (IntegrationPathMarkers.Any(m => normalized.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return "integration";
            }
            if (ComponentPathMarkers.Any(m => normalized.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return "component";
            }
        }

        return "unit";
    }

    private static bool IsTestReference(string? referenceDisplay)
    {
        if (string.IsNullOrWhiteSpace(referenceDisplay)) return false;

        var name = Path.GetFileNameWithoutExtension(referenceDisplay);
        return TestKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTestProjectNameSuffix(string projectName, IReadOnlyList<string> suffixes)
    {
        return suffixes.Any(suffix =>
            EndsWithNamedAffix(projectName, suffix) ||
            projectName.Contains($".{suffix}.", StringComparison.OrdinalIgnoreCase));
    }

    private static bool EndsWithNamedAffix(string value, string affix)
    {
        if (!value.EndsWith(affix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var affixStart = value.Length - affix.Length;
        return affixStart == 0 ||
               char.IsUpper(value[affixStart]) ||
               value[affixStart - 1] is '.' or '_' or '-' or '/' or '\\';
    }
}
