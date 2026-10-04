using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Models;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp.Formatting;

internal static class McpToolResults
{
    internal const string SuccessStatusPrefix = "Status: operation=ok, completeness=complete\n";
    internal const string TruncatedSuccessStatusPrefix = "Status: operation=ok, completeness=truncated\n";
    internal const string ErrorStatusPrefix = "Status: operation=error, completeness=not_applicable\n";
    internal const string LoadingStatusPrefix = "Status: operation=retry, completeness=not_applicable\n";
    internal const string RunningStatusPrefix = "Status: operation=running, completeness=not_applicable\n";
    internal const string DomainTruncatedMarker = "\u001eNAVIGATION_DOMAIN_TRUNCATED\n";

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

    internal static CallToolResult CompleteOrBudgetTooSmall(
        string text,
        int maxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
        int? maxResponseTokens = null,
        int? minimumResponseBytes = null,
        int? minimumResponseTokens = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var formatted = McpResponseFormatter.Format(
            text,
            maxResponseBytes,
            maxResponseTokens,
            responsePrefix: SuccessStatusPrefix);
        if (formatted.ErrorCode is null && !formatted.IsTruncated)
        {
            return Create(formatted.Text, isError: false);
        }

        var completeBytes = Math.Max(
            McpResponseBudgetLimits.MinimumBytes,
            minimumResponseBytes ?? Encoding.UTF8.GetByteCount(SuccessStatusPrefix + text));
        var completeTokens = minimumResponseTokens ?? McpResponseFormatter.CountTokens(SuccessStatusPrefix + text);
        var retryWithBytes = completeBytes <= McpResponseBudgetLimits.MaximumBytes;
        var recoveryHint = !retryWithBytes
            ? $"recovery: narrow the required result to fit within {McpResponseBudgetLimits.MaximumBytes} bytes."
            : completeBytes > maxResponseBytes
                ? $"retry: repeat with maxResponseBytes={completeBytes} and maxResponseTokens at least {completeTokens}."
                : $"retry: repeat with maxResponseTokens at least {completeTokens}.";
        var failure = new McpResponseFormatResult(
            string.Empty,
            0,
            0,
            IsTruncated: false,
            ErrorCode: "RESPONSE_BUDGET_TOO_SMALL",
            MinimumResponseBytes: completeBytes,
            MinimumResponseTokens: completeTokens,
            NextOffset: null,
            OmittedUtf8Bytes: 0,
            ContinuationHint: null,
            CanRetryWithLargerResponseBudget: retryWithBytes,
            RecoveryHint: recoveryHint);
        return BudgetTooSmall(failure, maxResponseBytes, maxResponseTokens);
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

    internal const string RunningNextAction = "Wait at least 1000 ms, then repeat the same tool, target and query with this operationToken; preserve an active resultCursor and omit continuationToken.";

    internal static (int Bytes, int Tokens) RunningAdmissionMinimum(string operationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationToken);
        var variants = Enum.GetValues<NavigationAnalysisPhase>().Select(phase =>
            RunningControlText(operationToken, new(long.MaxValue, phase), includeCounters: false)).ToArray();
        return (Math.Max(McpResponseBudgetLimits.MinimumBytes, variants.Max(Encoding.UTF8.GetByteCount)),
            variants.Max(McpResponseFormatter.CountTokens));
    }

    internal static CallToolResult? PreflightRunningControl(string token, int bytes, int? tokens)
    {
        _ = Fits(string.Empty, bytes, tokens);
        var minimum = RunningAdmissionMinimum(token);
        return bytes < minimum.Bytes || tokens is { } budget && budget < minimum.Tokens
            ? RunningBudgetError(minimum, bytes, tokens, isPoll: false)
            : null;
    }

    internal static CallToolResult Running(string operationToken, int maxResponseBytes, int? maxResponseTokens) =>
        Running(operationToken, new(0, NavigationAnalysisPhase.Loading), maxResponseBytes, maxResponseTokens,
            RunningAdmissionMinimum(operationToken));

    internal static CallToolResult Running(string token, NavigationOperationProgressSnapshot progress,
        int bytes, int? tokens, (int Bytes, int Tokens) minimum)
    {
        var required = RunningControlText(token, progress, includeCounters: false);
        if (!Fits(required, bytes, tokens)) return RunningBudgetError(minimum, bytes, tokens, isPoll: true);
        var measured = RunningControlText(token, progress, includeCounters: true);
        return Create(Fits(measured, bytes, tokens) ? measured : required, isError: false);
    }

    internal static string RunningControlText(string token, NavigationOperationProgressSnapshot progress, bool includeCounters)
    {
        var lines = new List<string>
        {
            RunningStatusPrefix.TrimEnd('\n'),
            $"operationToken={token}",
            FormattableString.Invariant($"elapsedMilliseconds: {progress.ElapsedMilliseconds}"),
            $"phase: {progress.Phase.ToString().ToLowerInvariant()}",
            "retryAfterMilliseconds: 1000"
        };
        if (includeCounters && progress.ProcessedDocuments is { } processed)
            lines.Add(FormattableString.Invariant($"processedDocuments: {processed}"));
        if (includeCounters && progress.TotalDocuments is { } total)
            lines.Add(FormattableString.Invariant($"totalDocuments: {total}"));
        lines.Add($"nextAction: {RunningNextAction}");
        return string.Join("\n", lines);
    }

    private static CallToolResult RunningBudgetError((int Bytes, int Tokens) minimum, int bytes, int? tokens, bool isPoll)
    {
        var action = isPoll
            ? $"Repeat the unchanged poll with maxResponseBytes={minimum.Bytes} and maxResponseTokens={minimum.Tokens}, the same operationToken and active resultCursor; omit continuationToken."
            : $"Repeat the unchanged fresh call with maxResponseBytes={minimum.Bytes} and maxResponseTokens={minimum.Tokens}.";
        var text = ErrorStatusPrefix + $"RESPONSE_BUDGET_TOO_SMALL\nminimumResponseBytes: {minimum.Bytes}\nminimumResponseTokens: {minimum.Tokens}\nnextAction: {action}";
        // Error envelopes are atomic too; preserve the existing unrepresentable-budget validation.
        if (!Fits(text, bytes, tokens))
            throw new ArgumentOutOfRangeException(nameof(tokens), "The response budget cannot represent the complete budget error envelope.");
        return Create(text, isError: true);
    }

    private static bool Fits(string text, int bytes, int? tokens)
    {
        if (!McpResponseBudgetLimits.IsPublicBudget(bytes)) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (tokens is <= 0) throw new ArgumentOutOfRangeException(nameof(tokens));
        return Encoding.UTF8.GetByteCount(text) <= bytes && (tokens is null || McpResponseFormatter.CountTokens(text) <= tokens);
    }

    internal static CallToolResult TextResult(string text, bool isError, JsonElement? structuredContent = null) =>
        Create(text, isError, structuredContent);

    internal static CallToolResult DomainTruncated(string text, string nextAction)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextAction);
        return TextResult($"{DomainTruncatedMarker}nextAction: {nextAction}\n{text}", isError: false);
    }

    internal static string NormalizeExistingResult(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Content.Count != 1 || result.Content[0] is not TextContentBlock text)
        {
            throw new InvalidOperationException("Long-running and continuation results must contain exactly one text content block.");
        }

        return text.Text;
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
