#nullable enable

using System.IO;
using AiNetCodeNavigator.Core.FileStructure;
using AiNetCodeNavigator.TestKit.Fixtures;
using Xunit;

namespace AiNetCodeNavigator.FastTests.FileStructure;

[Trait("Category", "Unit")]
public sealed class GetFileTreeScannerTests
{
    [Fact]
    public void Scan_Directory_CollectsFilesAndIgnoresExcluded()
    {
        using var tempDir = TestTempDirectory.Create();
        var subDir = Path.Combine(tempDir.DirectoryPath, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Code.cs"), "public class Code {}");
        File.WriteAllText(Path.Combine(subDir, "Doc.txt"), "readme");

        var binDir = Path.Combine(tempDir.DirectoryPath, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "Artifact.dll"), "binary");

        var request = new FileTreeScanRequest(
            RootDirectory: tempDir.DirectoryPath,
            View: "files");

        var result = GetFileTreeScanner.Scan(request);

        Assert.Equal(2, result.TotalFiles);
        Assert.Contains(result.Entries, e => e.Name == "Code.cs");
        Assert.Contains(result.Entries, e => e.Name == "Doc.txt");
        Assert.DoesNotContain(result.Entries, e => e.Name == "Artifact.dll");
    }

    [Fact]
    public void Scan_FileFilter_FiltersMatchingFiles()
    {
        using var tempDir = TestTempDirectory.Create();
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "Test.cs"), "public class Test {}");
        File.WriteAllText(Path.Combine(tempDir.DirectoryPath, "Config.json"), "{}");

        var request = new FileTreeScanRequest(
            RootDirectory: tempDir.DirectoryPath,
            FileFilter: "*.cs");

        var result = GetFileTreeScanner.Scan(request);

        Assert.Single(result.Entries);
        Assert.Equal("Test.cs", result.Entries[0].Name);
    }

    [Fact]
    public void Scan_SummaryView_GeneratesSummaryByDirectory()
    {
        using var tempDir = TestTempDirectory.Create();
        var sub = Path.Combine(tempDir.DirectoryPath, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "File1.cs"), "1");
        File.WriteAllText(Path.Combine(sub, "File2.cs"), "2");

        var request = new FileTreeScanRequest(
            RootDirectory: tempDir.DirectoryPath,
            View: "summary");

        var result = GetFileTreeScanner.Scan(request);

        Assert.NotEmpty(result.Summaries);
        var subSummary = result.Summaries.FirstOrDefault(s => s.DirectoryPath.Contains("sub"));
        Assert.NotNull(subSummary);
        Assert.Equal(2, subSummary.FileCount);
        Assert.Contains("| Verzeichnis | Dateien | Gesamtgröße |", result.FormattedText);
    }
}
