#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies.Coordinators;
using AiNetCodeNavigator.Core.Assemblies.Locking;

namespace AiNetCodeNavigator.Core.Assemblies;

internal sealed partial class AssemblyDecompilationCache
{
    private const int PointerPublishAttempts = 3;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly AssemblyArtifactFileLockRegistry PublishLocks = new();
    private static readonly ConcurrentDictionary<string, FileStream> ActiveStagingLeases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = true,
    };

    static AssemblyDecompilationCache()
    {
        JsonOptions.Converters.Add(new AssemblyDecompilationManifestJsonConverter());
    }

    internal AssemblyDecompilationCache(
        string? cacheRoot = null,
        Action<string>? beforePublishReturn = null,
        Action<string>? beforePointerValidation = null)
    {
        RootPath = AssemblyCacheContract.ResolveRootPath(cacheRoot);
        this.beforePublishReturn = beforePublishReturn;
        this.beforePointerValidation = beforePointerValidation;
    }

    internal string RootPath { get; }

    private readonly Action<string>? beforePublishReturn;
    private readonly Action<string>? beforePointerValidation;

    internal string GetEntryDirectory(AssemblyDecompilationCacheKey key)
    {
        var pathHash = HashSegment(key.CanonicalPath, 16);
        var keyHash = HashSegment(key.StableValue, 32);
        return Path.Combine(RootPath, pathHash, keyHash);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The owner lock stream is retained in ActiveStagingLeases until the staging directory is discarded.")]
    internal string CreateStagingDirectory(AssemblyDecompilationCacheKey key)
    {
        var entryDirectory = GetEntryDirectory(key);
        Directory.CreateDirectory(entryDirectory);
        var stagingDirectory = Path.GetFullPath(Path.Combine(
            entryDirectory,
            AssemblyCacheContract.GenerationDirectoryPrefix + Guid.NewGuid().ToString("N") + AssemblyCacheContract.StagingDirectorySuffix));
        var ownerLockPath = GetStagingOwnerLockPath(stagingDirectory);
        FileStream? ownerLock = null;
        try
        {
            ownerLock = new FileStream(ownerLockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            Directory.CreateDirectory(stagingDirectory);
            if (!ActiveStagingLeases.TryAdd(stagingDirectory, ownerLock))
            {
                throw new IOException("A staging directory owner lock could not be registered.");
            }
            ownerLock = null;
            return stagingDirectory;
        }
        catch
        {
            ownerLock?.Dispose();
            AssemblyCacheCleanup.DeleteDirectory(stagingDirectory);
            AssemblyCacheCleanup.DeleteFile(ownerLockPath);
            throw;
        }
    }

    internal void DiscardStagingDirectory(string? stagingDirectory)
    {
        if (stagingDirectory is null) return;
        var fullPath = Path.GetFullPath(stagingDirectory);
        if (ActiveStagingLeases.TryRemove(fullPath, out var ownerLock))
        {
            ownerLock.Dispose();
            AssemblyCacheCleanup.DeleteDirectory(fullPath);
            AssemblyCacheCleanup.DeleteFile(GetStagingOwnerLockPath(fullPath));
        }
    }

    internal static string GetStagingOwnerLockPath(string stagingDirectory) => stagingDirectory + ".owner.lock";

    internal bool TryRead(
        AssemblyCacheReadRequest request,
        out CachedDecompilationGeneration? generation,
        out AssemblySessionDiagnostic? diagnostic)
    {
        generation = null;
        diagnostic = null;
        var entryDirectory = GetEntryDirectory(request.Key);
        var pointerPath = Path.Combine(entryDirectory, AssemblyCacheContract.CurrentPointerFileName);
        if (!File.Exists(pointerPath)) return false;

        try
        {
            var generationDirectory = ReadPointer(entryDirectory, pointerPath);
            generation = ReadGeneration(generationDirectory, request.Key, request.Fingerprint, request.References);
            return true;
        }
        catch (Exception ex) when (IsCacheInputException(ex))
        {
            diagnostic = new(
                AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationCache), nameof(AssemblyCacheReadRequest)),
                $"The current assembly cache entry is invalid and will be discarded: {ex.Message}",
                AssemblyDiagnosticSeverity.Warning);
            return false;
        }
    }

    internal async Task<AssemblyCachePublishResult> PublishAsync(
        AssemblyCachePublishRequest request,
        CancellationToken cancellationToken = default)
    {
        var entryDirectory = GetEntryDirectory(request.CacheKey);
        await using var publishLock = await PublishLocks.AcquireAsync(entryDirectory, cancellationToken).ConfigureAwait(false);
        if (publishLock.IsStalled)
        {
            return new AssemblyCachePublishResult(
                false,
                null,
                new(
                    AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationCache), "PublishLockStall"),
                    $"Assembly artifact generation has stalled for more than {publishLock.StallThreshold?.TotalMinutes:0} minutes — another process holds the lock: {entryDirectory}",
                    AssemblyDiagnosticSeverity.Error));
        }

        try
        {
            ValidatePublishRequest(request);
            Directory.CreateDirectory(entryDirectory);
            return PublishCore(request, entryDirectory);
        }
        catch (Exception ex) when (IsCacheWriteException(ex))
        {
            return new AssemblyCachePublishResult(
                false,
                null,
                new(
                    AssemblyDiagnosticCodes.For(nameof(AssemblyDecompilationCache), nameof(AssemblyCachePublishRequest)),
                    $"Assembly cache generation could not be published: {ex.Message}",
                    AssemblyDiagnosticSeverity.Error));
        }
    }

    private AssemblyCachePublishResult PublishCore(
        AssemblyCachePublishRequest request,
        string entryDirectory)
    {
        var effectiveRequest = PreparePublishRequest(request, entryDirectory);
        var stagingDirectory = effectiveRequest.StagingDirectory
            ?? throw new InvalidDataException("Cache publishing requires a staging directory.");
        string? generationDirectory = null;
        var generationMoved = false;
        var pointerPublished = false;
        try
        {
            ValidateStagingDirectory(entryDirectory, effectiveRequest);
            WriteManifest(stagingDirectory, effectiveRequest);
            _ = ReadGeneration(stagingDirectory, effectiveRequest.CacheKey, effectiveRequest.Fingerprint, effectiveRequest.References);

            var readRequest = new AssemblyCacheReadRequest(effectiveRequest.CacheKey, effectiveRequest.Fingerprint, effectiveRequest.References);
            if (TryRead(readRequest, out _, out _))
            {
                var currentPointer = Path.Combine(entryDirectory, AssemblyCacheContract.CurrentPointerFileName);
                return ReturnSuccessful(ReadPointer(entryDirectory, currentPointer));
            }

            generationDirectory = Path.Combine(
                entryDirectory,
                AssemblyCacheContract.GenerationDirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.Move(stagingDirectory, generationDirectory);
            generationMoved = true;
            return PublishGeneration(entryDirectory, generationDirectory, effectiveRequest, ref generationMoved, ref pointerPublished);
        }
        finally
        {
            if (generationMoved
                && !pointerPublished
                && generationDirectory is not null
                && !IsGenerationReferencedByPointer(entryDirectory, generationDirectory))
            {
                AssemblyCacheCleanup.DeleteDirectory(generationDirectory);
            }

            if (Directory.Exists(stagingDirectory)) AssemblyCacheCleanup.DeleteDirectory(stagingDirectory);
        }
    }

    private AssemblyCachePublishResult PublishGeneration(
        string entryDirectory,
        string generationDirectory,
        AssemblyCachePublishRequest request,
        ref bool generationMoved,
        ref bool pointerPublished)
    {
        var publishOutcome = TryPublishPointer(entryDirectory, generationDirectory, request, out var diagnostic);
        if (publishOutcome == PointerPublishOutcome.Existing)
        {
            var currentPointer = Path.Combine(entryDirectory, AssemblyCacheContract.CurrentPointerFileName);
            AssemblyCacheCleanup.DeleteDirectory(generationDirectory);
            generationMoved = false;
            return ReturnSuccessful(ReadPointer(entryDirectory, currentPointer));
        }

        if (publishOutcome != PointerPublishOutcome.Published)
        {
            return new AssemblyCachePublishResult(false, null, diagnostic);
        }

        pointerPublished = true;
        AssemblyCacheCleanup.RetainGenerations(entryDirectory, Path.GetFileName(generationDirectory));
        return ReturnSuccessful(generationDirectory);
    }

    private AssemblyCachePublishResult ReturnSuccessful(string generationDirectory)
    {
        var result = new AssemblyCachePublishResult(true, generationDirectory, null);
        beforePublishReturn?.Invoke(generationDirectory);
        return result;
    }

    private static AssemblyCachePublishRequest PreparePublishRequest(
        AssemblyCachePublishRequest request,
        string entryDirectory)
    {
        if (request.StagingDirectory is not null) return request;

        var stagingDirectory = Path.Combine(
            entryDirectory,
            AssemblyCacheContract.GenerationDirectoryPrefix + Guid.NewGuid().ToString("N") + AssemblyCacheContract.StagingDirectorySuffix);
        Directory.CreateDirectory(stagingDirectory);
        var documents = WriteDecompiledDocuments(stagingDirectory, request.Decompilation.Documents);
        var projectFilePath = WriteSyntheticProject(stagingDirectory);
        return request with
        {
            StagingDirectory = stagingDirectory,
            Decompilation = request.Decompilation with
            {
                Documents = documents,
                ProjectFilePath = projectFilePath,
            },
        };
    }

    private static void ValidatePublishRequest(AssemblyCachePublishRequest request)
    {
        if (request.Status is AssemblySessionStatus.Loading or AssemblySessionStatus.Failed)
        {
            throw new InvalidDataException("Only analysable assembly states may be published to the cache.");
        }

        if (request.Decompilation.Documents.Count == 0)
        {
            throw new InvalidDataException("A published assembly generation requires at least one document.");
        }

        if (!request.Decompilation.IsComplete
            || request.Decompilation.Diagnostics.Any(diagnostic => !IsWarning(diagnostic)))
        {
            throw new InvalidDataException("An incomplete or failed decompilation must not be published as a cache generation.");
        }

        if (request.Status != AssemblySessionStatus.Complete) return;
        if (request.References.References.Any(reference => !reference.Resolved))
        {
            throw new InvalidDataException("A complete assembly generation must not contain errors or unresolved references.");
        }
    }

    private static void ValidateStagingDirectory(
        string entryDirectory,
        AssemblyCachePublishRequest request)
    {
        var stagingDirectory = request.StagingDirectory
            ?? throw new InvalidDataException("The staging directory is missing.");
        var fullEntryDirectory = Path.GetFullPath(entryDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullStagingDirectory = Path.GetFullPath(stagingDirectory);
        var stagingName = Path.GetFileName(fullStagingDirectory);
        if (!fullStagingDirectory.StartsWith(fullEntryDirectory, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(fullStagingDirectory), Path.GetFullPath(entryDirectory), StringComparison.OrdinalIgnoreCase)
            || !AssemblyCacheContract.IsSafeStagingName(stagingName)
            || !Directory.Exists(fullStagingDirectory))
        {
            throw new InvalidDataException("The cache staging directory is unsafe or missing.");
        }

        var actualFiles = Directory.EnumerateFiles(fullStagingDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => AssemblyCacheGenerationStorage.GetSafeRelativePath(fullStagingDirectory, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documentFiles = request.Decompilation.Documents
            .Select(document => AssemblyCacheGenerationStorage.GetSafeRelativePath(fullStagingDirectory, document.GeneratedPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (actualFiles.Count == 0 || !actualFiles.SetEquals(documentFiles))
        {
            throw new InvalidDataException("Staging directory and decompiled documents do not match.");
        }

        var projectPath = request.Decompilation.ProjectFilePath ?? AssemblyCacheGenerationStorage.FindProjectFile(fullStagingDirectory);
        if (projectPath is null || !File.Exists(AssemblyCacheGenerationStorage.GetSafePathForAbsoluteInput(fullStagingDirectory, projectPath, ".csproj")))
        {
            throw new InvalidDataException("The staging directory contains no real .csproj file.");
        }
    }

    private static IReadOnlyList<DecompiledDocument> WriteDecompiledDocuments(
        string generationDirectory,
        IReadOnlyList<DecompiledDocument> documents)
    {
        var result = new List<DecompiledDocument>(documents.Count);
        foreach (var (document, index) in documents.Select((value, index) => (value, index)))
        {
            var relativePath = $"{AssemblyCacheContract.SourceDirectoryName}/{index:D5}-{SanitizeFileName(document.TypeMetadataName)}.cs";
            var fullPath = AssemblyCacheGenerationStorage.ResolveSafePath(generationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            WriteTextAtomically(fullPath, document.CSharpSource ?? string.Empty);
            result.Add(document with { GeneratedPath = fullPath });
        }

        return result;
    }

    private static string WriteSyntheticProject(string generationDirectory)
    {
        var projectPath = AssemblyCacheGenerationStorage.ResolveSafePath(
            generationDirectory,
            AssemblyCacheContract.SyntheticProjectName + ".csproj");
        WriteTextAtomically(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        return projectPath;
    }

    private static void WriteManifest(string generationDirectory, AssemblyCachePublishRequest request)
    {
        var generatedFiles = request.Decompilation.Documents
            .Select(document => AssemblyCacheGenerationStorage.GetSafeRelativePath(generationDirectory, document.GeneratedPath))
            .ToList();
        var manifest = CreateManifest(request, generatedFiles);
        var manifestPath = AssemblyCacheGenerationStorage.ResolveSafePath(generationDirectory, AssemblyCacheContract.ManifestFileName);
        WriteTextAtomically(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static AssemblyDecompilationManifest CreateManifest(
        AssemblyCachePublishRequest request,
        IReadOnlyList<string> generatedFiles) =>
        new()
        {
            Input = new AssemblyManifestInput
            {
                CacheKey = request.CacheKey.StableValue,
                CanonicalPath = request.Fingerprint.CanonicalPath,
                OriginalPath = request.Fingerprint.CanonicalPath,
                Length = request.Fingerprint.Length,
                MtimeUtc = request.Fingerprint.MtimeUtc,
                Sha256 = request.Fingerprint.Sha256,
            },
            References = new AssemblyManifestReferences
            {
                AssemblyIdentity = request.References.Identity,
                References = request.References.References,
            },
            Format = new AssemblyManifestFormat
            {
                DecompilerVersion = request.CacheKey.DecompilerVersion,
                OptionsIdentity = request.CacheKey.OptionsIdentity,
                CacheSchemaVersion = request.CacheKey.CacheSchemaVersion,
                GeneratedFiles = generatedFiles,
                GeneratedFileHashes = request.Decompilation.Documents
                    .Select((document, index) => new KeyValuePair<string, string>(
                        generatedFiles[index],
                        AssemblyCacheGenerationStorage.ComputeTextSha256(document.CSharpSource ?? string.Empty)))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Encoding = AssemblyCacheContract.Utf8EncodingName,
            },
            Diagnostics = new AssemblyManifestDiagnostics
            {
                Warnings = request.Decompilation.Diagnostics.Where(IsWarning).Select(diagnostic => diagnostic.Message).ToList(),
                Errors = request.Decompilation.Diagnostics.Where(diagnostic => !IsWarning(diagnostic)).Select(diagnostic => diagnostic.Message).ToList(),
                UnresolvedReferences = request.References.References.Where(reference => !reference.Resolved).Select(reference => reference.Name).ToList(),
            },
            Status = new AssemblyManifestStatus
            {
                CreatedUtc = DateTime.UtcNow,
                LastAccessUtc = DateTime.UtcNow,
                Status = request.Status.ToWireValue(),
                Complete = request.Status == AssemblySessionStatus.Complete,
            },
        };

    private static void WriteTextAtomically(string path, string value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, AssemblyCacheContract.FileBufferSize, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, Utf8, leaveOpen: true);
        writer.Write(value);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    internal static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) || character is '.' or '+' or '`' ? '_' : character);
        }

        return builder.Length == 0 ? "assembly" : builder.ToString();
    }

    private static string HashSegment(string value, int length)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(value))).ToLowerInvariant();
        return hash[..Math.Min(length, hash.Length)];
    }

    private static bool IsWarning(AssemblySessionDiagnostic diagnostic) =>
        diagnostic.Severity == AssemblyDiagnosticSeverity.Warning;

    private static bool IsCacheInputException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException;

    private static bool IsCacheWriteException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException;

    private static CachedDecompilationGeneration ReadGeneration(
        string generationDirectory,
        AssemblyDecompilationCacheKey key,
        AssemblyFingerprint fingerprint,
        AssemblyReferenceResolution references) =>
        AssemblyCacheGenerationStorage.ReadGeneration(
            generationDirectory,
            new AssemblyCacheReadRequest(key, fingerprint, references),
            JsonOptions,
            Utf8);
}
