#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AiNetCodeNavigator.FastTests.Assemblies;

[Trait("Category", "Component")]
public sealed class ExactAssemblySymbolResolverTests
{
    [Fact]
    public async Task DegradedManagedMemberReferenceResolvesItsExactOverloadAndBody()
    {
        using var directory = TestTempDirectory.Create("assembly-degraded-member-");
        var dependency = AssemblyTestHelper.EmitAssembly(directory, "MemberDependency",
            "namespace Neutral; public sealed class Input { }");
        var path = AssemblyTestHelper.EmitAssembly(directory, "MemberOwner", """
            namespace Neutral;
            public sealed class Api
            {
                public string Choose(Input? value) => "reference overload";
                public string Choose(int value) => "integer overload";
            }
            """, dependency);
        var fingerprint = AssemblyFingerprintCalculator.Create(path);
        var options = AssemblyDecompilationOptions.Default;
        var references = new AssemblyReferenceResolver().Resolve(path);
        var decompilation = await new AssemblyDecompilationAdapter().DecompileAsync(
            new DecompilationRequest(path, fingerprint,
                AssemblyFingerprintCalculator.CreateCacheKey(fingerprint, options), options, CancellationToken.None),
            references);
        Assert.NotEmpty(decompilation.Documents);
        var request = new AssemblyWorkspaceRequest(path, fingerprint, decompilation.Documents,
            references.MetadataReferences.Where(reference => reference is not PortableExecutableReference portable
                || !string.Equals(portable.FilePath, dependency, StringComparison.OrdinalIgnoreCase)).ToArray(),
            AssemblySessionStatus.Partial, decompilation.ProjectFilePath);
        using var snapshot = await new AssemblyRoslynWorkspaceFactory().CreateAsync(
            request, "MemberOwner", fingerprint.Sha256, CancellationToken.None);
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(snapshot.Compilation.Assembly.GetTypeByMetadataName("Neutral.Api"));
        var method = Assert.Single(type.GetMembers("Choose").OfType<IMethodSymbol>()
            .Where(member => member.Parameters[0].Type.SpecialType != SpecialType.System_Int32));
        var declarationId = DocumentationCommentId.CreateDeclarationId(method);
        Assert.NotNull(declarationId);
        Assert.Empty(DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, snapshot.Compilation));

        var created = ExactAssemblySymbolResolver.CreateReference("MemberOwner", snapshot.Compilation.Assembly,
            snapshot.Compilation, method);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var resolved = ExactAssemblySymbolResolver.Resolve(snapshot.Compilation.Assembly, snapshot.Compilation, created.Value!);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.True(SymbolEqualityComparer.Default.Equals(method, resolved.Value));
        var body = SourceSymbolBodyResolver.Resolve(resolved.Value!, 100);
        Assert.Contains("reference overload", body.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("integer overload", body.Body, StringComparison.Ordinal);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, ExactAssemblySymbolResolver.Resolve(
            snapshot.Compilation.Assembly, snapshot.Compilation,
            created.Value! with { SimpleName = "DifferentOwner" }).Error?.Code);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, ExactAssemblySymbolResolver.Resolve(
            snapshot.Compilation.Assembly, snapshot.Compilation,
            new StableSymbolReference.Assembly("MemberOwner", "M:Neutral.Api.Removed")).Error?.Code);
    }

    [Fact]
    public void DegradedMemberReferenceRejectsCollidingDeclarationIds()
    {
        // Duplicate declarations are possible in an incomplete decompiled compilation.
        var compilation = CSharpCompilation.Create("CollisionOwner",
            [CSharpSyntaxTree.ParseText("""
                namespace Neutral;
                public class Api
                {
                    public void Choose(Missing value) { }
                    public void Choose(Missing value) { }
                }
                """)]);
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation.Assembly.GetTypeByMetadataName("Neutral.Api"));
        var methods = type.GetMembers("Choose");
        Assert.Equal(2, methods.Length);
        var declarationId = DocumentationCommentId.CreateDeclarationId(methods[0]);
        Assert.NotNull(declarationId);
        Assert.Equal(declarationId, DocumentationCommentId.CreateDeclarationId(methods[1]));
        Assert.Empty(DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation));
        var reference = new StableSymbolReference.Assembly("CollisionOwner", declarationId);
        var result = ExactAssemblySymbolResolver.Resolve(compilation.Assembly, compilation, reference);
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, result.Error?.Code);
        var created = ExactAssemblySymbolResolver.CreateReference("CollisionOwner", compilation.Assembly, compilation, methods[0]);
        Assert.False(created.IsSuccess);
        Assert.Equal(NavigationErrorCodes.AmbiguousSymbol, created.Error?.Code);
    }

    [Fact]
    public async Task SameSimpleNameInDifferentTargetsResolvesOnlyInsideSelectedLeasedScope()
    {
        using var firstDirectory = TestTempDirectory.Create("stable-assembly-owner-first-");
        using var secondDirectory = TestTempDirectory.Create("stable-assembly-owner-second-");
        var firstPath = AssemblyTestHelper.EmitAssembly(firstDirectory, "SameName", "namespace Collision; public sealed class Target { public string First() => \"first\"; }");
        var secondPath = AssemblyTestHelper.EmitAssembly(secondDirectory, "SameName", "namespace Collision; public sealed class Target { public string Second() => \"second\"; }");
        var firstOpened = await AssemblyNavigationSessionScope.OpenAsync(firstPath, default);
        var secondOpened = await AssemblyNavigationSessionScope.OpenAsync(secondPath, default);
        Assert.True(firstOpened.IsSuccess, firstOpened.Error?.ToString());
        Assert.True(secondOpened.IsSuccess, secondOpened.Error?.ToString());
        await using var firstScope = firstOpened.Value!;
        await using var secondScope = secondOpened.Value!;

        var firstSymbol = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.ISymbol>(firstScope.Context.Assembly.GetTypeByMetadataName("Collision.Target"));
        var secondSymbol = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.ISymbol>(secondScope.Context.Assembly.GetTypeByMetadataName("Collision.Target"));
        var firstReference = ExactAssemblySymbolResolver.CreateReference(firstScope, firstSymbol);
        var secondReference = ExactAssemblySymbolResolver.CreateReference(secondScope, secondSymbol);

        Assert.True(firstReference.IsSuccess, firstReference.Error?.Message);
        Assert.True(secondReference.IsSuccess, secondReference.Error?.Message);
        Assert.Equal(firstReference.Value, secondReference.Value);
        Assert.Equal("SameName", firstReference.Value!.SimpleName);
        Assert.NotEqual(Path.GetFullPath(firstPath), Path.GetFullPath(secondPath));
        Assert.Equal(Path.GetFullPath(firstPath), firstScope.Context.Origin.CanonicalPath);
        Assert.Equal(Path.GetFullPath(secondPath), secondScope.Context.Origin.CanonicalPath);

        var firstResolved = ExactAssemblySymbolResolver.Resolve(firstScope, firstReference.Value);
        var secondResolved = ExactAssemblySymbolResolver.Resolve(secondScope, firstReference.Value);
        Assert.True(firstResolved.IsSuccess, firstResolved.Error?.Message);
        Assert.True(secondResolved.IsSuccess, secondResolved.Error?.Message);
        Assert.Contains("First", Assert.IsAssignableFrom<INamedTypeSymbol>(firstResolved.Value).GetMembers().Select(member => member.Name));
        Assert.Contains("Second", Assert.IsAssignableFrom<INamedTypeSymbol>(secondResolved.Value).GetMembers().Select(member => member.Name));

        var foreignName = ExactAssemblySymbolResolver.Resolve(firstScope,
            new StableSymbolReference.Assembly("OtherName", firstReference.Value.DeclarationId));
        Assert.False(foreignName.IsSuccess);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, foreignName.Error?.Code);
        AssertRecoveryHint(foreignName.Error?.Hint, "ownerTargetPath");

        var missingDeclaration = ExactAssemblySymbolResolver.Resolve(firstScope,
            new StableSymbolReference.Assembly("SameName", "T:Collision.Missing"));
        Assert.False(missingDeclaration.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, missingDeclaration.Error?.Code);
        AssertRecoveryHint(missingDeclaration.Error?.Hint, "rediscover");
    }

    [Fact]
    public async Task StableAssemblyReferenceSurvivesRebuildWhileExistingScopeKeepsItsLeasedGeneration()
    {
        using var targetDirectory = TestTempDirectory.Create("stable-assembly-rebuild-");
        using var replacementDirectory = TestTempDirectory.Create("stable-assembly-replacement-");
        var path = AssemblyTestHelper.EmitAssembly(targetDirectory, "Rebuilt", "namespace Rebuild; public sealed class Target { public string Legacy() => \"old\"; }");
        var firstOpened = await AssemblyNavigationSessionScope.OpenAsync(path, default);
        Assert.True(firstOpened.IsSuccess, firstOpened.Error?.ToString());
        await using var firstScope = firstOpened.Value!;
        var firstGeneration = firstScope.Context.Generation;
        var firstType = Assert.IsAssignableFrom<INamedTypeSymbol>(firstScope.Context.Assembly.GetTypeByMetadataName("Rebuild.Target"));
        var reference = ExactAssemblySymbolResolver.CreateReference(firstScope, firstType);
        var removedMethod = ExactAssemblySymbolResolver.CreateReference(firstScope, Assert.Single(firstType.GetMembers("Legacy")));
        Assert.True(reference.IsSuccess, reference.Error?.Message);
        Assert.True(removedMethod.IsSuccess, removedMethod.Error?.Message);

        var replacement = AssemblyTestHelper.EmitAssembly(replacementDirectory, "Rebuilt", "namespace Rebuild; public sealed class Target { public string Current() => \"rebuilt\"; }");
        File.Copy(replacement, path, overwrite: true);
        var secondOpened = await AssemblyNavigationSessionScope.OpenAsync(path, default);
        Assert.True(secondOpened.IsSuccess, secondOpened.Error?.ToString());
        await using var secondScope = secondOpened.Value!;

        Assert.True(secondScope.Context.Generation > firstGeneration);
        var firstResolved = ExactAssemblySymbolResolver.Resolve(firstScope, reference.Value!);
        var secondResolved = ExactAssemblySymbolResolver.Resolve(secondScope, reference.Value!);
        Assert.True(firstResolved.IsSuccess, firstResolved.Error?.Message);
        Assert.True(secondResolved.IsSuccess, secondResolved.Error?.Message);
        Assert.Contains("Legacy", Assert.IsAssignableFrom<INamedTypeSymbol>(firstResolved.Value).GetMembers().Select(member => member.Name));
        Assert.Contains("Current", Assert.IsAssignableFrom<INamedTypeSymbol>(secondResolved.Value).GetMembers().Select(member => member.Name));
        Assert.Equal(reference.Value!.DeclarationId, DocumentationCommentId.CreateDeclarationId(secondResolved.Value!));

        var removedInNewGeneration = ExactAssemblySymbolResolver.Resolve(secondScope, removedMethod.Value!);
        Assert.False(removedInNewGeneration.IsSuccess);
        Assert.Equal(NavigationErrorCodes.SymbolNotFound, removedInNewGeneration.Error?.Code);
        AssertRecoveryHint(removedInNewGeneration.Error?.Hint, "rediscover");
    }

    [Fact]
    public async Task AssemblyReferenceReopensAfterOwnerSessionEviction()
    {
        using var directory = TestTempDirectory.Create("stable-assembly-eviction-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "EvictedOwner", "namespace Eviction; public sealed class Target { public void Run() { } }");
        await using var registry = new AssemblyAnalysisSessionRegistry();
        StableSymbolReference.Assembly reference;
        var firstAccess = await registry.AcquireAsync(path, default);
        Assert.True(firstAccess.IsSuccess, firstAccess.Error?.Message);
        var originalGeneration = firstAccess.Value!.Generation;
        var originalWorkspace = originalGeneration.Snapshot.Workspace;
        await using (var firstScope = AssemblyNavigationSessionScope.Create(firstAccess.Value!))
        {
            var symbol = Assert.IsAssignableFrom<ISymbol>(firstScope.Context.Assembly.GetTypeByMetadataName("Eviction.Target"));
            var created = ExactAssemblySymbolResolver.CreateReference(firstScope, symbol);
            Assert.True(created.IsSuccess, created.Error?.Message);
            reference = created.Value!;
        }

        await registry.ExpireIdleSessionsAsync(DateTime.UtcNow.AddMinutes(11), path);
        var reopenedAccess = await registry.AcquireAsync(path, default);
        Assert.True(reopenedAccess.IsSuccess, reopenedAccess.Error?.Message);
        Assert.NotSame(originalGeneration, reopenedAccess.Value!.Generation);
        Assert.NotSame(originalWorkspace, reopenedAccess.Value.Generation.Snapshot.Workspace);
        await using var reopenedScope = AssemblyNavigationSessionScope.Create(reopenedAccess.Value!);
        var resolved = ExactAssemblySymbolResolver.Resolve(reopenedScope, reference);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(reference.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
    }

    [Fact]
    public async Task AssemblyReferenceUsesMetadataNameAcrossFilenameRename()
    {
        using var sourceDirectory = TestTempDirectory.Create("stable-assembly-filename-source-");
        using var renamedDirectory = TestTempDirectory.Create("stable-assembly-filename-renamed-");
        var sourcePath = AssemblyTestHelper.EmitAssembly(sourceDirectory, "MetadataOwner", "namespace FileName; public sealed class Target { }");
        var renamedPath = renamedDirectory.GetPath("renamed-on-disk.dll");
        File.Copy(sourcePath, renamedPath);
        var sourceOpened = await AssemblyNavigationSessionScope.OpenAsync(sourcePath, default);
        var renamedOpened = await AssemblyNavigationSessionScope.OpenAsync(renamedPath, default);
        Assert.True(sourceOpened.IsSuccess, sourceOpened.Error?.Message);
        Assert.True(renamedOpened.IsSuccess, renamedOpened.Error?.Message);
        await using var sourceScope = sourceOpened.Value!;
        await using var renamedScope = renamedOpened.Value!;
        var sourceSymbol = Assert.IsAssignableFrom<ISymbol>(sourceScope.Context.Assembly.GetTypeByMetadataName("FileName.Target"));
        var created = ExactAssemblySymbolResolver.CreateReference(sourceScope, sourceSymbol);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal("MetadataOwner", created.Value!.SimpleName);
        Assert.NotEqual(Path.GetFullPath(sourcePath), Path.GetFullPath(renamedPath));

        var resolved = ExactAssemblySymbolResolver.Resolve(renamedScope, created.Value);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal(created.Value.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolved.Value!));
    }

    [Fact]
    public async Task ReferencedAssemblyDeclarationMustResolveInItsOwnSelectedScope()
    {
        using var libraryDirectory = TestTempDirectory.Create("stable-assembly-reference-library-");
        using var callerDirectory = TestTempDirectory.Create("stable-assembly-reference-caller-");
        var libraryPath = AssemblyTestHelper.EmitAssembly(libraryDirectory, "ReferencedOwner", "namespace Referenced; public sealed class ExternalType { }");
        var callerPath = AssemblyTestHelper.EmitAssembly(callerDirectory, "CallerOwner",
            "namespace Caller; public sealed class CallerType { public Referenced.ExternalType? Value { get; set; } }", libraryPath);
        File.Copy(libraryPath, callerDirectory.GetPath("ReferencedOwner.dll"));
        var callerOpened = await AssemblyNavigationSessionScope.OpenAsync(callerPath, default);
        var libraryOpened = await AssemblyNavigationSessionScope.OpenAsync(libraryPath, default);
        Assert.True(callerOpened.IsSuccess, callerOpened.Error?.Message);
        Assert.True(libraryOpened.IsSuccess, libraryOpened.Error?.Message);
        await using var callerScope = callerOpened.Value!;
        await using var libraryScope = libraryOpened.Value!;
        var referencedSymbol = Assert.IsAssignableFrom<ISymbol>(callerScope.Context.Compilation.GetTypeByMetadataName("Referenced.ExternalType"));

        var wrongOwner = ExactAssemblySymbolResolver.CreateReference(callerScope, referencedSymbol);
        Assert.False(wrongOwner.IsSuccess);
        Assert.Equal(NavigationErrorCodes.TargetMismatch, wrongOwner.Error?.Code);
        AssertRecoveryHint(wrongOwner.Error?.Hint, "ownerTargetPath");

        var ownerSymbol = Assert.IsAssignableFrom<ISymbol>(libraryScope.Context.Assembly.GetTypeByMetadataName("Referenced.ExternalType"));
        var ownerReference = ExactAssemblySymbolResolver.CreateReference(libraryScope, ownerSymbol);
        Assert.True(ownerReference.IsSuccess, ownerReference.Error?.Message);
        Assert.Equal("ReferencedOwner", ownerReference.Value!.SimpleName);
        var resolvedInOwner = ExactAssemblySymbolResolver.Resolve(libraryScope, ownerReference.Value);
        Assert.True(resolvedInOwner.IsSuccess, resolvedInOwner.Error?.Message);
        Assert.Equal(ownerReference.Value.DeclarationId, DocumentationCommentId.CreateDeclarationId(resolvedInOwner.Value!));
    }

    [Fact]
    public async Task AssemblyReferenceRejectsImplicitDeclarationWithoutAStableId()
    {
        using var directory = TestTempDirectory.Create("stable-assembly-no-id-");
        var path = AssemblyTestHelper.EmitAssembly(directory, "NoStableId", "namespace NoId; public sealed class Target { public void Use() { void Local() { } Local(); } }");
        var opened = await AssemblyNavigationSessionScope.OpenAsync(path, default);
        Assert.True(opened.IsSuccess, opened.Error?.ToString());
        await using var scope = opened.Value!;
        var type = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.INamedTypeSymbol>(scope.Context.Assembly.GetTypeByMetadataName("NoId.Target"));
        var implicitConstructor = Assert.Single(type.InstanceConstructors.Where(method => method.IsImplicitlyDeclared));

        var result = ExactAssemblySymbolResolver.CreateReference(scope, implicitConstructor);
        Assert.False(result.IsSuccess);
        Assert.Equal(NavigationErrorCodes.UnsupportedIdentifier, result.Error?.Code);
    }

    private static void AssertRecoveryHint(string? hint, string action)
    {
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.Contains(action, hint, StringComparison.OrdinalIgnoreCase);
    }
}
