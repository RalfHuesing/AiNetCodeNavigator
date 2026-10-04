#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceSnapshotIdentityContractTests
{
    [Fact]
    public async Task SourceContextPairsCurrentSolutionWithCapturedMetadataEvidence()
    {
        using var fixture = TestTempDirectory.Create("source-context-metadata-evidence-");
        var path = AssemblyTestHelper.EmitAssembly(fixture, "ContextContracts", "namespace External; public interface IContract { void Run(); }");
        var targetPath = fixture.CreateFile("Context.slnx", "<Solution />");
        await using var host = InMemorySourceTestHost.Create(targetPath,
            [new AiNetCodeNavigator.TestKit.Builders.ProjectSpec("App", [("App.cs", "public class App { }")],
                AdditionalReferences: [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)])]);
        var target = new AiNetCodeNavigator.Core.Workspace.AnalysisTarget(
            AiNetCodeNavigator.Core.Workspace.AnalysisTargetType.Project, targetPath, new(targetPath));
        var called = false;
        var response = await NavigationToolSupport.WithSourceSolutionAsync(host.Runtime, target,
            async (solution, context, ct) =>
            {
                called = true;
                Assert.Same(solution, context.ValidatedSnapshot.Solution);
                var resolved = await AiNetCodeNavigator.Core.Symbols.SourceMetadataContractResolver.ResolveAsync(
                    context.ValidatedSnapshot, "M:External.IContract.Run", path, ct);
                Assert.True(resolved.IsSuccess, resolved.Error?.Message);
                Assert.Equal(path, resolved.Selected!.OwnerPath);
                Assert.Single(resolved.Selected.Occurrences);
                return NavigationToolSupport.Success(new { checkedOwner = path });
            }, 16384, 4096, default);
        Assert.True(called);
        AssertSuccessWithinBudget(response, 16384, 4096);
    }

    [Fact]
    public async Task ReplacedMetadataImageWithPreservedTimestampRefreshesBindingAndKeepsSourceReferenceUsable()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-source-metadata-snapshot-identity-");
        var metadataPath = AssemblyTestHelper.EmitAssembly(fixture, "SnapshotMetadata",
            "namespace SnapshotMetadata; public sealed class OldApi { }");
        const string projectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
            + "<ItemGroup><Reference Include=\"SnapshotMetadata\"><HintPath>../../SnapshotMetadata.dll</HintPath></Reference></ItemGroup></Project>";
        fixture.CreateFile("src/App/App.csproj", projectText);
        fixture.CreateFile("src/App/App.cs",
            "namespace SnapshotConsumer; public sealed class Consumer { public int Read() => 1; }");
        var solutionPath = fixture.GetPath("SnapshotMetadata.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");

        var found = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "Consumer.Read", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        var sourceReference = Assert.Single(ReadStableReferences(found.Text));
        Assert.StartsWith("src:", sourceReference, StringComparison.Ordinal);
        var originalSnapshot = ReadHeader(found.FirstPage, "snapshotId");

        var initialOrigin = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(
            solutionPath, typeName: "SnapshotMetadata.OldApi", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Equal(originalSnapshot, ReadHeader(initialOrigin.FirstPage, "snapshotId"));
        using (var initialPayload = JsonDocument.Parse(initialOrigin.Text))
        {
            var root = initialPayload.RootElement;
            Assert.True(root.GetProperty("found").GetBoolean(), initialOrigin.Text);
            Assert.Equal("reference", root.GetProperty("assemblyOrigin").GetString());
            Assert.Contains("SnapshotMetadata", root.GetProperty("searchedAssemblies").EnumerateArray()
                .Select(value => value.GetString()));
        }

        var originalBytes = await File.ReadAllBytesAsync(metadataPath);
        var originalTimestamp = File.GetLastWriteTimeUtc(metadataPath);
        var replacedPath = AssemblyTestHelper.EmitAssembly(fixture, "SnapshotMetadata",
            "namespace SnapshotMetadata; public sealed class NewApi { }");
        Assert.Equal(metadataPath, replacedPath);
        var replacedBytes = await File.ReadAllBytesAsync(metadataPath);
        Assert.Equal(originalBytes.Length, replacedBytes.Length);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(originalBytes)),
            Convert.ToHexString(SHA256.HashData(replacedBytes)));
        File.SetLastWriteTimeUtc(metadataPath, originalTimestamp);
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(metadataPath));

        var refreshedOrigin = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(
            solutionPath, typeName: "SnapshotMetadata.OldApi", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.NotEqual(originalSnapshot, ReadHeader(refreshedOrigin.FirstPage, "snapshotId"));
        using (var refreshedPayload = JsonDocument.Parse(refreshedOrigin.Text))
        {
            Assert.False(refreshedPayload.RootElement.GetProperty("found").GetBoolean(), refreshedOrigin.Text);
            Assert.Contains("SnapshotMetadata", refreshedPayload.RootElement.GetProperty("searchedAssemblies")
                .EnumerateArray().Select(value => value.GetString()));
        }

        var replacementOrigin = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(
            solutionPath, typeName: "SnapshotMetadata.NewApi", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Equal(ReadHeader(refreshedOrigin.FirstPage, "snapshotId"), ReadHeader(replacementOrigin.FirstPage, "snapshotId"));
        using (var replacementPayload = JsonDocument.Parse(replacementOrigin.Text))
        {
            Assert.True(replacementPayload.RootElement.GetProperty("found").GetBoolean(), replacementOrigin.Text);
            Assert.Equal("reference", replacementPayload.RootElement.GetProperty("assemblyOrigin").GetString());
            Assert.Contains("SnapshotMetadata", replacementPayload.RootElement.GetProperty("searchedAssemblies")
                .EnumerateArray().Select(value => value.GetString()));
        }

        var body = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.GetSymbolBody(solutionPath,
            [sourceReference], maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation,
            continuationToken: continuation), 16384, 1024);
        var bodyText = body.Text;
        Assert.Equal(ReadHeader(refreshedOrigin.FirstPage, "snapshotId"), ReadHeader(body.FirstPage, "snapshotId"));
        Assert.Contains("Resolution status: resolved (availability: available)", bodyText, StringComparison.Ordinal);
        Assert.Contains("Read", bodyText, StringComparison.Ordinal);
        Assert.Contains("=> 1", bodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceReferenceSurvivesSameTimestampOptionChangeWithFreshBindingAndSnapshot()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-source-options-snapshot-identity-");
        const string projectTemplate = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            + "<DefineConstants>OPTION_A</DefineConstants></PropertyGroup></Project>";
        fixture.CreateFile("src/App/App.csproj", projectTemplate);
        fixture.CreateFile("src/App/App.cs", "namespace SnapshotOptions;\n"
            + "public sealed class OptionCatalog\n{\n#if OPTION_A\n public sealed class OptionA { }\n#endif\n"
            + "#if OPTION_B\n public sealed class OptionB { }\n#endif\n}\n"
            + "public sealed class Consumer { public int Read() => 1; }\n");
        var solutionPath = fixture.GetPath("SnapshotOptions.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
        var projectPath = fixture.GetPath("src/App/App.csproj");
        var originalTimestamp = File.GetLastWriteTimeUtc(projectPath);

        var discovered = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "Consumer.Read", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        var reference = Assert.Single(ReadStableReferences(discovered.Text));
        var oldSnapshot = ReadHeader(discovered.FirstPage, "snapshotId");
        var optionA = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(
            solutionPath, typeName: "SnapshotOptions.OptionCatalog.OptionA", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Equal(oldSnapshot, ReadHeader(optionA.FirstPage, "snapshotId"));
        using (var payload = JsonDocument.Parse(optionA.Text))
            Assert.True(payload.RootElement.GetProperty("found").GetBoolean(), optionA.Text);

        var changedProject = projectTemplate.Replace("OPTION_A", "OPTION_B", StringComparison.Ordinal);
        Assert.Equal(projectTemplate.Length, changedProject.Length);
        await File.WriteAllTextAsync(projectPath, changedProject);
        File.SetLastWriteTimeUtc(projectPath, originalTimestamp);
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(projectPath));

        var rediscovered = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "Consumer.Read", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        var newSnapshot = ReadHeader(rediscovered.FirstPage, "snapshotId");
        Assert.NotEqual(oldSnapshot, newSnapshot);
        Assert.Equal(reference, Assert.Single(ReadStableReferences(rediscovered.Text)));

        var missingOptionA = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(
            solutionPath, typeName: "SnapshotOptions.OptionCatalog.OptionA", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        var presentOptionB = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.ResolveTypeOrigin(
            solutionPath, typeName: "SnapshotOptions.OptionCatalog.OptionB", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Equal(newSnapshot, ReadHeader(missingOptionA.FirstPage, "snapshotId"));
        Assert.Equal(newSnapshot, ReadHeader(presentOptionB.FirstPage, "snapshotId"));
        using (var missingPayload = JsonDocument.Parse(missingOptionA.Text))
            Assert.False(missingPayload.RootElement.GetProperty("found").GetBoolean(), missingOptionA.Text);
        using (var presentPayload = JsonDocument.Parse(presentOptionB.Text))
            Assert.True(presentPayload.RootElement.GetProperty("found").GetBoolean(), presentOptionB.Text);

        var body = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.GetSymbolBody(solutionPath,
            [reference], maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation,
            continuationToken: continuation), 16384, 1024);
        Assert.Equal(newSnapshot, ReadHeader(body.FirstPage, "snapshotId"));
        Assert.Contains("Resolution status: resolved (availability: available)", body.Text, StringComparison.Ordinal);
        Assert.Contains("=> 1", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexScopeUsesStableDistinctInventoryIdentityForTheSameLoadedSolution()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);

        using var fixture = TestTempDirectory.Create("ainet-index-scope-domain-identity-");
        var projectPath = fixture.CreateFile("src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
            + "<TargetFrameworks>net10.0;net9.0</TargetFrameworks><ImplicitUsings>disable</ImplicitUsings>"
            + "<Nullable>enable</Nullable><GenerateAssemblyInfo>false</GenerateAssemblyInfo>"
            + "<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>"
            + "<GenerateMSBuildEditorConfigFile>false</GenerateMSBuildEditorConfigFile></PropertyGroup></Project>");
        fixture.CreateFile("src/App/.globalconfig", "is_global = true\nbuild_property.TargetFramework = net10.0\n");
        fixture.CreateFile("src/App/App.cs", "namespace SnapshotDomain; public sealed class Consumer { public int Read() => 1; }");
        var solutionPath = fixture.GetPath("SnapshotDomain.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");

        var source = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "Consumer.Read", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        var sourceSnapshot = ReadHeader(source.FirstPage, "snapshotId");
        var sourceReference = Assert.Single(ReadStableReferences(source.Text));
        var index = await ReadPagesAsync((bytes, tokens, operation, continuation) => structure.BrowseTarget(solutionPath, "scope",
            maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation,
            continuationToken: continuation), 16384, 1024);
        var indexSnapshot = ReadHeader(index.FirstPage, "snapshotId");
        Assert.NotEqual(sourceSnapshot, indexSnapshot);
        using var inventory = JsonDocument.Parse(index.Text);
        var project = inventory.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "project");
        Assert.Equal("net10.0", project.GetProperty("loadedFrameworkContext").GetString());
        Assert.True(project.GetProperty("configuredFrameworksKnown").GetBoolean());
        Assert.Equal(new[] { "net9.0" }, project.GetProperty("configuredFrameworksNotAnalyzed")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        var originalTimestamp = File.GetLastWriteTimeUtc(projectPath);
        var updatedProjectText = (await File.ReadAllTextAsync(projectPath)).Replace("net9.0", "net8.0", StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectPath, updatedProjectText);
        File.SetLastWriteTimeUtc(projectPath, originalTimestamp);
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(projectPath));

        var reloadedSource = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "Consumer.Read", kind: "method", maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        var reloadedSourceSnapshot = ReadHeader(reloadedSource.FirstPage, "snapshotId");
        Assert.Equal(sourceSnapshot, reloadedSourceSnapshot);
        Assert.Equal(sourceReference, Assert.Single(ReadStableReferences(reloadedSource.Text)));

        var updatedIndex = await ReadPagesAsync((bytes, tokens, operation, continuation) => structure.BrowseTarget(solutionPath, "scope",
            maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation,
            continuationToken: continuation), 16384, 1024);
        var updatedIndexSnapshot = ReadHeader(updatedIndex.FirstPage, "snapshotId");
        Assert.NotEqual(indexSnapshot, updatedIndexSnapshot);
        Assert.NotEqual(sourceSnapshot, updatedIndexSnapshot);
        using var updatedInventory = JsonDocument.Parse(updatedIndex.Text);
        var updatedProject = updatedInventory.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "project");
        Assert.Equal("net10.0", updatedProject.GetProperty("loadedFrameworkContext").GetString());
        Assert.True(updatedProject.GetProperty("configuredFrameworksKnown").GetBoolean());
        Assert.Equal(new[] { "net8.0" }, updatedProject.GetProperty("configuredFrameworksNotAnalyzed")
            .EnumerateArray().Select(value => value.GetString()).ToArray());

        var repeatedIndex = await ReadPagesAsync((bytes, tokens, operation, continuation) => structure.BrowseTarget(solutionPath, "scope",
            maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation,
            continuationToken: continuation), 16384, 1024);
        Assert.Equal(updatedIndexSnapshot, ReadHeader(repeatedIndex.FirstPage, "snapshotId"));
    }

    private static Task<(string Text, int Pages, string FirstPage)> ReadPagesAsync(
        Func<int, int?, string?, string?, Task<CallToolResult>> invoke,
        int maxResponseBytes,
        int? maxResponseTokens)
        => ReadOuterResponsePagesAsync((bytes, tokens, continuation) =>
            CompleteAsync(operation => invoke(bytes, tokens, operation, continuation)), maxResponseBytes, maxResponseTokens);

    private static async Task<CallToolResult> CompleteAsync(Func<string?, Task<CallToolResult>> invoke)
    {
        string? operation = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await invoke(operation);
            var text = TextOf(result);
            if (!text.Contains("operation=running", StringComparison.Ordinal)
                && !text.Contains("operation=retry", StringComparison.Ordinal)) return result;
            Assert.True(TryReadToken(text, "operationToken", out operation), text);
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The source snapshot metadata contract did not complete after operation polling.");
    }
}
