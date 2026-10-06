#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AiNetCodeNavigator.Core.Assemblies;

public sealed record AssemblyExportReferenceDiagnostic(string Code, string Message, bool IsError);

public sealed record AssemblyExportReferenceClosure(
    AssemblyIdentityDto? Identity,
    IReadOnlyList<AssemblyReferenceDto> References,
    IReadOnlyList<AssemblyExportReferenceDiagnostic> Diagnostics,
    bool IsComplete);

/// <summary>Static reference preflight for offline export, independent of MCP sessions.</summary>
public static class AssemblyExportReferenceResolver
{
    public static AssemblyExportReferenceClosure Resolve(string assemblyPath,
        Func<AssemblyReferenceDto, bool>? traverseReference = null, int maxDepth = 128, int maxNodes = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        var result = new AssemblyReferenceResolver(exportClosure: true, traverseReference: traverseReference,
            maxDepth: maxDepth, maxNodes: maxNodes).Resolve(assemblyPath);
        return new(result.Identity, result.References,
            result.Diagnostics.Select(diagnostic => new AssemblyExportReferenceDiagnostic(diagnostic.Code,
                diagnostic.Message, diagnostic.Severity == AssemblyDiagnosticSeverity.Error)).ToList(), result.IsComplete);
    }
}
