#nullable enable

using System;
using System.Text.RegularExpressions;

namespace AiNetCodeNavigator.Core.Common;

/// <summary>
/// Shared utility for cleaning up LLM and user input.
/// Cleans up backticks, quotation marks, method parentheses and generics.
/// </summary>
public static class InputNormalizer
{
    private static readonly Regex MethodParenRegex = new(@"^(.+?)\s*\(\s*\)$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GenericTypeRegex = new(@"^(.+?)\s*<.*?>$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public static string StripEnclosingQuotesAndBackticks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if ((trimmed.StartsWith('`') && trimmed.EndsWith('`') && trimmed.Length >= 2) ||
            (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2) ||
            (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static string NormalizeSymbolIdentifier(string? rawIdentifier)
    {
        if (string.IsNullOrWhiteSpace(rawIdentifier)) return string.Empty;
        var cleaned = StripEnclosingQuotesAndBackticks(rawIdentifier);

        var parenMatch = MethodParenRegex.Match(cleaned);
        if (parenMatch.Success)
        {
            cleaned = parenMatch.Groups[1].Value.TrimEnd();
        }

        var genericMatch = GenericTypeRegex.Match(cleaned);
        if (genericMatch.Success)
        {
            cleaned = genericMatch.Groups[1].Value.TrimEnd();
        }

        return cleaned;
    }

    /// <summary>
    /// Identifies opaque handoff handles without mistaking a Windows drive path for one.
    /// The registry recognizes the handle prefix without regard to case, while the wire format
    /// itself remains lowercase; routing uppercase variants to the registry preserves its
    /// typed malformed-handoff error.
    /// </summary>
    public static bool HasOpaqueHandoffPrefix(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !identifier.StartsWith("h:", StringComparison.OrdinalIgnoreCase))
            return false;

        return identifier.Length < 3 || identifier[2] is not ('\\' or '/');
    }
}
