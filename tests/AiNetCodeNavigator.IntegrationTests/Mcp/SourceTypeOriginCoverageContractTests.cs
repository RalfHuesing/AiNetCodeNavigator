using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

[Trait("Category", "Integration")]
public sealed class SourceTypeOriginCoverageContractTests
{
    [Fact]
    public async Task MissingDeclaredReferenceReportsLoadedMetadataBoundaryAcrossPages()
    {
        using var fixture = TestTempDirectory.Create("source-origin-missing-reference-");
        var target = CreateSolution(fixture);
        Assert.False(File.Exists(fixture.GetPath("OriginDependency.dll")));
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);

        var pages = await ReadOriginAsync(relationships, target, "OriginDependency.ExternalType", 2048);
        using var payload = JsonDocument.Parse(pages.Text);
        Assert.False(payload.RootElement.GetProperty("found").GetBoolean());
        Assert.Empty(payload.RootElement.GetProperty("sourceLocations").EnumerateArray());
        Assert.True(pages.Pages > 1, "The missing-reference result must exercise outer delivery recovery.");
        AssertMetadataBoundary(pages.FirstPage, payload.RootElement);
        AssertCompleteMetadataDelivery(pages.LastPage);
    }

    [Fact]
    public async Task AvailableMetadataOwnerRetainsBoundaryWhileSourceOwnerRemainsComplete()
    {
        using var fixture = TestTempDirectory.Create("source-origin-available-reference-");
        var dependency = AssemblyTestHelper.EmitAssembly(fixture, "OriginDependency",
            "namespace OriginDependency; public sealed class ExternalType { }");
        var target = CreateSolution(fixture);
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var relationships = new RelationshipTools(runtime);

        var metadata = await ReadOriginAsync(relationships, target, "OriginDependency.ExternalType", 2048);
        using (var payload = JsonDocument.Parse(metadata.Text))
        {
            var root = payload.RootElement;
            Assert.True(root.GetProperty("found").GetBoolean());
            Assert.False(root.GetProperty("isAmbiguous").GetBoolean());
            Assert.Equal("reference", root.GetProperty("assemblyOrigin").GetString());
            Assert.Equal(dependency, root.GetProperty("outputAssembly").GetString());
            Assert.Contains(dependency, root.GetProperty("candidatePaths").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("OriginDependency", root.GetProperty("searchedAssemblies").EnumerateArray().Select(value => value.GetString()));
            AssertMetadataBoundary(metadata.FirstPage, root);
            AssertCompleteMetadataDelivery(metadata.LastPage);
        }

        var source = await ReadOriginAsync(relationships, target, "OriginConsumer.LocalType", 16384);
        Assert.DoesNotContain("declaredReferenceCoverageUnknown", source.FirstPage, StringComparison.Ordinal);
        using var sourcePayload = JsonDocument.Parse(source.Text);
        var sourceRoot = sourcePayload.RootElement;
        Assert.True(sourceRoot.GetProperty("found").GetBoolean());
        Assert.Equal("source", sourceRoot.GetProperty("assemblyOrigin").GetString());
        Assert.Equal("App", sourceRoot.GetProperty("projectName").GetString());
        var location = Assert.Single(sourceRoot.GetProperty("sourceLocations").EnumerateArray());
        Assert.Equal(fixture.GetPath("src/App/App.cs"), location.GetProperty("filePath").GetString());
        Assert.Equal(1, location.GetProperty("line").GetInt32());
        Assert.True(location.GetProperty("column").GetInt32() > 0);
        Assert.False(sourceRoot.TryGetProperty("metadataSearchScope", out _));
        Assert.False(sourceRoot.TryGetProperty("declaredReferenceCoverage", out _));
    }

    private static string CreateSolution(TestTempDirectory fixture)
    {
        fixture.CreateFile("Directory.Build.props", "<Project />");
        fixture.CreateFile("Directory.Build.targets", "<Project />");
        fixture.CreateFile("Directory.Packages.props", "<Project />");
        fixture.CreateFile("src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>"
            + "<Reference Include=\"OriginDependency\"><HintPath>../../OriginDependency.dll</HintPath></Reference>"
            + "</ItemGroup></Project>");
        fixture.CreateFile("src/App/App.cs", "namespace OriginConsumer; public sealed class LocalType { } "
            + "public sealed class Consumer { public OriginDependency.ExternalType Value { get; set; } }");
        return fixture.CreateFile("OriginCoverage.slnx", "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
    }

    private static void AssertMetadataBoundary(string firstPage, JsonElement payload)
    {
        Assert.Contains("declaredReferenceCoverageUnknown", ReadHeader(firstPage, "omissions"), StringComparison.Ordinal);
        Assert.Contains("Restore unavailable references", firstPage, StringComparison.Ordinal);
        Assert.Equal("loadedReferences", payload.GetProperty("metadataSearchScope").GetString());
        Assert.Equal("unknown", payload.GetProperty("declaredReferenceCoverage").GetString());
    }

    private static void AssertCompleteMetadataDelivery(string lastPage)
    {
        Assert.False(TryReadToken(lastPage, "continuationToken", out _));
        Assert.Contains("declaredReferenceCoverageUnknown", ReadHeader(lastPage, "omissions"), StringComparison.Ordinal);
        Assert.Contains("Restore unavailable references", lastPage, StringComparison.Ordinal);
    }

    private static async Task<(string Text, int Pages, string FirstPage, string LastPage)> ReadOriginAsync(
        RelationshipTools relationships, string target, string typeName, int bytes)
    {
        var lastPage = string.Empty;
        var pages = await ReadOuterResponsePagesAsync(async (budget, tokens, continuation) =>
        {
            var response = await CompleteAsync(operation => relationships.ResolveTypeOrigin(target,
                typeName: typeName, maxResponseBytes: budget, maxResponseTokens: tokens,
                operationToken: operation, continuationToken: continuation));
            lastPage = TextOf(response);
            return response;
        }, bytes, null);
        return (pages.Text, pages.Pages, pages.FirstPage, lastPage);
    }

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
            await Task.Delay(1000);
        }
        throw new Xunit.Sdk.XunitException("The source type-origin operation did not finish within bounded polling.");
    }
}
