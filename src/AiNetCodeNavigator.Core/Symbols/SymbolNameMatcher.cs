#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Central name matcher for symbol searches.
/// Supports substrings, wildcards (*, ?), method names with parentheses removed and dot-separated type/member paths.
/// </summary>
public static class SymbolNameMatcher
{
    private const int MinWordLengthForSuggestions = 4;
    private const int MaxSuggestions = 5;

    public static string CleanPattern(string rawPattern) =>
        InputNormalizer.NormalizeSymbolIdentifier(rawPattern);

    public static Func<string, bool> CreateDeclarationNameFilter(string pattern)
    {
        var clean = CleanPattern(pattern);
        if (clean.Contains('.'))
        {
            var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var lastPart = parts[^1];
                var secondLastPart = parts.Length > 1 ? parts[^2] : null;

                var lastFilter = CreatePredicateForSimplePattern(lastPart);
                if (secondLastPart is null) return lastFilter;

                var secondLastFilter = CreatePredicateForSimplePattern(secondLastPart);
                return name => lastFilter(name) || secondLastFilter(name);
            }
        }

        return CreatePredicateForSimplePattern(clean);
    }

    public static bool MatchesSymbol(ISymbol symbol, string pattern) =>
        CreateSymbolFilter(pattern)(symbol);

    /// <summary>
    /// Prepares name matching once for reuse across a search's candidate symbols.
    /// </summary>
    public static Func<ISymbol, bool> CreateSymbolFilter(string pattern)
    {
        var clean = CleanPattern(pattern);
        if (clean.Contains('.'))
        {
            var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var lastFilter = CreatePredicateForSimplePattern(parts[^1]);
                var containerPrefix = string.Join(".", parts.Take(parts.Length - 1));
                return symbol =>
                {
                    var display = symbol.ToDisplayString();
                    return display.Contains(clean, StringComparison.OrdinalIgnoreCase)
                        || lastFilter(symbol.Name) && display.Contains(containerPrefix, StringComparison.OrdinalIgnoreCase);
                };
            }

            var simpleFilter = CreatePredicateForSimplePattern(clean);
            return symbol => symbol.ToDisplayString().Contains(clean, StringComparison.OrdinalIgnoreCase)
                || simpleFilter(symbol.Name);
        }

        var nameFilter = CreatePredicateForSimplePattern(clean);
        return symbol => nameFilter(symbol.Name);
    }

    public static async Task<IReadOnlyList<string>> FindSimilarSymbolNamesAsync(
        Solution solution,
        string rawPattern,
        CancellationToken ct,
        Func<ISymbol, bool>? includeSymbol = null)
    {
        var clean = CleanPattern(rawPattern).Trim('*', '?');
        var words = Regex.Matches(clean, @"[A-Z][a-z0-9]+|[a-z0-9]+", RegexOptions.None, TimeSpan.FromMilliseconds(100))
            .Select(m => m.Value)
            .Where(w => w.Length >= MinWordLengthForSuggestions)
            .Take(2)
            .ToList();

        if (words.Count == 0) return Array.Empty<string>();

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();
            var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
                solution,
                name => name.Contains(word, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.Type,
                ct).ConfigureAwait(false);

            foreach (var sym in symbols)
            {
                if (includeSymbol is not null && !includeSymbol(sym)) continue;
                candidates.Add(sym.Name);
                if (candidates.Count >= MaxSuggestions) break;
            }

            if (candidates.Count >= MaxSuggestions) break;
        }

        return candidates.ToList();
    }

    private static Func<string, bool> CreatePredicateForSimplePattern(string pattern)
    {
        if (pattern.Length >= 2 && pattern.StartsWith('*') && pattern.EndsWith('*')
            && !pattern[1..^1].Contains('*') && !pattern[1..^1].Contains('?'))
        {
            var sub = pattern.Trim('*');
            return name => name.Contains(sub, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var regexPattern = RegexAutoDetector.ConvertWildcardToRegex(pattern, anchored: true);
            if (RegexAutoDetector.IsValidRegex(regexPattern, out var globRegex))
            {
                return name => globRegex!.IsMatch(name);
            }
        }

        if (RegexAutoDetector.IsLikelyRegex(pattern) && RegexAutoDetector.IsValidRegex(pattern, out var likelyRegex))
        {
            return name => likelyRegex!.IsMatch(name);
        }

        return name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

}
