#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Workspace;

[Trait("Category", "Unit")]
public sealed class ResidentSolutionStalenessTests
{
    [Fact]
    public async Task GetCurrentSolution_ReflectsModifiedDocumentOnDisk()
    {
        using var tempDir = TestTempDirectory.Create("staleness-mod-");
        var filePath = tempDir.CreateFile("Greeter.cs", "public class Greeter { public string V => \"1\"; }");

        var solutionHandle = TestWorkspaceBuilder.Create()
            .WithProject("App", (filePath, await File.ReadAllTextAsync(filePath)))
            .Build();

        using (solutionHandle)
        {
            await using var resident = new ResidentSolution(solutionHandle.Solution);

            var initialSolution = resident.GetCurrentSolution();
            Assert.NotNull(initialSolution);
            var initialDoc = initialSolution.Projects.Single().Documents.Single();
            var initialText = await initialDoc.GetTextAsync();
            Assert.Contains("\"1\"", initialText.ToString(), StringComparison.Ordinal);

            // Modify file on disk
            await File.WriteAllTextAsync(filePath, "public class Greeter { public string V => \"2\"; }");
            // Set timestamp to future so mtime differs
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(5));

            var refreshedSolution = resident.GetCurrentSolution();
            Assert.NotNull(refreshedSolution);
            var refreshedDoc = refreshedSolution.Projects.Single().Documents.Single();
            var refreshedText = await refreshedDoc.GetTextAsync();
            Assert.Contains("\"2\"", refreshedText.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetCurrentSolution_ReflectsModifiedDocumentWhenTimestampIsUnchanged()
    {
        using var tempDir = TestTempDirectory.Create("staleness-same-mtime-");
        const string initialContent = "public class Greeter { public string V => \"1\"; }";
        const string changedContent = "public class Greeter { public string V => \"2\"; }";
        var filePath = tempDir.CreateFile("Greeter.cs", initialContent);

        var solutionHandle = TestWorkspaceBuilder.Create()
            .WithProject("App", (filePath, await File.ReadAllTextAsync(filePath)))
            .Build();

        using (solutionHandle)
        {
            await using var resident = new ResidentSolution(solutionHandle.Solution);
            var initial = resident.GetCurrentSolution();
            Assert.NotNull(initial);
            var initialDocument = initial.Projects.Single().Documents.Single();
            Assert.Equal(initialContent, (await initialDocument.GetTextAsync()).ToString());
            var originalMtime = File.GetLastWriteTimeUtc(filePath);

            await File.WriteAllTextAsync(filePath, changedContent);
            File.SetLastWriteTimeUtc(filePath, originalMtime);
            Assert.Equal(originalMtime, File.GetLastWriteTimeUtc(filePath));

            var refreshed = resident.GetCurrentSolution();
            Assert.NotNull(refreshed);
            var refreshedDocument = refreshed.Projects.Single().Documents.Single();
            Assert.Equal(changedContent, (await refreshedDocument.GetTextAsync()).ToString());
        }
    }

    [Fact]
    public async Task GetCurrentSolution_RefreshesEveryProjectDocumentForSharedFilePath()
    {
        using var tempDir = TestTempDirectory.Create("staleness-shared-file-");
        const string initialContent = "public class Shared { public int Value => 1; }";
        const string changedContent = "public class Shared { public int Value => 2; }";
        var filePath = tempDir.CreateFile("Shared.cs", initialContent);
        var solutionHandle = TestWorkspaceBuilder.Create()
            .WithProject("First", (filePath, initialContent))
            .WithProject("Second", (filePath, initialContent))
            .Build();

        using (solutionHandle)
        {
            await using var resident = new ResidentSolution(solutionHandle.Solution);
            var initial = resident.GetCurrentSolution();
            Assert.NotNull(initial);
            Assert.Equal(2, initial.Projects.Count());
            var originalMtime = File.GetLastWriteTimeUtc(filePath);

            await File.WriteAllTextAsync(filePath, changedContent);
            File.SetLastWriteTimeUtc(filePath, originalMtime);

            var refreshed = resident.GetCurrentSolution();
            Assert.NotNull(refreshed);
            var texts = await Task.WhenAll(refreshed.Projects.Select(async project =>
            {
                var document = Assert.Single(project.Documents);
                return (await document.GetTextAsync()).ToString();
            }));

            Assert.Equal(new[] { changedContent, changedContent }, texts);
        }
    }

    [Fact]
    public async Task GetCurrentSolution_RemovesDeletedDocumentOnDisk()
    {
        using var tempDir = TestTempDirectory.Create("staleness-del-");
        var file1 = tempDir.CreateFile("A.cs", "public class A {}");
        var file2 = tempDir.CreateFile("B.cs", "public class B {}");

        var solutionHandle = TestWorkspaceBuilder.Create()
            .WithProject("App", (file1, await File.ReadAllTextAsync(file1)), (file2, await File.ReadAllTextAsync(file2)))
            .Build();

        using (solutionHandle)
        {
            await using var resident = new ResidentSolution(solutionHandle.Solution);

            var initial = resident.GetCurrentSolution();
            Assert.NotNull(initial);
            Assert.Equal(2, initial.Projects.Single().Documents.Count());

            // Delete file2 on disk
            File.Delete(file2);

            var refreshed = resident.GetCurrentSolution();
            Assert.NotNull(refreshed);
            var docs = refreshed.Projects.Single().Documents.ToList();
            Assert.Single(docs);
            Assert.Equal(file1, docs[0].FilePath);
        }
    }
}
