#nullable enable

using System;
using System.Collections.Generic;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Central registry for ephemeral handoff handles.
/// Manages the one-to-one mapping between internal navigation IDs and short external opaque handles (h:...).
/// </summary>
public sealed class HandoffHandleRegistry
{
    private static readonly Lazy<HandoffHandleRegistry> DefaultInstance =
        new(() => new HandoffHandleRegistry(HandoffCounterStore.Default));

    private readonly Dictionary<string, string> internalToExternal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> externalToInternal = new(StringComparer.Ordinal);
    private readonly Lock syncLock = new();
    private readonly IHandoffCounterStore counterStore;

    public HandoffHandleRegistry() : this(HandoffCounterStore.Default)
    {
    }

    public HandoffHandleRegistry(IHandoffCounterStore counterStore)
    {
        ArgumentNullException.ThrowIfNull(counterStore);
        this.counterStore = counterStore;
    }

    public static HandoffHandleRegistry Default => DefaultInstance.Value;

    public int Count
    {
        get
        {
            lock (syncLock)
            {
                return internalToExternal.Count;
            }
        }
    }

    /// <summary>
    /// Creates or returns a compact, opaque handle for an internal handoff ID.
    /// </summary>
    public Result<string> GetOrCreateOpaqueHandleForOutput(string internalHandoffId)
    {
        if (string.IsNullOrEmpty(internalHandoffId))
        {
            return Result<string>.Failure(
                NavigationErrorCodes.InvalidArgument,
                "The internal handoff ID must not be empty.");
        }

        lock (syncLock)
        {
            if (internalToExternal.TryGetValue(internalHandoffId, out var existingHandle))
            {
                return Result<string>.Success(existingHandle);
            }
        }

        var nextCounterResult = counterStore.Next();
        if (!nextCounterResult.IsSuccess)
        {
            return Result<string>.Failure(nextCounterResult.Error!.Value);
        }

        var counter = nextCounterResult.Value!;
        if (!HandoffCounterAlphabet.IsValidCounter(counter))
        {
            return Result<string>.Failure(
                NavigationErrorCodes.HandoffCounterUnavailable,
                "The counter store returned an invalid handoff counter.");
        }

        var handle = HandoffCounterAlphabet.FormatHandle(counter);
        lock (syncLock)
        {
            if (internalToExternal.TryGetValue(internalHandoffId, out var existingHandle))
            {
                return Result<string>.Success(existingHandle);
            }

            if (externalToInternal.ContainsKey(handle))
            {
                return Result<string>.Failure(
                    NavigationErrorCodes.HandoffCounterUnavailable,
                    "The counter store returned a previously used handoff counter.");
            }

            internalToExternal.Add(internalHandoffId, handle);
            externalToInternal.Add(handle, internalHandoffId);
            return Result<string>.Success(handle);
        }
    }

    public string GetOpaqueHandleForOutputOrThrow(string internalHandoffId)
    {
        var result = GetOrCreateOpaqueHandleForOutput(internalHandoffId);
        if (result.IsSuccess)
        {
            return result.Value!;
        }

        throw new InvalidOperationException(
            $"Handoff output could not be created ({result.Error!.Value.Code}).");
    }

    /// <summary>
    /// Restores an external handoff handle to an internal handoff ID or passes semantic inputs through unchanged.
    /// </summary>
    public Result<string> RestoreInternalHandoffForInput(string externalHandleOrSemanticInput)
    {
        if (string.IsNullOrEmpty(externalHandleOrSemanticInput))
        {
            return Result<string>.Success(externalHandleOrSemanticInput);
        }

        if (HandoffCounterAlphabet.IsWindowsDrivePath(externalHandleOrSemanticInput))
        {
            return Result<string>.Success(externalHandleOrSemanticInput);
        }

        if (externalHandleOrSemanticInput.StartsWith(HandoffCounterAlphabet.HandlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!HandoffCounterAlphabet.IsValidHandle(externalHandleOrSemanticInput))
            {
                return Result<string>.Failure(
                    NavigationErrorCodes.InvalidHandoff,
                    $"The handoff handle '{externalHandleOrSemanticInput}' is syntactically invalid.",
                    hint: "Use a valid handle from the current tool response (format: h:...).");
            }

            lock (syncLock)
            {
                if (externalToInternal.TryGetValue(externalHandleOrSemanticInput, out var internalId))
                {
                    return Result<string>.Success(internalId);
                }
            }

            return Result<string>.Failure(
                NavigationErrorCodes.HandoffUnknown,
                $"The handoff handle '{externalHandleOrSemanticInput}' is unknown. The MCP host may have restarted.",
                hint: "Find the symbol again using find_symbol, get_file_skeleton or a suitable producer.");
        }

        return Result<string>.Success(externalHandleOrSemanticInput);
    }
}
