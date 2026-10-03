using System.Text.Json;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp;
using AiNetCodeNavigator.Mcp.Tools;
using AiNetCodeNavigator.Mcp.Tools.Relationships;
using AiNetCodeNavigator.Mcp.Tools.Symbols;
using AiNetCodeNavigator.TestKit;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using static AiNetCodeNavigator.IntegrationTests.Mcp.IntegrationMcpAssertions;

namespace AiNetCodeNavigator.IntegrationTests.Mcp;

// @covers StructureTools.BuildSkeletonsAsync
// @covers RelationshipTools.BuildContextDeclaration
// @covers RelationshipTools.ResolveAssemblySymbolAsync
[Trait("Category", "Integration")]
public sealed class StableReferenceResolutionContractTests
{
    [Fact]
    public async Task SourceSkeletonSemanticReferenceFailuresRetainPerItemRecoveryActions()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("r02-source-skeleton-actions-");
        const string sourceText = "namespace SourceSkeletonProbe; public sealed class Target { public int Read() => 1; }";
        var target = fixture.CreateFile("R02Source.slnx", string.Empty);
        var sourcePath = fixture.CreateFile("src/App/Target.cs", sourceText);
        using var sourceWorkspace = TestWorkspaceBuilder.Create().WithVirtualSolutionPath(target)
            .WithProject("SourceSkeletonProbe", (sourcePath, sourceText)).Build();
        await using var registry = new ProjectRegistry(new ProjectRegistryOptions(
            _ => ResidentSolutionCreation.Resident(new ResidentSolution(sourceWorkspace.Solution)), TimeProvider.System));
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>(), projectRegistry: registry);
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);

        var discovered = await symbols.FindSymbol(target, pattern: "SourceSkeletonProbe.Target", kind: "class",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(discovered, 16384, 1024);
        var sourceReference = ReadReference(TextOf(discovered));
        Assert.True(StableSymbolReferenceCodec.TryParse(sourceReference, out var parsedSource, out var sourceParseError), sourceParseError?.Message);
        var source = Assert.IsType<StableSymbolReference.Source>(parsedSource);
        var missingOwner = StableSymbolReferenceCodec.Format(source with { ProjectPath = "src/Missing/Missing.csproj" });
        var missingDeclaration = StableSymbolReferenceCodec.Format(new StableSymbolReference.Source(
            source.ProjectPath, "T:SourceSkeletonProbe.Missing"));
        Assert.True(StableSymbolReferenceCodec.TryParse(missingDeclaration, out var parsedMissingSource, out var missingSourceError), missingSourceError?.Message);
        Assert.Equal("T:SourceSkeletonProbe.Missing", parsedMissingSource!.DeclarationId);
        var wrongOrigin = StableSymbolReferenceCodec.Format(new StableSymbolReference.Assembly("OtherOwner", "T:SourceSkeletonProbe.Target"));
        var failedReferences = new[] { missingOwner, missingDeclaration, wrongOrigin };

        var mixed = await structure.GetFileSkeleton(target, [sourceReference, .. failedReferences],
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(mixed, 32768, 4096);
        var mixedText = TextOf(mixed);
        Assert.Contains("### Target", mixedText, StringComparison.Ordinal);
        Assert.Contains("Read", mixedText, StringComparison.Ordinal);
        AssertPerItemRecoveryAction(mixedText, missingOwner, "TARGET_MISMATCH");
        AssertPerItemRecoveryAction(mixedText, missingDeclaration, "SYMBOL_NOT_FOUND");
        AssertPerItemRecoveryAction(mixedText, wrongOrigin, "TARGET_MISMATCH");

        var allFailed = await structure.GetFileSkeleton(target, failedReferences,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.True(allFailed.IsError ?? false, TextOf(allFailed));
        AssertBudget(TextOf(allFailed), 32768, 4096);
        var allFailedText = TextOf(allFailed);
        AssertPerItemRecoveryAction(allFailedText, missingOwner, "TARGET_MISMATCH");
        AssertPerItemRecoveryAction(allFailedText, missingDeclaration, "SYMBOL_NOT_FOUND");
        AssertPerItemRecoveryAction(allFailedText, wrongOrigin, "TARGET_MISMATCH");
    }

    [Fact]
    public async Task AssemblySkeletonSemanticReferenceFailuresRetainPerItemRecoveryActions()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        using var fixture = TestTempDirectory.Create("r02-assembly-skeleton-actions-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "R02SkeletonOwner",
            "namespace R02SkeletonOwner; public sealed class Target { public int Read() => 1; }");
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        var symbols = new SymbolTools(runtime);
        var structure = new StructureTools(runtime);

        var discovered = await symbols.FindSymbol(assemblyPath, pattern: "R02SkeletonOwner.Target", kind: "class",
            maxResponseBytes: 16384, maxResponseTokens: 1024);
        AssertSuccessWithinBudget(discovered, 16384, 1024);
        var assemblyReference = ReadReference(TextOf(discovered));
        Assert.True(StableSymbolReferenceCodec.TryParse(assemblyReference, out var parsedAssembly, out var assemblyParseError), assemblyParseError?.Message);
        var assembly = Assert.IsType<StableSymbolReference.Assembly>(parsedAssembly);
        var wrongOwner = StableSymbolReferenceCodec.Format(assembly with { SimpleName = "DifferentOwner" });
        var missingDeclaration = StableSymbolReferenceCodec.Format(new StableSymbolReference.Assembly(
            assembly.SimpleName, "T:R02SkeletonOwner.Missing"));
        Assert.True(StableSymbolReferenceCodec.TryParse(missingDeclaration, out var parsedMissingAssembly, out var missingAssemblyError), missingAssemblyError?.Message);
        Assert.Equal("T:R02SkeletonOwner.Missing", parsedMissingAssembly!.DeclarationId);
        var wrongOrigin = StableSymbolReferenceCodec.Format(new StableSymbolReference.Source("src/App/App.csproj", "T:R02SkeletonOwner.Target"));
        var failedReferences = new[] { wrongOwner, missingDeclaration, wrongOrigin };

        var mixed = await structure.GetFileSkeleton(assemblyPath, [assemblyReference, .. failedReferences],
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        AssertSuccessWithinBudget(mixed, 32768, 4096);
        var mixedText = TextOf(mixed);
        Assert.Contains("### Target", mixedText, StringComparison.Ordinal);
        Assert.Contains("Read", mixedText, StringComparison.Ordinal);
        AssertPerItemRecoveryAction(mixedText, wrongOwner, "TARGET_MISMATCH");
        AssertPerItemRecoveryAction(mixedText, missingDeclaration, "SYMBOL_NOT_FOUND");
        AssertPerItemRecoveryAction(mixedText, wrongOrigin, "TARGET_MISMATCH");

        var allFailed = await structure.GetFileSkeleton(assemblyPath, failedReferences,
            maxResponseBytes: 32768, maxResponseTokens: 4096);
        Assert.True(allFailed.IsError ?? false, TextOf(allFailed));
        AssertBudget(TextOf(allFailed), 32768, 4096);
        var allFailedText = TextOf(allFailed);
        AssertPerItemRecoveryAction(allFailedText, wrongOwner, "TARGET_MISMATCH");
        AssertPerItemRecoveryAction(allFailedText, missingDeclaration, "SYMBOL_NOT_FOUND");
        AssertPerItemRecoveryAction(allFailedText, wrongOrigin, "TARGET_MISMATCH");
    }

    [Fact]
    public async Task AssemblyClosureContextDeclarationRetainsExactOwnerReferenceForBodyFollowup()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("r02-context-closure-reference-");
        var leafPath = AssemblyTestHelper.EmitAssembly(fixture, "R02ContextLeaf",
            "namespace R02ContextLeaf; public sealed class Target { public int Read() => 7; }");
        var rootPath = AssemblyTestHelper.EmitAssembly(fixture, "R02ContextRoot",
            "namespace R02ContextRoot; public sealed class Root { public int Read() => new R02ContextLeaf.Target().Read(); }", leafPath);
        var symbols = new SymbolTools(runtime);
        var relationships = new RelationshipTools(runtime);
        var discovered = await symbols.FindSymbol(leafPath, pattern: "R02ContextLeaf.Target.Read", kind: "method",
            maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(discovered, 32768, 2048);
        var originalReference = ReadReference(TextOf(discovered));

        var direct = await relationships.GetContext(leafPath, originalReference, ["body", "callers"], includeReferences: false,
            maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(direct, 32768, 2048);
        var directReference = AssertContextReference(direct, leafPath);
        Assert.Equal(originalReference, directReference);

        var closure = await relationships.GetContext(leafPath, originalReference, ["body", "callers"], includeReferences: true,
            maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(closure, 32768, 2048);
        var projectedReference = AssertContextReference(closure, leafPath);
        Assert.Equal(originalReference, projectedReference);

        var rawDocumentationId = await relationships.GetContext(rootPath, "M:R02ContextLeaf.Target.Read", ["body", "callers"],
            includeReferences: true, maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(rawDocumentationId, 32768, 2048);
        var rawProjectedReference = AssertContextReference(rawDocumentationId, leafPath);
        Assert.Equal(originalReference, rawProjectedReference);

        var body = await symbols.GetSymbolBody(leafPath, [projectedReference], maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(body, 32768, 2048);
        Assert.Contains("return 7;", TextOf(body), StringComparison.Ordinal);

        var rawBody = await symbols.GetSymbolBody(leafPath, [rawProjectedReference], maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(rawBody, 32768, 2048);
        Assert.Contains("return 7;", TextOf(rawBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssemblySymbolResolutionFailureAfterScopeAcquisitionReleasesLease()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("r02-symbol-resolution-lease-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "R02SymbolLeaseOwner",
            "namespace R02SymbolLeaseOwner; public sealed class Target { public int Read() => 7; }");
        var symbols = new SymbolTools(runtime);
        var discovered = await symbols.FindSymbol(assemblyPath, pattern: "R02SymbolLeaseOwner.Target.Read", kind: "method",
            maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(discovered, 32768, 2048);
        var reference = ReadReference(TextOf(discovered));
        var relationships = new RelationshipTools(runtime);
        AssemblyNavigationSessionScope? acquiredScope = null;
        var activeAfterFailure = -1;
        CallToolResult failed;
        try
        {
            relationships.AfterAssemblySymbolScopeOpenedForTesting = (scope, _) =>
            {
                acquiredScope = scope;
                return Task.FromException(new InvalidOperationException("forced symbol resolution failure after scope acquisition"));
            };
            failed = await PollAsync(operation => relationships.GetCallTree(assemblyPath, reference,
                depth: 1, topN: 5, maxResponseBytes: 32768, maxResponseTokens: 2048, operationToken: operation));
            activeAfterFailure = runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath);
        }
        finally
        {
            relationships.AfterAssemblySymbolScopeOpenedForTesting = null;
            if (acquiredScope is not null) await acquiredScope.DisposeAsync();
        }
        Assert.True(failed.IsError ?? false, TextOf(failed));

        await AssertNormalAssemblySymbolResolutionCleanupAsync(relationships, runtime, assemblyPath, "R02SymbolLeaseOwner", reference);
        Assert.Equal(0, activeAfterFailure);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
    }

    [Fact]
    public async Task AssemblySymbolResolutionCancellationAfterScopeAcquisitionReleasesLease()
    {
        using var host = Host.CreateApplicationBuilder(Array.Empty<string>()).Build();
        await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
        using var fixture = TestTempDirectory.Create("r02-symbol-resolution-cancel-lease-");
        var assemblyPath = AssemblyTestHelper.EmitAssembly(fixture, "R02SymbolCancelOwner",
            "namespace R02SymbolCancelOwner; public sealed class Target { public int Read() => 7; }");
        var symbols = new SymbolTools(runtime);
        var discovered = await symbols.FindSymbol(assemblyPath, pattern: "R02SymbolCancelOwner.Target.Read", kind: "method",
            maxResponseBytes: 32768, maxResponseTokens: 2048);
        AssertSuccessWithinBudget(discovered, 32768, 2048);
        var reference = ReadReference(TextOf(discovered));
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelledResolver = new CancellationTokenSource();
        await cancelledResolver.CancelAsync();
        var cancelledResolverTask = Task.FromCanceled(cancelledResolver.Token);
        Assert.True(cancelledResolverTask.IsCanceled);
        AssemblyNavigationSessionScope? acquiredScope = null;
        var relationships = new RelationshipTools(runtime)
        {
            AfterAssemblySymbolScopeOpenedForTesting = (scope, _) =>
            {
                acquiredScope = scope;
                entered.TrySetResult(true);
                return cancelledResolverTask;
            },
        };
        var activeAfterCancellation = -1;
        CallToolResult cancelled;
        try
        {
            cancelled = await PollAsync(operation => relationships.GetCallTree(assemblyPath, reference, depth: 1, topN: 5,
                maxResponseBytes: 32768, maxResponseTokens: 2048, operationToken: operation));
            Assert.True(entered.Task.IsCompleted, TextOf(cancelled));
            activeAfterCancellation = runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath);
        }
        finally
        {
            relationships.AfterAssemblySymbolScopeOpenedForTesting = null;
            if (acquiredScope is not null) await acquiredScope.DisposeAsync();
        }
        Assert.True(cancelled.IsError ?? false, TextOf(cancelled));

        await AssertNormalAssemblySymbolResolutionCleanupAsync(relationships, runtime, assemblyPath, "R02SymbolCancelOwner", reference);
        Assert.Equal(0, activeAfterCancellation);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
    }

    private static string ReadReference(string text)
    {
        var references = ReadStableReferences(text);
        return Assert.Single(references);
    }

    private static void AssertPerItemRecoveryAction(string response, string reference)
        => AssertPerItemRecoveryAction(response, reference, expectedCode: null);

    private static void AssertPerItemRecoveryAction(string response, string reference, string? expectedCode)
    {
        var item = ReadItemSection(response, reference);
        var actionStart = item.IndexOf("Next action: ", StringComparison.Ordinal);
        Assert.True(actionStart >= 0, $"Missing item recovery action for {reference}:\n{item}");
        Assert.True(item[(actionStart + "Next action: ".Length)..].Trim().Length > 0, item);
        Assert.Contains(expectedCode is null ? "Resolution status: failed (" : $"Resolution status: failed ({expectedCode})",
            item, StringComparison.Ordinal);
    }

    private static string AssertContextReference(CallToolResult context, string expectedOwnerPath)
    {
        using var json = JsonDocument.Parse(BodyOf(TextOf(context)));
        var declaration = json.RootElement.GetProperty("target");
        var reference = Assert.IsType<string>(declaration.GetProperty("handoffId").GetString());
        var ownerPath = Assert.IsType<string>(declaration.GetProperty("ownerTargetPath").GetString());
        Assert.Equal(Path.GetFullPath(expectedOwnerPath), Path.GetFullPath(ownerPath));
        Assert.True(StableSymbolReferenceCodec.TryParse(reference, out var parsed, out var error), error?.Message);
        Assert.IsType<StableSymbolReference.Assembly>(parsed);
        Assert.Equal(reference, StableSymbolReferenceCodec.Format(parsed!));
        return reference;
    }

    private static async Task AssertNormalAssemblySymbolResolutionCleanupAsync(
        RelationshipTools relationships, NavigatorHostRuntime runtime, string assemblyPath, string assemblyName, string stableReference)
    {
        var missing = await PollAsync(operation => relationships.GetCallTree(assemblyPath,
            $"M:{assemblyName}.Target.Missing", depth: 1, topN: 5, maxResponseBytes: 32768,
            maxResponseTokens: 2048, operationToken: operation));
        AssertErrorWithinBudget(missing, "SYMBOL_NOT_FOUND", 32768, 2048);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));

        var rawSuccess = await PollAsync(operation => relationships.GetCallTree(assemblyPath,
            $"M:{assemblyName}.Target.Read", depth: 1, topN: 5, maxResponseBytes: 32768,
            maxResponseTokens: 2048, operationToken: operation));
        AssertSuccessWithinBudget(rawSuccess, 32768, 2048);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));

        var stableSuccess = await PollAsync(operation => relationships.GetCallTree(assemblyPath, stableReference,
            depth: 1, topN: 5, maxResponseBytes: 32768, maxResponseTokens: 2048, operationToken: operation));
        AssertSuccessWithinBudget(stableSuccess, 32768, 2048);
        Assert.Equal(0, runtime.AssemblyRegistry.GetActiveAccessCount(assemblyPath));
    }

    private static async Task<CallToolResult> PollAsync(Func<string?, Task<CallToolResult>> invoke)
    {
        string? operation = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var result = await invoke(operation);
            var text = TextOf(result);
            if (!text.Contains("operation=running", StringComparison.Ordinal)) return result;
            Assert.True(TryReadToken(text, "operationToken", out var token), text);
            operation = token;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The assembly operation did not complete after polling its operation token.");
    }
}
