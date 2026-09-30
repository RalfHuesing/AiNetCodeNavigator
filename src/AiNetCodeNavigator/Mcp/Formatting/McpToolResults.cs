using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp.Formatting;

internal static class McpToolResults
{
    internal const string SuccessStatusPrefix = "Status: operation=ok, completeness=complete\n";
    internal const string TruncatedSuccessStatusPrefix = "Status: operation=ok, completeness=truncated\n";
    internal const string ErrorStatusPrefix = "Status: operation=error, completeness=not_applicable\n";
    internal const string LoadingStatusPrefix = "Status: operation=retry, completeness=not_applicable\n";

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
            var truncatedProjection = McpResponseFormatter.Format(
                text,
                maxResponseBytes,
                maxResponseTokens,
                responsePrefix: TruncatedSuccessStatusPrefix);
            if (truncatedProjection.ErrorCode is null)
            {
                if (truncatedProjection.IsTruncated && structuredContent.HasValue)
                {
                    throw new InvalidOperationException("Structured content cannot accompany a truncated text result.");
                }

                return Create(truncatedProjection.Text, isError: false, structuredContent);
            }

            var completeText = SuccessStatusPrefix + text;
            var completeBytes = Encoding.UTF8.GetByteCount(completeText);
            var completeTokens = McpResponseFormatter.CountTokens(completeText);
            var truncatedMinimumBytes = truncatedProjection.MinimumResponseBytes ?? int.MaxValue;
            var completeProjectionFitsPublicMaximum = completeBytes <= McpResponseBudgetLimits.MaximumBytes;
            var truncatedProjectionFitsPublicMaximum = truncatedMinimumBytes <= McpResponseBudgetLimits.MaximumBytes;

            var retryProjection = completeProjectionFitsPublicMaximum
                && (!truncatedProjectionFitsPublicMaximum || completeBytes <= truncatedMinimumBytes)
                ? formatted with
                {
                    MinimumResponseBytes = completeBytes,
                    MinimumResponseTokens = completeTokens,
                    CanRetryWithLargerResponseBudget = completeBytes > maxResponseBytes,
                    RecoveryHint = completeBytes > maxResponseBytes
                        ? $"retry: repeat with maxResponseBytes={completeBytes} and maxResponseTokens at least {completeTokens}."
                        : $"recovery: repeat with maxResponseTokens at least {completeTokens}."
                }
                : truncatedProjection;

            if (!completeProjectionFitsPublicMaximum && !truncatedProjectionFitsPublicMaximum)
            {
                retryProjection = truncatedProjection with
                {
                    CanRetryWithLargerResponseBudget = false,
                    RecoveryHint = $"recovery: narrow the query to fit complete response units within {McpResponseBudgetLimits.MaximumBytes} bytes."
                };
            }

            return BudgetTooSmall(retryProjection, maxResponseBytes, maxResponseTokens);
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
        return FormatFailure(string.Join("\n", lines), context, maxResponseBytes, maxResponseTokens);
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
        string? context = null,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null) =>
        Recoverable("INVALID_ARGUMENT", message, nextAction, context: context, fieldPath: fieldPath,
            maxResponseBytes: maxResponseBytes, maxResponseTokens: maxResponseTokens);

    internal static CallToolResult Loading(
        string message = "The server is still loading the workspace.",
        string nextAction = "Wait briefly and repeat the same call.",
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextAction);
        var visibleMessage = LimitLoadingMessage(message);
        var formatted = McpResponseFormatter.Format(
            $"nextAction: {nextAction}\n[INFO]: {visibleMessage}",
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: LoadingStatusPrefix);
        if (formatted.ErrorCode is not null)
        {
            return BudgetTooSmall(formatted, maxResponseBytes, maxResponseTokens);
        }

        if (formatted.IsTruncated && !formatted.Text.Contains($"nextAction: {nextAction}", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "The response budget cannot represent the loading retry instruction.");
        }

        return Create(formatted.Text, isError: false);
    }

    internal static CallToolResult BudgetTooSmall(
        McpResponseFormatResult result,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.ErrorCode, "RESPONSE_BUDGET_TOO_SMALL", StringComparison.Ordinal))
        {
            throw new ArgumentException("The response must carry RESPONSE_BUDGET_TOO_SMALL.", nameof(result));
        }

        if (result.Text.StartsWith(ErrorStatusPrefix, StringComparison.Ordinal)
            && result.Utf8Bytes <= maxResponseBytes
            && (maxResponseTokens is null || result.TokenCount <= maxResponseTokens.Value))
        {
            return Create(result.Text, isError: true);
        }

        var body = string.Join("\n",
            result.ErrorCode,
            $"minimumResponseBytes: {result.MinimumResponseBytes}",
            $"minimumResponseTokens: {result.MinimumResponseTokens}",
            result.RecoveryHint);
        var error = McpResponseFormatter.Format(
            body,
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: ErrorStatusPrefix);
        if (error.IsTruncated)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "The response budget cannot represent the complete budget error envelope.");
        }

        return Create(error.Text, isError: true);
    }

    private static CallToolResult FormatFailure(
        string requiredBody,
        string? optionalContext,
        int maxResponseBytes,
        int? maxResponseTokens)
    {
        var required = McpResponseFormatter.Format(
            requiredBody,
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: ErrorStatusPrefix);
        if (required.ErrorCode is not null)
        {
            return BudgetTooSmall(required, maxResponseBytes, maxResponseTokens);
        }

        if (required.IsTruncated)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "The response budget cannot represent all required error fields.");
        }

        if (string.IsNullOrWhiteSpace(optionalContext))
        {
            return Create(required.Text, isError: true);
        }

        var withContext = McpResponseFormatter.Format(
            $"{requiredBody}\ncontext: {optionalContext}",
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: ErrorStatusPrefix);
        if (withContext.ErrorCode is not null
            || withContext.IsTruncated && !withContext.Text.StartsWith(required.Text, StringComparison.Ordinal))
        {
            return Create(required.Text, isError: true);
        }

        return Create(withContext.Text, isError: true);
    }

    private static string LimitLoadingMessage(string message)
    {
        const int maxRunes = 64;
        var runes = message.EnumerateRunes().Take(maxRunes + 1).ToArray();
        return runes.Length <= maxRunes
            ? message
            : string.Concat(runes.Take(maxRunes).Select(static rune => rune.ToString())) + "…";
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
