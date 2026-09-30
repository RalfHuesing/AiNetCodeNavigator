using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp.Formatting;

internal static class McpToolResults
{
    internal const string SuccessStatusPrefix = "Status: operation=ok, completeness=complete\n";
    internal const string TruncatedSuccessStatusPrefix = "Status: operation=ok, completeness=truncated\n";
    internal const string ErrorStatusPrefix = "Status: operation=error, completeness=not_applicable\n";

    internal static CallToolResult Success(
        string text,
        JsonElement? structuredContent = null,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var formatted = McpResponseFormatter.Format(
            text,
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: SuccessStatusPrefix);
        if (formatted.ErrorCode is null && formatted.IsTruncated)
        {
            formatted = McpResponseFormatter.Format(
                text,
                maxResponseBytes,
                maxResponseTokens,
                responsePrefix: TruncatedSuccessStatusPrefix);
        }

        if (formatted.ErrorCode is not null)
        {
            var budgetError = McpResponseFormatter.Format(
                text,
                maxResponseBytes,
                maxResponseTokens,
                responsePrefix: ErrorStatusPrefix);
            return BudgetTooSmall(budgetError);
        }

        if (formatted.IsTruncated && structuredContent.HasValue)
        {
            throw new InvalidOperationException("Structured content cannot accompany a truncated text result.");
        }

        return Create(formatted.Text, isError: false, structuredContent);
    }

    internal static CallToolResult Error(
        string code,
        string message,
        string? context = null,
        string? nextAction = null,
        string? fieldPath = null,
        int? requestedBytes = null,
        int? minimumResponseBytes = null,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null)
    {
        ValidateError(code, message);
        var lines = new List<string> { $"[ERROR]: {code}: {message}" };
        AddOptionalLine(lines, "context", context);
        AddOptionalLine(lines, "fieldPath", fieldPath);
        if (requestedBytes is { } requested) lines.Add($"requestedBytes: {requested}");
        if (minimumResponseBytes is { } minimum)
        {
            lines.Add($"minimumResponseBytes: {minimum}");
            var requestedBudget = requestedBytes ?? McpResponseBudgetLimits.MinimumBytes;
            var retryBudget = Math.Max(minimum, McpResponseBudgetLimits.MinimumBytes);
            lines.Add(minimum > McpResponseBudgetLimits.MaximumBytes
                ? $"recovery: narrow the query to fit within {McpResponseBudgetLimits.MaximumBytes} bytes."
                : minimum > requestedBudget
                    ? $"retry: repeat with maxResponseBytes={retryBudget}."
                    : "recovery: increase the token budget if set, or narrow the query.");
        }

        AddOptionalLine(lines, "nextAction", nextAction);
        return FormatFailure(string.Join("\n", lines), maxResponseBytes, maxResponseTokens);
    }

    internal static CallToolResult Recoverable(
        string code,
        string message,
        string nextAction,
        string? context = null,
        string? fieldPath = null,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null) =>
        Error(code, message, context, nextAction, fieldPath, maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

    internal static CallToolResult InvalidArgument(
        string message,
        string fieldPath,
        string nextAction,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null) =>
        Recoverable("INVALID_ARGUMENT", message, nextAction, fieldPath: fieldPath,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

    internal static CallToolResult Loading(
        string message = "The server is still loading the workspace.",
        string nextAction = "Wait briefly and repeat the same call.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextAction);
        return Create(
            $"Status: operation=retry, completeness=not_applicable\n[INFO]: {message}\nnextAction: {nextAction}",
            isError: false);
    }

    internal static CallToolResult BudgetTooSmall(McpResponseFormatResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.ErrorCode, "RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
        {
            throw new ArgumentException("The response must carry RESPONSE_BUDGET_TOO_SMALL.", nameof(result));
        }

        if (!result.Text.StartsWith(ErrorStatusPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The budget error must contain the standard error status block.", nameof(result));
        }

        return Create(result.Text, isError: true);
    }

    private static CallToolResult FormatFailure(string body, int maxResponseBytes, int? maxResponseTokens)
    {
        var formatted = McpResponseFormatter.Format(
            body,
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: ErrorStatusPrefix);
        return formatted.ErrorCode is not null
            ? BudgetTooSmall(formatted)
            : Create(formatted.Text, isError: true);
    }

    private static CallToolResult Create(string text, bool isError, JsonElement? structuredContent = null) => new()
    {
        IsError = isError,
        Content = [new TextContentBlock { Text = text }],
        StructuredContent = structuredContent,
    };

    private static void ValidateError(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }

    private static void AddOptionalLine(List<string> lines, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{name}: {value}");
    }
}
