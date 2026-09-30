#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Workspace;

internal sealed record SolutionStructureInputs(
    IReadOnlyCollection<string> ImportedFiles,
    IReadOnlyCollection<string> CompileGlobRoots);

/// <summary>
/// Collects the effective MSBuild imports and wildcard roots used by loaded projects.
/// </summary>
internal static class MSBuildStructureInputCollector
{
    internal static SolutionStructureInputs Collect(Solution solution)
    {
        MSBuildSolutionLoader.EnsureMSBuildRegistered();
        var importedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var compileGlobRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectPaths = solution.Projects
            .Select(project => project.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        using var collection = new ProjectCollection(MSBuildSolutionLoader.CreateWorkspaceProperties());

        foreach (var projectPath in projectPaths)
        {
            var project = collection.LoadProject(projectPath);
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

            var projectFiles = importedProjectFiles.Append(project.Xml);
            foreach (var projectFile in projectFiles)
            {
                AddCompileGlobRoots(project, projectFile, compileGlobRoots);
            }
        }

        collection.UnloadAllProjects();
        return new SolutionStructureInputs(
            importedFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            compileGlobRoots.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void AddCompileGlobRoots(
        Microsoft.Build.Evaluation.Project evaluatedProject,
        ProjectRootElement projectFile,
        ISet<string> roots)
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
