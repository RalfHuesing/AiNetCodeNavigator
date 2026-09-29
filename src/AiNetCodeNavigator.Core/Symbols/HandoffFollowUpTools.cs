#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Gemeinsamer Vertrag für erlaubte Folge-Tools kanonischer Handoff-IDs.</summary>
public static class HandoffFollowUpTools
{
    public static IReadOnlyList<string> For(ISymbol symbol) =>
        ForKind(symbol is INamedTypeSymbol ? "type" : "member");

    public static IReadOnlyList<string> ForKind(string kind) =>
        kind == "type"
            ? ["get_symbol_body", "get_class_structure", "get_type_hierarchy", "find_implementations", "find_references", "get_call_tree"]
            : ["get_symbol_body", "find_references", "get_call_tree", "get_impact", "get_test_context"];
}
