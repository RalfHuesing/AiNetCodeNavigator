#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Globbing;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Computes the disk inputs that can change the projects or document membership in a loaded solution.
/// </summary>
internal static class SolutionStructureFingerprint
{
    internal static string Create(Solution solution, string solutionPath, SolutionStructureInputs? structureInputs = null)
    {
        var inputs = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddHashedFile(inputs, solutionPath);

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is { Length: > 0 } projectPath)
            {
                AddHashedFile(inputs, projectPath);
                var projectDirectory = Path.GetDirectoryName(projectPath);
                AddMSBuildConfigurationFiles(inputs, projectDirectory);
                if (!string.IsNullOrEmpty(projectDirectory) && Directory.Exists(projectDirectory))
                {
                    foreach (var sourcePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                        .Where(filePath => !IsGeneratedOrBuildPath(projectDirectory, filePath)))
                    {
                        inputs[Path.GetFullPath(sourcePath)] = "source";
                    }
                }
            }

            foreach (var document in project.Documents)
            {
                if (document.FilePath is { Length: > 0 } documentPath && File.Exists(documentPath))
                {
                    inputs[Path.GetFullPath(documentPath)] = "source";
                }
            }
        }

        if (structureInputs is not null)
        {
            inputs["unresolved-msbuild-expressions"] = string.Join("\0", structureInputs.UnresolvedExpressions.Order(StringComparer.Ordinal));
            foreach (var importPath in structureInputs.ImportedFiles)
            {
                AddHashedFile(inputs, importPath);
            }

            foreach (var potentialImportPath in structureInputs.PotentialImportPaths)
            {
                AddHashedFile(inputs, potentialImportPath);
            }

            foreach (var globRoot in structureInputs.CompileGlobRoots)
            {
                AddSourceFilesUnderRoot(inputs, globRoot);
            }

            foreach (var wildcardImportPattern in structureInputs.WildcardImportPatterns)
            {
                AddWildcardImportMatches(inputs, wildcardImportPattern);
            }
        }

        AddMSBuildConfigurationFiles(inputs, Path.GetDirectoryName(solutionPath));
        var aggregate = string.Join("\n", inputs.Select(pair => $"{pair.Key}\0{pair.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(aggregate)));
    }

    private static void AddHashedFile(IDictionary<string, string> inputs, string path)
    {
        var canonicalPath = Path.GetFullPath(path);
        inputs[canonicalPath] = ReadStableFileFingerprint(canonicalPath);
    }

    private static string ReadStableFileFingerprint(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = new FileInfo(path);
            if (!before.Exists)
            {
                return "missing";
            }

            var beforeLength = before.Length;
            var beforeWriteTime = before.LastWriteTimeUtc;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                var after = new FileInfo(path);
                if (!after.Exists)
                {
                    return "missing";
                }

                if (after.Length == beforeLength && after.LastWriteTimeUtc == beforeWriteTime)
                {
                    return $"{beforeWriteTime.Ticks}:{beforeLength}:{hash}";
                }
            }
            catch (IOException) when (attempt < 2)
            {
                // Retry transient replacement or sharing races with a fresh metadata/hash sample.
            }
        }

        throw new IOException($"The file changed while its structure fingerprint was being read: '{path}'.");
    }

    private static void AddSourceFilesUnderRoot(IDictionary<string, string> inputs, string root)
    {
        var canonicalRoot = Path.GetFullPath(root);
        if (!Directory.Exists(canonicalRoot))
        {
            inputs[canonicalRoot] = "directory-missing";
            return;
        }

        inputs[canonicalRoot] = "directory";
        foreach (var sourcePath in Directory.EnumerateFiles(canonicalRoot, "*.cs", SearchOption.AllDirectories)
            .Where(filePath => !IsGeneratedOrBuildPath(canonicalRoot, filePath)))
        {
            inputs[Path.GetFullPath(sourcePath)] = "source";
        }
    }

    private static void AddWildcardImportMatches(IDictionary<string, string> inputs, string pattern)
    {
        var glob = MSBuildGlob.Parse(pattern);
        var fixedDirectory = glob.FixedDirectoryPart;
        if (string.IsNullOrEmpty(fixedDirectory) || !Directory.Exists(fixedDirectory))
        {
            inputs[$"glob:{pattern}"] = string.Empty;
            return;
        }

        var matchedFiles = Directory.EnumerateFiles(fixedDirectory, "*", SearchOption.AllDirectories)
            .Where(glob.IsMatch)
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        inputs[$"glob:{pattern}"] = string.Join("\0", matchedFiles);
        foreach (var matchedFile in matchedFiles)
        {
            AddHashedFile(inputs, matchedFile);
        }
    }

    private static void AddMSBuildConfigurationFiles(IDictionary<string, string> inputs, string? directory)
    {
        for (var current = directory; !string.IsNullOrEmpty(current); current = Directory.GetParent(current)?.FullName)
        {
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json" })
            {
                AddHashedFile(inputs, Path.Combine(current, name));
            }
        }
    }

    private static bool IsGeneratedOrBuildPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }
}
