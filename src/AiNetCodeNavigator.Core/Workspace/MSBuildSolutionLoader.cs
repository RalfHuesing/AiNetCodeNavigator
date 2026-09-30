#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Lädt echte .sln- und .slnx-Dateien über den Roslyn <see cref="MSBuildWorkspace"/>
/// mit Design-Time-Build-Flags (schnell, ohne Compiler-Ausführung und Analyzer).
/// </summary>
public static class MSBuildSolutionLoader
{
    private static readonly Lock RegistrationLock = new();

    public static Dictionary<string, string> CreateWorkspaceProperties() => new()
    {
        ["DesignTimeBuild"] = "true",
        ["SkipCompilerExecution"] = "true",
        ["ProvideCommandLineArgs"] = "true",
        ["RunAnalyzers"] = "false",
        ["RunCodeAnalysis"] = "false",
    };

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
                Console.Error.WriteLine($"[WARN]: MSBuildLocator konnte nicht registriert werden: {ex.Message}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", null);
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null);
                Environment.SetEnvironmentVariable("MSBuildSDKsPath", null);
            }
        }
    }

    public static MSBuildWorkspace CreateWorkspace()
    {
        EnsureMSBuildRegistered();
        return MSBuildWorkspace.Create(CreateWorkspaceProperties());
    }

    public static async Task<(Solution Solution, Microsoft.CodeAnalysis.Workspace Workspace)> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solutionPath);
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution-Datei nicht gefunden: {solutionPath}", solutionPath);
        }

        var workspace = CreateWorkspace();
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
            throw;
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
