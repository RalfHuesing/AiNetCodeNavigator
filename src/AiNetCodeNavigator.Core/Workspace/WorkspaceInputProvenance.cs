#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Creator proof for opaque analysis providers. It contains per-project provider references only;
/// it never retains the Solution, Workspace, Compilation, or a global provider-keyed project list.
/// </summary>
internal sealed class WorkspaceInputProvenance
{
    private static readonly ConditionalWeakTable<Solution, WorkspaceInputProvenance> TestWorkspaceOutputs = new();
    private readonly Dictionary<ProjectId, ProjectInputProof> projects;
    private readonly Dictionary<ProjectId, ImmutableArray<GeneratorCreatorInputContract>> generatorContracts;
    private readonly string? trustedSdkDistributionPath;

    private WorkspaceInputProvenance(
        Solution solution,
        IEnumerable<GeneratorCreatorInputContract>? creatorContracts = null,
        string? trustedSdkDistributionPath = null)
    {
        projects = new Dictionary<ProjectId, ProjectInputProof>();
        generatorContracts = new Dictionary<ProjectId, ImmutableArray<GeneratorCreatorInputContract>>();
        this.trustedSdkDistributionPath = trustedSdkDistributionPath;
        var contracts = creatorContracts?.ToImmutableArray() ?? ImmutableArray<GeneratorCreatorInputContract>.Empty;
        foreach (var project in solution.Projects)
        {
            projects.Add(project.Id, ProjectInputProof.Capture(project));
            var projectContracts = contracts
                .Where(contract => project.FilePath is not null
                    && PathsEqual(contract.ProjectPath, project.FilePath))
                .Where(contract => project.AnalyzerReferences.OfType<AnalyzerFileReference>()
                    .Any(reference => PathsEqual(
                        contract.AnalyzerPath,
                        AnalyzerImageCapture.GetSourcePath(reference) ?? reference.FullPath)))
                .ToImmutableArray();
            if (!projectContracts.IsDefaultOrEmpty)
            {
                generatorContracts.Add(project.Id, projectContracts);
            }
        }
    }

    private WorkspaceInputProvenance(
        Dictionary<ProjectId, ProjectInputProof> carriedProjects,
        Dictionary<ProjectId, ImmutableArray<GeneratorCreatorInputContract>> carriedGeneratorContracts,
        string? trustedSdkDistributionPath)
    {
        projects = carriedProjects;
        generatorContracts = carriedGeneratorContracts;
        this.trustedSdkDistributionPath = trustedSdkDistributionPath;
    }

    internal static WorkspaceInputProvenance CreateFromTrustedLoader(
        Solution solution,
        IEnumerable<GeneratorCreatorInputContract>? creatorContracts = null,
        string? trustedSdkDistributionPath = null) => new(solution, creatorContracts, trustedSdkDistributionPath);

    /// <summary>Registers only the exact output built by TestWorkspaceBuilder from its public defaults.</summary>
    internal static void RegisterTestWorkspaceBuilderOutput(Solution solution) =>
        TestWorkspaceOutputs.Add(solution, new WorkspaceInputProvenance(solution));

    internal static WorkspaceInputProvenance? FindTestWorkspaceBuilderOutput(Solution solution) =>
        TestWorkspaceOutputs.TryGetValue(solution, out var proof) ? proof : null;

    internal static WorkspaceInputProvenance CreateForCreatorContracts(
        Solution solution,
        IEnumerable<GeneratorCreatorInputContract> creatorContracts) => new(solution, creatorContracts);

    internal GeneratorCreatorInputContract? FindGeneratorContract(
        Project project,
        string sourceAnalyzerPath,
        string analyzerSha256)
    {
        if (!generatorContracts.TryGetValue(project.Id, out var contracts))
        {
            return null;
        }

        var matches = contracts.Where(contract =>
            project.FilePath is not null
            && contract.MatchesProjectAndAnalyzer(project.FilePath, sourceAnalyzerPath)
            && StringComparer.Ordinal.Equals(contract.AnalyzerSha256, analyzerSha256)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal GeneratorCreatorInputContract? CreateTrustedSdkGeneratorContract(
        Project project,
        string analyzerPath,
        ImmutableArray<SourceIdentityCapturedInput> capturedInputs,
        IDictionary<string, MetadataReferenceImageCapture.CapturedImage> imagesByPath,
        int attempt,
        System.Threading.CancellationToken cancellationToken,
        MetadataReferenceImageCapture.MetadataImageCaptureObserver? observer) =>
        TrustedSdkGeneratorCreator.CreateContract(
            project,
            analyzerPath,
            capturedInputs,
            imagesByPath,
            trustedSdkDistributionPath,
            attempt,
            cancellationToken,
            observer);

    internal bool Matches(Project project)
    {
        if (!projects.TryGetValue(project.Id, out var proof))
        {
            return false;
        }

        // Public typed option values are serialized by the identity encoder and may change immutably.
        // These opaque providers must remain the exact instances created by the trusted owner.
        var options = project.CompilationOptions;
        return ReferenceEquals(proof.AnalyzerConfigOptionsProvider, project.AnalyzerOptions.AnalyzerConfigOptionsProvider)
            && ReferenceEquals(proof.SyntaxTreeOptionsProvider, options?.SyntaxTreeOptionsProvider)
            && ReferenceEquals(proof.MetadataReferenceResolver, options?.MetadataReferenceResolver)
            && IsKnownSourceResolver(options?.SourceReferenceResolver)
            && IsKnownXmlResolver(options?.XmlReferenceResolver)
            && ReferenceEquals(proof.StrongNameProvider, options?.StrongNameProvider)
            && IsKnownAssemblyIdentityComparer(options?.AssemblyIdentityComparer);
    }

    /// <summary>Carries creator proof across the resident's own immutable text-document updates.</summary>
    internal WorkspaceInputProvenance? CarryKnownTextChanges(Solution original, Solution updated)
    {
        var carried = new Dictionary<ProjectId, ProjectInputProof>();
        var carriedContracts = new Dictionary<ProjectId, ImmutableArray<GeneratorCreatorInputContract>>();
        foreach (var originalProject in original.Projects)
        {
            if (!projects.TryGetValue(originalProject.Id, out var proof) || !Matches(originalProject))
            {
                return null;
            }

            var updatedProject = updated.GetProject(originalProject.Id);
            if (updatedProject is null)
            {
                return null;
            }

            var updatedOptions = updatedProject.CompilationOptions;
            if (!ReferenceEquals(proof.MetadataReferenceResolver, updatedOptions?.MetadataReferenceResolver)
                || !IsKnownSourceResolver(updatedOptions?.SourceReferenceResolver)
                || !IsKnownXmlResolver(updatedOptions?.XmlReferenceResolver)
                || !ReferenceEquals(proof.StrongNameProvider, updatedOptions?.StrongNameProvider)
                || !IsKnownAssemblyIdentityComparer(updatedOptions?.AssemblyIdentityComparer))
            {
                return null;
            }

            carried.Add(updatedProject.Id, ProjectInputProof.Capture(updatedProject));
            if (generatorContracts.TryGetValue(originalProject.Id, out var contracts))
            {
                if (!ProjectsRetainContractBinding(originalProject, updatedProject, contracts))
                {
                    return null;
                }

                carriedContracts.Add(updatedProject.Id, contracts);
            }
        }

        return new WorkspaceInputProvenance(carried, carriedContracts, trustedSdkDistributionPath);
    }

    /// <summary>Carries proof across the capture owner's own PE-reference-only solution rewrite.</summary>
    internal WorkspaceInputProvenance? CarryKnownMetadataReferenceChanges(Solution original, Solution updated)
    {
        var carried = new Dictionary<ProjectId, ProjectInputProof>();
        var carriedContracts = new Dictionary<ProjectId, ImmutableArray<GeneratorCreatorInputContract>>();
        foreach (var originalProject in original.Projects)
        {
            if (!projects.TryGetValue(originalProject.Id, out var proof) || !Matches(originalProject))
            {
                return null;
            }

            var updatedProject = updated.GetProject(originalProject.Id);
            if (updatedProject is null)
            {
                return null;
            }

            var options = updatedProject.CompilationOptions;
            if (!ReferenceEquals(proof.MetadataReferenceResolver, options?.MetadataReferenceResolver)
                || !ReferenceEquals(proof.StrongNameProvider, options?.StrongNameProvider)
                || !IsKnownSourceResolver(options?.SourceReferenceResolver)
                || !IsKnownXmlResolver(options?.XmlReferenceResolver)
                || !IsKnownAssemblyIdentityComparer(options?.AssemblyIdentityComparer))
            {
                return null;
            }

            carried.Add(updatedProject.Id, ProjectInputProof.Capture(updatedProject));
            if (generatorContracts.TryGetValue(originalProject.Id, out var contracts))
            {
                if (!ProjectsRetainContractBinding(originalProject, updatedProject, contracts))
                {
                    return null;
                }

                carriedContracts.Add(updatedProject.Id, contracts);
            }
        }

        return new WorkspaceInputProvenance(carried, carriedContracts, trustedSdkDistributionPath);
    }

    private static bool ProjectsRetainContractBinding(
        Project originalProject,
        Project updatedProject,
        ImmutableArray<GeneratorCreatorInputContract> contracts) =>
        originalProject.Id == updatedProject.Id
        && originalProject.FilePath is not null
        && updatedProject.FilePath is not null
        && PathsEqual(originalProject.FilePath, updatedProject.FilePath)
        && contracts.All(contract => updatedProject.AnalyzerReferences.OfType<AnalyzerFileReference>()
            .Any(reference => contract.MatchesProjectAndAnalyzer(updatedProject.FilePath, GetSourcePath(reference))));

    private static string GetSourcePath(AnalyzerFileReference reference) =>
        AnalyzerImageCapture.GetSourcePath(reference) ?? reference.FullPath;

    private static bool PathsEqual(string left, string right) => PathComparer.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static bool IsKnownSourceResolver(SourceReferenceResolver? resolver) =>
        resolver is null || resolver.GetType() == typeof(SourceFileResolver);

    private static bool IsKnownXmlResolver(XmlReferenceResolver? resolver) =>
        resolver is null || resolver.GetType() == typeof(XmlFileResolver);

    private static bool IsKnownAssemblyIdentityComparer(AssemblyIdentityComparer? comparer) =>
        comparer is null
        || ReferenceEquals(comparer, AssemblyIdentityComparer.Default)
        || comparer.GetType() == typeof(DesktopAssemblyIdentityComparer);

    private sealed record ProjectInputProof(
        AnalyzerConfigOptionsProvider AnalyzerConfigOptionsProvider,
        SyntaxTreeOptionsProvider? SyntaxTreeOptionsProvider,
        MetadataReferenceResolver? MetadataReferenceResolver,
        SourceReferenceResolver? SourceReferenceResolver,
        XmlReferenceResolver? XmlReferenceResolver,
        StrongNameProvider? StrongNameProvider,
        AssemblyIdentityComparer? AssemblyIdentityComparer)
    {
        internal static ProjectInputProof Capture(Project project)
        {
            var options = project.CompilationOptions;
            return new ProjectInputProof(
                project.AnalyzerOptions.AnalyzerConfigOptionsProvider,
                options?.SyntaxTreeOptionsProvider,
                options?.MetadataReferenceResolver,
                options?.SourceReferenceResolver,
                options?.XmlReferenceResolver,
                options?.StrongNameProvider,
                options?.AssemblyIdentityComparer);
        }
    }
}
