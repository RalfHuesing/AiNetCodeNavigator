#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AiNetCodeNavigator.Core.Assemblies;

public static class AssemblyPaging
{
    public enum BoundCursorStatus { Valid, InvalidFormat, StaleBinding }

    public static int ReadOffset(string? cursor) =>
        TryReadUnboundOffset(cursor, out var offset) ? offset : 0;

    public static string CreateToken(int offset) => offset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string CreateToken(int offset, string binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        return $"v1.{Math.Max(0, offset).ToString(System.Globalization.CultureInfo.InvariantCulture)}.{binding}";
    }

    public static string CreateBinding(string canonicalPath, string contentHash, params string?[] queryParts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var material = string.Join("\u001f", new[] { canonicalPath, contentHash }.Concat(queryParts));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    public static string CreateInspectBinding(
        string canonicalPath,
        string contentHash,
        string referenceSnapshotHash,
        InspectAssemblyRequest arguments) =>
        CreateBinding(
            canonicalPath,
            contentHash,
            referenceSnapshotHash,
            "inspect_assembly",
            arguments.Namespace,
            arguments.TypeName,
            arguments.MemberName,
            arguments.PublicOnly.ToString(),
            arguments.MaxResults.ToString(),
            arguments.ExactTypeName.ToString(),
            arguments.MemberNames is null ? null : string.Join("\u001e", arguments.MemberNames),
            arguments.MaxMembers.ToString(),
            arguments.IncludeReferences?.ToString());

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
            arguments.SearchKind,
            arguments.Query,
            arguments.CaseSensitive.ToString(),
            arguments.UseRegex?.ToString(),
            arguments.FileFilter,
            arguments.DeclarationOnly.ToString(),
            arguments.ContextLines.ToString(),
            arguments.MaxResults.ToString(),
            arguments.MaxFiles.ToString(),
            arguments.Kind);

    public static bool TryReadBoundOffset(string? cursor, string binding, out int offset)
        => ReadBoundOffset(cursor, binding, out offset) == BoundCursorStatus.Valid;

    public static BoundCursorStatus ReadBoundOffset(string? cursor, string binding, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return BoundCursorStatus.Valid;

        var parts = cursor.Split('.', 3, StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !int.TryParse(parts[1], out offset)
            || offset < 0
            || parts[2].Length != binding.Length)
        {
            offset = 0;
            return BoundCursorStatus.InvalidFormat;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[2]), Encoding.ASCII.GetBytes(binding))
            ? BoundCursorStatus.Valid
            : BoundCursorStatus.StaleBinding;
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
