using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Mcp.Formatting;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp;

internal sealed record LongRunningToolCallRequest(
    string ToolName,
    string Target,
    string ArgumentsKey,
    Func<string?, CancellationToken, Task<CallToolResult>> Operation,
    string? OperationToken = null,
    string? ContinuationToken = null,
    int MaxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
    int? MaxResponseTokens = null,
    bool DomainTruncated = false,
    string? DomainNextAction = null,
    string? DomainCursor = null,
    string? AnalysisSnapshotId = null,
    string? CoreDomainCursor = null,
    string? ResultSection = null,
    NavigationAnalysisMetadata? AnalysisMetadata = null,
    bool ProvidesDetailedProgress = false);

/// <summary>Owns bounded tool executions and their opaque operation/continuation tokens.</summary>
internal sealed class LongRunningToolCallStore : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationEntry> _operations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reservedTokens = new(StringComparer.Ordinal);
    private readonly HashSet<OperationEntry> _running = [];
    private readonly McpResponseContinuationStore _continuations;
    private readonly TimeSpan _responseWindow;
    private readonly TimeSpan _pollResponseWindow;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _operationTokenFactory;
    private readonly TimeSpan _runningIdleTtl;
    private readonly TimeSpan _completedIdleTtl;
    private readonly int _maxRunning;
    private readonly int _maxCompleted;
    private readonly int _maxFinalResponseVariants;
    private readonly CancellationTokenSource _lifetime;
    private bool _disposed;

    internal LongRunningToolCallStore(
        TimeSpan responseWindow,
        CancellationToken lifetimeToken = default,
        TimeSpan? runningIdleTtl = null,
        TimeSpan? completedIdleTtl = null,
        TimeSpan? continuationIdleTtl = null,
        int maxRunning = 4,
        int maxCompleted = 32,
        int maxContinuationSnapshots = 32,
        long maxContinuationBytes = 8 * 1024 * 1024,
        int maxContinuationPages = 512,
        int maxContinuationBudgetVariants = 4,
        TimeSpan? pollResponseWindow = null,
        TimeProvider? timeProvider = null,
        Func<string>? operationTokenFactory = null)
    {
        if (responseWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(responseWindow));
        if (maxRunning <= 0) throw new ArgumentOutOfRangeException(nameof(maxRunning));
        if (maxCompleted <= 0) throw new ArgumentOutOfRangeException(nameof(maxCompleted));
        if (maxContinuationSnapshots <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationSnapshots));
        if (maxContinuationBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationBytes));
        if (maxContinuationPages <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationPages));
        if (maxContinuationBudgetVariants <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationBudgetVariants));
        _responseWindow = responseWindow;
        _pollResponseWindow = pollResponseWindow ?? TimeSpan.FromSeconds(1);
        if (_pollResponseWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollResponseWindow));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _operationTokenFactory = operationTokenFactory ?? McpResponseContinuationStore.CreateOpaqueToken;
        _runningIdleTtl = runningIdleTtl ?? TimeSpan.FromMinutes(30);
        _completedIdleTtl = completedIdleTtl ?? TimeSpan.FromMinutes(30);
        if (_runningIdleTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(runningIdleTtl));
        if (_completedIdleTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(completedIdleTtl));
        _maxRunning = maxRunning;
        _maxCompleted = maxCompleted;
        _maxFinalResponseVariants = maxContinuationBudgetVariants;
        _continuations = new McpResponseContinuationStore(continuationIdleTtl ?? TimeSpan.FromMinutes(30), maxContinuationSnapshots, maxContinuationBytes, maxContinuationPages, maxContinuationBudgetVariants);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
    }

    internal async Task<CallToolResult> RunAsync(LongRunningToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ArgumentNullException.ThrowIfNull(request.ArgumentsKey);
        ArgumentNullException.ThrowIfNull(request.Operation);
        if (request.OperationToken is not null && request.ContinuationToken is not null
            || request.ContinuationToken is not null && request.DomainCursor is not null)
        {
            return McpToolResults.InvalidArgument("Only one continuation token may be supplied.", "operationToken",
                "Supply either operationToken or continuationToken.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }

        ThrowIfDisposed();
        await ExpireEntriesAsync().ConfigureAwait(false);
        if (request.ContinuationToken is { } continuationToken)
        {
            return _continuations.GetPage(continuationToken, request);
        }

        if (request.OperationToken is { } operationToken)
        {
            return await PollAsync(operationToken, request, cancellationToken).ConfigureAwait(false);
        }

        if (request.DomainCursor is { } domainCursor)
        {
            var resolvedCursor = _continuations.ResolveDomainCursor(domainCursor, request);
            if (resolvedCursor.Error is not null) return resolvedCursor.Error;
            request = request with { AnalysisSnapshotId = resolvedCursor.SnapshotId, CoreDomainCursor = resolvedCursor.CoreCursor };
        }

        string token;
        lock (_gate)
        {
            ThrowIfDisposed();
            do { token = _operationTokenFactory(); }
            while (_operations.ContainsKey(token) || !_reservedTokens.Add(token));
        }
        OperationEntry entry;
        try
        {
            if (McpToolResults.PreflightRunningControl(token, request.MaxResponseBytes, request.MaxResponseTokens) is { } budgetError)
                return budgetError;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_running.Count >= _maxRunning)
                {
                    return McpToolResults.Recoverable("TOO_MANY_OPERATIONS", "The server is at its active operation limit.",
                        "Wait at least 1000 milliseconds, then retry the original request without operationToken.",
                        maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens,
                        retryAfterMilliseconds: 1000);
                }

                entry = new OperationEntry(token, request, _lifetime.Token, DateTimeOffset.UtcNow, _runningIdleTtl,
                    _timeProvider, McpToolResults.RunningAdmissionMinimum(token));
                entry.ResetIdleDeadline(_runningIdleTtl);
                _operations.Add(token, entry);
                _running.Add(entry);
                entry.Task = ExecuteAsync(entry);
            }
        }
        finally
        {
            lock (_gate) _reservedTokens.Remove(token);
        }

        return await WaitForResultAsync(entry, request, cancellationToken, initialRequest: true).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        OperationEntry[] entries;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _operations.Values.Concat(_running).Distinct().ToArray();
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(entries.Select(static entry => entry.Task)).ConfigureAwait(false); }
        catch { /* Individual operation failures are translated at the result boundary. */ }
        foreach (var entry in entries) entry.DisposeCancellationSource();
        _lifetime.Dispose();
        _continuations.Dispose();
    }

    private async Task<CallToolResult> PollAsync(string token, LongRunningToolCallRequest request, CancellationToken cancellationToken)
    {
        OperationEntry? entry;
        lock (_gate)
        {
            _operations.TryGetValue(token, out entry);
            if (entry is not null && !entry.Matches(request)) entry = null;
            if (entry is not null)
            {
                entry.LastAccess = DateTimeOffset.UtcNow;
                if (!entry.WorkCompleted && !entry.Cancellation.IsCancellationRequested) entry.ResetIdleDeadline(_runningIdleTtl);
            }
        }

        if (entry is null)
        {
            return McpToolResults.Recoverable("OPERATION_EXPIRED", "The operation token is unknown, expired, or bound to a different request.",
                "Start the tool call again with the same target and arguments.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }

        return await WaitForResultAsync(entry, request, cancellationToken, initialRequest: false).ConfigureAwait(false);
    }

    private async Task<CallToolResult> WaitForResultAsync(OperationEntry entry, LongRunningToolCallRequest request, CancellationToken cancellationToken, bool initialRequest)
    {
        try
        {
            var result = await entry.Task.WaitAsync(initialRequest ? _responseWindow : _pollResponseWindow,
                _timeProvider, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                entry.LastAccess = DateTimeOffset.UtcNow;
                TrimCompleted();
            }
            return GetFinalResponse(entry, result, request);
        }
        catch (TimeoutException)
        {
            return McpToolResults.Running(entry.Token, entry.Progress.Snapshot(), request.MaxResponseBytes,
                request.MaxResponseTokens, entry.AdmissionMinimum);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (initialRequest) await entry.RequestCancellationAsync().ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            if (entry.WasCancelledByOwner && DateTimeOffset.UtcNow >= entry.IdleDeadline)
            {
                return McpToolResults.Recoverable("OPERATION_EXPIRED", "The operation was idle for too long and was cancelled.", "Start the tool call again.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            }
            return McpToolResults.Recoverable("OPERATION_CANCELLED", "The operation was cancelled before producing a result.", "Start the tool call again.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }
    }

    private async Task<CallToolResult> ExecuteAsync(OperationEntry entry)
    {
        var cancelledBeforeCompletion = false;
        try
        {
            return await Task.Run(
                async () =>
                {
                    using var progressScope = NavigationOperationProgress.Enter(entry.Progress);
                    if (!entry.Request.ProvidesDetailedProgress) entry.Progress.Advance(NavigationAnalysisPhase.Analyzing);
                    var result = await entry.Request.Operation(entry.Request.CoreDomainCursor, entry.Cancellation.Token).ConfigureAwait(false);
                    entry.Progress.Advance(NavigationAnalysisPhase.Formatting);
                    return result;
                },
                entry.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            entry.Progress.Advance(NavigationAnalysisPhase.Formatting);
            return McpToolResults.Recoverable("OPERATION_FAILED", "The tool operation failed.", "Correct the request and retry.",
                maxResponseBytes: entry.Request.MaxResponseBytes, maxResponseTokens: entry.Request.MaxResponseTokens);
        }
        finally
        {
            cancelledBeforeCompletion = entry.Cancellation.IsCancellationRequested;
            entry.Cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            try { await entry.RequestCancellationAsync().ConfigureAwait(false); }
            catch { /* A faulty cancellation callback must not corrupt store retention. */ }

            lock (_gate)
            {
                _running.Remove(entry);
                entry.WorkCompleted = true;
                entry.WasCancelledByOwner = cancelledBeforeCompletion;
                entry.LastAccess = DateTimeOffset.UtcNow;
                entry.CancellationDispatched = true;
                if (cancelledBeforeCompletion) _operations.Remove(entry.Token);
                else TrimCompleted();
                DisposeCancellationWhenSafe(entry);
            }
        }
    }

    private CallToolResult GetFinalResponse(OperationEntry entry, CallToolResult result, LongRunningToolCallRequest request)
    {
        lock (entry.ResponseGate)
        {
            var budgetKey = (request.MaxResponseBytes, request.MaxResponseTokens);
            if (entry.FinalResponses.TryGetValue(budgetKey, out var cached))
            {
                if (_continuations.RefreshCachedContinuation(cached, request)) return cached;
                var refreshed = _continuations.CreateFirstPage(result, request, entry.SnapshotId);
                if (refreshed.IsError != true) entry.FinalResponses[budgetKey] = refreshed;
                return refreshed;
            }
            if (entry.FinalResponses.Count >= _maxFinalResponseVariants)
            {
                return McpToolResults.Recoverable("OPERATION_RESPONSE_CAPACITY", "This operation reached its response-budget variant limit.",
                    "Reuse a previously used byte/token budget pair or start a narrower query.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            }

            var response = _continuations.CreateFirstPage(result, request, entry.SnapshotId);
            if (response.IsError != true && !IsTransientRetryResponse(response)) entry.FinalResponses.Add(budgetKey, response);
            return response;
        }
    }

    private static bool IsTransientRetryResponse(CallToolResult response) =>
        response.IsError != true
        && McpToolResults.NormalizeExistingResult(response).StartsWith(McpToolResults.LoadingStatusPrefix, StringComparison.Ordinal);

    private async Task ExpireEntriesAsync()
    {
        List<OperationEntry> expired = [];
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _operations.ToArray())
            {
                var entry = pair.Value;
                var ttl = entry.WorkCompleted ? _completedIdleTtl : _runningIdleTtl;
                if (now - entry.LastAccess <= ttl) continue;
                _operations.Remove(pair.Key);
                if (!entry.WorkCompleted)
                {
                    entry.ExpirationCancelPending = true;
                    expired.Add(entry);
                }
                else
                {
                    entry.Retired = true;
                    DisposeCancellationWhenSafe(entry);
                }
            }
        }
        foreach (var entry in expired)
        {
            try { await entry.RequestCancellationAsync().ConfigureAwait(false); }
            catch { /* Expiring an entry must not fail an unrelated tool call. */ }
            finally
            {
                lock (_gate)
                {
                    entry.CancellationDispatched = true;
                    entry.ExpirationCancelPending = false;
                    DisposeCancellationWhenSafe(entry);
                }
            }
        }
    }

    private void TrimCompleted()
    {
        var completed = _operations.Values.Where(static entry => entry.WorkCompleted && !entry.WasCancelledByOwner)
            .OrderBy(static entry => entry.LastAccess).ToArray();
        foreach (var entry in completed.Take(Math.Max(0, completed.Length - _maxCompleted)))
        {
            _operations.Remove(entry.Token);
            entry.Retired = true;
            DisposeCancellationWhenSafe(entry);
        }
    }

    private void DisposeCancellationWhenSafe(OperationEntry entry)
    {
        if (entry.WorkCompleted && entry.CancellationDispatched && !entry.ExpirationCancelPending)
        {
            entry.DisposeCancellationSource();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LongRunningToolCallStore));
    }

    private sealed class OperationEntry(string token, LongRunningToolCallRequest request, CancellationToken lifetimeToken,
        DateTimeOffset lastAccess, TimeSpan runningIdleTtl, TimeProvider timeProvider, (int Bytes, int Tokens) admissionMinimum)
    {
        private readonly object _cancellationGate = new();
        private Task? _cancellationTask;
        private bool _cancellationSourceDisposed;
        internal string Token { get; } = token;
        internal NavigationOperationProgress Progress { get; } = new(timeProvider);
        internal (int Bytes, int Tokens) AdmissionMinimum { get; } = admissionMinimum;
        internal LongRunningToolCallRequest Request { get; } = request;
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        internal string SnapshotId { get; } = request.AnalysisSnapshotId ?? McpResponseContinuationStore.CreateOpaqueToken();
        internal object ResponseGate { get; } = new();
        internal Dictionary<(int Bytes, int? Tokens), CallToolResult> FinalResponses { get; } = [];
        internal DateTimeOffset LastAccess { get; set; } = lastAccess;
        internal DateTimeOffset IdleDeadline { get; private set; } = lastAccess + runningIdleTtl;
        internal Task<CallToolResult> Task { get; set; } = System.Threading.Tasks.Task.FromResult(new CallToolResult());
        internal bool WorkCompleted { get; set; }
        internal bool WasCancelledByOwner { get; set; }
        internal bool CancellationDispatched { get; set; }
        internal bool ExpirationCancelPending { get; set; }
        internal bool Retired { get; set; }

        internal Task RequestCancellationAsync()
        {
            lock (_cancellationGate)
            {
                if (_cancellationSourceDisposed) return System.Threading.Tasks.Task.CompletedTask;
                return _cancellationTask ??= Cancellation.CancelAsync();
            }
        }

        internal void DisposeCancellationSource()
        {
            lock (_cancellationGate)
            {
                if (_cancellationSourceDisposed) return;
                _cancellationSourceDisposed = true;
                Cancellation.Dispose();
            }
        }

        internal void ResetIdleDeadline(TimeSpan ttl)
        {
            lock (_cancellationGate)
            {
                if (_cancellationSourceDisposed) return;
                IdleDeadline = DateTimeOffset.UtcNow + ttl;
                Cancellation.CancelAfter(ttl);
            }
        }
        internal bool Matches(LongRunningToolCallRequest other) =>
            string.Equals(Request.ToolName, other.ToolName, StringComparison.Ordinal)
            && string.Equals(Request.Target, other.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Request.ArgumentsKey, other.ArgumentsKey, StringComparison.Ordinal)
            && string.Equals(Request.DomainCursor, other.DomainCursor, StringComparison.Ordinal);
    }
}

/// <summary>Stores immutable text snapshots and opaque continuation pages with bounded lifetime and memory.</summary>
internal sealed class McpResponseContinuationStore : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PageEntry> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DomainCursorEntry> _domainCursors = new(StringComparer.Ordinal);
    private readonly TimeSpan _idleTtl;
    private readonly int _maxSnapshots;
    private readonly long _maxSnapshotBytes;
    private readonly int _maxPages;
    private readonly int _maxBudgetVariants;
    private bool _disposed;

    internal McpResponseContinuationStore(TimeSpan idleTtl, int maxSnapshots, long maxSnapshotBytes, int maxPages, int maxBudgetVariants)
    {
        if (idleTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTtl));
        _idleTtl = idleTtl;
        _maxSnapshots = maxSnapshots;
        _maxSnapshotBytes = maxSnapshotBytes;
        _maxPages = maxPages;
        _maxBudgetVariants = maxBudgetVariants;
    }

    internal CallToolResult GetPage(string token, LongRunningToolCallRequest request)
    {
        lock (_gate)
        {
            Expire();
            if (!_pages.TryGetValue(token, out var page))
                return McpToolResults.Recoverable("CONTINUATION_EXPIRED", "The continuation token is unknown or expired.", "Repeat the original tool call.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            if (!page.Matches(request))
                return McpToolResults.Recoverable("CONTINUATION_ARGUMENT_MISMATCH", "The continuation token belongs to a different tool request.", "Repeat the continuation with the original target and arguments.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            page.LastAccess = DateTimeOffset.UtcNow;
            request = request with { DomainTruncated = page.DomainTruncated, DomainNextAction = page.DomainNextAction,
                AnalysisMetadata = page.AnalysisMetadata, AnalysisSnapshotId = page.AnalysisMetadata?.SnapshotId };
            var budgetKey = (request.MaxResponseBytes, request.MaxResponseTokens);
            if (page.Results.TryGetValue(budgetKey, out var cached))
            {
                if (HasLiveOuterContinuation(cached, request)) return cached;
                var refreshed = CreatePage(page.SnapshotId, page.Snapshot, page.Offset, request, page.CoreSnapshot);
                if (refreshed.IsError != true) page.Results[budgetKey] = refreshed;
                return refreshed;
            }
            if (page.Results.Count >= _maxBudgetVariants)
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "This continuation token reached its response-budget variant limit.", "Reuse a previously used budget pair or start a narrower query.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var result = CreatePage(page.SnapshotId, page.Snapshot, page.Offset, request, page.CoreSnapshot);
            if (result.IsError != true) page.Results.Add(budgetKey, result);
            return result;
        }
    }

    internal (string? SnapshotId, CallToolResult? Error, string? CoreCursor) ResolveDomainCursor(string token, LongRunningToolCallRequest request)
    {
        lock (_gate)
        {
            Expire();
            if (!_domainCursors.TryGetValue(token, out var cursor))
                return (null, McpToolResults.Recoverable("RESULT_CURSOR_EXPIRED", "The result cursor is unknown or expired.",
                    "Repeat the original query to create a fresh result cursor.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens), null);
            if (!cursor.Matches(request))
                return (null, McpToolResults.Recoverable("RESULT_CURSOR_ARGUMENT_MISMATCH", "The result cursor belongs to a different tool, target, or query.",
                    "Repeat the result cursor with the original target and query.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens), null);
            cursor.LastAccess = DateTimeOffset.UtcNow;
            return (cursor.SnapshotId, null, cursor.CoreCursor);
        }
    }

    internal bool RefreshCachedContinuation(CallToolResult response, LongRunningToolCallRequest request)
    {
        lock (_gate) return HasLiveContinuation(response, request);
    }

    private bool HasLiveContinuation(CallToolResult response, LongRunningToolCallRequest request)
    {
        var text = McpToolResults.NormalizeExistingResult(response);
        Expire();
        var tokenLine = text.Split('\n').FirstOrDefault(static line => line.StartsWith("continuationToken=", StringComparison.Ordinal));
        if (tokenLine is null) return RefreshDomainCursorAccess(ExtractStatusBody(text), request);
        var token = tokenLine["continuationToken=".Length..];
        if (!_pages.TryGetValue(token, out var page) || !page.Matches(request)) return false;
        page.LastAccess = DateTimeOffset.UtcNow;
        if (!RefreshDomainCursorAccess(page.Snapshot, request)) return false;
        return true;
    }

    private bool HasLiveOuterContinuation(CallToolResult response, LongRunningToolCallRequest request)
    {
        var text = McpToolResults.NormalizeExistingResult(response);
        Expire();
        var tokenLine = text.Split('\n').FirstOrDefault(static line => line.StartsWith("continuationToken=", StringComparison.Ordinal));
        if (tokenLine is null) return true;
        var token = tokenLine["continuationToken=".Length..];
        if (!_pages.TryGetValue(token, out var page) || !page.Matches(request)) return false;
        page.LastAccess = DateTimeOffset.UtcNow;
        return true;
    }

    private bool RefreshDomainCursorAccess(string source, LongRunningToolCallRequest request)
    {
        try
        {
            var document = JsonNode.Parse(source);
            if (document is null) return true;
            var tokens = new List<string>();
            CollectPublicDomainCursors(document, tokens);
            foreach (var token in tokens)
            {
                if (!_domainCursors.TryGetValue(token, out var cursor) || !cursor.Matches(request)) return false;
                cursor.LastAccess = DateTimeOffset.UtcNow;
            }
            return true;
        }
        catch (JsonException) { return true; }
    }

    private static string ExtractStatusBody(string text)
    {
        var lines = text.Split('\n');
        var first = lines.Length > 0 && lines[0].StartsWith("Status:", StringComparison.Ordinal) ? 1 : 0;
        while (first < lines.Length && (lines[first].StartsWith("snapshotId=", StringComparison.Ordinal)
            || lines[first].StartsWith("omissions=", StringComparison.Ordinal)
            || lines[first].StartsWith("nextAction: ", StringComparison.Ordinal)
            || lines[first].StartsWith("domainNextAction: ", StringComparison.Ordinal)
            || lines[first].StartsWith("continuationToken=", StringComparison.Ordinal))) first++;
        return string.Join('\n', lines.Skip(first));
    }

    private static void CollectPublicDomainCursors(JsonNode? element, ICollection<string> cursors)
    {
        if (element is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (string.Equals(pair.Key, "resultCursor", StringComparison.Ordinal) && pair.Value is JsonValue value
                    && value.TryGetValue<string>(out var cursor) && !string.IsNullOrWhiteSpace(cursor)) cursors.Add(cursor);
                else CollectPublicDomainCursors(pair.Value, cursors);
            }
        }
        else if (element is JsonArray array)
        {
            foreach (var item in array) CollectPublicDomainCursors(item, cursors);
        }
    }

    internal CallToolResult CreateFirstPage(CallToolResult result, LongRunningToolCallRequest request, string snapshotId)
    {
        if (result.IsError == true)
        {
            var errorText = McpToolResults.NormalizeExistingResult(result);
            var bytes = Encoding.UTF8.GetByteCount(errorText);
            var tokens = McpResponseFormatter.CountTokens(errorText);
            if (bytes <= request.MaxResponseBytes && (request.MaxResponseTokens is null || tokens <= request.MaxResponseTokens.Value)) return result;
            return CreateAtomicBudgetError(errorText, request, "The stored tool error");
        }

        var returnedText = McpToolResults.NormalizeExistingResult(result);
        if (returnedText.StartsWith(McpToolResults.LoadingStatusPrefix, StringComparison.Ordinal))
        {
            if (result.StructuredContent.HasValue)
                return McpToolResults.Recoverable("INVALID_CONTROL_RESULT", "A loading control cannot include structured content.", "Return a text-only loading result.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var loadingBody = returnedText[McpToolResults.LoadingStatusPrefix.Length..];
            var firstLineEnd = loadingBody.IndexOf('\n');
            var firstLine = firstLineEnd < 0 ? loadingBody : loadingBody[..firstLineEnd];
            if (!firstLine.StartsWith("nextAction: ", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(firstLine["nextAction: ".Length..]))
                return McpToolResults.Recoverable("INVALID_CONTROL_RESULT", "A loading control must include its complete nextAction instruction.", "Return a text-only loading result with nextAction.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var bytes = Encoding.UTF8.GetByteCount(returnedText);
            var tokens = McpResponseFormatter.CountTokens(returnedText);
            if (bytes <= request.MaxResponseBytes && (request.MaxResponseTokens is null || tokens <= request.MaxResponseTokens.Value)) return result;
            return CreateAtomicBudgetError(returnedText, request, "The stored loading control");
        }

        if (returnedText.StartsWith(McpToolResults.RunningStatusPrefix, StringComparison.Ordinal))
            return McpToolResults.Recoverable("OPERATION_CONTROL_UNSUPPORTED", "A delegate cannot return a running token owned by another operation store.",
                "Return a loading/retry result, or complete this operation with its result.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);

        var source = ExtractText(result);
        if (source.StartsWith(McpToolResults.DomainTruncatedMarker, StringComparison.Ordinal))
        {
            var markerEnd = source.IndexOf('\n', McpToolResults.DomainTruncatedMarker.Length);
            if (markerEnd < 0)
                return McpToolResults.Recoverable("INVALID_DOMAIN_COMPLETENESS", "A truncated navigation result omitted its next action.",
                    "Repeat the original query with a valid result limit.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var action = source[McpToolResults.DomainTruncatedMarker.Length..markerEnd];
            if (!action.StartsWith("nextAction: ", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(action["nextAction: ".Length..]))
                return McpToolResults.Recoverable("INVALID_DOMAIN_COMPLETENESS", "A truncated navigation result omitted its next action.",
                    "Repeat the original query with a valid result limit.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            source = source[(markerEnd + 1)..];
            request = request with { DomainTruncated = true, DomainNextAction = action["nextAction: ".Length..] };
        }
        if (request.DomainTruncated && result.StructuredContent.HasValue)
            return McpToolResults.Recoverable("STRUCTURED_RESULT_INCOMPLETE", "Domain-truncated navigation results cannot include complete structured content.", "Repeat with limits that return a complete domain result.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        if (result.StructuredContent.HasValue)
        {
            var complete = McpResponseFormatter.Format(source, request.MaxResponseBytes, request.MaxResponseTokens,
                responsePrefix: McpToolResults.SuccessStatusPrefix);
            if (complete.ErrorCode is null && !complete.IsTruncated)
                return McpToolResults.Success(source, result.StructuredContent, request.MaxResponseBytes, request.MaxResponseTokens);
            return McpToolResults.Recoverable("STRUCTURED_RESULT_TOO_LARGE", "Structured content cannot be returned with a partial text page.", "Narrow the query or increase the response budget.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }
        var analysis = ReadAnalysisMetadata(result, source);
        if (analysis is not null)
        {
            request = request with { AnalysisSnapshotId = analysis.SnapshotId, AnalysisMetadata = analysis };
            // Assembly scanners carry typed analysis in their JSON model. Retain it
            // internally and project the compact preamble only once for the caller.
            try
            {
                if (JsonNode.Parse(source) is JsonObject document && document.Remove("analysis"))
                    source = McpResponseFormatter.FormatCompactJson(document.ToJsonString());
            }
            catch (JsonException) { /* Text bodies have no embedded analysis metadata. */ }
        }
        var registeredCursors = RegisterDomainCursors(source, request, snapshotId);
        if (registeredCursors.Error is not null) return registeredCursors.Error;
        var response = CreatePage(snapshotId, registeredCursors.Source, offset: 0, request, source);
        if (response.IsError == true) RollbackDomainCursors(registeredCursors.InsertedTokens, snapshotId);
        return response;
    }

    private (string Source, IReadOnlyList<string> InsertedTokens, CallToolResult? Error) RegisterDomainCursors(
        string source, LongRunningToolCallRequest request, string snapshotId)
    {
        try
        {
            var document = JsonNode.Parse(source);
            if (document is null) return (source, Array.Empty<string>(), null);
            var cursorProperties = new List<(JsonObject Parent, string Name, string CoreCursor, string PublicCursor)>();
            CollectDomainCursors(document, cursorProperties);
            if (cursorProperties.Count == 0) return (source, Array.Empty<string>(), null);
            var insertedTokens = new List<string>();
            lock (_gate)
            {
                Expire();
                var mappedCursors = cursorProperties.Select(property =>
                {
                    var existing = _domainCursors.Values.FirstOrDefault(cursor => cursor.Matches(request)
                        && string.Equals(cursor.SnapshotId, request.AnalysisSnapshotId ?? snapshotId, StringComparison.Ordinal)
                        && string.Equals(cursor.CoreCursor, property.CoreCursor, StringComparison.Ordinal));
                    return (Property: property, PublicToken: existing?.Token ?? property.PublicCursor, Existing: existing is not null);
                }).ToArray();
                var newCursors = mappedCursors.Where(static cursor => !cursor.Existing)
                    .GroupBy(static cursor => cursor.PublicToken, StringComparer.Ordinal).Select(static group => group.First()).ToArray();
                var storedBytes = GetStoredBytes();
                var snapshotIds = _pages.Values.Select(static page => page.SnapshotId).Concat(_domainCursors.Values.Select(static cursor => cursor.StorageSnapshotId))
                    .Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                var snapshotIsStored = snapshotIds.Contains(snapshotId);
                if (_pages.Count + _domainCursors.Count + newCursors.Length > _maxPages
                    || !snapshotIsStored && snapshotIds.Count >= _maxSnapshots
                    || storedBytes + newCursors.Sum(cursor => (long)DomainCursorEntry.MeasureBytes(
                        cursor.PublicToken, cursor.Property.CoreCursor, request)) > _maxSnapshotBytes)
                    return (source, Array.Empty<string>(), McpToolResults.Recoverable("RESULT_CURSOR_CAPACITY", "The result cursor store reached its bounded retention limit.",
                        "Retry after older result cursors expire or repeat a narrower query.",
                        maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens));
                foreach (var cursor in mappedCursors)
                {
                    if (!cursor.Existing)
                    {
                        _domainCursors.Add(cursor.PublicToken, new DomainCursorEntry(cursor.PublicToken, cursor.Property.CoreCursor,
                            request.AnalysisSnapshotId ?? snapshotId, snapshotId, request, DateTimeOffset.UtcNow));
                        insertedTokens.Add(cursor.PublicToken);
                    }
                    cursor.Property.Parent[cursor.Property.Name] = cursor.PublicToken;
                }
            }
            return (McpResponseFormatter.FormatCompactJson(document.ToJsonString()), insertedTokens, null);
        }
        catch (JsonException)
        {
            // Non-JSON text handlers do not expose domain cursors.
            return (source, Array.Empty<string>(), null);
        }
    }

    private static void CollectDomainCursors(JsonNode? element, ICollection<(JsonObject Parent, string Name, string CoreCursor, string PublicCursor)> cursors)
    {
        if (element is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (string.Equals(pair.Key, "resultCursor", StringComparison.Ordinal) && pair.Value is JsonValue value
                    && value.TryGetValue<string>(out var coreCursor) && !string.IsNullOrWhiteSpace(coreCursor))
                {
                    cursors.Add((obj, pair.Key, coreCursor, CreateOpaqueToken()));
                }
                else CollectDomainCursors(pair.Value, cursors);
            }
        }
        else if (element is JsonArray array)
        {
            foreach (var item in array) CollectDomainCursors(item, cursors);
        }
    }

    private long GetStoredBytes() =>
        _pages.Values.GroupBy(static page => page.SnapshotId, StringComparer.Ordinal)
            .Sum(group => (long)group.Max(static page => page.SnapshotBytes)
                + group.Sum(static page => (long)page.EntryBytes + page.CachedResponseBytes))
        + _domainCursors.Values.Sum(static cursor => (long)cursor.StoredBytes);

    private void RollbackDomainCursors(IReadOnlyList<string> tokens, string snapshotId)
    {
        lock (_gate)
        {
            foreach (var token in tokens)
                if (_domainCursors.TryGetValue(token, out var cursor) && string.Equals(cursor.SnapshotId, snapshotId, StringComparison.Ordinal))
                    _domainCursors.Remove(token);
        }
    }

    private static CallToolResult CreateAtomicBudgetError(string projection, LongRunningToolCallRequest request, string description)
    {
        var bytes = Encoding.UTF8.GetByteCount(projection);
        var tokens = McpResponseFormatter.CountTokens(projection);
        var minimumBytes = Math.Max(McpResponseBudgetLimits.MinimumBytes, bytes);
        var canRetryWithLargerBudget = bytes <= McpResponseBudgetLimits.MaximumBytes;
        var recoveryHint = canRetryWithLargerBudget
            ? $"retry: repeat with maxResponseBytes={minimumBytes} and maxResponseTokens={tokens}."
            : $"recovery: {description} exceeds the {McpResponseBudgetLimits.MaximumBytes}-byte limit; narrow it and repeat the same request.";
        return McpToolResults.BudgetTooSmall(new McpResponseFormatResult(
            string.Empty, 0, 0, IsTruncated: false, ErrorCode: "RESPONSE_BUDGET_TOO_SMALL",
            MinimumResponseBytes: minimumBytes, MinimumResponseTokens: tokens, NextOffset: null, OmittedUtf8Bytes: 0,
            ContinuationHint: null, CanRetryWithLargerResponseBudget: canRetryWithLargerBudget, RecoveryHint: recoveryHint),
            request.MaxResponseBytes, request.MaxResponseTokens);
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _pages.Clear(); _domainCursors.Clear(); }
    }

    private CallToolResult CreatePage(string snapshotId, string source, int offset, LongRunningToolCallRequest request, string? coreSnapshot = null)
    {
        var analysis = request.AnalysisMetadata ?? ReadAnalysisMetadata(null, source);
        var analysisMetadata = CreateAnalysisMetadata(analysis);
        var completePrefix = (request.DomainTruncated
            ? $"nextAction: {request.DomainNextAction}\n"
            : string.Empty) + analysisMetadata;
        var complete = McpResponseFormatter.Format(source, request.MaxResponseBytes, request.MaxResponseTokens,
            startOffset: offset, responsePrefix: completePrefix);
        if (complete.ErrorCode is null && !complete.IsTruncated)
            return McpToolResults.TextResult(complete.Text, isError: false);
        var token = CreateOpaqueToken();
        // Outer paging and domain coverage are independent. Put the executable outer-page
        // action first; domain cursors/body windows apply only after all outer pages.
        var prefix = $"continuationToken={token}\n"
            + "nextAction: Read all outer pages with continuationToken alone before following resultCursor or body windows.\n"
            + (request.DomainTruncated ? $"domainNextAction: {request.DomainNextAction}\n" : string.Empty)
            + analysisMetadata;
        var page = McpResponseFormatter.Format(source, request.MaxResponseBytes, request.MaxResponseTokens,
            startOffset: offset, responsePrefix: prefix);
        if (page.ErrorCode is not null)
        {
            if (request.ContinuationToken is not null)
                page = page with { RecoveryHint = page.RecoveryHint + " Keep the same continuationToken; prior pages remain valid." };
            return McpToolResults.BudgetTooSmall(page, request.MaxResponseBytes, request.MaxResponseTokens);
        }
        if (!page.IsTruncated || page.NextOffset is null)
            return McpToolResults.TextResult(completePrefix + source[offset..], isError: false);

        var text = page.ContinuationHint is { } hint && page.Text.EndsWith("\n" + hint, StringComparison.Ordinal)
            ? page.Text[..^(hint.Length + 1)]
            : page.Text;
        var response = McpToolResults.TextResult(text, isError: false);
        lock (_gate)
        {
            Expire();
            if (_disposed) return McpToolResults.Recoverable("CONTINUATION_EXPIRED", "Continuation storage is no longer available.", "Repeat the original tool call.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var snapshotBytes = Encoding.UTF8.GetByteCount(source);
            var snapshotCount = _pages.Values.Select(static page => page.SnapshotId).Concat(_domainCursors.Values.Select(static cursor => cursor.StorageSnapshotId))
                .Distinct(StringComparer.Ordinal).Count();
            var snapshotIsStored = _pages.Values.Any(page => page.SnapshotId == snapshotId)
                || _domainCursors.Values.Any(cursor => cursor.StorageSnapshotId == snapshotId);
            var storedBytes = GetStoredBytes();
            var entryBytes = Encoding.UTF8.GetByteCount(token) + Encoding.UTF8.GetByteCount(request.ToolName)
                + Encoding.UTF8.GetByteCount(request.Target) + Encoding.UTF8.GetByteCount(request.ArgumentsKey)
                + Encoding.UTF8.GetByteCount(request.ResultSection ?? string.Empty);
            var responseBytes = Encoding.UTF8.GetByteCount(text);
            if (!snapshotIsStored && snapshotCount >= _maxSnapshots
                || storedBytes + (snapshotIsStored ? 0 : snapshotBytes) + entryBytes + responseBytes > _maxSnapshotBytes)
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "The continuation store is at its snapshot limit.", "Retry with a narrower query or after older pages expire.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            if (_pages.Count + _domainCursors.Count >= _maxPages)
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "The continuation store reached its page-token limit.", "Retry with a narrower query or after older pages expire.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            if (!_pages.TryAdd(token, new PageEntry(token, snapshotId, source, coreSnapshot ?? source, page.NextOffset.Value, request, snapshotBytes, DateTimeOffset.UtcNow)))
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "A continuation token collision prevented storing the next page.", "Repeat the original tool call.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }
        return response;
    }

    private static NavigationAnalysisMetadata? ReadAnalysisMetadata(CallToolResult? result, string source)
    {
        if (result?.Meta is { } metadata && metadata["navigationAnalysis"] is { } typed)
        {
            try { return JsonSerializer.Deserialize<NavigationAnalysisMetadata>(typed.ToJsonString(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); }
            catch (JsonException) { return null; }
        }
        try
        {
            using var document = JsonDocument.Parse(source);
            if (!document.RootElement.TryGetProperty("analysis", out var value)) return null;
            return JsonSerializer.Deserialize<NavigationAnalysisMetadata>(value.GetRawText(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int MeasureAnalysisMetadata(NavigationAnalysisMetadata? metadata) => metadata is null
        ? 0
        : Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(metadata));

    private static string CreateAnalysisMetadata(NavigationAnalysisMetadata? analysis)
    {
        if (analysis is null) return string.Empty;
        var metadata = $"snapshotId={EscapeHeaderValue(analysis.SnapshotId)}\n";
        if (analysis.OmissionReasons.Count > 0)
            metadata += $"omissions={string.Join(',', analysis.OmissionReasons.Select(EscapeHeaderValue))}\n";
        return metadata;
    }

    private static string EscapeHeaderValue(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    internal static string CreateOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true).ToString("D39", CultureInfo.InvariantCulture);
    }

    private static string ExtractText(CallToolResult result)
    {
        if (result.IsError == true) return McpToolResults.NormalizeExistingResult(result);
        var text = McpToolResults.NormalizeExistingResult(result);
        if (text.StartsWith(McpToolResults.TruncatedSuccessStatusPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("A truncated result cannot be used as a complete continuation snapshot.");
        return text;
    }

    private void Expire()
    {
        var cutoff = DateTimeOffset.UtcNow - _idleTtl;
        foreach (var key in _pages.Where(pair => pair.Value.LastAccess < cutoff).Select(static pair => pair.Key).ToArray()) _pages.Remove(key);
        foreach (var key in _domainCursors.Where(pair => pair.Value.LastAccess < cutoff).Select(static pair => pair.Key).ToArray()) _domainCursors.Remove(key);
    }

    private sealed class DomainCursorEntry(string token, string coreCursor, string snapshotId, string storageSnapshotId, LongRunningToolCallRequest request, DateTimeOffset lastAccess)
    {
        internal string Token { get; } = token;
        internal string SnapshotId { get; } = snapshotId;
        internal string StorageSnapshotId { get; } = storageSnapshotId;
        internal string CoreCursor { get; } = coreCursor;
        internal int StoredBytes { get; } = MeasureBytes(token, coreCursor, request);
        internal DateTimeOffset LastAccess { get; set; } = lastAccess;
        internal static int MeasureBytes(string token, string coreCursor, LongRunningToolCallRequest request) =>
            Encoding.UTF8.GetByteCount(token) + Encoding.UTF8.GetByteCount(coreCursor) + Encoding.UTF8.GetByteCount(request.ToolName)
            + Encoding.UTF8.GetByteCount(request.Target) + Encoding.UTF8.GetByteCount(request.ArgumentsKey)
            + Encoding.UTF8.GetByteCount(request.ResultSection ?? string.Empty) + Encoding.UTF8.GetByteCount(request.AnalysisSnapshotId ?? string.Empty)
            + MeasureAnalysisMetadata(request.AnalysisMetadata);
        internal bool Matches(LongRunningToolCallRequest other) =>
            string.Equals(request.ToolName, other.ToolName, StringComparison.Ordinal)
            && string.Equals(request.Target, other.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.ArgumentsKey, other.ArgumentsKey, StringComparison.Ordinal)
            && string.Equals(request.ResultSection, other.ResultSection, StringComparison.Ordinal);
    }

    private sealed class PageEntry(string token, string snapshotId, string snapshot, string coreSnapshot, int offset, LongRunningToolCallRequest request, int snapshotBytes, DateTimeOffset lastAccess)
    {
        internal string SnapshotId { get; } = snapshotId;
        internal string RootSnapshotId { get; } = snapshotId;
        internal string Tool { get; } = request.ToolName;
        internal string Target { get; } = request.Target;
        internal string Arguments { get; } = request.ArgumentsKey;
        internal string? Section { get; } = request.ResultSection;
        internal int SnapshotBytes { get; private set; } = snapshotBytes;
        internal int EntryBytes { get; } = Encoding.UTF8.GetByteCount(token) + Encoding.UTF8.GetByteCount(request.ToolName)
            + Encoding.UTF8.GetByteCount(request.Target) + Encoding.UTF8.GetByteCount(request.ArgumentsKey)
            + Encoding.UTF8.GetByteCount(request.ResultSection ?? string.Empty) + MeasureAnalysisMetadata(request.AnalysisMetadata);
        internal int CachedResponseBytes => Results.Values.Sum(static result => Encoding.UTF8.GetByteCount(McpToolResults.NormalizeExistingResult(result)));
        internal bool DomainTruncated { get; } = request.DomainTruncated;
        internal string? DomainNextAction { get; } = request.DomainNextAction;
        internal NavigationAnalysisMetadata? AnalysisMetadata { get; } = request.AnalysisMetadata;
        internal DateTimeOffset LastAccess { get; set; } = lastAccess;
        internal bool Matches(LongRunningToolCallRequest other) =>
            string.Equals(Tool, other.ToolName, StringComparison.Ordinal)
            && string.Equals(Target, other.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Arguments, other.ArgumentsKey, StringComparison.Ordinal)
            && string.Equals(Section, other.ResultSection, StringComparison.Ordinal);
        internal Dictionary<(int Bytes, int? Tokens), CallToolResult> Results { get; } = [];
        internal string Token { get; } = token;
        internal string Snapshot { get; private set; } = snapshot;
        internal string CoreSnapshot { get; } = coreSnapshot;
        internal int Offset { get; } = offset;
    }
}
