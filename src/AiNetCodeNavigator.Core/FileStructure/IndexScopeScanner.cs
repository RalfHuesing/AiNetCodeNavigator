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

/// <summary>Summarizes the Roslyn document scope of a solution or project.</summary>
public static class IndexScopeScanner
{
    public const int DefaultMaxProjects = 100;
    public const int MaxProjectsCap = 500;
    public const int DefaultMaxFileTypes = 64;
    public const int MaxFileTypesCap = 128;

    public static Task<IndexScopePayload> ScanAsync(
        Solution solution,
        CancellationToken ct = default,
        IndexScopeScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ct.ThrowIfCancellationRequested();

        var requested = options ?? new IndexScopeScanOptions();
        var requestedProjectName = string.IsNullOrWhiteSpace(requested.ProjectName) ? null : requested.ProjectName.Trim();
        var effectiveMaxProjects = ClampBound(requested.MaxProjects, MaxProjectsCap);
        var effectiveMaxFileTypes = ClampBound(requested.MaxFileTypes, MaxFileTypesCap);
        var boundsWereClamped = effectiveMaxProjects != requested.MaxProjects || effectiveMaxFileTypes != requested.MaxFileTypes;
        var solutionPath = solution.FilePath ?? "in-memory-solution";
        var allProjects = solution.Projects.ToList();
        var scopedProjects = requestedProjectName is null
            ? allProjects
            : allProjects.Where(project => string.Equals(project.Name, requestedProjectName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (requestedProjectName is not null && scopedProjects.Count == 0)
        {
            return Task.FromResult(ErrorPayload(solutionPath, requestedProjectName, requested, effectiveMaxProjects, effectiveMaxFileTypes,
                boundsWereClamped, $"Project '{requestedProjectName}' was not found."));
        }

        var projectName = requestedProjectName is null ? null : scopedProjects[0].Name;

        var projectEntries = new List<ProjectScopeEntry>();
        var extensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var coveredExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalDocuments = 0;
        var cSharpDocumentCount = 0;
        var testProjectCount = 0;

        try
        {
            foreach (var project in scopedProjects)
            {
                ct.ThrowIfCancellationRequested();
                var isCSharpProject = string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);
                var isTestProject = TestDetector.IsTestProject(project);
                if (isTestProject) testProjectCount++;

                var documents = project.Documents.ToList();
                totalDocuments += documents.Count;
                var projectCSharpDocumentCount = 0;

                foreach (var document in documents)
                {
                    ct.ThrowIfCancellationRequested();
                    var extension = NormalizeExtension(document.FilePath ?? document.Name);
                    extensionCounts[extension] = extensionCounts.GetValueOrDefault(extension) + 1;
                    if (isCSharpProject && extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        cSharpDocumentCount++;
                        projectCSharpDocumentCount++;
                        coveredExtensionCounts[extension] = coveredExtensionCounts.GetValueOrDefault(extension) + 1;
                    }
                }

                projectEntries.Add(new ProjectScopeEntry(
                    Name: project.Name,
                    DocumentCount: documents.Count,
                    IsTestProject: isTestProject,
                    CSharpDocumentCount: projectCSharpDocumentCount,
                    IsCSharpProject: isCSharpProject));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(ErrorPayload(solutionPath, projectName, requested, effectiveMaxProjects, effectiveMaxFileTypes,
                boundsWereClamped, $"Index scope scan failed: {ex.Message}"));
        }

        var allFileTypes = extensionCounts
            .Select(entry => new FileTypeScopeEntry(
                Extension: entry.Key,
                Count: entry.Value,
                SymbolGraphCovered: entry.Key.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    && coveredExtensionCounts.GetValueOrDefault(entry.Key) == entry.Value))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Extension, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Extension, StringComparer.Ordinal)
            .ToList();
        var orderedProjects = projectEntries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        var shownProjects = orderedProjects.Take(effectiveMaxProjects).ToList();
        var shownFileTypes = allFileTypes.Take(effectiveMaxFileTypes).ToList();
        var truncatedBy = new List<string>(2);
        if (shownProjects.Count < orderedProjects.Count) truncatedBy.Add("maxProjects");
        if (shownFileTypes.Count < allFileTypes.Count) truncatedBy.Add("maxFileTypes");
        var nextAction = GetNextAction(truncatedBy, projectName);
        var formatted = FormatReport(
            solutionPath,
            projectName,
            orderedProjects.Count,
            totalDocuments,
            cSharpDocumentCount,
            testProjectCount,
            allFileTypes.Count,
            shownProjects,
            shownFileTypes,
            truncatedBy,
            nextAction);

        return Task.FromResult(new IndexScopePayload(
            SolutionPath: solutionPath,
            ProjectCount: orderedProjects.Count,
            TotalDocumentCount: totalDocuments,
            CSharpFileCount: cSharpDocumentCount,
            TestProjectCount: testProjectCount,
            Projects: shownProjects,
            FileTypes: shownFileTypes,
            FormattedText: formatted,
            ScopeProjectName: projectName,
            TotalFileTypeCount: allFileTypes.Count,
            ShownProjectCount: shownProjects.Count,
            ShownFileTypeCount: shownFileTypes.Count,
            ScanCompleted: true,
            IsTruncated: truncatedBy.Count > 0,
            TruncatedBy: truncatedBy,
            RequestedMaxProjects: requested.MaxProjects,
            EffectiveMaxProjects: effectiveMaxProjects,
            RequestedMaxFileTypes: requested.MaxFileTypes,
            EffectiveMaxFileTypes: effectiveMaxFileTypes,
            BoundsWereClamped: boundsWereClamped,
            NextAction: nextAction));
    }

    private static int ClampBound(int requested, int cap) => requested < 1 ? 1 : Math.Min(requested, cap);

    private static string NormalizeExtension(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return string.IsNullOrEmpty(extension) ? "(no extension)" : extension;
    }

    private static string? GetNextAction(IReadOnlyList<string> truncatedBy, string? projectName)
    {
        if (truncatedBy.Count == 0) return null;
        var actions = new List<string>(2);
        if (truncatedBy.Contains("maxProjects", StringComparer.Ordinal))
        {
            actions.Add(projectName is null
                ? $"increase MaxProjects (up to {MaxProjectsCap}) or select one project"
                : $"increase MaxProjects (up to {MaxProjectsCap})");
        }
        if (truncatedBy.Contains("maxFileTypes", StringComparer.Ordinal))
        {
            actions.Add($"increase MaxFileTypes (up to {MaxFileTypesCap})");
        }

        return $"Increase the result bounds to {string.Join(" and ", actions)}.";
    }

    private static IndexScopePayload ErrorPayload(
        string solutionPath,
        string? projectName,
        IndexScopeScanOptions requested,
        int effectiveMaxProjects,
        int effectiveMaxFileTypes,
        bool boundsWereClamped,
        string error)
    {
        var scope = projectName is null ? "all projects" : $"project '{projectName}'";
        var formatted = $"# Index Scope: {Path.GetFileName(solutionPath)}{Environment.NewLine}> Path: {solutionPath.Replace('\\', '/')}{Environment.NewLine}{Environment.NewLine}{error}";
        return new IndexScopePayload(
            SolutionPath: solutionPath,
            ProjectCount: 0,
            TotalDocumentCount: 0,
            CSharpFileCount: 0,
            TestProjectCount: 0,
            Projects: [],
            FileTypes: [],
            FormattedText: formatted,
            ScopeProjectName: projectName,
            ScanCompleted: false,
            Error: $"{error} Requested scope: {scope}.",
            RequestedMaxProjects: requested.MaxProjects,
            EffectiveMaxProjects: effectiveMaxProjects,
            RequestedMaxFileTypes: requested.MaxFileTypes,
            EffectiveMaxFileTypes: effectiveMaxFileTypes,
            BoundsWereClamped: boundsWereClamped);
    }

    private static string FormatReport(
        string solutionPath,
        string? projectName,
        int projectCount,
        int totalDocuments,
        int cSharpDocumentCount,
        int testProjectCount,
        int totalFileTypeCount,
        IReadOnlyList<ProjectScopeEntry> projects,
        IReadOnlyList<FileTypeScopeEntry> fileTypes,
        IReadOnlyList<string> truncatedBy,
        string? nextAction)
    {
        var title = projectName is null
            ? $"# Index Scope: {Path.GetFileName(solutionPath)}"
            : $"# Index Scope: {projectName} ({Path.GetFileName(solutionPath)})";
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine($"> Path: {solutionPath.Replace('\\', '/')}");
        sb.AppendLine($"> Projects: {projectCount} ({testProjectCount} test, {projectCount - testProjectCount} production)");
        sb.AppendLine($"> Roslyn documents: {totalDocuments} (.cs: {cSharpDocumentCount})");
        sb.AppendLine($"> File types: {totalFileTypeCount} total, {fileTypes.Count} shown");
        if (truncatedBy.Count > 0)
        {
            sb.AppendLine($"> Truncated by: {string.Join(", ", truncatedBy)}");
            sb.AppendLine($"> Next step: {nextAction}");
        }
        sb.AppendLine();

        sb.AppendLine("## Projects");
        sb.AppendLine("| Project | Documents | C# documents | Language | Kind |");
        sb.AppendLine("| :--- | ---: | ---: | :--- | :--- |");
        foreach (var project in projects)
        {
            var language = project.IsCSharpProject ? "C#" : "Other";
            var kind = project.IsTestProject ? "Test" : "Production";
            sb.AppendLine($"| {project.Name} | {project.DocumentCount} | {project.CSharpDocumentCount} | {language} | {kind} |");
        }
        sb.AppendLine();

        sb.AppendLine("## File types in Roslyn index");
        sb.AppendLine("| Extension | Document entries | Symbol navigation covered |");
        sb.AppendLine("| :--- | ---: | :---: |");
        foreach (var fileType in fileTypes)
        {
            var covered = fileType.SymbolGraphCovered ? "Yes (C# documents)" : "No";
            sb.AppendLine($"| `{fileType.Extension}` | {fileType.Count} | {covered} |");
        }

        return sb.ToString().TrimEnd();
    }
}
