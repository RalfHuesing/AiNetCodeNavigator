#nullable enable

using System;
using System.Linq;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AiNetCodeNavigator.Core.Common;

/// <summary>Creates deterministic internal offsets bound to one target snapshot and result query.</summary>
public static class BoundResultCursor
{
    public enum CursorStatus { Valid, InvalidFormat, StaleBinding }

    public static string CreateBinding(string canonicalTarget, string snapshotId, string section, params string?[] queryParts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        using var stream = new System.IO.MemoryStream();
        Span<byte> length = stackalloc byte[4];
        var fields = new string?[] { canonicalTarget, snapshotId, section }.Concat(queryParts).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(length, fields.Length);
        stream.Write(length);
        foreach (var part in fields)
        {
            var bytes = part is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32LittleEndian(length, part is null ? -1 : bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public static string CreateToken(int offset, string binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        return $"v1.{Math.Max(0, offset).ToString(System.Globalization.CultureInfo.InvariantCulture)}.{binding}";
    }

    public static CursorStatus ReadOffset(string? cursor, string binding, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return CursorStatus.Valid;
        var parts = cursor.Split('.', 3, StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out offset)
            || offset < 0 || parts[2].Length != binding.Length)
        {
            offset = 0;
            return CursorStatus.InvalidFormat;
        }
        if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[2]), Encoding.ASCII.GetBytes(binding)))
            return CursorStatus.Valid;
        offset = 0;
        return CursorStatus.StaleBinding;
    }

    public static (T[] Items, int TotalCount, string? NextCursor) Page<T>(
        System.Collections.Generic.IReadOnlyList<T> entries, int offset, int pageSize, string binding)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        if (offset > entries.Count) throw new ArgumentOutOfRangeException(nameof(offset));
        var items = entries.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = offset + items.Length;
        return (items, entries.Count, nextOffset < entries.Count ? CreateToken(nextOffset, binding) : null);
    }
}
