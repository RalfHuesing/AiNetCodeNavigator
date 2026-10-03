#nullable enable

using System;
using System.Linq;
using AiNetCodeNavigator.Core.Common;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class AssemblyPaging
{
    public enum BoundCursorStatus { Valid, InvalidFormat, StaleBinding }

    public static int ReadOffset(string? cursor) =>
        TryReadUnboundOffset(cursor, out var offset) ? offset : 0;

    public static string CreateToken(int offset) => offset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string CreateToken(int offset, string binding)
    {
        return BoundResultCursor.CreateToken(offset, binding);
    }

    public static string CreateBinding(string canonicalPath, string contentHash, params string?[] queryParts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        return BoundResultCursor.CreateBinding(canonicalPath, contentHash, "assembly", queryParts);
    }

    public static string CreateInspectBinding(
        string canonicalPath,
        string contentHash,
        string referenceSnapshotHash,
        InspectAssemblyRequest arguments)
    {
        var queryParts = new string?[]
        {
            referenceSnapshotHash, "inspect_assembly", arguments.Namespace, arguments.TypeName, arguments.MemberName,
            arguments.PublicOnly.ToString(), arguments.MaxResults.ToString(), arguments.ExactTypeName.ToString(),
            arguments.MemberNames?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            arguments.IncludeReferences.ToString(),
        }.Concat(arguments.MemberNames?.Cast<string?>() ?? []).ToArray();
        return CreateBinding(canonicalPath, contentHash, queryParts);
    }

    public static string CreateSearchBinding(
        string canonicalPath,
        string contentHash,
        string referenceSnapshotHash,
        AssemblySearchRequest arguments) =>
        CreateBinding(
            canonicalPath,
            contentHash,
            referenceSnapshotHash,
            "search_assembly",
            arguments.Query,
            arguments.CaseSensitive.ToString(),
            arguments.UseRegex.ToString(),
            arguments.FileFilter,
            arguments.DeclarationOnly.ToString(),
            arguments.ContextLines.ToString(),
            arguments.MaxResults.ToString(),
            arguments.MaxFiles.ToString(),
            arguments.Kind);

    public static string CreateExtensionsBinding(
        string canonicalPath,
        string contentHash,
        string referenceSnapshotHash,
        FindAssemblyExtensionsRequest arguments) =>
        CreateBinding(canonicalPath, contentHash, referenceSnapshotHash, "find_assembly_extensions",
            arguments.ReceiverType, arguments.ExtensionName, arguments.Namespace, arguments.IncludeReferences.ToString(),
            arguments.MaxResults.ToString());

    public static bool TryReadBoundOffset(string? cursor, string binding, out int offset)
        => ReadBoundOffset(cursor, binding, out offset) == BoundCursorStatus.Valid;

    public static BoundCursorStatus ReadBoundOffset(string? cursor, string binding, out int offset)
    {
        return BoundResultCursor.ReadOffset(cursor, binding, out offset) switch
        {
            BoundResultCursor.CursorStatus.Valid => BoundCursorStatus.Valid,
            BoundResultCursor.CursorStatus.InvalidFormat => BoundCursorStatus.InvalidFormat,
            _ => BoundCursorStatus.StaleBinding,
        };
    }

    public static bool TryReadUnboundOffset(string? cursor, out int offset)
    {
        if (int.TryParse(cursor?.Trim(), out offset) && offset >= 0) return true;

        var parts = cursor?.Split('.', 3, StringSplitOptions.None);
        if (parts is { Length: 3 }
            && string.Equals(parts[0], "v1", StringComparison.Ordinal)
            && int.TryParse(parts[1], out offset)
            && offset >= 0)
        {
            return true;
        }

        offset = 0;
        return false;
    }
}
