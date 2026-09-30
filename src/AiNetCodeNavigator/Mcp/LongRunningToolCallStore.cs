using System.Collections.Concurrent;
using System.Text;
using AiNetCodeNavigator.Mcp.Formatting;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp;

internal sealed record LongRunningToolCallRequest(
    string ToolName,
    string Target,
    string ArgumentsKey,
    Func<CancellationToken, Task<CallToolResult>> Operation,
    string? OperationToken = null,
    string? ContinuationToken = null,
    int MaxResponseBytes = McpResponseBudgetLimits.DefaultBytes,
    int? MaxResponseTokens = null);

/// <summary>Owns bounded tool executions and their opaque operation/continuation tokens.</summary>
internal sealed class LongRunningToolCallStore : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationEntry> _operations = new(StringComparer.Ordinal);
    private readonly HashSet<OperationEntry> _running = [];
    private readonly McpResponseContinuationStore _continuations;
    private readonly TimeSpan _responseWindow;
    private readonly TimeSpan _runningIdleTtl;
    private readonly TimeSpan _completedIdleTtl;
    private readonly int _maxRunning;
    private readonly int _maxCompleted;
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
        int maxContinuationBudgetVariants = 4)
    {
        if (responseWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(responseWindow));
        if (maxRunning <= 0) throw new ArgumentOutOfRangeException(nameof(maxRunning));
        if (maxCompleted <= 0) throw new ArgumentOutOfRangeException(nameof(maxCompleted));
        if (maxContinuationSnapshots <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationSnapshots));
        if (maxContinuationBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationBytes));
        if (maxContinuationPages <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationPages));
        if (maxContinuationBudgetVariants <= 0) throw new ArgumentOutOfRangeException(nameof(maxContinuationBudgetVariants));
        _responseWindow = responseWindow;
        _runningIdleTtl = runningIdleTtl ?? TimeSpan.FromMinutes(30);
        _completedIdleTtl = completedIdleTtl ?? TimeSpan.FromMinutes(30);
        if (_runningIdleTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(runningIdleTtl));
        if (_completedIdleTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(completedIdleTtl));
        _maxRunning = maxRunning;
        _maxCompleted = maxCompleted;
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
        if (request.OperationToken is not null && request.ContinuationToken is not null)
        {
            return McpToolResults.InvalidArgument("Only one continuation token may be supplied.", "operationToken",
                "Supply either operationToken or continuationToken.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }

        ThrowIfDisposed();
        ExpireEntries();
        if (request.ContinuationToken is { } continuationToken)
        {
            return _continuations.GetPage(continuationToken, request);
        }

        if (request.OperationToken is { } operationToken)
        {
            return await PollAsync(operationToken, request, cancellationToken).ConfigureAwait(false);
        }

        var token = Guid.NewGuid().ToString("N");
        var runningResult = McpToolResults.Running(token, request.MaxResponseBytes, request.MaxResponseTokens);
        if (runningResult.IsError == true) return runningResult;

        OperationEntry entry;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_running.Count >= _maxRunning)
            {
                return McpToolResults.Recoverable("TOO_MANY_OPERATIONS", "The server is at its active operation limit.",
                    "Retry after an active operation completes.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            }

            entry = new OperationEntry(token, request, _lifetime.Token, DateTimeOffset.UtcNow, _runningIdleTtl);
            entry.ResetIdleDeadline(_runningIdleTtl);
            _operations.Add(token, entry);
            _running.Add(entry);
            entry.Task = ExecuteAsync(entry);
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
        foreach (var entry in entries) entry.Cancellation.Dispose();
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
                if (!entry.Task.IsCompleted) entry.ResetIdleDeadline(_runningIdleTtl);
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
            var result = await entry.Task.WaitAsync(_responseWindow, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                entry.LastAccess = DateTimeOffset.UtcNow;
                TrimCompleted();
            }
            return _continuations.CreateFirstPage(result, request);
        }
        catch (TimeoutException)
        {
            return McpToolResults.Running(entry.Token, request.MaxResponseBytes, request.MaxResponseTokens);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (initialRequest) await entry.Cancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= entry.IdleDeadline)
            {
                return McpToolResults.Recoverable("OPERATION_EXPIRED", "The operation was idle for too long and was cancelled.", "Start the tool call again.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            }
            return McpToolResults.Recoverable("OPERATION_CANCELLED", "The operation was cancelled before producing a result.", "Start the tool call again.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }
    }

    private async Task<CallToolResult> ExecuteAsync(OperationEntry entry)
    {
        try
        {
            return await Task.Run(
                async () => await entry.Request.Operation(entry.Cancellation.Token).ConfigureAwait(false),
                entry.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return McpToolResults.Recoverable("OPERATION_FAILED", "The tool operation failed.", "Correct the request and retry.",
                maxResponseBytes: entry.Request.MaxResponseBytes, maxResponseTokens: entry.Request.MaxResponseTokens);
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(entry);
                if (entry.Cancellation.IsCancellationRequested) _operations.Remove(entry.Token);
                if (!_operations.ContainsKey(entry.Token) && !_disposed) entry.Cancellation.Dispose();
            }
        }
    }

    private void ExpireEntries()
    {
        List<OperationEntry> expired = [];
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _operations.ToArray())
            {
                var entry = pair.Value;
                var ttl = entry.Task.IsCompleted ? _completedIdleTtl : _runningIdleTtl;
                if (now - entry.LastAccess <= ttl) continue;
                _operations.Remove(pair.Key);
                if (!entry.Task.IsCompleted) expired.Add(entry);
                else entry.Cancellation.Dispose();
            }
        }
        foreach (var entry in expired) entry.Cancellation.Cancel();
    }

    private void TrimCompleted()
    {
        var completed = _operations.Values.Where(static entry => entry.Task.IsCompleted).OrderBy(static entry => entry.LastAccess).ToArray();
        foreach (var entry in completed.Take(Math.Max(0, completed.Length - _maxCompleted)))
        {
            _operations.Remove(entry.Token);
            entry.Cancellation.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LongRunningToolCallStore));
    }

    private sealed class OperationEntry(string token, LongRunningToolCallRequest request, CancellationToken lifetimeToken, DateTimeOffset lastAccess, TimeSpan runningIdleTtl)
    {
        internal string Token { get; } = token;
        internal LongRunningToolCallRequest Request { get; } = request;
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        internal DateTimeOffset LastAccess { get; set; } = lastAccess;
        internal DateTimeOffset IdleDeadline { get; private set; } = lastAccess + runningIdleTtl;
        internal Task<CallToolResult> Task { get; set; } = System.Threading.Tasks.Task.FromResult(new CallToolResult());
        internal void ResetIdleDeadline(TimeSpan ttl)
        {
            IdleDeadline = DateTimeOffset.UtcNow + ttl;
            Cancellation.CancelAfter(ttl);
        }
        internal bool Matches(LongRunningToolCallRequest other) =>
            string.Equals(Request.ToolName, other.ToolName, StringComparison.Ordinal)
            && string.Equals(Request.Target, other.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Request.ArgumentsKey, other.ArgumentsKey, StringComparison.Ordinal);
    }
}

/// <summary>Stores immutable text snapshots and opaque continuation pages with bounded lifetime and memory.</summary>
internal sealed class McpResponseContinuationStore : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PageEntry> _pages = new(StringComparer.Ordinal);
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
            var budgetKey = (request.MaxResponseBytes, request.MaxResponseTokens);
            if (page.Results.TryGetValue(budgetKey, out var cached)) return cached;
            if (page.Results.Count >= _maxBudgetVariants)
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "This continuation token reached its response-budget variant limit.", "Reuse a previously used budget pair or start a narrower query.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var result = CreatePage(page.SnapshotId, page.Snapshot, page.Offset, request);
            page.Results.Add(budgetKey, result);
            return result;
        }
    }

    internal CallToolResult CreateFirstPage(CallToolResult result, LongRunningToolCallRequest request)
    {
        var source = ExtractText(result);
        if (result.IsError == true)
        {
            var errorText = McpToolResults.NormalizeExistingResult(result);
            var bytes = Encoding.UTF8.GetByteCount(errorText);
            var tokens = McpResponseFormatter.CountTokens(errorText);
            if (bytes <= request.MaxResponseBytes && (request.MaxResponseTokens is null || tokens <= request.MaxResponseTokens.Value)) return result;
            return McpToolResults.Recoverable("TOOL_ERROR_TOO_LARGE", "The tool error exceeded the requested response budget.", "Retry with a larger response budget.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }
        if (result.StructuredContent.HasValue)
        {
            var complete = McpResponseFormatter.Format(source, request.MaxResponseBytes, request.MaxResponseTokens,
                responsePrefix: McpToolResults.SuccessStatusPrefix);
            if (complete.ErrorCode is null && !complete.IsTruncated)
                return McpToolResults.Success(source, result.StructuredContent, request.MaxResponseBytes, request.MaxResponseTokens);
            return McpToolResults.Recoverable("STRUCTURED_RESULT_TOO_LARGE", "Structured content cannot be returned with a partial text page.", "Narrow the query or increase the response budget.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        }
        return CreatePage(Guid.NewGuid().ToString("N"), source, offset: 0, request);
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _pages.Clear(); }
    }

    private CallToolResult CreatePage(string snapshotId, string source, int offset, LongRunningToolCallRequest request)
    {
        var complete = McpResponseFormatter.Format(source, request.MaxResponseBytes, request.MaxResponseTokens,
            startOffset: offset, responsePrefix: McpToolResults.SuccessStatusPrefix);
        if (complete.ErrorCode is null && !complete.IsTruncated)
            return McpToolResults.Success(source[offset..], maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
        var token = Guid.NewGuid().ToString("N");
        var prefix = McpToolResults.TruncatedSuccessStatusPrefix + $"continuationToken={token}\n";
        var page = McpResponseFormatter.Format(source, request.MaxResponseBytes, request.MaxResponseTokens,
            startOffset: offset, responsePrefix: prefix);
        if (page.ErrorCode is not null)
            return McpToolResults.BudgetTooSmall(page, request.MaxResponseBytes, request.MaxResponseTokens);
        if (!page.IsTruncated || page.NextOffset is null)
            return McpToolResults.Success(source[offset..], maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);

        var text = page.ContinuationHint is { } hint && page.Text.EndsWith("\n" + hint, StringComparison.Ordinal)
            ? page.Text[..^(hint.Length + 1)]
            : page.Text;
        var response = McpToolResults.TextResult(text, isError: false);
        lock (_gate)
        {
            Expire();
            if (_disposed) return McpToolResults.Recoverable("CONTINUATION_EXPIRED", "Continuation storage is no longer available.", "Repeat the original tool call.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            var snapshotBytes = Encoding.UTF8.GetByteCount(source);
            var related = _pages.Values.Where(p => p.SnapshotId == snapshotId).Select(p => p.SnapshotBytes).FirstOrDefault();
            var snapshotCount = _pages.Values.Select(static p => p.SnapshotId).Distinct(StringComparer.Ordinal).Count();
            var storedBytes = _pages.Values.Select(static p => (p.SnapshotId, p.SnapshotBytes)).Distinct().Sum(static p => (long)p.SnapshotBytes);
            if (related == 0 && (snapshotCount >= _maxSnapshots || storedBytes + snapshotBytes > _maxSnapshotBytes))
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "The continuation store is at its snapshot limit.", "Retry with a narrower query or after older pages expire.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            if (_pages.Count >= _maxPages)
                return McpToolResults.Recoverable("CONTINUATION_CAPACITY", "The continuation store reached its page-token limit.", "Retry with a narrower query or after older pages expire.", maxResponseBytes: request.MaxResponseBytes, maxResponseTokens: request.MaxResponseTokens);
            _pages[token] = new PageEntry(token, snapshotId, source, page.NextOffset.Value, request, snapshotBytes, DateTimeOffset.UtcNow);
        }
        return response;
    }

    private static string ExtractText(CallToolResult result)
    {
        if (result.IsError == true) return McpToolResults.NormalizeExistingResult(result);
        var text = McpToolResults.NormalizeExistingResult(result);
        if (text.StartsWith(McpToolResults.SuccessStatusPrefix, StringComparison.Ordinal)) text = text[McpToolResults.SuccessStatusPrefix.Length..];
        if (text.StartsWith(McpToolResults.TruncatedSuccessStatusPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("A truncated result cannot be used as a complete continuation snapshot.");
        return text;
    }

    private void Expire()
    {
        var cutoff = DateTimeOffset.UtcNow - _idleTtl;
        foreach (var key in _pages.Where(pair => pair.Value.LastAccess < cutoff).Select(static pair => pair.Key).ToArray()) _pages.Remove(key);
    }

    private sealed class PageEntry(string token, string snapshotId, string snapshot, int offset, LongRunningToolCallRequest request, int snapshotBytes, DateTimeOffset lastAccess)
    {
        internal string SnapshotId { get; } = snapshotId;
        internal string RootSnapshotId { get; } = snapshotId;
        internal string Tool { get; } = request.ToolName;
        internal string Target { get; } = request.Target;
        internal string Arguments { get; } = request.ArgumentsKey;
        internal int SnapshotBytes { get; } = snapshotBytes;
        internal DateTimeOffset LastAccess { get; set; } = lastAccess;
        internal bool Matches(LongRunningToolCallRequest other) =>
            string.Equals(Tool, other.ToolName, StringComparison.Ordinal)
            && string.Equals(Target, other.Target, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Arguments, other.ArgumentsKey, StringComparison.Ordinal);
        internal Dictionary<(int Bytes, int? Tokens), CallToolResult> Results { get; } = [];
        internal string Token { get; } = token;
        internal string Snapshot { get; } = snapshot;
        internal int Offset { get; } = offset;
    }
}
