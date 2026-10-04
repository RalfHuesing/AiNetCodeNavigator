#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Request-lived source relationships checked against actual bound reference images.</summary>
internal sealed class SourceMetadataRelationshipScanner
{
    private sealed record Owner(Project Project, Compilation Compilation);
    private readonly SourceIdentityValidatedSnapshot snapshot;
    private readonly SourceMetadataContractCandidate contract;
    private readonly Owner[] owners;
    private readonly string typeId;
    private readonly bool interfaceContract;
    private string? missingProof;

    private SourceMetadataRelationshipScanner(SourceIdentityValidatedSnapshot snapshot,
        SourceMetadataContractCandidate contract, Owner[] owners)
    {
        this.snapshot = snapshot;
        this.contract = contract;
        this.owners = owners;
        var symbol = contract.Occurrences[0].Symbol;
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType!;
        typeId = DocumentationCommentId.CreateDeclarationId(type.OriginalDefinition)!;
        interfaceContract = type.TypeKind == TypeKind.Interface;
    }

    internal static async Task<Result<ImmutableArray<ISymbol>>> CollectAsync(SourceIdentityValidatedSnapshot snapshot,
        SourceMetadataContractCandidate contract, bool hierarchy, CancellationToken ct)
    {
        var root = contract.Occurrences[0].Symbol;
        if (hierarchy && root is not INamedTypeSymbol)
            return Result<ImmutableArray<ISymbol>>.Failure(NavigationErrorCodes.InvalidArgument, "Hierarchy requires a named type, not a metadata member ID.");
        if (root is INamedTypeSymbol type && (hierarchy ? type.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct)
                : type.TypeKind is not (TypeKind.Class or TypeKind.Interface))
            || root is not INamedTypeSymbol && !IsSupportedMember(root))
            return Result<ImmutableArray<ISymbol>>.Failure(NavigationErrorCodes.InvalidArgument, "Use a supported type, interface member, or abstract/virtual member.");
        var owners = new List<Owner>();
        foreach (var project in snapshot.Solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
                return Result<ImmutableArray<ISymbol>>.Failure(NavigationErrorCodes.WorkspaceDiagnostic, "A source project compilation is unavailable for exact metadata relationships.");
            owners.Add(new(project, compilation));
        }
        var scanner = new SourceMetadataRelationshipScanner(snapshot, contract, owners.ToArray());
        var found = new Dictionary<(ProjectId, string), ISymbol>();
        foreach (var owner in owners)
            foreach (var sourceType in AssemblyAnalysisSymbolTraversal.GetAllTypes(owner.Compilation.Assembly.GlobalNamespace))
            {
                ct.ThrowIfCancellationRequested();
                if (sourceType.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;
                if (root is INamedTypeSymbol)
                {
                    if (scanner.interfaceContract ? scanner.FindInterfaces(sourceType, owner).Count > 0
                        : sourceType.TypeKind == TypeKind.Class && scanner.DerivesFrom(sourceType, owner))
                        Add(sourceType, owner);
                }
                else if (scanner.interfaceContract)
                {
                    foreach (var binding in scanner.FindInterfaces(sourceType, owner))
                    {
                        var member = binding.Interface.GetMembers().SingleOrDefault(member =>
                            DocumentationCommentId.CreateDeclarationId(member.OriginalDefinition) == contract.DeclarationId);
                        if (member is null) continue;
                        var implementation = binding.Type.FindImplementationForInterfaceMember(member);
                        if (implementation is not null && scanner.SourceOwner(implementation) is { } actual)
                            Add(actual.Symbol, actual.Owner);
                    }
                }
                else
                {
                    foreach (var member in sourceType.GetMembers())
                        if (scanner.OverridesContract(member, owner)) Add(member, owner);
                }
            }
        return scanner.missingProof is null
            ? Result<ImmutableArray<ISymbol>>.Success(found.Values.ToImmutableArray())
            : Result<ImmutableArray<ISymbol>>.Failure(NavigationErrorCodes.WorkspaceDiagnostic, scanner.missingProof);

        void Add(ISymbol symbol, Owner owner)
        {
            var id = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
            if (id is not null) found.TryAdd((owner.Project.Id, id), symbol);
        }
    }

    private static bool IsSupportedMember(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract || method.IsVirtual || method.IsOverride && !method.IsSealed,
        IPropertySymbol property => property.ContainingType.TypeKind == TypeKind.Interface || property.IsAbstract || property.IsVirtual || property.IsOverride && !property.IsSealed,
        IEventSymbol evt => evt.ContainingType.TypeKind == TypeKind.Interface || evt.IsAbstract || evt.IsVirtual || evt.IsOverride && !evt.IsSealed,
        _ => false,
    };

    private bool? HasSelectedImage(ISymbol symbol, Owner owner)
    {
        var assembly = symbol.ContainingAssembly;
        var reference = assembly is null ? null : owner.Compilation.GetMetadataReference(assembly);
        if (reference is null) return null;
        var ordinal = owner.Project.MetadataReferences.Select((item, index) => (item, index))
            .Where(item => ReferenceEquals(item.item, reference)).Select(item => (int?)item.index).SingleOrDefault();
        if (ordinal is null) return null;
        var evidence = snapshot.Inputs.MetadataReferences.SingleOrDefault(item => item.OwnerProjectId == owner.Project.Id && item.ReferenceOrdinal == ordinal);
        if (evidence is null) { missingProof = "A bound metadata relationship has no captured reference image evidence."; return null; }
        return contract.Occurrences.Any(occurrence => occurrence.Images.SequenceEqual(evidence.Images));
    }

    private IReadOnlyList<(INamedTypeSymbol Type, INamedTypeSymbol Interface)> FindInterfaces(INamedTypeSymbol type, Owner owner)
    {
        var bindings = new List<(INamedTypeSymbol Type, INamedTypeSymbol Interface)>();
        var unknown = false;
        foreach (var iface in type.AllInterfaces.Where(iface => DocumentationCommentId.CreateDeclarationId(iface.OriginalDefinition) == typeId))
        {
            var selected = HasSelectedImage(iface, owner);
            if (selected == true) bindings.Add((type, iface));
            unknown |= selected is null;
        }
        if (unknown)
        {
            if (type.BaseType is { } sourceBase && SourceOwner(sourceBase) is { Symbol: INamedTypeSymbol original, Owner: var baseOwner })
                bindings.AddRange(FindInterfaces(original, baseOwner));
            else
                missingProof = "The metadata interface binding cannot be traced to a captured owner image.";
        }
        return bindings;
    }

    private bool DerivesFrom(INamedTypeSymbol type, Owner owner)
    {
        var lastSource = type;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (DocumentationCommentId.CreateDeclarationId(current.OriginalDefinition) == typeId)
            {
                var selected = HasSelectedImage(current, owner);
                if (selected is not null) return selected.Value;
                if (SourceOwner(lastSource) is { Symbol: INamedTypeSymbol original, Owner: var originalOwner }
                    && !ReferenceEquals(original, lastSource)) return DerivesFrom(original, originalOwner);
                missingProof = "The metadata base-type binding cannot be traced to a captured owner image.";
                return false;
            }
            if (current.DeclaringSyntaxReferences.Length > 0) lastSource = current;
        }
        return false;
    }

    private bool OverridesContract(ISymbol member, Owner owner)
    {
        var lastSource = member;
        for (var current = Overridden(member); current is not null; current = Overridden(current))
        {
            if (DocumentationCommentId.CreateDeclarationId(current.OriginalDefinition) == contract.DeclarationId)
            {
                var selected = HasSelectedImage(current, owner);
                if (selected is not null) return selected.Value;
                if (SourceOwner(lastSource) is { } original && !ReferenceEquals(original.Symbol, lastSource))
                    return OverridesContract(original.Symbol, original.Owner);
                missingProof = "The metadata override binding cannot be traced to a captured owner image.";
                return false;
            }
            if (current.DeclaringSyntaxReferences.Length > 0) lastSource = current;
        }
        return false;
    }

    private static ISymbol? Overridden(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.OverriddenMethod,
        IPropertySymbol property => property.OverriddenProperty,
        IEventSymbol evt => evt.OverriddenEvent,
        _ => null,
    };

    private (ISymbol Symbol, Owner Owner)? SourceOwner(ISymbol symbol)
    {
        var trees = symbol.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree).ToHashSet();
        if (trees.Count == 0) return null;
        var matches = owners.Where(owner => owner.Compilation.SyntaxTrees.Any(trees.Contains)).ToArray();
        if (matches.Length != 1)
        {
            missingProof = "The source relationship declaration does not identify one exact loaded project owner.";
            return null;
        }
        var owner = matches[0];
        var id = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
        var declarations = id is null ? [] : DocumentationCommentId.GetSymbolsForDeclarationId(id, owner.Compilation)
            .Where(value => ReferenceEquals(value.ContainingAssembly, owner.Compilation.Assembly)
                && value.DeclaringSyntaxReferences.Any(reference => trees.Contains(reference.SyntaxTree)))
            .Distinct(SymbolEqualityComparer.Default).ToArray();
        if (declarations.Length == 1) return (declarations[0], owner);
        missingProof = "The source relationship declaration does not round-trip uniquely in its proven project owner.";
        return null;
    }
}
