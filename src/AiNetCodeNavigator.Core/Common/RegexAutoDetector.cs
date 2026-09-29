#nullable enable

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AiNetCodeNavigator.Core.Common;

/// <summary>
/// Erkennung und Validierung von Regex-Mustern und Wildcard-Globs (* und ?).
/// </summary>
public static class RegexAutoDetector
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(100);

    public static readonly RegexOptions DefaultOptions =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    public static bool IsLikelyRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var trimmed = pattern.Trim();

        if ((trimmed.StartsWith('^') && trimmed.Length > 1) || (trimmed.EndsWith('$') && trimmed.Length > 1))
        {
            if (IsValidRegex(trimmed, out _)) return true;
        }

        if (ContainsRegexEscapeSequence(trimmed))
        {
            if (IsValidRegex(trimmed, out _)) return true;
        }

        if (trimmed.Contains("(?") && IsValidRegex(trimmed, out _))
        {
            return true;
        }

        return false;
    }

    public static bool HasRegexMetaCharacters(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        return pattern.Contains(".*") || pattern.Contains(".+")
            || pattern.Contains('|') || (pattern.Contains('[') && pattern.Contains(']'))
            || pattern.Contains('*') || pattern.Contains('?');
    }

    public static bool IsValidRegex(string pattern, out Regex? regex, TimeSpan? timeout = null)
    {
        try
        {
            regex = new Regex(pattern, DefaultOptions, timeout ?? DefaultTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            regex = null;
            return false;
        }
    }

    public static bool ContainsRegexEscapeSequence(string text)
    {
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '\\')
            {
                var next = text[i + 1];
                if (next is 's' or 'S' or 'd' or 'D' or 'w' or 'W' or 'b' or 'B' or 'p' or 'P' or 'x')
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string ConvertWildcardToRegex(string wildcardPattern, bool anchored = false)
    {
        var sb = new StringBuilder();
        if (anchored) sb.Append('^');
        foreach (var c in wildcardPattern)
        {
            if (c == '*')
            {
                sb.Append(".*");
            }
            else if (c == '?')
            {
                sb.Append('.');
            }
            else if (c is '/' or '\\')
            {
                sb.Append("[/\\\\]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        if (anchored) sb.Append('$');
        return sb.ToString();
    }
}
