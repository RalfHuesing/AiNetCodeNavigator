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
        Assert.True(result.CanRetryWithLargerResponseBudget);
        Assert.False(result.IsTruncated);
        Assert.Null(result.NextOffset);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", result.Text, StringComparison.Ordinal);
        Assert.Contains($"minimumResponseBytes: {result.MinimumResponseBytes}", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_UnitWhoseRetryExceedsPublicMaximum_OffersSupportedRecovery()
    {
        var text = new string('x', 65_490) + "\n" + new string('y', 100);

        var atMaximum = McpResponseFormatter.Format(new string('x', 65_536), 65_536);
        var result = McpResponseFormatter.Format(text, 65_536);
        var overMaximum = McpResponseFormatter.Format(new string('x', 65_537), 65_536);

        Assert.False(atMaximum.IsTruncated);
        Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", result.ErrorCode);
        Assert.True(result.MinimumResponseBytes > McpResponseBudgetLimits.MaximumBytes);
        Assert.False(result.CanRetryWithLargerResponseBudget);
        Assert.Contains("narrow the query", result.RecoveryHint, StringComparison.Ordinal);
        Assert.DoesNotContain("retry: repeat with maxResponseBytes=", result.Text, StringComparison.Ordinal);
        Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", overMaximum.ErrorCode);
        Assert.False(overMaximum.CanRetryWithLargerResponseBudget);
    }

    [Fact]
    public void Format_RejectsContinuationOffsetInsideAtomicLine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => McpResponseFormatter.Format("alpha\nbeta", 512, startOffset: 1));
    }

    [Fact]
    public void Format_BudgetErrorHonorsTokenCapOrRejectsUnrepresentableError()
    {
        var text = new string('x', 600);
        var error = McpResponseFormatter.Format(text, 512);

        var exact = McpResponseFormatter.Format(text, 512, error.TokenCount);

        Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", exact.ErrorCode);
        Assert.Equal(error.TokenCount, exact.TokenCount);
        Assert.True(exact.TokenCount <= error.TokenCount);
        var compact = McpResponseFormatter.Format(text, 512, maxResponseTokens: 80);
        Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", compact.ErrorCode);
        Assert.Contains("minimumResponseBytes:", compact.Text, StringComparison.Ordinal);
        Assert.Contains("minimumResponseTokens:", compact.Text, StringComparison.Ordinal);
        Assert.InRange(compact.TokenCount, 0, 80);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            McpResponseFormatter.Format(text, 512, maxResponseTokens: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            McpResponseFormatter.Format("hello world from the navigator", 512, maxResponseTokens: 1));
    }

    [Fact]
    public void Format_AdvertisesByteMinimumWithinPublicBudgetFloorForTokenRecovery()
    {
        var text = new string('x', 300) + "\n" + new string('y', 300);

        var recovery = McpResponseFormatter.Format(text, 65_536, maxResponseTokens: 50);

        Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", recovery.ErrorCode);
        Assert.Equal(McpResponseBudgetLimits.MinimumBytes, recovery.MinimumResponseBytes);
        Assert.True(recovery.MinimumResponseTokens > 50);
        var retry = McpResponseFormatter.Format(text, recovery.MinimumResponseBytes!.Value, recovery.MinimumResponseTokens);
        Assert.Null(retry.ErrorCode);
        Assert.True(retry.IsTruncated);
        Assert.InRange(retry.Utf8Bytes, 0, recovery.MinimumResponseBytes.Value);
        Assert.InRange(retry.TokenCount, 0, recovery.MinimumResponseTokens.Value);
    }

    [Fact]
    public void Format_CompactBudgetErrorKeepsExactMetadataAwareMinimaAtEightyAndOneHundredTwentyTokens()
    {
        var text = "{\n  \"items\": [\n" + string.Join(",\n", Enumerable.Range(0, 20).Select(index => $"    \"item-{index:D2}\"")) + "\n  ]\n}";
        var responsePrefix = "Status: operation=ok\nsnapshotId=source:0123456789abcdef01234567\n"
            + $"analyzedScope=findSymbol(pattern={new string('s', 600)})\nanalysisCompleteness=complete\n"
            + "resultContinuation=none\nomissions=none\n";

        foreach (var tokenBudget in new[] { 80, 120 })
        {
            var failure = McpResponseFormatter.Format(text, 65_536, tokenBudget, responsePrefix: responsePrefix);

            Assert.Equal("RESPONSE_BUDGET_TOO_SMALL", failure.ErrorCode);
            Assert.Contains("minimumResponseBytes:", failure.Text, StringComparison.Ordinal);
            Assert.Contains("minimumResponseTokens:", failure.Text, StringComparison.Ordinal);
            Assert.InRange(failure.TokenCount, 0, tokenBudget);

            var retry = McpResponseFormatter.Format(text, failure.MinimumResponseBytes!.Value,
                failure.MinimumResponseTokens!.Value, responsePrefix: responsePrefix);
            Assert.Null(retry.ErrorCode);
            Assert.True(retry.IsTruncated);
            Assert.InRange(retry.Utf8Bytes, 0, failure.MinimumResponseBytes.Value);
            Assert.InRange(retry.TokenCount, 0, failure.MinimumResponseTokens.Value);
        }
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
