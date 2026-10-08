using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.AssemblyExport;

internal static class ExportRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static async Task<int> RunAsync(ExportPlan plan, TextWriter output, TextWriter errors,
        CancellationToken cancellationToken = default,
        Func<PlannedAssembly, string, CancellationToken, Task<AssemblyProjectExportResult>>? export = null,
        int? maxDegreeOfParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        var degree = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount - 2);
        if (degree < 1) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

        export ??= (item, stage, token) => AssemblyProjectExporter.ExportAsync(item.SourcePath, stage,
            item.Identity, item.ContentHash, item.DecompilationReferences, token);

        var owner = new ExportDumpOwnership(plan);
        using var runLock = owner.AcquireRunLock();
        owner.ResetRoot();
        owner.CreateTemporaryRoot();
        var temporaryCleanupAttempted = false;
        try
        {
            owner.ValidateLogPath();
            await using var logStream = new FileStream(Path.Combine(plan.OutputDirectory, "last-run.log"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var log = new StreamWriter(logStream, new UTF8Encoding(false)) { AutoFlush = true };
            using var logGate = new SemaphoreSlim(1, 1);

            async Task WriteLineAsync(TextWriter destination, string line)
            {
                await logGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await log.WriteLineAsync(line).ConfigureAwait(false);
                    await destination.WriteLineAsync(line).ConfigureAwait(false);
                }
                finally { logGate.Release(); }
            }

            Task WriteOutputAsync(string line) => WriteLineAsync(output, line);
            Task WriteErrorAsync(string line) => WriteLineAsync(errors, line);

            var runId = Guid.NewGuid().ToString("N");
            var count = plan.Assemblies.Count;
            DumpNavigationArtifacts.WriteReadme(owner, plan.OutputDirectory);
            DumpNavigationArtifacts.WriteCatalog(owner, plan.OutputDirectory, runId, "running", count, 0, 0, []);
            var childPaths = plan.Assemblies.ToDictionary(item => item.SourcePath, item => item.ChildRelativePath,
                StringComparer.OrdinalIgnoreCase);
            await WriteOutputAsync($"RUN START selected={count} workers={degree}").ConfigureAwait(false);
            foreach (var issue in plan.Issues)
            {
                var label = issue.BlocksAssembly ? "INPUT FAILURE" : "CLOSURE LIMITATION";
                await WriteErrorAsync($"{label}: {ShortPath(issue.Input)}: {ShortMessage(issue.Error)}").ConfigureAwait(false);
            }

            var blocked = plan.Issues.Where(issue => issue.BlocksAssembly && issue.SourcePath is not null)
                .Select(issue => issue.SourcePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var results = new WorkResult?[count];
            var interrupted = false;
            string? runFailure = null;
            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, count),
                new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var item = plan.Assemblies[index];
                    var label = $"[{index + 1}/{count}] {ShortPath(item.SourcePath)}";
                    await WriteOutputAsync($"START {label}").ConfigureAwait(false);
                    if (blocked.Contains(item.SourcePath))
                    {
                        var issue = plan.Issues.Last(candidate => candidate.SourcePath?.Equals(item.SourcePath,
                            StringComparison.OrdinalIgnoreCase) == true);
                        await WriteErrorAsync($"FAILURE {label}: {ShortMessage(issue.Error)}").ConfigureAwait(false);
                        results[index] = new(item, null, "failed", issue.Error, []);
                        return;
                    }

                    string? stage = null;
                    try
                    {
                        stage = owner.CreateStagingPath();
                        owner.ValidateStagingPath(stage);
                        Directory.CreateDirectory(stage);
                        token.ThrowIfCancellationRequested();
                        var result = await export(item, stage, token).ConfigureAwait(false);
                        if (result.ProjectRelativePath is null || result.SourceRelativePaths.Count == 0)
                        {
                            var details = string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message));
                            throw new InvalidDataException(string.IsNullOrWhiteSpace(details)
                                ? "Decompilation produced no usable project or C# source files."
                                : details);
                        }

                        owner.ValidateStagingPath(stage);
                        AssemblyProjectExporter.ValidateArtifacts(stage, result.ProjectRelativePath, result.SourceRelativePaths);
                        WriteSolution(stage, result.ProjectRelativePath);
                        var state = IsPartial(item, result) ? "partial" : "complete";
                        WriteManifest(item, stage, result, runId, state, childPaths);
                        var classes = DumpClassMaps.Read(stage, item.ChildRelativePath, result.SourceRelativePaths, token);
                        results[index] = new(item, stage, state, null, result.Diagnostics, classes);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        var cleanup = stage is null ? null : TryDeleteStaging(owner, stage);
                        var message = cleanup is null ? "Export interrupted." : $"Export interrupted; {cleanup}";
                        results[index] = new(item, null, "failed", message, []);
                        await WriteErrorAsync($"FAILURE {label}: {ShortMessage(message)}").ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception exception) when (AssemblyProjectExporter.IsRecoverableFailure(exception))
                    {
                        var cleanup = stage is null ? null : TryDeleteStaging(owner, stage);
                        var message = cleanup is null ? exception.Message : $"{exception.Message}; {cleanup}";
                        results[index] = new(item, null, "failed", message, []);
                        await WriteErrorAsync($"FAILURE {label}: {ShortMessage(message)}").ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                interrupted = true;
            }
            catch (Exception exception) when (AssemblyProjectExporter.IsRecoverableFailure(exception))
            {
                runFailure = exception.Message;
                await WriteErrorAsync($"RUN ERROR: {ShortMessage(exception.Message)}").ConfigureAwait(false);
            }

            // Publish successful stages serially in deterministic plan order.
            for (var index = 0; index < count; index++)
            {
                var result = results[index];
                if (result?.StagePath is not { } stage) continue;
                try
                {
                    owner.ValidateStagingPath(stage);
                    owner.PublishStaging(stage, result.Item.ChildPath);
                    await WriteOutputAsync($"PUBLISHED [{index + 1}/{count}] {ShortPath(result.Item.ChildPath)} ({result.State})")
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (AssemblyProjectExporter.IsRecoverableFailure(exception))
                {
                    results[index] = result with { State = "failed", Error = exception.Message };
                    await WriteErrorAsync($"FAILURE [{index + 1}/{count}] {ShortPath(result.Item.SourcePath)}: {ShortMessage(exception.Message)}")
                        .ConfigureAwait(false);
                }
            }

            temporaryCleanupAttempted = true;
            string? cleanupFailure = null;
            try { owner.DeleteTemporaryRoot(); }
            catch (Exception exception) when (AssemblyProjectExporter.IsRecoverableFailure(exception))
            {
                cleanupFailure = exception.Message;
                await WriteErrorAsync($"TEMP CLEANUP FAILURE: {ShortMessage(exception.Message)}").ConfigureAwait(false);
            }

            var unprocessed = results.Count(result => result is null);
            var failed = results.Count(result => result?.State == "failed") + unprocessed
                + plan.Issues.Count(issue => !issue.BlocksAssembly || issue.SourcePath is null
                    || !plan.Assemblies.Any(item => item.SourcePath.Equals(issue.SourcePath, StringComparison.OrdinalIgnoreCase)))
                + (runFailure is null ? 0 : 1) + (cleanupFailure is null ? 0 : 1);
            var exported = results.Count(result => result?.State is "complete" or "partial");
            var partial = results.Count(result => result?.State == "partial");
            DumpClassMaps.Write(owner, plan.OutputDirectory,
                results.Where(result => result?.State is "complete" or "partial")
                    .SelectMany(result => result!.Classes ?? []));
            var stateLabel = interrupted ? "RUN INTERRUPTED" : failed > 0 ? "RUN FAILED" : "RUN COMPLETE";
            DumpNavigationArtifacts.WriteCatalog(owner, plan.OutputDirectory, runId,
                interrupted ? "interrupted" : failed > 0 ? "failed" : "complete", count, partial, failed,
                results.Where(result => result?.State is "complete" or "partial")
                    .Select(result => new PublishedAssembly(result!.Item, result.State)));
            await WriteOutputAsync($"{stateLabel} exported={exported} partial={partial} failed={failed}").ConfigureAwait(false);
            return failed > 0 || interrupted ? 1 : 0;
        }
        finally
        {
            if (!temporaryCleanupAttempted) owner.DeleteTemporaryRoot();
        }
    }

    private static bool IsPartial(PlannedAssembly item, AssemblyProjectExportResult result) =>
        !result.IsComplete || !item.Closure.IsComplete || result.Diagnostics.Count > 0
        || item.Closure.References.Any(reference => !reference.Resolved);

    private static string? TryDeleteStaging(ExportDumpOwnership owner, string stage)
    {
        try
        {
            owner.DeleteStaging(stage);
            return null;
        }
        catch (Exception exception) when (AssemblyProjectExporter.IsRecoverableFailure(exception))
        {
            // Continue only while the root, temporary marker, and remaining stage tree still validate.
            owner.ValidateStagingPath(stage);
            return $"staging cleanup failed: {ShortMessage(exception.Message)}";
        }
    }

    private static void WriteManifest(PlannedAssembly item, string stage, AssemblyProjectExportResult result,
        string runId, string state, IReadOnlyDictionary<string, string> childPaths)
    {
        var directLinks = item.DecompilationReferences
            .Select(edge =>
            {
                var child = edge.ResolvedPath is not null && childPaths.TryGetValue(edge.ResolvedPath, out var relativePath)
                    ? relativePath : null;
                return new
                {
                    edge.Name,
                    edge.Version,
                    edge.Culture,
                    edge.ResolutionState,
                    edge.ResolutionProvenance,
                    edge.Diagnostic,
                    childRelativePath = child,
                };
            }).ToArray();

        const string sourceFilesPath = "source-files.json";
        var dependenciesPath = directLinks.Length > 0 || item.FilteredReferences.Count > 0 ? "dependencies.json" : null;
        var diagnosticsPath = item.Closure.Diagnostics.Count > 0 || result.Diagnostics.Count > 0 ? "diagnostics.json" : null;
        WriteJson(Path.Combine(stage, sourceFilesPath), result.SourceRelativePaths);
        if (dependenciesPath is not null)
            WriteJson(Path.Combine(stage, dependenciesPath), new { dependencies = directLinks, filteredEdges = item.FilteredReferences });
        if (diagnosticsPath is not null)
            WriteJson(Path.Combine(stage, diagnosticsPath), new { referenceClosure = item.Closure.Diagnostics, decompilation = result.Diagnostics });

        WriteJson(Path.Combine(stage, "export-manifest.json"), new
        {
            schemaVersion = 2,
            runId,
            sourcePath = item.SourcePath,
            identity = item.Identity,
            childRelativePath = item.ChildRelativePath,
            contentHash = result.ContentHash ?? item.ContentHash,
            decompilerVersion = result.DecompilerVersion,
            isExplicit = item.IsExplicit,
            completionState = state,
            projectPath = result.ProjectRelativePath,
            counts = new
            {
                sourceFiles = result.SourceRelativePaths.Count,
                dependencies = directLinks.Length,
                filteredEdges = item.FilteredReferences.Count,
                referenceClosureDiagnostics = item.Closure.Diagnostics.Count,
                decompilationDiagnostics = result.Diagnostics.Count,
            },
            sourceFilesPath,
            dependenciesPath,
            diagnosticsPath,
        });
    }

    private static string ShortPath(string path)
    {
        var fileName = Path.GetFileName(path);
        var parent = Path.GetFileName(Path.GetDirectoryName(path));
        return string.IsNullOrEmpty(parent) ? fileName : Path.Combine(parent, fileName);
    }

    private static string ShortMessage(string message)
    {
        var oneLine = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 240 ? oneLine : oneLine[..237] + "...";
    }

    private static void WriteJson<T>(string path, T value)
    {
        ExportDumpOwnership.RejectReparseAncestors(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private static void WriteSolution(string stage, string relativeProject)
    {
        if (Path.IsPathRooted(relativeProject)) throw new InvalidDataException("Project entry must be relative to staging.");
        var project = Path.GetFullPath(Path.Combine(stage, relativeProject));
        if (!ExportDumpOwnership.IsWithin(project, stage) || !File.Exists(project))
            throw new InvalidDataException("Generated project entry escaped staging or is missing.");
        ExportDumpOwnership.RejectReparseAncestors(project);
        var projectName = Path.GetFileNameWithoutExtension(relativeProject);
        var solution = $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "{{projectName}}", "{{relativeProject.Replace('/', '\\')}}", "{{Guid.NewGuid().ToString("B").ToUpperInvariant()}}"
            EndProject
            Global
            EndGlobal
            """;
        var solutionPath = Path.Combine(stage, projectName + ".sln");
        ExportDumpOwnership.RejectReparseAncestors(solutionPath);
        using var stream = new FileStream(solutionPath, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(solution + "\n");
    }

    private sealed record WorkResult(PlannedAssembly Item, string? StagePath, string State,
        string? Error, IReadOnlyList<AssemblyExportReferenceDiagnostic> Diagnostics,
        IReadOnlyList<DumpClassEntry>? Classes = null);

}
