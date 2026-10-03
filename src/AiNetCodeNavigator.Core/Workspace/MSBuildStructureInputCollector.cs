#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Globbing;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Workspace;

internal sealed record SolutionStructureInputs(
    IReadOnlyCollection<string> ImportedFiles,
    IReadOnlyCollection<string> PotentialImportPaths,
    IReadOnlyCollection<string> CompileGlobRoots,
    IReadOnlyCollection<string> WildcardImportPatterns,
    IReadOnlyCollection<string> UnresolvedExpressions,
    IReadOnlyDictionary<string, ConfiguredTargetFrameworks> ConfiguredTargetFrameworks)
{
    internal bool HasUnexpandedExpressions => UnresolvedExpressions.Count > 0;
}

/// <summary>
/// Collects the effective MSBuild imports and wildcard roots used by loaded projects.
/// </summary>
internal static class MSBuildStructureInputCollector
{
    internal static SolutionStructureInputs Collect(Solution solution)
    {
        MSBuildSolutionLoader.EnsureMSBuildRegistered();
        var importedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var potentialImportPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var compileGlobRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wildcardImportPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedExpressions = new HashSet<string>(StringComparer.Ordinal);
        var configuredTargetFrameworks = new Dictionary<string, ConfiguredTargetFrameworks>(StringComparer.OrdinalIgnoreCase);
        var projectPaths = solution.Projects
            .Select(project => project.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        using var collection = new ProjectCollection(MSBuildSolutionLoader.CreateWorkspaceProperties());

        foreach (var projectPath in projectPaths)
        {
            var project = collection.LoadProject(projectPath);
            var frameworks = project.GetPropertyValue("TargetFrameworks");
            if (string.IsNullOrWhiteSpace(frameworks))
                frameworks = project.GetPropertyValue("TargetFramework");
            configuredTargetFrameworks[NormalizeProjectPath(projectPath)] = new ConfiguredTargetFrameworks(true,
                frameworks.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .ToArray());
            var importedProjectFiles = project.Imports
                .Select(import => import.ImportedProject)
                .ToArray();
            foreach (var import in project.Imports)
            {
                if (!string.IsNullOrWhiteSpace(import.ImportedProject.FullPath))
                {
                    importedFiles.Add(Path.GetFullPath(import.ImportedProject.FullPath));
                }
            }

            var projectFiles = importedProjectFiles.Append(project.Xml).Distinct();
            foreach (var projectFile in projectFiles)
            {
                AddPotentialImportPaths(project, projectFile, potentialImportPaths, wildcardImportPatterns, unresolvedExpressions);
                AddCompileGlobRoots(project, projectFile, compileGlobRoots, unresolvedExpressions);
            }
        }

        collection.UnloadAllProjects();
        return new SolutionStructureInputs(
            importedFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            potentialImportPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            compileGlobRoots.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            wildcardImportPatterns.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            unresolvedExpressions.Order(StringComparer.Ordinal).ToArray(),
            configuredTargetFrameworks);
    }

    private static string NormalizeProjectPath(string projectPath) => Path.GetFullPath(projectPath).Replace('\\', '/');

    private static void AddPotentialImportPaths(
        Microsoft.Build.Evaluation.Project evaluatedProject,
        ProjectRootElement projectFile,
        ISet<string> paths,
        ISet<string> wildcardPatterns,
        ISet<string> unresolvedExpressions)
    {
        foreach (var import in EnumerateImportElements(projectFile))
        {
            var containingDirectory = Path.TrimEndingDirectorySeparator(import.ContainingProject.DirectoryPath);
            var importExpression = import.Project
                .Replace("$(MSBuildThisFileDirectory)", containingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                .Replace("$(MSBuildThisFileFullPath)", import.ContainingProject.FullPath, StringComparison.OrdinalIgnoreCase);
            var expandedImports = evaluatedProject.ExpandString(importExpression);
            if (ContainsUnexpandedExpression(expandedImports))
            {
                unresolvedExpressions.Add($"{projectFile.FullPath}|Import|{importExpression}|{expandedImports}");
                continue;
            }

            foreach (var importPath in expandedImports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (importPath.IndexOfAny(['*', '?']) >= 0)
                {
                    try
                    {
                        wildcardPatterns.Add(Path.GetFullPath(importPath, containingDirectory));
                    }
                    catch (ArgumentException)
                    {
                        unresolvedExpressions.Add($"{projectFile.FullPath}|Import|{importExpression}|{expandedImports}");
                    }

                    continue;
                }

                try
                {
                    paths.Add(Path.GetFullPath(importPath, containingDirectory));
                }
                catch (ArgumentException)
                {
                    // An unresolved or invalid optional import must not make an otherwise loaded solution fail.
                    unresolvedExpressions.Add($"{projectFile.FullPath}|Import|{importExpression}|{expandedImports}");
                }
            }
        }

    }

    private static IEnumerable<ProjectImportElement> EnumerateImportElements(ProjectElementContainer container)
    {
        foreach (var child in container.Children)
        {
            if (child is ProjectImportElement import)
            {
                yield return import;
            }
            else if (child is ProjectElementContainer nestedContainer)
            {
                foreach (var nestedImport in EnumerateImportElements(nestedContainer))
                {
                    yield return nestedImport;
                }
            }
        }
    }

    private static bool ContainsUnexpandedExpression(string value) =>
        value.Contains("$(", StringComparison.Ordinal)
        || value.Contains("@(", StringComparison.Ordinal)
        || value.Contains("%(", StringComparison.Ordinal);

    private static void AddCompileGlobRoots(
        Microsoft.Build.Evaluation.Project evaluatedProject,
        ProjectRootElement projectFile,
        ISet<string> roots,
        ISet<string> unresolvedExpressions)
    {
        foreach (var item in projectFile.Items.Where(item =>
            item.ItemType.Equals("Compile", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.Include)))
        {
            var include = item.Include.Replace(
                "$(MSBuildThisFileDirectory)",
                Path.TrimEndingDirectorySeparator(item.ContainingProject.DirectoryPath) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
            var expandedIncludes = evaluatedProject.ExpandString(include);
            if (ContainsUnexpandedExpression(expandedIncludes))
            {
                unresolvedExpressions.Add($"{projectFile.FullPath}|Compile|{include}|{expandedIncludes}");
                continue;
            }

            foreach (var expandedInclude in expandedIncludes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var wildcardIndex = expandedInclude.IndexOfAny(['*', '?']);
                if (wildcardIndex < 0)
                {
                    continue;
                }

                var pattern = Path.GetFullPath(expandedInclude, item.ContainingProject.DirectoryPath);
                var expandedWildcardIndex = pattern.IndexOfAny(['*', '?']);
                var lastSeparatorIndex = pattern.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], expandedWildcardIndex);
                var rootPath = lastSeparatorIndex < 0
                    ? item.ContainingProject.DirectoryPath
                    : pattern[..lastSeparatorIndex];
                roots.Add(Path.GetFullPath(rootPath));
            }
        }

    }
}
