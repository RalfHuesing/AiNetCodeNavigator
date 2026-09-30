using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Mcp.Formatting;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class McpToolResultsTests
{
    [Fact]
    public void Success_ReturnsStatusTextAndOptionalStructuredContent()
    {
        using var structured = JsonDocument.Parse("{\"count\":3}");

        var result = McpToolResults.Success(
            "Three symbols found.",
            structuredContent: structured.RootElement.Clone());

        Assert.False(result.IsError ?? false);
        Assert.Equal("Status: operation=ok, completeness=complete\nThree symbols found.", TextOf(result));
        Assert.Equal(3, result.StructuredContent?.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ErrorAndRecoverableResults_AreErrorsWithCodeAndNextAction()
    {
        var fatal = McpToolResults.Error("ANALYSIS_FAILED", "Analysis could not complete.", nextAction: "Retry once.");
        var recoverable = McpToolResults.Recoverable("SYMBOL_NOT_FOUND", "No symbol matched.", nextAction: "Search with find_symbol.");

        Assert.True(fatal.IsError);
        Assert.Contains("Status: operation=error, completeness=not_applicable", TextOf(fatal), StringComparison.Ordinal);
        Assert.Contains("[ERROR]: ANALYSIS_FAILED: Analysis could not complete.", TextOf(fatal), StringComparison.Ordinal);
        Assert.Contains("nextAction: Retry once.", TextOf(fatal), StringComparison.Ordinal);
        Assert.True(recoverable.IsError);
        Assert.Contains("SYMBOL_NOT_FOUND", TextOf(recoverable), StringComparison.Ordinal);
        Assert.Contains("nextAction: Search with find_symbol.", TextOf(recoverable), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidArgument_IsErrorWithFieldPathAndCorrection()
    {
        var result = McpToolResults.InvalidArgument(
            "The path must be relative.",
            fieldPath: "$.filePath",
            nextAction: "Pass a path relative to the solution.");

        Assert.True(result.IsError);
        Assert.Contains("INVALID_ARGUMENT", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("fieldPath: $.filePath", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("nextAction: Pass a path relative to the solution.", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_IsSuccessfulRetryState()
    {
        var result = McpToolResults.Loading();

        Assert.False(result.IsError ?? false);
        Assert.Contains("Status: operation=retry, completeness=not_applicable", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("nextAction: Wait briefly and repeat the same call.", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetTooSmall_UsesErrorClassificationAndRetainsRecoveryDetails()
    {
        var projection = McpResponseFormatter.Format(
            new string('x', 600),
            512,
            responsePrefix: McpToolResults.ErrorStatusPrefix);

        var result = McpToolResults.BudgetTooSmall(projection);
        var text = TextOf(result);

        Assert.True(result.IsError);
        Assert.Contains("Status: operation=error, completeness=not_applicable", text, StringComparison.Ordinal);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", text, StringComparison.Ordinal);
        Assert.Contains("minimumResponseBytes:", text, StringComparison.Ordinal);
        Assert.Contains("retry:", text, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 512);
    }

    [Fact]
    public void BudgetTooSmall_RechecksAnExistingErrorEnvelopeAgainstRequestedTokenBudget()
    {
        var projection = McpResponseFormatter.Format(
            new string('x', 600),
            512,
            responsePrefix: McpToolResults.ErrorStatusPrefix);

        Assert.Throws<ArgumentOutOfRangeException>(() => McpToolResults.BudgetTooSmall(
            projection,
            maxResponseBytes: 512,
            maxResponseTokens: 1));
    }

    [Fact]
    public void Success_RespectsUtf8ByteBudgetIncludingStatusBlock()
    {
        var text = string.Join("\n", Enumerable.Repeat("Grüße 🌍", 100));

        var result = McpToolResults.Success(text, maxResponseBytes: 512);
        var visibleText = TextOf(result);

        Assert.False(result.IsError ?? false);
        Assert.True(Encoding.UTF8.GetByteCount(visibleText) <= 512);
        Assert.Contains(McpToolResults.TruncatedSuccessStatusPrefix.TrimEnd(), visibleText, StringComparison.Ordinal);
        Assert.Contains("Truncated; continue at UTF-16 offset", visibleText, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_AtUtf8BudgetBoundarySucceedsAndOneByteOverReturnsBudgetError()
    {
        var prefixBytes = Encoding.UTF8.GetByteCount(McpToolResults.SuccessStatusPrefix);
        var exactText = new string('a', 512 - prefixBytes);
        var overText = exactText + "a";

        var exact = McpToolResults.Success(exactText, maxResponseBytes: 512);
        var over = McpToolResults.Success(overText, maxResponseBytes: 512);

        Assert.False(exact.IsError ?? false);
        Assert.Equal(512, Encoding.UTF8.GetByteCount(TextOf(exact)));
        Assert.True(over.IsError);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", TextOf(over), StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(TextOf(over)) <= 512);
    }

    [Fact]
    public void Success_BudgetRecoveryUsesSuccessProjectionAtPublicMaximum()
    {
        var text = new string('x', 65_492);

        var result = McpToolResults.Success(text, maxResponseBytes: 65_535);
        var response = TextOf(result);

        Assert.True(result.IsError);
        Assert.Contains("minimumResponseBytes: 65536", response, StringComparison.Ordinal);
        Assert.Contains("retry: repeat with maxResponseBytes=65536", response, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(response) <= 65_535);
        Assert.False(McpToolResults.Success(text, maxResponseBytes: 65_536).IsError ?? false);
    }

    [Fact]
    public void Success_BudgetRecoveryReportsExactByteAndTokenMinima()
    {
        var text = new string('w', 700);
        var exactProjection = McpToolResults.SuccessStatusPrefix + text;
        var tokenMinimum = McpResponseFormatter.CountTokens(exactProjection);

        var byteLimited = McpToolResults.Success(text, maxResponseBytes: 512);
        var tokenLimited = McpToolResults.Success(text, maxResponseBytes: 4096, maxResponseTokens: tokenMinimum - 1);

        Assert.Contains("minimumResponseBytes: 744", TextOf(byteLimited), StringComparison.Ordinal);
        Assert.Contains($"minimumResponseTokens: {tokenMinimum}", TextOf(tokenLimited), StringComparison.Ordinal);
        Assert.Contains($"minimumResponseBytes: {Encoding.UTF8.GetByteCount(exactProjection)}", TextOf(tokenLimited), StringComparison.Ordinal);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(tokenLimited)) <= tokenMinimum - 1);
    }

    [Fact]
    public void Recoverable_LargeContextKeepsCorrectionWithinBudget()
    {
        var context = string.Concat(Enumerable.Repeat("ctx\n", 200));

        var result = McpToolResults.Recoverable(
            "SYMBOL_NOT_FOUND",
            "No symbol matched.",
            "Search with find_symbol.",
            context: context,
            maxResponseBytes: 512);
        var response = TextOf(result);

        Assert.True(result.IsError);
        Assert.Contains("SYMBOL_NOT_FOUND", response, StringComparison.Ordinal);
        Assert.Contains("nextAction: Search with find_symbol.", response, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(response) <= 512);
    }

    [Fact]
    public void InvalidArgument_LargeContextKeepsFieldPathAndCorrectionWithinBudgets()
    {
        var context = string.Concat(Enumerable.Repeat("ctx\n", 200));

        var result = McpToolResults.InvalidArgument(
            "The path must be relative.",
            fieldPath: "$.filePath",
            nextAction: "Pass a path relative to the solution.",
            context: context,
            maxResponseBytes: 512,
            maxResponseTokens: 120);
        var response = TextOf(result);

        Assert.True(result.IsError);
        Assert.Contains("fieldPath: $.filePath", response, StringComparison.Ordinal);
        Assert.Contains("nextAction: Pass a path relative to the solution.", response, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(response) <= 512);
        Assert.True(McpResponseFormatter.CountTokens(response) <= 120);
    }

    [Fact]
    public void Recoverable_UnrepresentableRequiredActionIsRejectedAtomically()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => McpToolResults.Recoverable(
            "INVALID_ARGUMENT",
            "The argument is invalid.",
            new string('x', 700),
            fieldPath: "$.symbolIdentifier",
            maxResponseBytes: 512));

        Assert.Throws<ArgumentOutOfRangeException>(() => McpToolResults.Recoverable(
            "INVALID_ARGUMENT",
            "The argument is invalid.",
            "Search again.",
            maxResponseBytes: 512,
            maxResponseTokens: 5));
    }

    [Fact]
    public void Loading_AppliesDefaultBudgetToLongMessageAndRetainsRetryAction()
    {
        var message = string.Concat(Enumerable.Repeat("Grüße 🌍\n", 10_000));

        var result = McpToolResults.Loading(message, "Wait and retry.");
        var response = TextOf(result);

        Assert.False(result.IsError ?? false);
        Assert.StartsWith(McpToolResults.LoadingStatusPrefix, response, StringComparison.Ordinal);
        Assert.Contains("nextAction: Wait and retry.", response, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(response) <= McpResponseBudgetLimits.DefaultBytes);
    }

    [Fact]
    public void Loading_AppliesRequestedByteAndTokenBudgets()
    {
        var message = string.Concat(Enumerable.Repeat("Grüße 🌍\n", 100));

        var result = McpToolResults.Loading(message, "Wait and retry.", maxResponseBytes: 512, maxResponseTokens: 120);
        var response = TextOf(result);

        Assert.False(result.IsError ?? false);
        Assert.Contains("nextAction: Wait and retry.", response, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(response) <= 512);
        Assert.True(McpResponseFormatter.CountTokens(response) <= 120);
    }

    [Fact]
    public void Loading_UnrepresentableRetryInstructionReturnsBudgetError()
    {
        var result = McpToolResults.Loading(nextAction: new string('x', 70_000));
        var response = TextOf(result);

        Assert.True(result.IsError);
        Assert.StartsWith(McpToolResults.ErrorStatusPrefix, response, StringComparison.Ordinal);
        Assert.Contains("RESPONSE_BUDGET_TOO_SMALL", response, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(response) <= McpResponseBudgetLimits.DefaultBytes);
    }

    [Fact]
    public void Success_TruncatedTextDoesNotClaimCompleteness()
    {
        var text = string.Join("\n", Enumerable.Repeat("entry", 100));

        var result = McpToolResults.Success(text, maxResponseBytes: 512);
        var visibleText = TextOf(result);

        Assert.False(result.IsError ?? false);
        Assert.Contains(McpToolResults.TruncatedSuccessStatusPrefix.TrimEnd(), visibleText, StringComparison.Ordinal);
        Assert.DoesNotContain(McpToolResults.SuccessStatusPrefix, visibleText, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_RejectsStructuredContentWhenTextWasTruncated()
    {
        using var structured = JsonDocument.Parse("{\"complete\":true}");
        var text = string.Join("\n", Enumerable.Repeat("item", 100));

        Assert.Throws<InvalidOperationException>(() => McpToolResults.Success(
            text,
            structuredContent: structured.RootElement.Clone(),
            maxResponseBytes: 512));
    }

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
