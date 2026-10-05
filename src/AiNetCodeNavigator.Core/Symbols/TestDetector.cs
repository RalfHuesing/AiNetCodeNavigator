#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Central service for detecting and classifying test artifacts (projects, files, classes, methods).
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

    private static readonly string[] TestPathPrefixes =
    [
        "tests/", "test/"
    ];

    private static readonly string[] DefaultTestProjectNameSuffixes =
    [
        "Tests", "Test", "UnitTests", "UnitTest", "IntegrationTests", "IntegrationTest",
        "FastTests", "ComponentTests", "TestKit", "Specs", "Spec"
    ];

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

    public static bool IsTestProject(
        Project project,
        IReadOnlyList<string>? testProjectNameSuffixes = null,
        string? classificationPath = null)
    {
        foreach (var reference in project.MetadataReferences)
        {
            if (TestFrameworkClassifier.IsFrameworkReference(reference))
            {
                return true;
            }
        }

        var suffixes = testProjectNameSuffixes ?? DefaultTestProjectNameSuffixes;
        if (HasTestProjectNameSuffix(project.Name, suffixes))
        {
            return true;
        }

        var path = classificationPath ?? project.FilePath;
        if (path is not null)
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

        if (TestPathPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

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
        return ClassifyTestMethod(methodSymbol) is not null;
    }

    public static TestMethodClassification? ClassifyTestMethod(IMethodSymbol methodSymbol) =>
        TestFrameworkClassifier.Classify(methodSymbol);

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
