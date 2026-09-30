using System.Text;
using AiNetCodeNavigator.Mcp.Formatting;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class McpFormattingTests
{
    [Fact]
    public void PublicByteBudget_UsesInclusiveMinimumAndMaximum()
    {
        Assert.Equal(16_384, McpResponseBudgetLimits.DefaultBytes);
        Assert.False(McpResponseBudgetLimits.IsPublicBudget(511));
        Assert.True(McpResponseBudgetLimits.IsPublicBudget(512));
        Assert.True(McpResponseBudgetLimits.IsPublicBudget(65_536));
        Assert.False(McpResponseBudgetLimits.IsPublicBudget(65_537));
    }

    [Fact]
    public void Format_ExactUtf8ByteBudget_IncludesWholeUnicodeText()
    {
        var text = string.Join("\n", Enumerable.Repeat("Grüße 🌍", 40));
        var budget = Encoding.UTF8.GetByteCount(text);

        var result = McpResponseFormatter.Format(text, budget);

        Assert.Equal(text, result.Text);
        Assert.Equal(budget, result.Utf8Bytes);
        Assert.False(result.IsTruncated);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Format_OneByteBelowExactBudget_ReportsContinuationWithoutBreakingUtf8()
    {
        var text = string.Join("\n", Enumerable.Repeat("Grüße 🌍", 40));
        var budget = Encoding.UTF8.GetByteCount(text) - 1;

        var result = McpResponseFormatter.Format(text, budget);

        Assert.True(result.IsTruncated);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.NextOffset);
        Assert.True(result.NextOffset < text.Length);
        Assert.True(result.OmittedUtf8Bytes > 0);
        Assert.Contains($"continue at UTF-16 offset {result.NextOffset}", result.Text, StringComparison.Ordinal);
        Assert.Equal(result.Utf8Bytes, Encoding.UTF8.GetByteCount(result.Text));
        Assert.True(result.Utf8Bytes <= budget);
    }

    [Fact]
    public void Format_ReportsSharpTokenAccounting_AndHonorsTokenBoundary()
    {
        const string text = "hello world from the navigator";
        var tokenCount = McpResponseFormatter.CountTokens(text);

        var exact = McpResponseFormatter.Format(text, 512, tokenCount);
        var tooSmall = McpResponseFormatter.Format(string.Join("\n", Enumerable.Repeat(text, 80)), 4096, 40);

        Assert.Equal(tokenCount, exact.TokenCount);
        Assert.Equal(text, exact.Text);
        Assert.True(tooSmall.TokenCount <= 40);
        Assert.True(tooSmall.IsTruncated);
    }

    [Fact]
    public void Format_AtomicLineExceedingBudget_ReturnsRecoverableMinimumBudgetError()
    {
        var text = new string('x', 600);

        var result = McpResponseFormatter.Format(text, 512);

        Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", result.ErrorCode);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), result.MinimumResponseBytes);
        Assert.False(result.IsTruncated);
        Assert.Null(result.NextOffset);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", result.Text, StringComparison.Ordinal);
        Assert.Contains($"minimumResponseBytes: {result.MinimumResponseBytes}", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ContinuationOffsetResumesAtTheNextCompleteLine()
    {
        var text = string.Join("\n", Enumerable.Range(0, 120).Select(index => $"entry-{index:D3}"));
        var first = McpResponseFormatter.Format(text, 512);

        Assert.True(first.IsTruncated);
        Assert.NotNull(first.NextOffset);

        var second = McpResponseFormatter.Format(text, 512, startOffset: first.NextOffset.Value);

        Assert.Contains("entry-", second.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("entry-000", second.Text, StringComparison.Ordinal);
        Assert.True(second.Utf8Bytes <= 512);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(65_537)]
    public void Format_RejectsPublicByteBudgetsOutsideInclusiveBounds(int maxResponseBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => McpResponseFormatter.Format("ok", maxResponseBytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_RejectsNonPositiveTokenBudgets(int maxResponseTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => McpResponseFormatter.Format("ok", 512, maxResponseTokens));
    }

    [Fact]
    public void Format_RejectsOffsetsInsideSurrogatePairsAndInvalidUnicode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => McpResponseFormatter.Format("🌍", 512, startOffset: 1));
        Assert.Throws<ArgumentException>(() => McpResponseFormatter.Format("\uD800", 512));
    }
}
