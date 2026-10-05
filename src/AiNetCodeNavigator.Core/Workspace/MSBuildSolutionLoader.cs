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
using Serilog;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Loads actual .sln and .slnx files through Roslyn <see cref="MSBuildWorkspace"/>
/// with design-time build flags. Roslyn can still run referenced source generators when a navigation
/// request materializes the project's compilation.
/// </summary>
public static class MSBuildSolutionLoader
{
    private static readonly Lock RegistrationLock = new();
    private static readonly Lock ScratchLock = new();
    private static FileStream? processScratchOwnership;

    public static Dictionary<string, string> CreateWorkspaceProperties()
    {
        var processRoot = Path.Combine(DesignTimeScratchMaintenance.Root, Environment.ProcessId.ToString());
        lock (ScratchLock)
        {
            Directory.CreateDirectory(processRoot);
            processScratchOwnership ??= new FileStream(DesignTimeScratchMaintenance.OwnerFilePath(DesignTimeScratchMaintenance.Root, Environment.ProcessId),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
        var scratchRoot = Path.Combine(processRoot, Guid.NewGuid().ToString("N"));
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
        lock (ScratchLock)
        {
            processScratchOwnership?.Dispose();
            processScratchOwnership = null;
            TryDeleteDirectory(scratchRoot);
            try
            {
                File.Delete(DesignTimeScratchMaintenance.OwnerFilePath(DesignTimeScratchMaintenance.Root, Environment.ProcessId));
            }
            catch (IOException exception)
            {
                Log.Warning(exception, "Could not remove the design-time scratch ownership file.");
            }
            catch (UnauthorizedAccessException exception)
            {
                Log.Warning(exception, "Could not remove the design-time scratch ownership file.");
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "Could not remove design-time scratch directory {ScratchDirectory}; shutdown or a later startup will retry.", path);
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Could not remove design-time scratch directory {ScratchDirectory}; shutdown or a later startup will retry.", path);
        }
    }

    private static string EnsureDesignTimeTargets(string scratchRoot)
    {
        Directory.CreateDirectory(scratchRoot);
        var targetsPath = Path.Combine(scratchRoot, "Navigator.DesignTime.targets");
        var escapedRoot = scratchRoot.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        // Hash the project and build dimensions rather than mirroring the source directory tree.
        // A 128-bit prefix keeps design-time artifacts below legacy Windows path limits for normal temp roots.
        var content = $"""
            <Project>
              <PropertyGroup>
                <NavigatorProjectScratchHash>$([MSBuild]::StableStringHash('$(MSBuildProjectFullPath)|$(Configuration)|$(Platform)|$(TargetFramework)|$(RuntimeIdentifier)', 'Sha256'))</NavigatorProjectScratchHash>
                <NavigatorProjectScratchKey>$(NavigatorProjectScratchHash.Substring(0,32))</NavigatorProjectScratchKey>
                <IntermediateOutputPath>{escapedRoot}\$(NavigatorProjectScratchKey)\obj\</IntermediateOutputPath>
                <OutputPath>{escapedRoot}\$(NavigatorProjectScratchKey)\bin\</OutputPath>
              </PropertyGroup>
            </Project>
            """;
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

    internal static void DisposeWorkspace(Microsoft.CodeAnalysis.Workspace? workspace)
    {
        var properties = (workspace as MSBuildWorkspace)?.Properties;
        try
        {
            workspace?.Dispose();
        }
        finally
        {
            if (properties is not null) CleanupWorkspaceProperties(properties);
        }
    }

    internal static void CleanupWorkspaceProperties(IReadOnlyDictionary<string, string> properties)
    {
        if (!properties.TryGetValue("NavigatorAnalysisScratchRoot", out var scratchRoot)) return;
        var processRoot = Path.Combine(Path.GetTempPath(), "AiNetCodeNavigator", "msbuild-analysis", Environment.ProcessId.ToString());
        var fullPath = Path.GetFullPath(scratchRoot);
        if (string.Equals(Path.GetDirectoryName(fullPath), processRoot, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _))
        {
            TryDeleteDirectory(fullPath);
        }
    }

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
            DisposeWorkspace(workspace);
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
            return new ResidentLoadedState(solution, workspace)
            {
                StructureInputs = inputs,
                InputProvenance = WorkspaceInputProvenance.CreateFromTrustedLoader(solution),
            };
        }
        catch
        {
            DisposeWorkspace(workspace);
            throw;
        }
    }
}
