#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Symbols;

public interface IHandoffCounterStore
{
    Result<string> Next();
}

public sealed record HandoffCounterState(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("lastIssued")] string LastIssued);

/// <summary>
/// Manages the persistent high-water mark of the alphabetic handoff counter with an internal prefetch buffer.
/// Prevents reuse of handle values across process restarts and parallel hosts.
/// </summary>
public sealed class HandoffCounterStore : IHandoffCounterStore
{
    public const int DefaultBatchSize = 1000;
    private static readonly Lazy<HandoffCounterStore> DefaultStore = new(() => new HandoffCounterStore(DefaultFilePath));
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock inProcessGate = new();
    private readonly Queue<string> preallocatedBuffer = new();
    private readonly string filePath;
    private readonly string lockFilePath;
    private readonly int batchSize;
    private readonly TimeSpan lockTimeout;

    public HandoffCounterStore() : this(DefaultFilePath, DefaultBatchSize)
    {
    }

    public HandoffCounterStore(string filePath, int batchSize = DefaultBatchSize, TimeSpan? lockTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("The counter file path must not be empty.", nameof(filePath));
        }

        this.filePath = Path.GetFullPath(filePath);
        this.lockFilePath = this.filePath + ".lock";
        this.batchSize = batchSize > 0 ? batchSize : DefaultBatchSize;
        this.lockTimeout = lockTimeout is { } timeout && timeout > TimeSpan.Zero ? timeout : DefaultLockTimeout;
    }

    public static HandoffCounterStore Default => DefaultStore.Value;

    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RalfHuesing",
            "AiNetCodeNavigator",
            "handoff-counter.json");

    public int BufferedCount
    {
        get
        {
            lock (inProcessGate)
            {
                return preallocatedBuffer.Count;
            }
        }
    }

    public Result<string> Next()
    {
        lock (inProcessGate)
        {
            if (preallocatedBuffer.Count > 0)
            {
                return Result<string>.Success(preallocatedBuffer.Dequeue());
            }

            var refillResult = RefillBufferUnderLock();
            if (!refillResult.IsSuccess)
            {
                return refillResult;
            }

            return Result<string>.Success(preallocatedBuffer.Dequeue());
        }
    }

    private Result<string> RefillBufferUnderLock()
    {
        using var lockStream = AcquireCrossProcessLock(lockTimeout);
        if (lockStream is null)
        {
            return Result<string>.Failure(
                NavigationErrorCodes.HandoffCounterUnavailable,
                "The high-water mark file could not be locked to allocate a handle.");
        }

        var batchResult = GenerateNextBatch(batchSize);
        if (!batchResult.IsSuccess)
        {
            return Result<string>.Failure(batchResult.Error!.Value);
        }

        var batch = batchResult.Value!;
        var persistResult = PersistCounterAtomically(batch[^1]);
        if (!persistResult.IsSuccess)
        {
            return persistResult;
        }

        foreach (var counter in batch)
        {
            preallocatedBuffer.Enqueue(counter);
        }

        return Result<string>.Success(batch[0]);
    }

    private Result<IReadOnlyList<string>> GenerateNextBatch(int count)
    {
        if (!File.Exists(filePath))
        {
            var initialBatch = new List<string>(count) { HandoffCounterAlphabet.FirstCounter };
            var currentCounter = HandoffCounterAlphabet.FirstCounter;
            for (var i = 1; i < count; i++)
            {
                currentCounter = HandoffCounterAlphabet.GetNext(currentCounter);
                initialBatch.Add(currentCounter);
            }

            return Result<IReadOnlyList<string>>.Success(initialBatch);
        }

        try
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Result<IReadOnlyList<string>>.Failure(
                    NavigationErrorCodes.HandoffCounterUnavailable,
                    "The handoff counter file is empty or corrupted.");
            }

            HandoffCounterState? state;
            try
            {
                state = JsonSerializer.Deserialize<HandoffCounterState>(json);
            }
            catch (JsonException)
            {
                return Result<IReadOnlyList<string>>.Failure(
                    NavigationErrorCodes.HandoffCounterUnavailable,
                    "The handoff counter file does not contain valid JSON.");
            }

            if (state is null || state.FormatVersion != 1 || !HandoffCounterAlphabet.IsValidCounter(state.LastIssued))
            {
                return Result<IReadOnlyList<string>>.Failure(
                    NavigationErrorCodes.HandoffCounterUnavailable,
                    "The handoff counter file contains an invalid format version or an inconsistent counter value.");
            }

            var batch = new List<string>(count);
            var current = state.LastIssued;
            for (var i = 0; i < count; i++)
            {
                current = HandoffCounterAlphabet.GetNext(current);
                batch.Add(current);
            }

            return Result<IReadOnlyList<string>>.Success(batch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<string>>.Failure(
                NavigationErrorCodes.HandoffCounterUnavailable,
                $"Error reading the high-water mark file: {ex.Message}");
        }
    }

    private Result<string> PersistCounterAtomically(string nextCounter)
    {
        var directory = Path.GetDirectoryName(filePath);
        var tempFilePath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var updatedState = new HandoffCounterState(1, nextCounter);
            var updatedJson = JsonSerializer.Serialize(updatedState, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempFilePath, updatedJson);
            File.Move(tempFilePath, filePath, overwrite: true);
            return Result<string>.Success(nextCounter);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(
                NavigationErrorCodes.HandoffCounterUnavailable,
                $"Error writing the high-water mark file: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    private FileStream? AcquireCrossProcessLock(TimeSpan timeout)
    {
        var directory = Path.GetDirectoryName(lockFilePath);
        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return null;
        }

        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeout.TotalMilliseconds)
        {
            try
            {
                return new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException)
            {
                Thread.Sleep(15);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(15);
            }
        }

        return null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }
}
