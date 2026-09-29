#nullable enable

using System;
using System.IO;
using System.Text.Json;
using AiNetCodeNavigator.Core.Assemblies.Coordinators;

namespace AiNetCodeNavigator.Core.Assemblies;

internal sealed partial class AssemblyDecompilationCache
{
    private PointerPublishOutcome TryPublishPointer(
        string entryDirectory,
        string generationDirectory,
        AssemblyCachePublishRequest request,
        out AssemblySessionDiagnostic? diagnostic)
    {
        diagnostic = null;
        var pointerPath = Path.Combine(entryDirectory, AssemblyCacheContract.CurrentPointerFileName);
        var generationName = Path.GetFileName(generationDirectory);
        for (var attempt = 0; attempt < PointerPublishAttempts; attempt++)
        {
            var readRequest = new AssemblyCacheReadRequest(request.CacheKey, request.Fingerprint, request.References);
            if (TryRead(readRequest, out _, out _)) return PointerPublishOutcome.Existing;
            var attemptResult = PublishPointerAttempt(pointerPath, generationName, readRequest);
            if (attemptResult.Succeeded)
            {
                return attemptResult.GenerationPublished
                    ? PointerPublishOutcome.Published
                    : PointerPublishOutcome.Existing;
            }

            diagnostic = attemptResult.Diagnostic;
        }

        diagnostic ??= new(AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationCache), nameof(AssemblyCachePublishRequest)), "Current pointer could not be validly published after limited attempts.", AssemblyDiagnosticSeverity.Error);
        return PointerPublishOutcome.Failed;
    }

    private PointerPublishAttempt PublishPointerAttempt(
        string pointerPath,
        string generationName,
        AssemblyCacheReadRequest readRequest)
    {
        var temporaryPointer = pointerPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WritePointer(temporaryPointer, generationName);
            ReplacePointer(pointerPath, temporaryPointer);
            beforePointerValidation?.Invoke(
                Path.Combine(Path.GetDirectoryName(pointerPath)!, generationName));
            var succeeded = TryRead(readRequest, out _, out _);
            var generationPublished = succeeded
                && string.Equals(
                    Path.GetFileName(ReadPointer(Path.GetDirectoryName(pointerPath)!, pointerPath)),
                    generationName,
                    StringComparison.OrdinalIgnoreCase);
            return new(
                succeeded,
                generationPublished,
                succeeded ? null : new(AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationCache), nameof(AssemblyCacheContract.CurrentPointerFileName)), "The newly published current pointer could not be re-validated.", AssemblyDiagnosticSeverity.Warning));
        }
        catch (IOException ex)
        {
            var diagnostic = new AssemblySessionDiagnostic(AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationCache), nameof(AssemblyCacheContract.CurrentPointerFileName)), $"Current pointer could not be replaced: {ex.Message}", AssemblyDiagnosticSeverity.Warning);
            var succeeded = TryRead(readRequest, out _, out _);
            var generationPublished = succeeded
                && string.Equals(
                    Path.GetFileName(ReadPointer(Path.GetDirectoryName(pointerPath)!, pointerPath)),
                    generationName,
                    StringComparison.OrdinalIgnoreCase);
            return new(succeeded, generationPublished, diagnostic);
        }
        finally
        {
            AssemblyCacheCleanup.DeleteFile(temporaryPointer);
        }
    }

    private static bool IsGenerationReferencedByPointer(
        string entryDirectory,
        string generationDirectory)
    {
        var pointerPath = Path.Combine(entryDirectory, AssemblyCacheContract.CurrentPointerFileName);
        try
        {
            var currentGeneration = ReadPointer(entryDirectory, pointerPath);
            return string.Equals(
                currentGeneration,
                Path.GetFullPath(generationDirectory),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    private static void ReplacePointer(string pointerPath, string temporaryPointer)
    {
        if (File.Exists(pointerPath))
        {
            File.Replace(temporaryPointer, pointerPath, null, ignoreMetadataErrors: true);
            return;
        }

        if (File.Exists(pointerPath)) return;
        File.Move(temporaryPointer, pointerPath);
    }

    private static void WritePointer(string path, string generation)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, AssemblyCacheContract.FileBufferSize, FileOptions.WriteThrough);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString(nameof(generation), generation);
        writer.WriteEndObject();
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static string ReadPointer(string entryDirectory, string pointerPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(pointerPath, Utf8));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The current pointer is not a JSON object.");
        string? generation = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, nameof(generation), StringComparison.Ordinal) || generation is not null)
            {
                throw new InvalidDataException("The current pointer contains unexpected or duplicate fields.");
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The current pointer must reference a generation.");
            }

            generation = property.Value.GetString();
        }

        if (string.IsNullOrWhiteSpace(generation)) throw new InvalidDataException("The current pointer contains no generation.");
        var normalized = generation.Replace('\\', '/');
        if (Path.IsPathFullyQualified(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException("The current pointer contains an unsafe generation path.");
        }

        var generationDirectory = AssemblyCacheGenerationStorage.ResolveSafePath(entryDirectory, normalized);
        if (!Directory.Exists(generationDirectory)) throw new InvalidDataException("The referenced cache generation is missing.");
        return generationDirectory;
    }

    private enum PointerPublishOutcome
    {
        Failed,
        Existing,
        Published,
    }

    private sealed record PointerPublishAttempt(
        bool Succeeded,
        bool GenerationPublished,
        AssemblySessionDiagnostic? Diagnostic);
}
