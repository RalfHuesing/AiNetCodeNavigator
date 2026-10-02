using System.Globalization;
using System.Text;
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

    private static bool IsResponseMetadata(string line) =>
        line.StartsWith("snapshotId=", StringComparison.Ordinal)
        || line.StartsWith("analyzedScope=", StringComparison.Ordinal)
        || line.StartsWith("analysisCompleteness=", StringComparison.Ordinal)
        || line.StartsWith("resultContinuation=", StringComparison.Ordinal)
        || line.StartsWith("omissions=", StringComparison.Ordinal)
        || line.StartsWith("nextAction: ", StringComparison.Ordinal)
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
