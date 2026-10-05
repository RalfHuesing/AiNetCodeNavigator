using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiNetCodeNavigator.Configuration;
using AiNetCodeNavigator.Mcp.Formatting;

namespace AiNetCodeNavigator.Mcp.TrafficCapture;

/// <summary>Stores raw MCP tool-call frames and bounded session metadata under the configured log directory.</summary>
internal sealed class TrafficCaptureSession : IAsyncDisposable
{
    private const string QuotaLockName = ".traffic-capture-quota.lock";
    private const string SessionLockName = ".active.lock";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex StatusPattern = new(@"(?:^|\n)Status:\s*operation=(ok|error|retry|running)(?=\s|/|,|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex MetadataPattern = new(@"(?:^|\n)(continuationToken|operationToken|resultCursor)=([^\s,]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly object _gate = new();
    private readonly string _trafficRoot;
    private readonly string _sessionDirectory;
    private readonly TrafficCaptureOptions _options;
    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly FileStream _sessionLock;
    private readonly Dictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private int _sequence;
    private bool _capturing = true;
    private string? _captureStoppedReason;
    private bool _closed;
    private string? _clientName;
    private string? _clientVersion;

    internal TrafficCaptureSession(
        string logsDirectory,
        TrafficCaptureOptions options,
        string serverName,
        string serverVersion,
        Action<string>? onSessionLockAcquired = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverVersion);
        _options = options;
        _serverName = serverName;
        _serverVersion = serverVersion;

        _trafficRoot = Path.Combine(logsDirectory, "traffic");
        EnsureSafeDirectory(_trafficRoot);
        WithQuotaLock(() => PruneSessions(DateTimeOffset.UtcNow));
        SessionId = Guid.NewGuid();
        _sessionDirectory = Path.Combine(_trafficRoot, SessionId.ToString("D"));
        Directory.CreateDirectory(_sessionDirectory);
        _sessionLock = new FileStream(Path.Combine(_sessionDirectory, SessionLockName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _sessionLock.Write(Encoding.UTF8.GetBytes(SessionId.ToString("D")));
            _sessionLock.Flush(flushToDisk: true);
            onSessionLockAcquired?.Invoke(_sessionDirectory);
            WriteSessionMetadata();
        }
        catch
        {
            _sessionLock.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    internal Guid SessionId { get; }

    internal bool IsCapturing
    {
        get { lock (_gate) return _capturing; }
    }

    internal long MaximumFrameBytes => Math.Min(Math.Max(1, _options.MaxTotalBytes), 1024 * 1024);

    internal void StopForOversizedFrame() => StopCapture("maximumFrameBytesReached");

    internal void RecordInbound(ReadOnlyMemory<byte> frame)
    {
        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array) return;

            RecordInboundMessage(root, frame);
        }
        catch
        {
            // A malformed or non-JSON frame is outside the tool-call capture contract.
        }
    }

    internal void RecordOutbound(ReadOnlyMemory<byte> frame)
    {
        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array) return;

            RecordOutboundMessage(root, frame);
        }
        catch
        {
            // A malformed or non-JSON frame is outside the tool-call capture contract.
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            foreach (var pending in _pending.Values.ToArray()) FinalizeWithoutResponse(pending, "incomplete");

            _pending.Clear();
            _capturing = false;
            _closed = true;
            try { WriteSessionMetadata(); } catch { }
        }

        await _sessionLock.DisposeAsync().ConfigureAwait(false);
    }

    private void RecordInboundMessage(JsonElement message, ReadOnlyMemory<byte> frame)
    {
        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var method = methodElement.GetString();
        if (string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            ReadClientInfo(message);
            return;
        }

        if (string.Equals(method, "notifications/cancelled", StringComparison.Ordinal))
        {
            MarkCancellation(message);
            return;
        }

        if (!string.Equals(method, "tools/call", StringComparison.Ordinal)
            || !message.TryGetProperty("id", out var idElement))
        {
            return;
        }

        var requestId = GetIdKey(idElement);
        var rawRequestId = idElement.Clone();
        var rawToolName = message.TryGetProperty("params", out var parameters) ? ReadString(parameters, "name") : null;
        var toolName = SanitizeToolName(rawToolName ?? "unknown_tool");
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var arguments = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var args) ? args : default;
        var operationToken = ReadString(arguments, "operationToken");
        var continuationToken = ReadString(arguments, "continuationToken");
        var resultCursor = ReadString(arguments, "resultCursor");

        lock (_gate)
        {
            if (!_capturing || _pending.Count >= 2048)
            {
                if (_pending.Count >= 2048) StopCapture();
                return;
            }

            var sequence = ++_sequence;
            var callId = Guid.NewGuid();
            var directory = Path.Combine(_sessionDirectory, toolName, callId.ToString("D"));
            try
            {
                if (!TryWriteFile(Path.Combine(directory, "input.json"), frame.Span))
                {
                    StopCapture();
                    return;
                }

                _pending[requestId] = new PendingCall(
                    requestId,
                    rawRequestId,
                    toolName,
                    rawToolName,
                    callId,
                    sequence,
                    directory,
                    startedAtUtc,
                    stopwatch,
                    frame.Length,
                    operationToken,
                    continuationToken,
                    resultCursor);
            }
            catch
            {
                StopCapture();
            }
        }
    }

    private void RecordOutboundMessage(JsonElement message, ReadOnlyMemory<byte> frame)
    {
        if (!message.TryGetProperty("id", out var idElement))
        {
            return;
        }

        var requestId = GetIdKey(idElement);
        lock (_gate)
        {
            if (!_capturing || !_pending.Remove(requestId, out var pending))
            {
                return;
            }

            try
            {
                if (!TryWriteFile(Path.Combine(pending.Directory, "output.json"), frame.Span))
                {
                    StopCapture();
                    return;
                }

                var response = AnalyzeResponse(message);
                pending.Stopwatch.Stop();
                var summary = CreateSummary(pending, response, frame.Length, "success");
                if (!WriteJson(Path.Combine(pending.Directory, "summary.json"), summary)) return;
                AppendCallIndex(summary);
            }
            catch
            {
                StopCapture();
            }
        }
    }

    private void ReadClientInfo(JsonElement message)
    {
        try
        {
            if (message.TryGetProperty("params", out var parameters)
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("clientInfo", out var clientInfo))
            {
                lock (_gate)
                {
                    _clientName = ReadString(clientInfo, "name");
                    _clientVersion = ReadString(clientInfo, "version");
                    if (_capturing) WriteSessionMetadata();
                }
            }
        }
        catch
        {
            StopCapture();
        }
    }

    private void MarkCancellation(JsonElement message)
    {
        if (!message.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("requestId", out var idElement))
        {
            return;
        }

        lock (_gate)
        {
            if (_pending.TryGetValue(GetIdKey(idElement), out var pending)) pending.Cancelled = true;
        }
    }

    private object CreateSummary(PendingCall call, ResponseAnalysis response, int? outputBytes, string outcome) => new
    {
        sessionId = SessionId,
        callId = call.CallId,
        sequence = call.Sequence,
        tool = call.OriginalToolName,
        toolDirectory = call.ToolName,
        requestId = call.RawRequestId,
        startedAtUtc = call.StartedAtUtc,
        completedAtUtc = DateTimeOffset.UtcNow,
        durationMs = call.Stopwatch.Elapsed.TotalMilliseconds,
        inputBytes = call.InputBytes,
        outputBytes,
        visibleTextBytes = response.VisibleTextBytes,
        visibleTextTokens = response.VisibleTextTokens,
        tokenEncoding = "cl100k_base",
        outcome = response.ErrorCode == "-32800"
            ? "cancelled"
            : response.ErrorMessage is not null || response.IsError ? "error" : outcome,
        errorCode = response.ErrorCode,
        errorMessage = response.ErrorMessage,
        cancellationRequested = call.Cancelled,
        operationToken = call.OperationToken,
        continuationToken = call.ContinuationToken,
        resultCursor = call.ResultCursor,
        operation = response.Operation,
        outgoingOperationToken = response.OperationToken,
        outgoingContinuationToken = response.ContinuationToken,
        outgoingResultCursor = response.ResultCursor
    };

    private void FinalizeWithoutResponse(PendingCall pending, string outcome)
    {
        try
        {
            pending.Stopwatch.Stop();
            var summary = CreateSummary(pending, ResponseAnalysis.Empty, null, outcome);
            WriteJson(Path.Combine(pending.Directory, "summary.json"), summary);
            AppendCallIndex(summary);
        }
        catch
        {
            _capturing = false;
        }
    }

    private ResponseAnalysis AnalyzeResponse(JsonElement message)
    {
        if (message.TryGetProperty("error", out var error))
        {
            return new ResponseAnalysis(0, 0, false, null,
                error.TryGetProperty("code", out var code) ? code.ToString() : null,
                ReadString(error, "message"), null, null, null);
        }

        if (!message.TryGetProperty("result", out var result)) return ResponseAnalysis.Empty;
        var isError = result.TryGetProperty("isError", out var isErrorElement) && isErrorElement.ValueKind == JsonValueKind.True;
        var texts = new List<string>();
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (string.Equals(ReadString(item, "type"), "text", StringComparison.Ordinal)
                    && item.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    texts.Add(textElement.GetString() ?? string.Empty);
                }
            }
        }

        var visibleText = string.Join("\n", texts);
        var metadata = ReadVisibleMetadata(string.Join("\n", texts));
        var structured = result.TryGetProperty("structuredContent", out var structuredContent)
            ? ReadStructuredMetadata(structuredContent)
            : Metadata.Empty;
        return new ResponseAnalysis(
            Encoding.UTF8.GetByteCount(visibleText),
            visibleText.Length == 0 ? 0 : McpResponseFormatter.CountTokens(visibleText),
            isError,
            metadata.Operation ?? structured.Operation ?? (isError ? "error" : "ok"),
            null,
            isError ? visibleText : null,
            metadata.OperationToken ?? structured.OperationToken,
            metadata.ContinuationToken ?? structured.ContinuationToken,
            metadata.ResultCursor ?? structured.ResultCursor);
    }

    private static Metadata ReadVisibleMetadata(string text)
    {
        var statusMatch = StatusPattern.Match(text);
        string? operation = statusMatch.Success ? statusMatch.Groups[1].Value : null;
        string? operationToken = null;
        string? continuationToken = null;
        string? resultCursor = null;
        foreach (Match match in MetadataPattern.Matches(text))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            switch (key)
            {
                case "operationToken": operationToken ??= value; break;
                case "continuationToken": continuationToken ??= value; break;
                case "resultCursor": resultCursor ??= value; break;
            }
        }

        return new Metadata(operation, operationToken, continuationToken, resultCursor);
    }

    private static Metadata ReadStructuredMetadata(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return Metadata.Empty;
        return new Metadata(
            ReadString(value, "operation"),
            ReadString(value, "operationToken"),
            ReadString(value, "continuationToken"),
            ReadString(value, "resultCursor"));
    }

    private void WriteSessionMetadata()
    {
        var session = new
        {
            sessionId = SessionId,
            startedAtUtc = Directory.GetCreationTimeUtc(_sessionDirectory),
            server = new { name = _serverName, version = _serverVersion },
            client = _clientName is null && _clientVersion is null ? null : new { name = _clientName, version = _clientVersion },
            captureState = _captureStoppedReason is not null ? "stopped" : _closed ? "complete" : "capturing",
            captureStoppedReason = _captureStoppedReason
        };
        WriteJson(Path.Combine(_sessionDirectory, "session.json"), session);
    }

    private bool WriteJson(string path, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (!TryWriteFile(path, bytes))
        {
            StopCapture("maximumStorageBytesReached");
            return false;
        }

        return true;
    }

    private void AppendCallIndex(object summary)
    {
        var json = JsonSerializer.Serialize(summary, JsonOptions).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        var path = Path.Combine(_sessionDirectory, "calls.jsonl");
        var unsafePath = false;
        var quotaExceeded = false;
        WithQuotaLock(() =>
        {
            if (!IsSafeCapturePath(path))
            {
                unsafePath = true;
                return;
            }

            if (!HasQuota(bytes.Length, existingFileBytes: 0))
            {
                quotaExceeded = true;
                return;
            }

            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        });
        if (unsafePath) StopCapture("unsafeCapturePath");
        else if (quotaExceeded) StopCapture("maximumStorageBytesReached");
    }

    private bool TryWriteFile(string path, ReadOnlySpan<byte> bytes)
    {
        var materialized = bytes.ToArray();
        var written = false;
        var unsafePath = false;
        var quotaExceeded = false;
        WithQuotaLock(() =>
        {
            if (!IsSafeCapturePath(path))
            {
                unsafePath = true;
                return;
            }

            var existingBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (!HasQuota(materialized.Length, existingBytes))
            {
                quotaExceeded = true;
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!IsSafeCapturePath(path))
            {
                unsafePath = true;
                return;
            }

            File.WriteAllBytes(path, materialized);
            written = true;
        });
        if (unsafePath) StopCapture("unsafeCapturePath");
        else if (quotaExceeded) StopCapture("maximumStorageBytesReached");
        return written;
    }

    private bool IsSafeCapturePath(string path)
    {
        try
        {
            var root = Path.GetFullPath(_sessionDirectory);
            var fullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(root, fullPath);
            if (Path.IsPathRooted(relativePath)
                || relativePath.Equals("..", StringComparison.Ordinal)
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            var currentPath = root;
            if (IsReparsePoint(currentPath)) return false;
            foreach (var component in relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, component);
                if (IsReparsePoint(currentPath)) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private bool HasQuota(long newBytes, long existingFileBytes)
    {
        var total = GetStoredBytes(_trafficRoot);
        return total - existingFileBytes <= _options.MaxTotalBytes - newBytes;
    }

    private void StopCapture(string reason = "captureWriteFailed")
    {
        lock (_gate)
        {
            if (!_capturing) return;
            _capturing = false;
            _captureStoppedReason = reason;
            Serilog.Log.Warning("MCP traffic capture stopped for session {SessionId}: {Reason}", SessionId, reason);
            try { WriteSessionMetadata(); } catch { }
        }
    }

    private void WithQuotaLock(Action action)
    {
        FileStream? quotaLock = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                quotaLock = new FileStream(Path.Combine(_trafficRoot, QuotaLockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException) when (attempt < 199)
            {
                Thread.Sleep(10);
            }
        }

        if (quotaLock is null) throw new IOException("Could not acquire the traffic capture quota lock.");
        using (quotaLock) action();
    }

    private void PruneSessions(DateTimeOffset now)
    {
        var sessions = new List<DirectoryInfo>();
        foreach (var directory in Directory.EnumerateDirectories(_trafficRoot))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (!Guid.TryParseExact(info.Name, "D", out _)) continue;
            sessions.Add(info);
        }

        foreach (var session in sessions.OrderBy(static directory => directory.LastWriteTimeUtc))
        {
            if (IsSessionActive(session.FullName)) continue;
            var age = now - new DateTimeOffset(session.LastWriteTimeUtc, TimeSpan.Zero);
            if (age.TotalDays > _options.RetentionDays)
            {
                TryDeleteDirectory(session.FullName);
            }
        }

        foreach (var session in sessions.OrderBy(static directory => directory.LastWriteTimeUtc))
        {
            if (GetStoredBytes(_trafficRoot) <= _options.MaxTotalBytes) break;
            if (!Directory.Exists(session.FullName) || IsSessionActive(session.FullName)) continue;
            TryDeleteDirectory(session.FullName);
        }
    }

    private static bool IsSessionActive(string directory)
    {
        var lockPath = Path.Combine(directory, SessionLockName);
        if (!File.Exists(lockPath)) return false;
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void EnsureSafeDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Traffic capture directory cannot be a reparse point.");
        }
    }

    private static long GetStoredBytes(string root)
    {
        long total = 0;
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        while (pendingDirectories.TryPop(out var directory))
        {
            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) == 0) pendingDirectories.Push(childDirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(file), QuotaLockName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(file), SessionLockName, StringComparison.OrdinalIgnoreCase)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) total += new FileInfo(file).Length;
            }
        }

        return total;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch { /* Cleanup failure does not affect navigation; quota checks stop further capture if necessary. */ }
    }

    private static string SanitizeToolName(string toolName)
    {
        var sanitized = new string(toolName.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').Take(100).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized)) return "unknown_tool";
        if (sanitized is "." or "..") return "_" + sanitized.Replace('.', '_');
        var deviceName = sanitized.Split('.')[0];
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || (deviceName.Length == 4 && (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && deviceName[3] is >= '1' and <= '9'))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private static string GetIdKey(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => "s:" + (id.GetString() ?? string.Empty),
        JsonValueKind.Number when id.TryGetInt64(out var integer) => "n:" + integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.Number => "n:" + id.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => "x:" + id.GetRawText()
    };

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed class PendingCall(
        string requestId,
        JsonElement rawRequestId,
        string toolName,
        string? originalToolName,
        Guid callId,
        int sequence,
        string directory,
        DateTimeOffset startedAtUtc,
        Stopwatch stopwatch,
        int inputBytes,
        string? operationToken,
        string? continuationToken,
        string? resultCursor)
    {
        internal string RequestId { get; } = requestId;
        internal JsonElement RawRequestId { get; } = rawRequestId;
        internal string ToolName { get; } = toolName;
        internal string? OriginalToolName { get; } = originalToolName;
        internal Guid CallId { get; } = callId;
        internal int Sequence { get; } = sequence;
        internal string Directory { get; } = directory;
        internal DateTimeOffset StartedAtUtc { get; } = startedAtUtc;
        internal Stopwatch Stopwatch { get; } = stopwatch;
        internal int InputBytes { get; } = inputBytes;
        internal string? OperationToken { get; } = operationToken;
        internal string? ContinuationToken { get; } = continuationToken;
        internal string? ResultCursor { get; } = resultCursor;
        internal bool Cancelled { get; set; }
    }

    private sealed record Metadata(string? Operation, string? OperationToken, string? ContinuationToken, string? ResultCursor)
    {
        internal static Metadata Empty { get; } = new(null, null, null, null);
    }

    private sealed record ResponseAnalysis(
        int VisibleTextBytes,
        int VisibleTextTokens,
        bool IsError,
        string? Operation,
        string? ErrorCode,
        string? ErrorMessage,
        string? OperationToken,
        string? ContinuationToken,
        string? ResultCursor)
    {
        internal static ResponseAnalysis Empty { get; } = new(0, 0, false, null, null, null, null, null, null);
    }
}
