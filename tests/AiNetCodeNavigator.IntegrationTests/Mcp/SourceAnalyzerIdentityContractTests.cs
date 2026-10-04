using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

// @covers SymbolTools.FindSymbol
// @covers SymbolTools.GetSymbolBody
[Trait("Category", "Integration")]
public sealed class SourceAnalyzerIdentityContractTests
{
    [Fact]
    public async Task MsBuildOptionsGeneratorSupportsRegularAndGeneratedHandlerNavigation()
    {
        using var fixture = TestTempDirectory.Create("options-generator-");
        var solutionPath = fixture.GetPath("GeneratorFixture.slnx");
        fixture.CreateFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.Options" />
              </ItemGroup>
            </Project>
            """);
        fixture.CreateFile("src/App/Options.cs", """
            using Microsoft.Extensions.Options;
            namespace GeneratorFixture;
            public sealed class SampleOptions
            {
                [System.ComponentModel.DataAnnotations.Required]
                public string Name { get; set; } = string.Empty;
            }
            [OptionsValidator]
            public partial class SampleOptionsValidator : IValidateOptions<SampleOptions> { }
            """);
        fixture.CreateFile("src/App/Consumer.cs", """
            using Microsoft.Extensions.Options;
            namespace GeneratorFixture;
            public static class RegularConsumer
            {
                public static ValidateOptionsResult Check(SampleOptionsValidator validator, SampleOptions options) =>
                    validator.Validate(Options.DefaultName, options);
            }
            """);
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
        fixture.CreateFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.1" />
                <PackageVersion Include="Microsoft.Build.Framework" Version="18.9.6" />
                <PackageVersion Include="Microsoft.NET.StringTools" Version="18.9.6" />
              </ItemGroup>
            </Project>
            """);
        var nugetConfigPath = fixture.CreateFile("NuGet.Config",
            "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solutionPath, fixture.DirectoryPath, nugetConfigPath, "Options generator fixture restore");

        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var registry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(),
            projectRegistry: registry);
        var leaseResult = registry.Lease(solutionPath);
        Assert.True(leaseResult.Succeeded, leaseResult.ErrorMessage);
        using var lease = leaseResult.Lease!;
        await lease.ResidentSolution.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));

        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var structures = new StructureTools(runtime);
            var indexScope = await CompleteAsync(operation => structures.GetIndexScope(solutionPath,
                maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
            var indexScopeText = TextOf(indexScope);
            Assert.False(indexScope.IsError ?? false, indexScopeText);
            Assert.Contains("App", indexScopeText, StringComparison.Ordinal);

            var symbols = new SymbolTools(runtime);
            var regular = await CompleteAsync(operation => symbols.FindSymbol(solutionPath,
                pattern: "GeneratorFixture.RegularConsumer.Check", kind: "method", maxResponseBytes: 16384,
                maxResponseTokens: 1024, operationToken: operation));
            var regularText = TextOf(regular);
            Assert.False(regular.IsError ?? false, regularText);
            Assert.Equal("complete", ReadHeader(regularText, "analysisCompleteness"));
            var regularReference = Assert.Single(ReadStableReferences(regularText));

            var generated = await CompleteAsync(operation => symbols.FindSymbol(solutionPath,
                pattern: "GeneratorFixture.SampleOptionsValidator.Validate", kind: "method", includeGenerated: true,
                maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
            var generatedText = TextOf(generated);
            Assert.False(generated.IsError ?? false, generatedText);
            Assert.Equal("complete", ReadHeader(generatedText, "analysisCompleteness"));
            var generatedReference = Assert.Single(ReadStableReferences(generatedText));

            var regularBody = await CompleteAsync(operation => symbols.GetSymbolBody(solutionPath, [regularReference],
                maxBodyLines: 20, maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
            var regularBodyText = TextOf(regularBody);
            Assert.False(regularBody.IsError ?? false, regularBodyText);
            Assert.Contains("validator.Validate", regularBodyText, StringComparison.Ordinal);

            var generatedBody = await CompleteAsync(operation => symbols.GetSymbolBody(solutionPath, [generatedReference],
                maxBodyLines: 40, maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
            var generatedBodyText = TextOf(generatedBody);
            Assert.False(generatedBody.IsError ?? false, generatedBodyText);
            Assert.Contains("Validate", generatedBodyText, StringComparison.Ordinal);
            Assert.Contains("Name", generatedBodyText, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public async Task MsBuildGeneratorImageReplacementRefreshesSnapshotAndGeneratedMethodBody()
    {
        using var fixture = TestTempDirectory.Create("source-analyzer-identity-");

        var solutionPath = fixture.GetPath("AnalyzerFixture.slnx");
        var generatorPath = fixture.GetPath("tools/IdentityGenerator.dll");
        fixture.CreateFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
              <ItemGroup><Analyzer Include="../../tools/IdentityGenerator.dll" /></ItemGroup>
            </Project>
            """);
        fixture.CreateFile("src/App/App.cs", "namespace AnalyzerFixture; public sealed class RegularType { }");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
        var nugetConfigPath = fixture.CreateFile("NuGet.Config",
            "<configuration><packageSources><clear /></packageSources></configuration>");
        var version17 = await EmitGeneratorAsync(generatorPath, 17);
        var version18 = await EmitGeneratorAsync(generatorPath, 18);
        await File.WriteAllBytesAsync(generatorPath, version17);
        await FixtureRestore.RunAsync(solutionPath, fixture.DirectoryPath, nugetConfigPath, "Analyzer identity fixture restore");

        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await using var registry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        await using var runtime = new NavigatorHostRuntime(lifetime, projectRegistry: registry);
        var symbols = new SymbolTools(runtime);

        var residentLeaseResult = registry.Lease(solutionPath);
        Assert.True(residentLeaseResult.Succeeded, residentLeaseResult.ErrorMessage);
        using var residentLease = residentLeaseResult.Lease!;
        var residentSnapshot = await residentLease.ResidentSolution.GetCurrentSnapshotAsync();
        Assert.True(residentSnapshot.Succeeded, residentSnapshot.Error?.Message);
        var generatorProject = Assert.Single(residentSnapshot.Solution!.Projects);
        var generatedDocuments = (await generatorProject.GetSourceGeneratedDocumentsAsync()).ToArray();
        var generatedSource = string.Join(Environment.NewLine,
            await Task.WhenAll(generatedDocuments.Select(async document =>
                (await document.GetTextAsync()).ToString())));
        var generatorCompilation = await generatorProject.GetCompilationAsync();
        var generatorDiagnostics = generatorCompilation is null
            ? "The generator project produced no compilation."
            : string.Join(Environment.NewLine,
                generatorCompilation.GetDiagnostics().Select(diagnostic => diagnostic.ToString()));
        var generatedMethod = generatorCompilation?.GetTypeByMetadataName("AnalyzerFixture.Generated.GeneratedProbe")?
            .GetMembers("Read").OfType<IMethodSymbol>().SingleOrDefault();
        Assert.NotNull(generatedMethod);
        var generatedMethodDeclarationId = DocumentationCommentId.CreateDeclarationId(generatedMethod!);
        Assert.NotNull(generatedMethodDeclarationId);
        Assert.True(StableSymbolReferenceCodec.IsCanonicalDeclarationId(generatedMethodDeclarationId!), generatedMethodDeclarationId);
        Assert.True(generatedSource.Contains("GeneratedProbe", StringComparison.Ordinal)
            && generatedSource.Contains("=> 17", StringComparison.Ordinal),
            $"The registered MSBuild snapshot did not materialize the expected generated source. Generated documents: {generatedDocuments.Length}.\n{generatorDiagnostics}");

        var initialBytes = version17;
        var initialHash = Convert.ToHexString(SHA256.HashData(initialBytes));
        var initialTimestamp = File.GetLastWriteTimeUtc(generatorPath);

        var excluded = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "AnalyzerFixture.Generated.GeneratedProbe.Read", kind: "method", includeGenerated: false, maxResponseBytes: bytes,
            maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Empty(ReadGeneratedMethodReferences(excluded.Text, generatedMethodDeclarationId!));

        var discovered = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "AnalyzerFixture.Generated.GeneratedProbe.Read", kind: "method", includeGenerated: true, maxResponseBytes: bytes,
            maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Equal("complete", ReadHeader(discovered.FirstPage, "analysisCompleteness"));
        var discoveredReferences = ReadGeneratedMethodReferences(discovered.Text, generatedMethodDeclarationId!);
        Assert.True(discoveredReferences.Length == 1, discovered.Text);
        var reference = discoveredReferences[0];
        var generatedTypeDiscovery = await ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.FindSymbol(solutionPath,
            pattern: "AnalyzerFixture.Generated.GeneratedProbe", kind: "class", includeGenerated: true,
            maxResponseBytes: bytes, maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation), 16384, 1024);
        var generatedTypeReferences = ReadGeneratedTypeReferences(generatedTypeDiscovery.Text);
        Assert.True(generatedTypeReferences.Length == 1, generatedTypeDiscovery.Text);
        var generatedTypeReference = generatedTypeReferences[0];

        var structures = new StructureTools(runtime);
        var excludedStructure = await ReadPagesAsync((bytes, tokens, operation, continuation) => structures.GetClassStructure(solutionPath,
            generatedTypeReference, includeGenerated: false, maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.DoesNotContain("Read", excludedStructure.Text, StringComparison.Ordinal);
        var includedStructure = await ReadPagesAsync((bytes, tokens, operation, continuation) => structures.GetClassStructure(solutionPath,
            generatedTypeReference, includeGenerated: true, maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        Assert.Contains("Read", includedStructure.Text, StringComparison.Ordinal);

        var relationships = new RelationshipTools(runtime);
        var excludedContext = await CompleteAsync(operation => relationships.GetContext(solutionPath, reference, ["body"],
            maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
        Assert.True(excludedContext.IsError == true, TextOf(excludedContext));
        Assert.Contains("generated source", TextOf(excludedContext), StringComparison.Ordinal);
        var includedContext = await ReadPagesAsync((bytes, tokens, operation, continuation) => relationships.GetContext(solutionPath,
            reference, ["body"], includeGenerated: true, maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 16384, 1024);
        using var includedContextDocument = JsonDocument.Parse(includedContext.Text);
        var includedContextBody = includedContextDocument.RootElement.GetProperty("sections")[0]
            .GetProperty("items").GetProperty("body").GetString();
        Assert.True(includedContextBody?.Contains("=> 17", StringComparison.Ordinal) == true,
            $"Generated body was absent from the parsed get_context payload. Body: {includedContextBody}\nPayload: {includedContext.Text}");

        var initialSnapshot = ReadHeader(discovered.FirstPage, "snapshotId");
        var initialBody = await ReadBodyAsync(symbols, solutionPath, reference);
        Assert.Equal(initialSnapshot, ReadHeader(initialBody.FirstPage, "snapshotId"));
        Assert.Contains("=> 17", initialBody.Text, StringComparison.Ordinal);

        await File.WriteAllBytesAsync(generatorPath, version18);
        var replacementBytes = await File.ReadAllBytesAsync(generatorPath);
        Assert.Equal(initialBytes.Length, replacementBytes.Length);
        var replacementHash = Convert.ToHexString(SHA256.HashData(replacementBytes));
        Assert.NotEqual(initialHash, replacementHash);
        File.SetLastWriteTimeUtc(generatorPath, initialTimestamp);
        Assert.Equal(initialTimestamp, File.GetLastWriteTimeUtc(generatorPath));

        var refreshedSymbols = new SymbolTools(runtime);
        var rediscovered = await ReadPagesAsync((bytes, tokens, operation, continuation) => refreshedSymbols.FindSymbol(solutionPath,
            pattern: "AnalyzerFixture.Generated.GeneratedProbe.Read", kind: "method", includeGenerated: true, maxResponseBytes: bytes,
            maxResponseTokens: tokens, operationToken: operation, continuationToken: continuation), 16384, 1024);
        var refreshedSnapshot = ReadHeader(rediscovered.FirstPage, "snapshotId");
        Assert.NotEqual(initialSnapshot, refreshedSnapshot);
        Assert.Equal(reference, Assert.Single(ReadGeneratedMethodReferences(rediscovered.Text, generatedMethodDeclarationId!)));

        var refreshedBody = await ReadBodyAsync(refreshedSymbols, solutionPath, reference);
        Assert.Equal(refreshedSnapshot, ReadHeader(refreshedBody.FirstPage, "snapshotId"));
        Assert.Contains("=> 18", refreshedBody.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("=> 17", refreshedBody.Text, StringComparison.Ordinal);
    }

    private static string[] ReadGeneratedMethodReferences(string text, string expectedDeclarationId) => ReadStableReferences(text)
        .Where(reference => StableSymbolReferenceCodec.TryParse(reference, out var parsed, out _)
            && parsed is StableSymbolReference.Source source
            && source.DeclarationId == expectedDeclarationId)
        .ToArray();

    private static string[] ReadGeneratedTypeReferences(string text) => ReadStableReferences(text)
        .Where(reference => StableSymbolReferenceCodec.TryParse(reference, out var parsed, out _)
            && parsed is StableSymbolReference.Source source
            && source.DeclarationId == "T:AnalyzerFixture.Generated.GeneratedProbe")
        .ToArray();

    private static Task<(string Text, int Pages, string FirstPage)> ReadBodyAsync(SymbolTools symbols, string solutionPath,
        string reference) => ReadPagesAsync((bytes, tokens, operation, continuation) => symbols.GetSymbolBody(solutionPath,
            [reference], maxBodyLines: 20, maxResponseBytes: bytes, maxResponseTokens: tokens,
            operationToken: operation, continuationToken: continuation), 32768, 2048);

    [Fact]
    public async Task MsBuildSourceNavigation_AllowsReferencedXmlDocumentationWithExternalInclude()
    {
        using var fixture = TestTempDirectory.Create("source-xml-include-");
        var solutionPath = fixture.GetPath("IncludeFixture.slnx");
        var referencePath = AssemblyTestHelper.EmitAssembly(fixture, "Vendor.Api",
            "namespace Vendor.Api; public sealed class Entry { public static int Read() => 1; }");
        var referenceDocumentationPath = Path.ChangeExtension(referencePath, ".xml");
        const string externalInclude = "../../../doc/snippets/vendor/Entry.xml";
        await File.WriteAllTextAsync(referenceDocumentationPath, $$"""
            <?xml version="1.0"?>
            <doc>
              <assembly><name>Vendor.Api</name></assembly>
              <members>
                <member name="T:Vendor.Api.Entry">
                  <include file="{{externalInclude}}" path="/doc/members/member[@name='T:Vendor.Api.Entry']" />
                </member>
              </members>
            </doc>
            """);
        Assert.Contains(externalInclude, await File.ReadAllTextAsync(referenceDocumentationPath), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(referenceDocumentationPath)!, externalInclude))));

        fixture.CreateFile("src/App/App.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Vendor.Api">
                  <HintPath>../../{{Path.GetFileName(referencePath)}}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
        fixture.CreateFile("src/App/Consumer.cs", $$"""
            namespace IncludeFixture;

            public static class Consumer
            {
                /// <include file="{{externalInclude}}" path="/doc/members/member[@name='M:IncludeFixture.Consumer.Read']" />
                public static int Read() => Vendor.Api.Entry.Read();
            }
            """);
        await File.WriteAllTextAsync(solutionPath,
            "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
        var nugetConfigPath = fixture.CreateFile("NuGet.Config",
            "<configuration><packageSources><clear /></packageSources></configuration>");
        await FixtureRestore.RunAsync(solutionPath, fixture.DirectoryPath, nugetConfigPath,
            "XML include navigation fixture restore");

        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var registry = new ProjectRegistry(ProjectRegistryOptions.ForMSBuild());
        await using var runtime = new NavigatorHostRuntime(
            host.Services.GetRequiredService<IHostApplicationLifetime>(), projectRegistry: registry);
        var leaseResult = registry.Lease(solutionPath);
        Assert.True(leaseResult.Succeeded, leaseResult.ErrorMessage);
        using var lease = leaseResult.Lease!;
        await lease.ResidentSolution.LoadTask!.WaitAsync(TimeSpan.FromSeconds(30));

        var structures = new StructureTools(runtime);
        var indexScope = await CompleteAsync(operation => structures.GetIndexScope(solutionPath,
            maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
        var indexScopeText = TextOf(indexScope);
        Assert.False(indexScope.IsError ?? false, indexScopeText);
        Assert.Contains("App", indexScopeText, StringComparison.Ordinal);

        var symbols = new SymbolTools(runtime);
        var discovery = await CompleteAsync(operation => symbols.FindSymbol(solutionPath,
            pattern: "IncludeFixture.Consumer.Read", kind: "method", maxResponseBytes: 16384,
            maxResponseTokens: 1024, operationToken: operation));
        var discoveryText = TextOf(discovery);
        Assert.False(discovery.IsError ?? false, discoveryText);
        var reference = Assert.Single(ReadStableReferences(discoveryText));

        var body = await CompleteAsync(operation => symbols.GetSymbolBody(solutionPath, [reference],
            maxBodyLines: 20, maxResponseBytes: 16384, maxResponseTokens: 1024, operationToken: operation));
        var bodyText = TextOf(body);
        Assert.False(body.IsError ?? false, bodyText);
        Assert.Contains(externalInclude, bodyText, StringComparison.Ordinal);
        Assert.Contains("Vendor.Api.Entry.Read", bodyText, StringComparison.Ordinal);
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
            await Task.Delay(1000);
        }
        throw new Xunit.Sdk.XunitException("The source analyzer identity contract did not complete after operation polling.");
    }

    private static async Task<byte[]> EmitGeneratorAsync(
        string path,
        int bodyVersion)
    {
        var platformPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platformPaths.Append(typeof(ISourceGenerator).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static referencePath => MetadataReference.CreateFromFile(referencePath)).ToImmutableArray();
        var source = $$"""
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Text;
            [Generator]
            public sealed class IdentityGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context)
                {
                    const string generated = "namespace AnalyzerFixture.Generated; public sealed class GeneratedProbe { public int Read() => {{bodyVersion}}; }";
                    context.AddSource("GeneratedProbe.g.cs", SourceText.From(generated, Encoding.UTF8));
                }
            }
            """;
        var compilation = CSharpCompilation.Create("AnalyzerFixtureGenerator", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                deterministic: true));
        await using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var imageBytes = stream.ToArray();
        await File.WriteAllBytesAsync(path, imageBytes);
        return imageBytes;
    }
}
