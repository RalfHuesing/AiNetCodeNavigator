#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Computes the disk inputs that can change the projects or document membership in a loaded solution.
/// </summary>
internal static class SolutionStructureFingerprint
{
    internal static string Create(Solution solution, string solutionPath)
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

        AddMSBuildConfigurationFiles(inputs, Path.GetDirectoryName(solutionPath));
        var aggregate = string.Join("\n", inputs.Select(pair => $"{pair.Key}\0{pair.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(aggregate)));
    }

    private static void AddHashedFile(IDictionary<string, string> inputs, string path)
    {
        var canonicalPath = Path.GetFullPath(path);
        if (File.Exists(canonicalPath))
        {
            using var stream = new FileStream(canonicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            inputs[canonicalPath] = Convert.ToHexString(SHA256.HashData(stream));
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
