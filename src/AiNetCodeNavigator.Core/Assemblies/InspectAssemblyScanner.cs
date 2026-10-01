#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class InspectAssemblyScanner
{
    public const int DefaultMaxResults = 100;
    public const int MaxResults = 1000;
    public const int DefaultMaxMembers = 100;
    public const int MaxMembers = 1000;

    public static async Task<Result<InspectAssemblyPayload>> InspectAsync(
        InspectAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidatePath(request.AssemblyPath, out var fullPath, out var pathError))
        {
            return Result<InspectAssemblyPayload>.Failure(
                NavigationErrorCodes.InvalidArgument,
                pathError,
                "targetPath muss ein existierender absoluter lokaler .dll- oder .exe-Pfad sein.");
        }

        var opened = await AssemblyNavigationSessionScope.OpenAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            return Result<InspectAssemblyPayload>.Failure(opened.Error);
        }
        await using var scope = opened.Value!;
        var context = scope.Context;

        var binding = AssemblyPaging.CreateInspectBinding(
            fullPath,
            context.Origin.ContentHash,
            context.ReferenceSnapshotHash,
            request);
        if (!AssemblyPaging.TryReadBoundOffset(request.Cursor, binding, out var offset))
        {
            return Result<InspectAssemblyPayload>.Failure(
                NavigationErrorCodes.InvalidArgument,
                "continuationToken ist nicht an Target, Assembly-Hash und Abfrage gebunden oder abgelaufen.",
                "den zuletzt gelieferten continuationToken unverändert mit derselben Abfrage wiederverwenden.");
        }

        var maxResults = NormalizeLimit(request.MaxResults, DefaultMaxResults, MaxResults);
        var maxMembers = NormalizeLimit(request.MaxMembers, DefaultMaxMembers, MaxMembers);

        var allTypes = AssemblyAnalysisSymbolTraversal.GetAllTypes(context.Assembly.GlobalNamespace)
            .Where(type => !request.PublicOnly || IsPublicApi(type))
            .Where(type => MatchesNamespace(type, request.Namespace))
            .Where(type => MatchesType(type, request.TypeName, request.ExactTypeName))
            .OrderBy(type => type.ContainingNamespace.ToDisplayString(), StringComparer.Ordinal)
            .ThenBy(type => type.ToDisplayString(), StringComparer.Ordinal)
            .ToList();

        var limitedTypes = allTypes.Skip(Math.Max(0, offset)).Take(maxResults).ToList();
        var handoffIdentity = AnalysisSymbolIdentity.ForAssembly(
            context.Origin.CanonicalPath,
            context.Origin.ContentHash,
            context.Generation,
            context.ReferenceSnapshotHash);

        var typeDtos = limitedTypes
            .Select(type => ToTypeDto(type, request, maxMembers, handoffIdentity))
            .ToList();

        var namespaces = allTypes
            .Select(type => type.ContainingNamespace.ToDisplayString())
            .Where(ns => !string.IsNullOrEmpty(ns))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        var isTruncated = Math.Max(0, offset) + limitedTypes.Count < allTypes.Count;
        var continuationToken = isTruncated
            ? AssemblyPaging.CreateToken(Math.Max(0, offset) + limitedTypes.Count, binding)
            : null;

        var includeReferences = request.IncludeReferenceDetails;
        IReadOnlyList<AssemblyReferenceDto> references = includeReferences
            ? context.References.Take(32).ToList()
            : Array.Empty<AssemblyReferenceDto>();

        var referenceSummary = new AssemblyReferenceSummary(
            context.References.Count,
            includeReferences ? Math.Min(context.References.Count, 32) : 0,
            includeReferences ? context.References.Count > 32 : context.References.Count > 0);

        var payload = new InspectAssemblyPayload(
            fullPath,
            context.Identity,
            InspectAssemblyFormatter.CompactNamespaces(namespaces),
            references,
            typeDtos,
            context.Diagnostics,
            context.Status.ToCompletenessLabel(),
            isTruncated,
            allTypes.Count,
            typeDtos.Count,
            isTruncated ? ["maxResults"] : [],
            context.Origin,
            context.Generation,
            context.Status.ToWireValue(),
            referenceSummary,
            includeReferences,
            namespaces.Count,
            context.DecompiledProjectPaths?.DecompiledSourceRoot,
            continuationToken);

        var formattedText = InspectAssemblyFormatter.FormatText(payload, request.PublicOnly);
        return Result<InspectAssemblyPayload>.Success(payload with { FormattedText = formattedText });
    }

    public static bool TryValidatePath(string? assemblyPath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            error = "Pflichtparameter 'targetPath' fehlt oder ist leer.";
            return false;
        }

        if (!Path.IsPathFullyQualified(assemblyPath))
        {
            error = $"Der Parameter 'targetPath' muss ein absoluter lokaler Pfad sein: '{assemblyPath}'.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(assemblyPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Der Parameter 'targetPath' ist kein gültiger lokaler Pfad: '{assemblyPath}' ({ex.Message}).";
            return false;
        }

        var ext = Path.GetExtension(fullPath);
        if (!string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Der Parameter 'targetPath' muss auf eine .dll- oder .exe-Datei zeigen: '{assemblyPath}'.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = $"Die Assembly-Datei wurde nicht gefunden: '{fullPath}'.";
            return false;
        }

        return true;
    }

    public static int NormalizeLimit(int requested, int defaultValue, int maxValue) =>
        requested <= 0 ? defaultValue : Math.Clamp(requested, 1, maxValue);

    private static AssemblyTypeDto ToTypeDto(
        INamedTypeSymbol type,
        InspectAssemblyRequest request,
        int maxMembers,
        AnalysisSymbolIdentity handoffIdentity)
    {
        var matchingMembers = type.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared)
            .Where(member => !IsAccessor(member))
            .Where(member => !request.PublicOnly || IsPublicApi(member))
            .Where(member => MatchesMember(member, request.MemberName, request.MemberNames))
            .Select(member => ToMemberDto(member, handoffIdentity))
            .OrderBy(member => member.Kind, StringComparer.Ordinal)
            .ThenBy(member => member.Signature, StringComparer.Ordinal)
            .ToList();

        var members = matchingMembers
            .Take(maxMembers)
            .ToList();

        var stableId = StableId(type, handoffIdentity);
        return new AssemblyTypeDto(
            type.ContainingNamespace.ToDisplayString(),
            TypeName(type),
            TypeKindName(type),
            type.DeclaredAccessibility.ToString(),
            members,
            Attributes(type),
            matchingMembers.Count,
            members.Count < matchingMembers.Count,
            members.Count < matchingMembers.Count ? ["maxMembers"] : [],
            stableId,
            Handoff: stableId is not null,
            AllowedFollowUpTools: stableId is null ? Array.Empty<string>() : HandoffFollowUpTools.ForAssembly(type));
    }

    private static AssemblyMemberDto ToMemberDto(ISymbol member, AnalysisSymbolIdentity handoffIdentity)
    {
        var method = member as IMethodSymbol;
        var parameters = member switch
        {
            IMethodSymbol methodSymbol => Parameters(methodSymbol.Parameters),
            IPropertySymbol propertySymbol => Parameters(propertySymbol.Parameters),
            _ => Array.Empty<AssemblyParameterDto>(),
        };

        var stableId = StableId(member, handoffIdentity);
        return new AssemblyMemberDto(
            MemberKind(member),
            member.Name,
            member.DeclaredAccessibility.ToString(),
            method is null ? member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) : MethodSignature(method),
            parameters,
            method is null ? Array.Empty<string>() : GenericParameters(method),
            method is null ? Array.Empty<string>() : Constraints(method.TypeParameters),
            Attributes(member),
            stableId,
            Handoff: stableId is not null,
            AllowedFollowUpTools: stableId is null ? Array.Empty<string>() : HandoffFollowUpTools.ForAssembly(member));
    }

    private static string? StableId(ISymbol symbol, AnalysisSymbolIdentity? handoffIdentity) =>
        handoffIdentity?.FormatHandoff(symbol);

    private static string MethodSignature(IMethodSymbol method)
    {
        var format = SymbolDisplayFormat.CSharpErrorMessageFormat.WithParameterOptions(
            SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeOptionalBrackets);
        return $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} {method.ToDisplayString(format)}";
    }

    private static IReadOnlyList<AssemblyParameterDto> Parameters(IEnumerable<IParameterSymbol> parameters) =>
        parameters.Select(parameter => new AssemblyParameterDto(
            parameter.Name,
            parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            RefKindName(parameter.RefKind),
            parameter.IsOptional,
            DefaultValue(parameter))).ToList();

    private static string RefKindName(RefKind refKind) => refKind switch
    {
        RefKind.None => "none",
        RefKind.Ref => "ref",
        RefKind.Out => "out",
        RefKind.In => "in",
        RefKind.RefReadOnly or RefKind.RefReadOnlyParameter => "ref readonly",
        _ => refKind.ToString().ToLowerInvariant(),
    };

    private static string? DefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue) return null;
        return SymbolDisplay.FormatPrimitive(
                   parameter.ExplicitDefaultValue,
                   quoteStrings: true,
                   useHexadecimalNumbers: false)
               ?? parameter.ExplicitDefaultValue?.ToString();
    }

    private static IReadOnlyList<string> GenericParameters(IMethodSymbol method) =>
        method.TypeParameters.Select(parameter => parameter.Name).ToList();

    private static IReadOnlyList<string> Constraints(IEnumerable<ITypeParameterSymbol> parameters) =>
        parameters.Select(parameter =>
        {
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
            if (parameter.HasValueTypeConstraint) constraints.Add("struct");
            if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
            if (parameter.HasNotNullConstraint) constraints.Add("notnull");
            constraints.AddRange(parameter.ConstraintTypes.Select(type => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            if (parameter.HasConstructorConstraint) constraints.Add("new()");
            return constraints.Count == 0 ? parameter.Name : $"{parameter.Name}: {string.Join(", ", constraints)}";
        }).ToList();

    private static IReadOnlyList<string> Attributes(ISymbol symbol)
    {
        try
        {
            return symbol.GetAttributes()
                .Where(attribute => attribute.AttributeClass is not null)
                .Select(attribute => attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ["<Attribute konnten nicht aufgelöst werden>"];
        }
    }

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesType(INamedTypeSymbol type, string? filter, bool exact)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var normalized = filter.Trim();
        return exact
            ? string.Equals(type.Name, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.ToDisplayString(), normalized, StringComparison.OrdinalIgnoreCase)
            : Matches(type.ToDisplayString(), normalized);
    }

    private static bool MatchesMember(ISymbol member, string? filter, IReadOnlyList<string>? exactNames)
    {
        var hasSubstringFilter = !string.IsNullOrWhiteSpace(filter);
        var hasExactNames = exactNames?.Any(name => !string.IsNullOrWhiteSpace(name)) == true;
        if (!hasSubstringFilter && !hasExactNames) return true;
        return (hasSubstringFilter && Matches(member.Name, filter))
            || (hasExactNames && exactNames!.Any(name => !string.IsNullOrWhiteSpace(name)
                && string.Equals(member.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MatchesNamespace(INamedTypeSymbol type, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || string.Equals(type.ContainingNamespace.ToDisplayString(), filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicApi(ISymbol symbol)
    {
        if (symbol.DeclaredAccessibility != Accessibility.Public) return false;
        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (containing.DeclaredAccessibility != Accessibility.Public) return false;
        }

        return true;
    }

    private static string TypeKindName(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => "class",
        TypeKind.Interface => "interface",
        TypeKind.Struct => "struct",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        _ => type.TypeKind.ToString().ToLowerInvariant(),
    };

    private static string TypeName(INamedTypeSymbol type)
    {
        var containingType = type.ContainingType is null ? string.Empty : $"{TypeName(type.ContainingType)}.";
        var typeParameters = type.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", type.TypeParameters.Select(parameter => parameter.Name))}>";
        return $"{containingType}{type.Name}{typeParameters}";
    }

    private static string MemberKind(ISymbol member) => member switch
    {
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        _ => member.Kind.ToString().ToLowerInvariant(),
    };

    private static bool IsAccessor(ISymbol member) => member is IMethodSymbol
    {
        MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or
            MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise,
    };
}
