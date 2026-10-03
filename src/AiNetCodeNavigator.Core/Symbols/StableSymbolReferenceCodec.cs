#nullable enable

using System;
using System.Globalization;
using System.Text;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Owns stable reference routing, parsing, and canonical formatting.</summary>
public static class StableSymbolReferenceCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryPrepareReferenceInput(string? input, out string payload, out ResultError? error)
        => TryPrepareReferenceInput(input, null, out payload, out error);

    /// <summary>Routes and parses a reference using the original argument and one canonical codec.</summary>
    public static bool TryParseReferenceInput(
        string? input,
        string? discoveryNormalizedInput,
        out StableSymbolReference? reference,
        out ResultError? error)
    {
        reference = null;
        if (!TryPrepareReferenceInput(input, discoveryNormalizedInput, out var payload, out error)) return false;
        if (error is not null) return true;
        _ = TryParse(payload, out reference, out error);
        return true;
    }

    /// <summary>Uses the original argument even when discovery cleanup exposed a reference prefix.</summary>
    public static bool TryPrepareReferenceInput(string? input, string? discoveryNormalizedInput, out string payload, out ResultError? error)
    {
        payload = string.Empty;
        error = null;
        if (input is null) return false;

        var probe = input.Trim();
        probe = StripOneWrapper(probe);
        probe = probe.Trim();
        var recognized = StartsWithPrefix(probe, "src:") || StartsWithPrefix(probe, "asm:")
            || IsLegacyPrefix(probe, "h:") || IsLegacyPrefix(probe, "i:")
            || IsReferencePrefix(discoveryNormalizedInput);
        if (!recognized) return false;

        if (ContainsUnicodeControl(input) || !HasValidUnicode(input))
        {
            error = InvalidReference("Reference input contains a control character or invalid Unicode.");
            return true;
        }

        var value = TrimAsciiSpaces(input);
        value = StripOneWrapper(value);
        value = TrimAsciiSpaces(value);
        if (IsLegacyPrefix(value, "h:") || IsLegacyPrefix(value, "i:"))
        {
            error = InvalidReference("Legacy h:/i: identifiers are not canonical src:/asm: references.");
            return true;
        }

        payload = value;
        return true;
    }

    public static bool TryParse(string value, out StableSymbolReference? reference, out ResultError? error)
    {
        reference = null;
        error = null;
        if (value is null || ContainsUnicodeControl(value) || !HasValidUnicode(value))
        {
            error = InvalidReference("Reference contains a control character or invalid Unicode.");
            return false;
        }

        StableSymbolReference? candidate;
        if (value.StartsWith("src:", StringComparison.OrdinalIgnoreCase))
        {
            if (!value.StartsWith("src:", StringComparison.Ordinal))
            {
                error = InvalidReference("Reference prefixes must use lowercase canonical spelling.");
                return false;
            }

            if (!TrySplitAndDecode(value.AsSpan(4), out var owner, out var declarationId)
                || !IsCanonicalSourcePath(owner)
                || !IsCanonicalDeclarationId(declarationId))
            {
                error = InvalidReference("The source reference is malformed or noncanonical.");
                return false;
            }

            candidate = new StableSymbolReference.Source(owner, declarationId);
        }
        else if (value.StartsWith("asm:", StringComparison.OrdinalIgnoreCase))
        {
            if (!value.StartsWith("asm:", StringComparison.Ordinal))
            {
                error = InvalidReference("Reference prefixes must use lowercase canonical spelling.");
                return false;
            }

            if (!TrySplitAndDecode(value.AsSpan(4), out var owner, out var declarationId)
                || owner.Length == 0
                || !IsCanonicalComponent(owner)
                || !IsCanonicalDeclarationId(declarationId))
            {
                error = InvalidReference("The assembly reference is malformed or noncanonical.");
                return false;
            }

            candidate = new StableSymbolReference.Assembly(owner, declarationId);
        }
        else
        {
            error = InvalidReference("The value is not a stable source or assembly reference.");
            return false;
        }

        if (!string.Equals(Format(candidate), value, StringComparison.Ordinal))
        {
            error = InvalidReference("The reference is valid but its escaping is not canonical.");
            return false;
        }

        reference = candidate;
        return true;
    }

    public static string Format(StableSymbolReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!IsCanonicalDeclarationId(reference.DeclarationId))
            throw new ArgumentException("The declaration ID is not supported.", nameof(reference));

        return reference switch
        {
            StableSymbolReference.Source source when IsCanonicalSourcePath(source.ProjectPath)
                => "src:" + Encode(source.ProjectPath) + "|" + Encode(source.DeclarationId),
            StableSymbolReference.Assembly assembly when assembly.SimpleName.Length > 0 && IsCanonicalComponent(assembly.SimpleName)
                => "asm:" + Encode(assembly.SimpleName) + "|" + Encode(assembly.DeclarationId),
            _ => throw new ArgumentException("The reference owner is not canonical.", nameof(reference)),
        };
    }

    public static bool TryFormatCanonical(
        StableSymbolReference reference,
        out string formatted,
        out ResultError? error)
    {
        formatted = string.Empty;
        error = null;
        try
        {
            formatted = Format(reference);
        }
        catch (ArgumentException exception)
        {
            error = InvalidReference(exception.Message);
            return false;
        }

        if (!TryParse(formatted, out var parsed, out error) || !Equals(reference, parsed))
        {
            error ??= InvalidReference("The typed reference does not round-trip through the canonical codec.");
            formatted = string.Empty;
            return false;
        }
        return true;
    }

    public static bool IsCanonicalSourcePath(string path)
    {
        if (path.Length == 0 || path.Contains('\\') || path[^1] == '/' || !IsCanonicalComponent(path)) return false;
        var segments = path.Split('/');
        if (segments[0].Length >= 2 && char.IsAsciiLetter(segments[0][0]) && segments[0][1] == ':') return false;
        var hasNamedSegment = false;
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == ".") return false;
            if (segment == "..")
            {
                if (hasNamedSegment) return false;
                continue;
            }

            hasNamedSegment = true;
        }

        return hasNamedSegment;
    }

    public static bool IsCanonicalDeclarationId(string value) =>
        value.Length > 2
        && value[1] == ':'
        && value[0] is 'T' or 'M' or 'P' or 'F' or 'E'
        && IsCanonicalComponent(value);

    private static bool TrySplitAndDecode(ReadOnlySpan<char> value, out string owner, out string declarationId)
    {
        owner = string.Empty;
        declarationId = string.Empty;
        var separator = value.IndexOf('|');
        if (separator <= 0 || separator == value.Length - 1 || value[(separator + 1)..].IndexOf('|') >= 0) return false;
        if (!TryDecode(value[..separator], out owner) || !TryDecode(value[(separator + 1)..], out declarationId)) return false;
        return true;
    }

    private static bool TryDecode(ReadOnlySpan<char> value, out string decoded)
    {
        decoded = string.Empty;
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '%')
            {
                if (character == ' ') return false;
                builder.Append(character);
                continue;
            }

            if (index + 2 >= value.Length) return false;
            var escape = value.Slice(index, 3);
            if (escape.SequenceEqual("%25")) builder.Append('%');
            else if (escape.SequenceEqual("%7C")) builder.Append('|');
            else if (escape.SequenceEqual("%20")) builder.Append(' ');
            else return false;
            index += 2;
        }

        decoded = builder.ToString();
        return decoded.Length > 0 && IsCanonicalComponent(decoded);
    }

    private static string Encode(string value) => value.Replace("%", "%25", StringComparison.Ordinal)
        .Replace("|", "%7C", StringComparison.Ordinal)
        .Replace(" ", "%20", StringComparison.Ordinal);

    private static bool IsCanonicalComponent(string value) =>
        value.Length > 0 && HasValidUnicode(value) && !ContainsUnicodeControl(value);

    private static bool HasValidUnicode(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool ContainsUnicodeControl(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (System.Text.Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control) return true;
        }
        return false;
    }

    private static bool StartsWithPrefix(string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyPrefix(string value, string prefix)
    {
        if (!StartsWithPrefix(value, prefix)) return false;
        if (value.Length == prefix.Length) return true;
        var remainder = value[prefix.Length];
        if (remainder is not ('/' or '\\')) return true;

        // H:/ and I:\\ are malformed empty drive selectors; nonempty drive paths stay physical paths.
        return value.Length == prefix.Length + 1;
    }

    private static bool IsReferencePrefix(string? value) => value is not null
        && (StartsWithPrefix(value.Trim(), "src:") || StartsWithPrefix(value.Trim(), "asm:")
            || IsLegacyPrefix(value.Trim(), "h:") || IsLegacyPrefix(value.Trim(), "i:"));

    private static string StripOneWrapper(string value)
    {
        if (value.Length < 2) return value;
        var first = value[0];
        var expectedLast = first switch { '`' => '`', '"' => '"', '\'' => '\'', _ => '\0' };
        return expectedLast != '\0' && value[^1] == expectedLast ? value[1..^1] : value;
    }

    private static string TrimAsciiSpaces(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && value[start] == ' ') start++;
        while (end > start && value[end - 1] == ' ') end--;
        return value[start..end];
    }

    private static ResultError InvalidReference(string message) => new(
        NavigationErrorCodes.InvalidSymbolReference,
        message,
        "Rediscover the declaration and use the reference returned by that discovery.");
}
