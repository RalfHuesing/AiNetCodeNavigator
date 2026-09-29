#nullable enable

namespace AiNetCodeNavigator.TestKit.Builders;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Deklarative Projektbeschreibung für Test-Solutions.
/// </summary>
public sealed record ProjectSpec(
    string Name,
    IReadOnlyList<(string FileName, string Content)> Documents,
    IReadOnlyList<string>? ProjectReferences = null,
    IReadOnlyList<MetadataReference>? AdditionalReferences = null,
    NullableContextOptions Nullable = NullableContextOptions.Enable,
    IReadOnlyList<string>? PreprocessorSymbols = null,
    OutputKind OutputKind = OutputKind.DynamicallyLinkedLibrary,
    string? VirtualProjectDirectory = null);

/// <summary>
/// Verwalteter Solution-Snapshot inklusive Workspace-Lebenszyklus.
/// </summary>
public sealed record TestSolutionHandle(Solution Solution, Workspace Workspace) : IDisposable
{
    public void Dispose() => Workspace.Dispose();
}

/// <summary>
/// Flexibler In-Memory-Solution- und Workspace-Builder auf Basis von <see cref="AdhocWorkspace"/>.
/// Verwendet gecachte Kern-Referenzen der BCL für maximale Testgeschwindigkeit.
/// </summary>
public sealed class TestWorkspaceBuilder
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> CoreReferencesLazy = new(BuildCoreReferences);

    private readonly List<ProjectSpec> _projects = [];
    private string? _virtualSolutionFilePath;

    public static ImmutableArray<MetadataReference> CoreReferences => CoreReferencesLazy.Value;

    public static TestWorkspaceBuilder Create() => new();

    public TestWorkspaceBuilder WithVirtualSolutionPath(string path)
    {
        _virtualSolutionFilePath = path;
        return this;
    }

    public TestWorkspaceBuilder WithProject(ProjectSpec spec)
    {
        _projects.Add(spec);
        return this;
    }

    public TestWorkspaceBuilder WithProject(string name, params (string FileName, string Content)[] documents)
    {
        _projects.Add(new ProjectSpec(name, documents));
        return this;
    }

    public TestSolutionHandle Build()
    {
        return CreateSolutionCore(_virtualSolutionFilePath, [.. _projects]);
    }

    public static TestSolutionHandle CreateSolution(string source, string projectName = "TestProj", string docName = "Doc.cs")
    {
        return CreateSolution(new ProjectSpec(projectName, [(docName, source)]));
    }

    public static TestSolutionHandle CreateSolution(params ProjectSpec[] specs)
    {
        return CreateSolutionCore(null, specs);
    }

    public static TestSolutionHandle CreateSolution(string virtualSolutionFilePath, params ProjectSpec[] specs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualSolutionFilePath);
        return CreateSolutionCore(virtualSolutionFilePath, specs);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of AdhocWorkspace is transferred to TestSolutionHandle which implements IDisposable.")]
    private static TestSolutionHandle CreateSolutionCore(string? virtualSolutionFilePath, ProjectSpec[] specs)
    {
        var workspace = new AdhocWorkspace();
        var normalizedSolutionFilePath = virtualSolutionFilePath is null
            ? null
            : Path.GetFullPath(virtualSolutionFilePath);

        var solution = normalizedSolutionFilePath is null
            ? workspace.CurrentSolution
            : workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: normalizedSolutionFilePath));

        var solutionDirectory = normalizedSolutionFilePath is null
            ? null
            : Path.GetDirectoryName(normalizedSolutionFilePath)!;

        var projectIdsByName = new Dictionary<string, ProjectId>(StringComparer.Ordinal);

        foreach (var spec in specs)
        {
            var projectId = ProjectId.CreateNewId(spec.Name);
            projectIdsByName[spec.Name] = projectId;
            solution = AddProject(solution, projectId, spec, solutionDirectory);
        }

        foreach (var spec in specs)
        {
            solution = WireProjectReferences(solution, spec, projectIdsByName);
        }

        return new TestSolutionHandle(solution, workspace);
    }

    private static Solution WireProjectReferences(
        Solution solution, ProjectSpec spec, IReadOnlyDictionary<string, ProjectId> projectIdsByName)
    {
        if (spec.ProjectReferences is null)
        {
            return solution;
        }

        var projectId = projectIdsByName[spec.Name];
        foreach (var referencedName in spec.ProjectReferences)
        {
            if (!projectIdsByName.TryGetValue(referencedName, out var referencedId))
            {
                throw new InvalidOperationException(
                    $"ProjectSpec '{spec.Name}' referenziert unbekanntes Projekt '{referencedName}'.");
            }

            solution = solution.AddProjectReference(projectId, new ProjectReference(referencedId));
        }

        return solution;
    }

    private static Solution AddProject(
        Solution solution, ProjectId projectId, ProjectSpec spec, string? solutionDirectory)
    {
        var references = spec.AdditionalReferences is { Count: > 0 }
            ? CoreReferences.Concat(spec.AdditionalReferences).ToImmutableArray()
            : CoreReferences;

        var compilationOptions = new CSharpCompilationOptions(
            spec.OutputKind,
            nullableContextOptions: spec.Nullable);

        var projectDirectory = spec.VirtualProjectDirectory ?? spec.Name;
        var projectFilePath = solutionDirectory is null
            ? null
            : Path.GetFullPath(Path.Combine(solutionDirectory, projectDirectory, spec.Name + ".csproj"));

        var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                spec.Name,
                spec.Name,
                LanguageNames.CSharp,
                filePath: projectFilePath)
            .WithMetadataReferences(references)
            .WithCompilationOptions(compilationOptions);

        if (spec.PreprocessorSymbols is { Count: > 0 })
        {
            projectInfo = projectInfo.WithParseOptions(new CSharpParseOptions(preprocessorSymbols: spec.PreprocessorSymbols));
        }

        solution = solution.AddProject(projectInfo);

        foreach (var (fileName, content) in spec.Documents)
        {
            var documentId = DocumentId.CreateNewId(projectId);
            var filePath = Path.IsPathRooted(fileName)
                ? Path.GetFullPath(fileName)
                : (solutionDirectory is null ? null : Path.GetFullPath(Path.Combine(solutionDirectory, projectDirectory, fileName)));
            var docName = Path.GetFileName(fileName);
            solution = solution.AddDocument(documentId, docName, content, filePath: filePath);
        }

        return solution;
    }

    private static ImmutableArray<MetadataReference> BuildCoreReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(System.Runtime.GCSettings).Assembly,
            typeof(Enumerable).Assembly,
            typeof(System.Threading.Tasks.Task).Assembly,
        };

        return assemblies
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToImmutableArray();
    }
}
