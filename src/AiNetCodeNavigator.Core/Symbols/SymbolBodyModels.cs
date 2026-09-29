#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Symbols;

public sealed record SymbolBodyResult(
    string Body,
    string Availability,
    string ContentMode,
    string? Hint,
    int TotalLines,
    int DisplayedStart,
    int DisplayedEnd,
    bool HasMore,
    string? DocCommentId = null,
    string? HandoffId = null);

public sealed record BatchSymbolBodyResult(
    IReadOnlyList<SymbolBodyResult> Items);
