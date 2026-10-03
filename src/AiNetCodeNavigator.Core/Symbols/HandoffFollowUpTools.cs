#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Shared contract for permitted follow-up tools of canonical handoff IDs.</summary>
public static class HandoffFollowUpTools
{
    public static IReadOnlyList<string> ForAssembly(ISymbol symbol) => ["get_symbol_body"];

    public static IReadOnlyList<string> For(ISymbol symbol) =>
        ForKind(symbol is INamedTypeSymbol ? "type" : "member");

    public static IReadOnlyList<string> ForKind(string kind) =>
        kind == "type"
            ? ["get_symbol_body", "get_class_structure", "get_type_hierarchy", "find_implementations", "find_references", "get_call_tree"]
            : ["get_symbol_body", "find_references", "get_call_tree", "get_impact", "get_context"];
}
