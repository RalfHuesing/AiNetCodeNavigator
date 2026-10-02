#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AiNetCodeNavigator.Core.Assemblies;

internal static class AssemblyReferenceSnapshotFingerprint
{
    internal static string Create(AssemblyReferenceResolution resolution)
        => Create(resolution, useCapturedContent: true);

    internal static string CreateForCurrentContent(AssemblyReferenceResolution resolution)
        => Create(resolution, useCapturedContent: false);

    private static string Create(AssemblyReferenceResolution resolution, bool useCapturedContent)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var reference in resolution.References
                     .OrderBy(item => item.Name, StringComparer.Ordinal)
                     .ThenBy(item => item.Version, StringComparer.Ordinal)
                     .ThenBy(item => item.Culture, StringComparer.Ordinal)
                     .ThenBy(item => item.ResolvedPath, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, reference.Name);
            Append(hash, reference.Version);
            Append(hash, reference.Culture);
            Append(hash, reference.PublicKeyToken);
            Append(hash, reference.SourceAssemblyPath ?? string.Empty);
            Append(hash, reference.Resolved.ToString());
            Append(hash, reference.ResolutionState);
            Append(hash, reference.ResolvedPath ?? string.Empty);
            Append(hash, reference.Diagnostic ?? string.Empty);

            if (reference.ResolvedPath is null) continue;
            if (useCapturedContent && reference.ContentHash is { Length: > 0 } contentHash)
            {
                Append(hash, contentHash);
                continue;
            }

            if (AssemblyFingerprintCalculator.TryCreate(reference.ResolvedPath, out var fingerprint, out var diagnostic)
                && fingerprint is not null)
            {
                Append(hash, fingerprint.Sha256);
            }
            else
            {
                Append(hash, diagnostic?.Message ?? "reference-unreadable");
            }
        }

        foreach (var diagnostic in resolution.Diagnostics
                     .Select(item => item.Code + "\0" + item.Message)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            Append(hash, diagnostic);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
