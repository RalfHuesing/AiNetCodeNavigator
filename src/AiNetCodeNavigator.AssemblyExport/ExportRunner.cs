using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Assemblies;

namespace AiNetCodeNavigator.AssemblyExport;

internal sealed record ExportRunItem(string SourcePath, string ChildName, AssemblyIdentityDto Identity,
    bool IsExplicit, string State, IReadOnlyList<AssemblyReferenceDto> UnresolvedDependencies, string? Error = null,
    IReadOnlyList<AssemblyExportReferenceDiagnostic>? Diagnostics = null)
{
    public string ChildRelativePath { get; init; } = ChildName;
    public bool ExistingChildPreserved { get; init; }
}

internal static class ExportRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static async Task<int> RunAsync(ExportPlan plan, TextWriter output, TextWriter errors,
        CancellationToken cancellationToken = default,
        Func<PlannedAssembly, string, CancellationToken, Task<AssemblyProjectExportResult>>? export = null)
    {
        export ??= (item, stage, token) => AssemblyProjectExporter.ExportAsync(item.SourcePath, stage, item.Identity,
            item.ContentHash, item.DecompilationReferences, token);
        var owner = new ExportDumpOwnership(plan);
        owner.CreateOrValidateRoot();
        owner.ValidateLogPath();
        await using var logStream = new FileStream(Path.Combine(plan.OutputDirectory, "last-run.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var log = new StreamWriter(logStream, new UTF8Encoding(false)) { AutoFlush = true };
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var items = plan.Assemblies.Select(item => new ExportRunItem(item.SourcePath, Path.GetFileName(item.SourcePath),
            item.Identity, item.IsExplicit, "pending", item.Closure.References.Where(edge => !edge.Resolved).ToArray(), Diagnostics: [])
            { ChildRelativePath = item.ChildRelativePath }).ToArray();
        WriteRunReport("running");
        foreach (var issue in plan.Issues)
        {
            await WriteErrorAsync($"{(issue.BlocksAssembly ? "Input failure" : "Closure limitation")}: {issue.Input}: {issue.Error}").ConfigureAwait(false);
        }
        var interrupted = false;
        var blocked = new HashSet<string>(plan.Issues.Where(issue => issue.BlocksAssembly && issue.SourcePath is not null)
            .Select(issue => issue.SourcePath!), StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < plan.Assemblies.Count; index++)
        {
            var item = plan.Assemblies[index];
            if (blocked.Contains(item.SourcePath))
            {
                var issue = plan.Issues.Last(candidate => candidate.SourcePath?.Equals(item.SourcePath, StringComparison.OrdinalIgnoreCase) == true);
                items[index] = items[index] with
                {
                    State = "failed",
                    Error = issue.Error,
                    ExistingChildPreserved = Directory.Exists(item.ChildPath) || File.Exists(item.ChildPath),
                };
                await WriteOutputAsync($"Start: {item.SourcePath} -> {item.ChildPath}").ConfigureAwait(false);
                await WriteErrorAsync($"Failure: {item.SourcePath}: {issue.Error}").ConfigureAwait(false);
                WriteRunReport("running");
                continue;
            }
            var stage = Path.Combine(plan.OutputDirectory, ".assembly-export-stage-" + Guid.NewGuid().ToString("N"));
            await WriteOutputAsync($"Start: {item.SourcePath} -> {item.ChildPath}").ConfigureAwait(false);
            foreach (var edge in item.Closure.References)
            {
                var rule = AutomaticExportFilter.Match(edge.Name);
                await WriteOutputAsync($"Dependency: {edge.SourceAssemblyPath} -> {edge.Name}: {edge.ResolutionState}, {edge.ResolutionProvenance ?? "unresolved"}{(rule is null ? "" : ", excluded " + rule)}").ConfigureAwait(false);
                if (!edge.Resolved)
                    await WriteErrorAsync($"Unresolved dependency: {edge.SourceAssemblyPath} -> {edge.Name}, {edge.Version}: {edge.Diagnostic}").ConfigureAwait(false);
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.DeleteSelectedChild(item.ChildPath);
                items[index] = items[index] with { State = "running" };
                WriteRunReport("running");
                owner.ValidateStagingPath(stage);
                Directory.CreateDirectory(stage);
                var result = await export(item, stage, cancellationToken).ConfigureAwait(false);
                if (result.ProjectRelativePath is null || result.SourceRelativePaths.Count == 0)
                {
                    var failure = string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message));
                    throw new InvalidDataException(string.IsNullOrWhiteSpace(failure)
                        ? "Decompilation produced no usable project or C# source files."
                        : failure);
                }
                owner.ValidateStagingPath(stage);
                AssemblyProjectExporter.ValidateArtifacts(stage, result.ProjectRelativePath, result.SourceRelativePaths);
                WriteSolution(stage, result.ProjectRelativePath);
                var hasLimitations = !result.IsComplete || !item.Closure.IsComplete
                    || result.Diagnostics.Count > 0 || items[index].UnresolvedDependencies.Count > 0;
                var completionState = hasLimitations ? "partial" : "complete";
                foreach (var diagnostic in result.Diagnostics)
                    await WriteOutputAsync($"Diagnostic ({(diagnostic.IsError ? "error" : "warning")}): {item.SourcePath}: {diagnostic.Message}").ConfigureAwait(false);
                var automaticChildren = plan.Assemblies.Where(candidate => !candidate.IsExplicit
                    && item.DecompilationReferences.Any(edge => edge.ResolvedPath is not null
                        && edge.ResolvedPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase)))
                    .Select(candidate => candidate.ChildRelativePath).ToArray();
                WriteJson(Path.Combine(stage, "export-manifest.json"), new
                {
                    schemaVersion = 1, runId, sourcePath = item.SourcePath, identity = item.Identity,
                    childRelativePath = item.ChildRelativePath, contentHash = result.ContentHash,
                    decompilerVersion = result.DecompilerVersion, isExplicit = item.IsExplicit,
                    completionState,
                    referenceClosureComplete = item.Closure.IsComplete,
                    referenceClosureDiagnostics = item.Closure.Diagnostics,
                    projectPath = result.ProjectRelativePath, sourceFiles = result.SourceRelativePaths,
                    dependencies = item.Closure.References,
                    dependencyChildren = item.DecompilationReferences.Select((edge, index) => new
                    {
                        referenceIndex = index,
                        edge.ResolvedPath,
                        childRelativePath = edge.ResolvedPath is not { } resolvedPath ? null
                            : plan.Assemblies.FirstOrDefault(candidate => candidate.SourcePath.Equals(resolvedPath, StringComparison.OrdinalIgnoreCase))?.ChildRelativePath,
                    }).Where(link => link.childRelativePath is not null).ToArray(),
                    automaticallyExportedChildren = automaticChildren,
                    filteredEdges = item.FilteredReferences, unresolvedDependencies = items[index].UnresolvedDependencies,
                    diagnostics = result.Diagnostics,
                });
                owner.PublishStaging(stage, item.ChildPath);
                items[index] = items[index] with { State = completionState, Diagnostics = result.Diagnostics };
                await WriteOutputAsync($"Success ({items[index].State}): {item.ChildPath}").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                items[index] = items[index] with { State = "failed", Error = "Export interrupted." };
                interrupted = true;
            }
            catch (Exception ex) when (AssemblyProjectExporter.IsRecoverableFailure(ex))
            {
                items[index] = items[index] with { State = "failed", Error = ex.Message };
                await WriteErrorAsync($"Failure: {item.SourcePath}: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                try { owner.DeleteStaging(stage); }
                catch (Exception ex) when (AssemblyProjectExporter.IsRecoverableFailure(ex))
                {
                    items[index] = items[index] with { State = "failed", Error = $"Staging cleanup failed: {ex.Message}" };
                    await WriteErrorAsync(items[index].Error ?? "Staging cleanup failed.").ConfigureAwait(false);
                }
            }
            WriteRunReport("running");
            if (interrupted) break;
        }
        var failed = items.Count(item => item.State == "failed")
            + plan.Issues.Count(issue => !issue.BlocksAssembly || issue.SourcePath is null
                || !plan.Assemblies.Any(item => item.SourcePath.Equals(issue.SourcePath, StringComparison.OrdinalIgnoreCase)));
        var succeeded = items.Count(item => item.State is "complete" or "partial");
        var state = interrupted ? "interrupted" : failed > 0 ? "failed" : items.Any(item => item.State == "partial") ? "partial" : "complete";
        WriteRunReport(state);
        var exitCode = failed > 0 || interrupted ? 1 : 0;
        var assemblyFailures = items.Count(item => item.State == "failed");
        var unresolved = items.Sum(item => item.UnresolvedDependencies.Count);
        await WriteOutputAsync($"Finished: selected={items.Length}, exported={succeeded}, assemblyFailures={assemblyFailures}, planIssues={plan.Issues.Count}, unresolved={unresolved}, state={state}, exit={exitCode}.").ConfigureAwait(false);
        return exitCode;

        void WriteRunReport(string state)
        {
            owner.ValidateRootPreflight();
            owner.ValidateRunReportPath();
            var path = Path.Combine(plan.OutputDirectory, "last-run.json");
            var temporary = Path.Combine(plan.OutputDirectory, ".assembly-export-report-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                WriteJson(temporary, new
                {
                    schemaVersion = 1, runId, startedAt, updatedAt = DateTimeOffset.UtcNow,
                    outputArgument = plan.Arguments.OutputDirectory, outputDirectory = plan.OutputDirectory,
                    exactInputs = plan.Arguments.Sources, explicitPaths = plan.ExplicitPaths,
                    completionState = state, isComplete = state == "complete", selectedChildren = items,
                    inputFailures = plan.Issues,
                    exclusions = plan.Assemblies.SelectMany(item => item.FilteredReferences).ToArray(),
                    failures = items.Where(item => item.State == "failed").Cast<object>()
                        .Concat(plan.Issues.Where(issue => !issue.BlocksAssembly || issue.SourcePath is null
                            || !plan.Assemblies.Any(item => item.SourcePath.Equals(issue.SourcePath, StringComparison.OrdinalIgnoreCase)))
                            .Cast<object>()).ToArray(),
                });
                owner.ValidateRunReportPath();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                ExportDumpOwnership.RejectReparseAncestors(temporary);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        async Task WriteOutputAsync(string line)
        {
            await output.WriteLineAsync(line).ConfigureAwait(false);
            await log.WriteLineAsync(line).ConfigureAwait(false);
        }

        async Task WriteErrorAsync(string line)
        {
            await errors.WriteLineAsync(line).ConfigureAwait(false);
            await log.WriteLineAsync(line).ConfigureAwait(false);
        }
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
}
