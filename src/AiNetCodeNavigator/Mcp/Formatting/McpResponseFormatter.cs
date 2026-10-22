using System.Text;
using System.Text.Json;
using SharpToken;

namespace AiNetCodeNavigator.Mcp.Formatting;

internal static class McpResponseFormatter
{
    private const string EncodingName = "cl100k_base";
    private static readonly GptEncoding TokenEncoding = GptEncoding.GetEncoding(EncodingName);

    internal static string FormatCompactJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FormatCompactJson(document.RootElement);
    }

    internal static string FormatCompactJson(JsonElement root)
    {
        var output = new StringBuilder();
        WriteCompactJson(root, output);
        return output.ToString();
    }

    private static void WriteCompactJson(JsonElement element, StringBuilder output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!firstProperty) output.Append(',');
                    output.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    WriteCompactJson(property.Value, output);
                    firstProperty = false;
                }
                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) output.Append(',');
                    WriteCompactJson(item, output);
                    firstItem = false;
                }
                output.Append(']');
                break;
            default:
                output.Append(element.GetRawText());
                break;
        }
    }

    internal static int CountTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateUnicode(text);
        return TokenEncoding.CountTokens(text);
    }

    internal static McpResponseFormatResult Format(
        string text,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null,
        int startOffset = 0,
        string responsePrefix = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(responsePrefix);
        if (!McpResponseBudgetLimits.IsPublicBudget(maxResponseBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), maxResponseBytes,
                $"The response budget must be between {McpResponseBudgetLimits.MinimumBytes} and {McpResponseBudgetLimits.MaximumBytes} bytes.");
        }

        if (maxResponseTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseTokens), maxResponseTokens, "The token budget must be positive.");
        }

        ValidateUnicode(text);
        ValidateUnicode(responsePrefix);
        var boundaries = GetUnitBoundaries(text);
        ValidateOffset(text, startOffset, boundaries);

        var remaining = text[startOffset..];
        if (Fits(responsePrefix + remaining, maxResponseBytes, maxResponseTokens))
        {
            return Success(responsePrefix + remaining, isTruncated: false, nextOffset: null, omittedBytes: 0, hint: null);
        }

        var best = (Text: (string?)null, Offset: 0, OmittedBytes: 0, Hint: (string?)null);
        foreach (var nextOffset in boundaries)
        {
            if (nextOffset <= startOffset) continue;
            if (nextOffset >= text.Length)
            {
                break;
            }

            var prefix = text[startOffset..nextOffset];
            var omittedBytes = Encoding.UTF8.GetByteCount(text[nextOffset..]);
            var hint = CreateContinuationHint(nextOffset, omittedBytes);
            var candidate = $"{responsePrefix}{prefix}\n{hint}";
            if (Fits(candidate, maxResponseBytes, maxResponseTokens))
            {
                best = (candidate, nextOffset, omittedBytes, hint);
            }
        }

        if (best.Text is null)
        {
            var firstEnd = boundaries.FirstOrDefault(boundary => boundary > startOffset, text.Length);
            var firstUnit = text[startOffset..firstEnd];
            var omittedBytes = Encoding.UTF8.GetByteCount(text[firstEnd..]);
            var minimumText = responsePrefix + (omittedBytes == 0
                ? firstUnit
                : $"{firstUnit}\n{CreateContinuationHint(firstEnd, omittedBytes)}");
            var minimumBytes = Math.Max(McpResponseBudgetLimits.MinimumBytes, Encoding.UTF8.GetByteCount(minimumText));
            var minimumTokens = TokenEncoding.CountTokens(minimumText);
            var canRetryWithLargerBudget = minimumBytes > maxResponseBytes
                && minimumBytes <= McpResponseBudgetLimits.MaximumBytes;
            var recoveryHint = minimumBytes > McpResponseBudgetLimits.MaximumBytes
                ? $"recovery: narrow the query to use complete units within {McpResponseBudgetLimits.MaximumBytes} bytes."
                : canRetryWithLargerBudget
                    ? $"retry: repeat with maxResponseBytes={minimumBytes} and maxResponseTokens at least {minimumTokens}."
                    : $"recovery: repeat with maxResponseTokens at least {minimumTokens}.";
            var errorText = $"{responsePrefix}RESPONSE_BUDGET_TOO_SMALL\nminimumResponseBytes: {minimumBytes}\nminimumResponseTokens: {minimumTokens}\n{recoveryHint}";
            var errorTokenCount = TokenEncoding.CountTokens(errorText);
            if (maxResponseTokens is { } errorTokenBudget && errorTokenCount > errorTokenBudget)
            {
                // Preserve exact retry minima when the full explanatory hint does not fit.
                // Failure metadata can itself consume the caller's entire token allowance.
                // Keep the recovery code and exact minima; the larger retry restores full metadata.
                errorText = $"RESPONSE_BUDGET_TOO_SMALL\nminimumResponseBytes: {minimumBytes}\nminimumResponseTokens: {minimumTokens}";
                errorTokenCount = TokenEncoding.CountTokens(errorText);
                if (errorTokenCount > errorTokenBudget)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maxResponseTokens),
                        maxResponseTokens,
                        $"The token budget cannot represent RESPONSE_BUDGET_TOO_SMALL with exact retry minima; at least {errorTokenCount} tokens are required.");
                }
            }

            return new McpResponseFormatResult(
                errorText,
                Encoding.UTF8.GetByteCount(errorText),
                errorTokenCount,
                IsTruncated: false,
                ErrorCode: "RESPONSE_BUDGET_TOO_SMALL",
                MinimumResponseBytes: minimumBytes,
                MinimumResponseTokens: minimumTokens,
                NextOffset: null,
                OmittedUtf8Bytes: Encoding.UTF8.GetByteCount(remaining),
                ContinuationHint: null,
                CanRetryWithLargerResponseBudget: canRetryWithLargerBudget,
                RecoveryHint: recoveryHint);
        }

        return Success(best.Text, isTruncated: true, best.Offset, best.OmittedBytes, best.Hint);
    }

    private static bool Fits(string text, int maxBytes, int? maxTokens) =>
        Encoding.UTF8.GetByteCount(text) <= maxBytes
        && (maxTokens is null || TokenEncoding.CountTokens(text) <= maxTokens.Value);

    private static McpResponseFormatResult Success(
        string text,
        bool isTruncated,
        int? nextOffset,
        int omittedBytes,
        string? hint) =>
        new(
            text,
            Encoding.UTF8.GetByteCount(text),
            TokenEncoding.CountTokens(text),
            isTruncated,
            ErrorCode: null,
            MinimumResponseBytes: null,
            MinimumResponseTokens: null,
            nextOffset,
            omittedBytes,
            hint,
            CanRetryWithLargerResponseBudget: false,
            RecoveryHint: null);

    private static string CreateContinuationHint(int nextOffset, int omittedBytes) =>
        $"[Truncated; continue at UTF-16 offset {nextOffset}; {omittedBytes} UTF-8 bytes omitted.]";

    private static List<int> GetUnitBoundaries(string text)
    {
        var jsonBoundaries = GetJsonUnitBoundaries(text);
        if (jsonBoundaries is not null) return jsonBoundaries;

        var boundaries = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n') boundaries.Add(index + 1);
        }
        if (boundaries[^1] != text.Length) boundaries.Add(text.Length);
        return boundaries;
    }

    private static List<int>? GetJsonUnitBoundaries(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.IsEmpty || trimmed[0] is not ('{' or '[')) return null;

        var utf8 = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(utf8);
        var boundaries = new List<int> { 0 };
        var previousToken = JsonTokenType.None;
        var previousByteOffset = 0;
        var characterOffset = 0;
        try
        {
            while (reader.Read())
            {
                // Properties retain their complete primitive value. Container openings
                // stay with the property name, and empty containers remain atomic.
                var startsUnit = reader.TokenType switch
                {
                    JsonTokenType.PropertyName => true,
                    JsonTokenType.EndObject or JsonTokenType.EndArray =>
                        previousToken is not (JsonTokenType.StartObject or JsonTokenType.StartArray),
                    _ => previousToken != JsonTokenType.PropertyName,
                };
                var byteOffset = checked((int)reader.TokenStartIndex);
                if (startsUnit && byteOffset > previousByteOffset)
                {
                    // Token starts cannot bisect UTF-8 scalars or JSON escape sequences.
                    // Map incrementally so indexing a Unicode payload remains linear.
                    characterOffset += Encoding.UTF8.GetCharCount(utf8.AsSpan(previousByteOffset, byteOffset - previousByteOffset));
                    boundaries.Add(characterOffset);
                    previousByteOffset = byteOffset;
                }
                previousToken = reader.TokenType;
            }
        }
        catch (JsonException)
        {
            // Malformed JSON and ordinary text keep the newline-delimited contract.
            return null;
        }

        if (boundaries[^1] != text.Length) boundaries.Add(text.Length);
        return boundaries;
    }

    private static void ValidateOffset(string text, int startOffset, List<int> boundaries)
    {
        if (startOffset < 0 || startOffset > text.Length || boundaries.BinarySearch(startOffset) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }
    }

    private static void ValidateUnicode(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                {
                    throw new ArgumentException("Text must contain valid Unicode scalar values.", nameof(text));
                }

                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                throw new ArgumentException("Text must contain valid Unicode scalar values.", nameof(text));
            }
        }
    }
}

internal sealed record McpResponseFormatResult(
    string Text,
    int Utf8Bytes,
    int TokenCount,
    bool IsTruncated,
    string? ErrorCode,
    int? MinimumResponseBytes,
    int? MinimumResponseTokens,
    int? NextOffset,
    int OmittedUtf8Bytes,
    string? ContinuationHint,
    bool CanRetryWithLargerResponseBudget,
    string? RecoveryHint);
