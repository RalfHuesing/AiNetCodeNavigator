#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Loads actual .sln and .slnx files through Roslyn <see cref="MSBuildWorkspace"/>
/// with design-time build flags (fast, without compiler execution or analyzers).
/// </summary>
public static class MSBuildSolutionLoader
{
    private static readonly Lock RegistrationLock = new();

    public static Dictionary<string, string> CreateWorkspaceProperties()
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", Environment.ProcessId.ToString(), Guid.NewGuid().ToString("N"));
        var customTargets = EnsureDesignTimeTargets(scratchRoot);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DesignTimeBuild"] = "true",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
            ["RunAnalyzers"] = "false",
            ["RunCodeAnalysis"] = "false",
            ["DisableRarCache"] = "true",
            ["NavigatorAnalysisScratchRoot"] = scratchRoot,
            ["CustomBeforeMicrosoftCommonTargets"] = customTargets,
        };
    }

    /// <summary>Removes the process-scoped MSBuild design-time output after resident workspaces are disposed.</summary>
    public static void CleanupDesignTimeScratch()
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", Environment.ProcessId.ToString());
        try
        {
            if (Directory.Exists(scratchRoot)) Directory.Delete(scratchRoot, recursive: true);
        }
        catch (IOException)
        {
            // A second in-process MSBuild workspace may still be releasing files; the OS temp cleanup owns leftovers.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not fail host shutdown because a transient design-time output remains locked.
        }
    }

    private static string EnsureDesignTimeTargets(string scratchRoot)
    {
        Directory.CreateDirectory(scratchRoot);
        var targetsPath = Path.Combine(scratchRoot, "Navigator.DesignTime.targets");
        var escapedRoot = scratchRoot.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        var content = $"""<Project><PropertyGroup><NavigatorProjectScratchKey>$(MSBuildProjectDirectory.Replace(':','_'))</NavigatorProjectScratchKey><IntermediateOutputPath>{escapedRoot}\$(NavigatorProjectScratchKey)\$(MSBuildProjectName)\$(Configuration)\$(TargetFramework)\obj\</IntermediateOutputPath><OutputPath>{escapedRoot}\$(NavigatorProjectScratchKey)\$(MSBuildProjectName)\$(Configuration)\$(TargetFramework)\bin\</OutputPath></PropertyGroup></Project>""";
        if (!File.Exists(targetsPath) || !string.Equals(File.ReadAllText(targetsPath), content, StringComparison.Ordinal))
            File.WriteAllText(targetsPath, content);
        return targetsPath;
    }

    public static void EnsureMSBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (MSBuildLocator.IsRegistered)
            {
                return;
            }

            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN]: MSBuildLocator could not be registered: {ex.Message}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", null);
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null);
                Environment.SetEnvironmentVariable("MSBuildSDKsPath", null);
            }
        }
    }

    public static MSBuildWorkspace CreateWorkspace() => CreateWorkspace(CreateWorkspaceProperties());

    private static MSBuildWorkspace CreateWorkspace(IDictionary<string, string> properties)
    {
        EnsureMSBuildRegistered();
        return MSBuildWorkspace.Create(properties);
    }

    public static async Task<(Solution Solution, Microsoft.CodeAnalysis.Workspace Workspace)> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solutionPath);
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}", solutionPath);
        }

        EnsureMSBuildRegistered();
        var properties = CreateWorkspaceProperties();
        var scratchRoot = properties["NavigatorAnalysisScratchRoot"];
        try
        {
            ValidateDesignTimeOutputPaths(solutionPath, properties, scratchRoot);
        }
        catch
        {
            TryDeleteDirectory(scratchRoot);
            throw;
        }

        MSBuildWorkspace workspace;
        try
        {
            workspace = CreateWorkspace(properties);
        }
        catch
        {
            TryDeleteDirectory(scratchRoot);
            throw;
        }
        var failures = new ConcurrentQueue<string>();
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                    failures.Enqueue(args.Diagnostic.Message);
            }
        });

        try
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"MSBuild reported failures while loading '{solutionPath}': {string.Join(" | ", failures.Distinct(StringComparer.Ordinal))}");
            }

            return (solution, workspace);
        }
        catch
        {
            workspace.Dispose();
            TryDeleteDirectory(scratchRoot);
            throw;
        }
    }

    private static void ValidateDesignTimeOutputPaths(
        string solutionPath,
        IDictionary<string, string> properties,
        string scratchRoot)
    {
        var solution = SolutionFile.Parse(solutionPath);
        using var collection = new ProjectCollection(properties);
        foreach (var projectInSolution in solution.ProjectsInOrder.Where(project =>
                     project.ProjectType != SolutionProjectType.SolutionFolder
                     && !string.IsNullOrWhiteSpace(project.AbsolutePath)))
        {
            var project = collection.LoadProject(projectInSolution.AbsolutePath);
            foreach (var propertyName in new[] { "IntermediateOutputPath", "OutputPath" })
            {
                var outputPath = project.GetPropertyValue(propertyName);
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    continue;
                }

                if (outputPath.Contains("$(", StringComparison.Ordinal)
                    || outputPath.Contains("@(", StringComparison.Ordinal)
                    || outputPath.Contains("%(", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"MSBuild output property '{propertyName}' in '{project.FullPath}' could not be evaluated safely.");
                }

                var fullOutputPath = Path.GetFullPath(outputPath, project.DirectoryPath);
                if (!IsWithinDirectory(fullOutputPath, scratchRoot))
                {
                    throw new InvalidOperationException(
                        $"MSBuild output property '{propertyName}' in '{project.FullPath}' resolves outside the design-time scratch directory: '{fullOutputPath}'.");
                }
            }

            // The default extensions path is an input location for restored assets, not an output used by this design-time load.
            // If a project explicitly redirects it away from BaseIntermediateOutputPath, reject an external override as unsupported.
            var extensionsPath = project.GetPropertyValue("MSBuildProjectExtensionsPath");
            var baseIntermediatePath = project.GetPropertyValue("BaseIntermediateOutputPath");
            if (!string.IsNullOrWhiteSpace(extensionsPath) && !string.IsNullOrWhiteSpace(baseIntermediatePath))
            {
                var fullExtensionsPath = Path.GetFullPath(extensionsPath, project.DirectoryPath);
                var fullBaseIntermediatePath = Path.GetFullPath(baseIntermediatePath, project.DirectoryPath);
                if (!string.Equals(fullExtensionsPath, fullBaseIntermediatePath, StringComparison.OrdinalIgnoreCase)
                    && !IsWithinDirectory(fullExtensionsPath, scratchRoot))
                {
                    throw new InvalidOperationException(
                        $"MSBuild property 'MSBuildProjectExtensionsPath' in '{project.FullPath}' has an unsupported external override: '{fullExtensionsPath}'.");
                }
            }
        }

        collection.UnloadAllProjects();
    }

    private static bool IsWithinDirectory(string path, string root)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relativePath == "."
            || (!Path.IsPathRooted(relativePath)
                && relativePath != ".."
                && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Process-level scratch cleanup handles files that are still held by MSBuild.
        }
        catch (UnauthorizedAccessException)
        {
            // Process-level scratch cleanup handles files that are still held by MSBuild.
        }
    }

    /// <summary>
    /// Creates a resident instance that loads and later reloads the given solution through MSBuildWorkspace.
    /// </summary>
    public static ResidentSolution CreateResidentSolution(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var canonicalPath = Path.GetFullPath(solutionPath);
        return new ResidentSolution(
            async cancellationToken => await LoadResidentStateAsync(canonicalPath, cancellationToken).ConfigureAwait(false),
            canonicalPath);
    }

    internal static async Task<ResidentLoadedState> LoadResidentStateAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var (solution, workspace) = await LoadSolutionAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        try
        {
            var inputs = MSBuildStructureInputCollector.Collect(solution);
            return new ResidentLoadedState(solution, workspace) { StructureInputs = inputs };
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }
}
