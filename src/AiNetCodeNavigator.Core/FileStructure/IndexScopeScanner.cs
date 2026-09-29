#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>
/// Ermittelt den Index-Scope der geladenen Solution (Projekte, Dokumente, Dateitypen, Testanteil).
/// </summary>
public static class IndexScopeScanner
{
    public static async Task<IndexScopePayload> ScanAsync(
        Solution solution,
        CancellationToken ct = default)
    {
        var solutionPath = solution.FilePath ?? "in-memory-solution";
        var projects = new List<ProjectScopeEntry>();
        var extCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var totalDocs = 0;
        var csFiles = 0;
        var testProjects = 0;

        foreach (var proj in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var isTest = TestDetector.IsTestProject(proj);
            if (isTest) testProjects++;

            var docCount = proj.Documents.Count();
            totalDocs += docCount;

            projects.Add(new ProjectScopeEntry(
                Name: proj.Name,
                DocumentCount: docCount,
                IsTestProject: isTest));

            foreach (var doc in proj.Documents)
            {
                var ext = Path.GetExtension(doc.FilePath ?? doc.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) ext = "(keine)";
                extCounts[ext] = extCounts.GetValueOrDefault(ext) + 1;

                if (ext == ".cs") csFiles++;
            }
        }

        var fileTypes = extCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FileTypeScopeEntry(
                Extension: kv.Key,
                Count: kv.Value,
                SymbolGraphCovered: kv.Key == ".cs"))
            .ToList();

        var formatted = FormatReport(solutionPath, solution.Projects.Count(), totalDocs, csFiles, testProjects, projects, fileTypes);

        return new IndexScopePayload(
            SolutionPath: solutionPath,
            ProjectCount: solution.Projects.Count(),
            TotalDocumentCount: totalDocs,
            CSharpFileCount: csFiles,
            TestProjectCount: testProjects,
            Projects: projects,
            FileTypes: fileTypes,
            FormattedText: formatted);
    }

    private static string FormatReport(
        string solutionPath,
        int projectCount,
        int totalDocs,
        int csFiles,
        int testProjects,
        List<ProjectScopeEntry> projects,
        List<FileTypeScopeEntry> fileTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Index Scope: {Path.GetFileName(solutionPath)}");
        sb.AppendLine($"> Pfad: {solutionPath.Replace('\\', '/')}");
        sb.AppendLine($"> Projekte: {projectCount} ({testProjects} Test-Projekte, {projectCount - testProjects} Produktiv-Projekte)");
        sb.AppendLine($"> Dokumente: {totalDocs} (.cs: {csFiles})");
        sb.AppendLine();

        sb.AppendLine("## Projekte:");
        sb.AppendLine("| Projekt | Dokumente | Typ |");
        sb.AppendLine("| :--- | :---: | :--- |");
        foreach (var p in projects)
        {
            var type = p.IsTestProject ? "Test" : "Produktion";
            sb.AppendLine($"| {p.Name} | {p.DocumentCount} | {type} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Dateitypen im Roslyn-Index:");
        sb.AppendLine("| Endung | Anzahl | Symbol-Navigation unterstützt |");
        sb.AppendLine("| :--- | :---: | :---: |");
        foreach (var f in fileTypes)
        {
            var covered = f.SymbolGraphCovered ? "Ja (Roslyn AST)" : "Nein";
            sb.AppendLine($"| `{f.Extension}` | {f.Count} | {covered} |");
        }

        return sb.ToString().TrimEnd();
    }
}
