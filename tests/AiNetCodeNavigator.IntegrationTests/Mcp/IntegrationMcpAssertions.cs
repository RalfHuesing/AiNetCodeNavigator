using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Symbols;
using ModelContextProtocol.Protocol;
using SharpToken;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

internal static class IntegrationMcpAssertions
{
    private static readonly GptEncoding TokenEncoding = GptEncoding.GetEncoding("cl100k_base");

    internal static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    internal static string BodyOf(string text)
    {
        var lines = text.Split('\n');
        var firstContentLine = lines.Length > 0 && lines[0].StartsWith("Status:", StringComparison.Ordinal) ? 1 : 0;
        while (firstContentLine < lines.Length && IsResponseMetadata(lines[firstContentLine])) firstContentLine++;
        return string.Join("\n", lines.Skip(firstContentLine));
    }

    internal static string[] ReadStableReferences(string text)
    {
        var body = BodyOf(text);
        var references = new List<string>();
        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = ParseJsonWithDiagnostics(body, "Stable-reference JSON payload");
            CollectStableReferences(document.RootElement, references);
            return references.Distinct(StringComparer.Ordinal).ToArray();
        }

        foreach (var rawLine in body.Split('\n'))
        {
            // Rendered tool text can arrive with CRLF line endings. Treat only the
            // terminal carriage return as line structure; keep reference payloads intact.
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            const string handoffMarker = "[handoff: ";
            var handoffStart = line.IndexOf(handoffMarker, StringComparison.Ordinal);
            if (handoffStart >= 0)
            {
                handoffStart += handoffMarker.Length;
                var handoffEnd = line.LastIndexOf(']');
                if (handoffStart < line.Length && line[handoffStart] == '`')
                    AddDelimitedReference(line, handoffStart + 1, references);
                else if (handoffEnd > handoffStart)
                    AddStableReference(line[handoffStart..handoffEnd], references);
            }

            const string mermaidMarker = "handoffId: ";
            var mermaidStart = line.IndexOf(mermaidMarker, StringComparison.Ordinal);
            if (mermaidStart >= 0)
            {
                mermaidStart += mermaidMarker.Length;
                if (mermaidStart < line.Length && line[mermaidStart] == '`')
                    AddDelimitedReference(line, mermaidStart + 1, references);
                else
                {
                    var ownerMarker = line.IndexOf("; targetPath: ", mermaidStart, StringComparison.Ordinal);
                    var mermaidEnd = ownerMarker < 0 ? line.Length : ownerMarker;
                    AddStableReference(line[mermaidStart..mermaidEnd], references);
                }
            }

            var referenceStart = 0;
            while (true)
            {
                var sourceStart = line.IndexOf("src:", referenceStart, StringComparison.Ordinal);
                var assemblyStart = line.IndexOf("asm:", referenceStart, StringComparison.Ordinal);
                var start = sourceStart < 0 ? assemblyStart
                    : assemblyStart < 0 ? sourceStart : Math.Min(sourceStart, assemblyStart);
                if (start < 0) break;
                var openingTick = line.LastIndexOf('`', start);
                if (openingTick >= 0)
                    AddDelimitedReference(line, start, references);
                referenceStart = start + 4;
            }
        }

        return references.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static JsonDocument ParseJsonWithDiagnostics(string json, string context)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"{context}: invalid JSON at line {exception.LineNumber}, byte {exception.BytePositionInLine}. "
                + $"Payload length={json.Length}. Full payload follows:\n{json}");
        }
    }

    internal static async Task<(string Text, int Pages, string FirstPage)> ReadOuterResponsePagesAsync(
        Func<int, int?, string?, Task<CallToolResult>> invoke,
        int maxResponseBytes,
        int? maxResponseTokens,
        string? initialContinuation = null)
    {
        var accumulated = new StringBuilder();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        var continuation = initialContinuation;
        if (continuation is not null) seenContinuations.Add(continuation);
        string? firstPage = null;
        string? snapshotId = null;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            var result = await invoke(maxResponseBytes, maxResponseTokens, continuation);
            var text = TextOf(result);
            firstPage ??= text;
            Assert.False(result.IsError ?? false, $"Outer page {pageNumber} failed: {text}");
            Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, maxResponseBytes);
            if (maxResponseTokens is { } tokens) Assert.InRange(TokenCount(text), 0, tokens);
            var pageSnapshotId = ReadHeader(text, "snapshotId");
            snapshotId ??= pageSnapshotId;
            Assert.Equal(snapshotId, pageSnapshotId);
            Assert.DoesNotContain("operation=running", text, StringComparison.Ordinal);
            Assert.DoesNotContain("operation=retry", text, StringComparison.Ordinal);
            accumulated.Append(BodyOf(text));
            if (!TryReadToken(text, "continuationToken", out var nextContinuation))
                return (accumulated.ToString(), pageNumber + 1, firstPage!);

            Assert.NotEmpty(nextContinuation);
            Assert.All(nextContinuation, character => Assert.True(char.IsAsciiDigit(character)));
            Assert.True(seenContinuations.Add(nextContinuation), "An outer page repeated a continuation token instead of advancing.");
            continuation = nextContinuation;
        }
        throw new Xunit.Sdk.XunitException("The outer response did not reach its final page.");
    }

    internal static void AssertActionableRediscovery(string text)
    {
        Assert.Contains("rediscover", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declaration", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reference", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returned", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectStableReferences(JsonElement element, ICollection<string> references)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject()) CollectStableReferences(property.Value, references);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectStableReferences(item, references);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            AddStableReference(element.GetString(), references);
        }
    }

    private static void AddStableReference(string? value, ICollection<string> references)
    {
        if (value is not null && StableSymbolReferenceCodec.TryParse(value, out _, out _)) references.Add(value);
    }

    private static void AddDelimitedReference(string line, int start, ICollection<string> references)
    {
        var end = line.IndexOf('`', start);
        while (end >= 0)
        {
            var tail = line[(end + 1)..];
            var hasRendererBoundary = tail.Length == 0
                || tail.StartsWith("]", StringComparison.Ordinal)
                || tail.StartsWith(" |", StringComparison.Ordinal)
                || tail.StartsWith(" (", StringComparison.Ordinal);
            if (hasRendererBoundary)
                AddStableReference(line[start..end].Replace("\\|", "|", StringComparison.Ordinal), references);
            end = line.IndexOf('`', end + 1);
        }
    }

    private static bool IsResponseMetadata(string line) =>
        line.StartsWith("snapshotId=", StringComparison.Ordinal)
        || line.StartsWith("omissions=", StringComparison.Ordinal)
        || line.StartsWith("nextAction: ", StringComparison.Ordinal)
        || line.StartsWith("domainNextAction: ", StringComparison.Ordinal)
        || line.StartsWith("continuationToken=", StringComparison.Ordinal);

    internal static bool TryReadToken(string text, string name, out string token)
    {
        var prefix = name + "=";
        var value = text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        token = value is null ? string.Empty : value[prefix.Length..];
        return value is not null;
    }

    internal static string ReadHeader(string text, string name)
    {
        var prefix = name + "=";
        var value = text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? throw new Xunit.Sdk.XunitException($"Missing {name} metadata.") : value[prefix.Length..];
    }

    internal static string ReadItemSection(string text, string selector)
    {
        var lines = text.Split('\n');
        var marker = "## " + selector;
        var start = Array.FindIndex(lines, line => string.Equals(line.TrimEnd('\r'), marker, StringComparison.Ordinal));
        Assert.True(start >= 0, $"Missing result section for '{selector}'. Response: {text}");
        var end = start + 1;
        while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal)) end++;
        return string.Join('\n', lines[start..end]);
    }

    internal static int ReadBudget(string text, string name)
    {
        var prefix = name + ": ";
        var value = text.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(value is not null, $"Missing {name} in response: {text}");
        return int.Parse(value![prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    internal static void AssertBudget(string text, int bytes, int tokens)
    {
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 0, bytes);
        Assert.InRange(TokenCount(text), 0, tokens);
    }

    internal static int TokenCount(string text) => TokenEncoding.CountTokens(text);

    internal static void AssertSuccessWithinBudget(CallToolResult result, int bytes, int tokens)
    {
        var text = TextOf(result);
        Assert.False(result.IsError ?? false, text);
        AssertBudget(text, bytes, tokens);
    }

    internal static void AssertErrorWithinBudget(CallToolResult result, string code, int bytes, int tokens)
    {
        var text = TextOf(result);
        Assert.True(result.IsError ?? false, text);
        Assert.Contains(code, text, StringComparison.Ordinal);
        AssertBudget(text, bytes, tokens);
    }
}
