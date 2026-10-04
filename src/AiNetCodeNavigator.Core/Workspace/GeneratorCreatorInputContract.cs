#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Immutable inputs attested by the component that produced a source generator image.
/// The capability is bound to one loaded project, one analyzer path, and the exact emitted bytes.
/// </summary>
internal sealed class GeneratorCreatorInputContract
{
    private GeneratorCreatorInputContract(
        string projectPath,
        string analyzerPath,
        ImmutableArray<GeneratorCreatorInputImage> effectiveInputs)
    {
        ProjectPath = Canonicalize(projectPath);
        AnalyzerPath = Canonicalize(analyzerPath);
        EffectiveInputs = effectiveInputs;
        var analyzerInput = effectiveInputs.SingleOrDefault(input => PathsEqual(input.CanonicalPath, AnalyzerPath));
        if (analyzerInput is null)
        {
            throw new ArgumentException("The complete generator input set must include the emitted analyzer image.", nameof(effectiveInputs));
        }

        AnalyzerSha256 = analyzerInput.Sha256;
    }

    internal string ProjectPath { get; }

    internal string AnalyzerPath { get; }

    internal string AnalyzerSha256 { get; }

    internal ImmutableArray<GeneratorCreatorInputImage> EffectiveInputs { get; }

    /// <summary>
    /// Called by the image producer with the exact output bytes and every immutable file input that
    /// can affect generator binding or execution. Each path is captured by value at creation time.
    /// </summary>
    internal static GeneratorCreatorInputContract CreateFromProducedImages(
        string projectPath,
        string analyzerPath,
        IEnumerable<GeneratorCreatorInputImage> effectiveInputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerPath);
        ArgumentNullException.ThrowIfNull(effectiveInputs);

        var inputs = effectiveInputs
            .Select(input => input ?? throw new ArgumentException("Generator input images cannot contain null values.", nameof(effectiveInputs)))
            .OrderBy(input => input.CanonicalPath, PathComparer)
            .ToImmutableArray();
        if (inputs.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The complete generator input set cannot be empty.", nameof(effectiveInputs));
        }

        if (inputs.Select(input => input.CanonicalPath).Distinct(PathComparer).Count() != inputs.Length)
        {
            throw new ArgumentException("The complete generator input set contains a duplicate physical path.", nameof(effectiveInputs));
        }

        return new GeneratorCreatorInputContract(projectPath, analyzerPath, inputs);
    }

    internal bool MatchesProjectAndAnalyzer(string projectPath, string analyzerPath) =>
        PathsEqual(ProjectPath, projectPath) && PathsEqual(AnalyzerPath, analyzerPath);

    internal static string Canonicalize(string path)
    {
        if (!AnalysisPathIdentity.TryNormalize(path, out var canonicalPath))
        {
            throw new ArgumentException($"Generator creator input path '{path}' is not an absolute path.", nameof(path));
        }

        return canonicalPath;
    }

    private static bool PathsEqual(string left, string right) => PathComparer.Equals(left, Canonicalize(right));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

/// <summary>A path and the exact immutable bytes supplied by a generator image producer.</summary>
internal sealed class GeneratorCreatorInputImage
{
    private GeneratorCreatorInputImage(string canonicalPath, ImmutableArray<byte> bytes)
    {
        CanonicalPath = canonicalPath;
        Bytes = bytes;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes.AsSpan()));
    }

    internal string CanonicalPath { get; }

    internal ImmutableArray<byte> Bytes { get; }

    internal string Sha256 { get; }

    internal static GeneratorCreatorInputImage FromBytes(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new GeneratorCreatorInputImage(
            GeneratorCreatorInputContract.Canonicalize(path),
            ImmutableArray.CreateRange(bytes.ToArray()));
    }
}
