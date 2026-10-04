#nullable enable

using System;
using System.Collections.Generic;
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

    private WorkspaceInputProvenance(Solution solution)
    {
        projects = new Dictionary<ProjectId, ProjectInputProof>();
        foreach (var project in solution.Projects)
        {
            projects.Add(project.Id, ProjectInputProof.Capture(project));
        }
    }

    private WorkspaceInputProvenance(
        Dictionary<ProjectId, ProjectInputProof> carriedProjects) => projects = carriedProjects;

    internal static WorkspaceInputProvenance CreateFromTrustedLoader(Solution solution) => new(solution);

    /// <summary>Registers only the exact output built by TestWorkspaceBuilder from its public defaults.</summary>
    internal static void RegisterTestWorkspaceBuilderOutput(Solution solution) =>
        TestWorkspaceOutputs.Add(solution, new WorkspaceInputProvenance(solution));

    internal static WorkspaceInputProvenance? FindTestWorkspaceBuilderOutput(Solution solution) =>
        TestWorkspaceOutputs.TryGetValue(solution, out var proof) ? proof : null;

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
        }

        return new WorkspaceInputProvenance(carried);
    }

    /// <summary>Carries proof across the capture owner's own PE-reference-only solution rewrite.</summary>
    internal WorkspaceInputProvenance? CarryKnownMetadataReferenceChanges(Solution original, Solution updated)
    {
        var carried = new Dictionary<ProjectId, ProjectInputProof>();
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
        }

        return new WorkspaceInputProvenance(carried);
    }

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
