#nullable enable

using System;
using System.Collections.Generic;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Zentrale Registry für flüchtige Handoff-Handles.
/// Verwaltet die 1:1-Bijektion zwischen internen Navigations-IDs und kurzen externen Opaque-Handles (h:...).
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
    /// Erzeugt oder liefert ein kompaktes, opaques Handle für eine interne Handoff-ID.
    /// </summary>
    public Result<string> GetOrCreateOpaqueHandleForOutput(string internalHandoffId)
    {
        if (string.IsNullOrEmpty(internalHandoffId))
        {
            return Result<string>.Failure(
                NavigationErrorCodes.InvalidArgument,
                "Die interne Handoff-ID darf nicht leer sein.");
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
                "Der Counter-Speicher hat einen ungültigen Handoff-Zähler geliefert.");
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
                    "Der Counter-Speicher hat einen bereits verwendeten Handoff-Zähler geliefert.");
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
            $"Handoff-Ausgabe konnte nicht erzeugt werden ({result.Error!.Value.Code}).");
    }

    /// <summary>
    /// Restauriert ein externes Handoff-Handle zu einer internen Handoff-ID oder reicht semantische Eingaben unverändert durch.
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
                    $"Das Handoff-Handle '{externalHandleOrSemanticInput}' ist syntaktisch ungültig.",
                    hint: "Ein gültiges Handle aus der aktuellen Tool-Antwort verwenden (Format: h:...).");
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
                $"Das Handoff-Handle '{externalHandleOrSemanticInput}' ist unbekannt. Der MCP-Host wurde möglicherweise neu gestartet.",
                hint: "Bitte das Symbol über find_symbol, get_file_skeleton oder einen passenden Producer erneut ermitteln.");
        }

        return Result<string>.Success(externalHandleOrSemanticInput);
    }
}
