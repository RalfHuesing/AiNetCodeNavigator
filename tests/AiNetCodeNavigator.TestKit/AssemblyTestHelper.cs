#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Threading;
using AiNetCodeNavigator.TestKit.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiNetCodeNavigator.TestKit;

/// <summary>
/// Describes an interface reference for a metadata-only fixture assembly.
/// </summary>
public sealed record AssemblyMetadataInterfaceReference(
    string AssemblyName,
    string NamespaceName,
    string InterfaceName);

/// <summary>
/// Helper for emitting in-memory Roslyn compilations to disk for assembly analysis tests.
/// </summary>
public static class AssemblyTestHelper
{
    private static readonly TimeSpan[] WriteRetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
    ];

    public static string EmitAssembly(
        TestTempDirectory temp,
        string name,
        string source,
        params string[] additionalReferences)
        => Emit(temp, name, source, OutputKind.DynamicallyLinkedLibrary, "dll", additionalReferences);

    public static string EmitExecutable(
        TestTempDirectory temp,
        string name,
        string source,
        params string[] additionalReferences)
        => Emit(temp, name, source, OutputKind.ConsoleApplication, "exe", additionalReferences);

    /// <summary>
    /// Emits a tiny managed assembly containing one public interface and optional interface references,
    /// without framework references or a runtime dependency closure.
    /// </summary>
    public static string EmitMetadataInterface(
        TestTempDirectory temp,
        string assemblyName,
        string namespaceName,
        string interfaceName,
        IReadOnlyList<AssemblyMetadataInterfaceReference>? implementedInterfaces = null)
    {
        ArgumentNullException.ThrowIfNull(temp);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(assemblyName + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            (AssemblyFlags)0,
            AssemblyHashAlgorithm.Sha256);

        var assemblyReferences = new Dictionary<string, AssemblyReferenceHandle>(StringComparer.Ordinal);
        var interfaceReferences = new List<TypeReferenceHandle>();
        foreach (var reference in implementedInterfaces ?? Array.Empty<AssemblyMetadataInterfaceReference>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.AssemblyName);
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.NamespaceName);
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.InterfaceName);

            if (!assemblyReferences.TryGetValue(reference.AssemblyName, out var assemblyReference))
            {
                assemblyReference = metadata.AddAssemblyReference(
                    metadata.GetOrAddString(reference.AssemblyName),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    (AssemblyFlags)0,
                    default);
                assemblyReferences.Add(reference.AssemblyName, assemblyReference);
            }

            interfaceReferences.Add(metadata.AddTypeReference(
                assemblyReference,
                metadata.GetOrAddString(reference.NamespaceName),
                metadata.GetOrAddString(reference.InterfaceName)));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var interfaceDefinition = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString(namespaceName),
            metadata.GetOrAddString(interfaceName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        foreach (var interfaceReference in interfaceReferences)
            metadata.AddInterfaceImplementation(interfaceDefinition, interfaceReference);

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);

        var outputPath = temp.GetPath($"{assemblyName}.dll");
        using var stream = new MemoryStream();
        image.WriteContentTo(stream);
        WriteFileWithRetry(outputPath, stream);
        return outputPath;
    }

    private static string Emit(
        TestTempDirectory temp,
        string name,
        string source,
        OutputKind outputKind,
        string extension,
        string[] additionalReferences)
    {
        ArgumentNullException.ThrowIfNull(temp);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(additionalReferences);

        var outputPath = temp.GetPath($"{name}.{extension}");
        var references = TestWorkspaceBuilder.CoreReferences
            .Concat(additionalReferences.Select(path => MetadataReference.CreateFromImage(
                ImmutableArray.CreateRange(File.ReadAllBytes(path)),
                filePath: path)))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(outputKind));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        }

        WriteFileWithRetry(outputPath, peStream);
        return outputPath;
    }

    private static void WriteFileWithRetry(string outputPath, MemoryStream stream)
    {
        for (var attempt = 0; attempt < WriteRetryDelays.Length; attempt++)
        {
            try
            {
                using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Position = 0;
                stream.CopyTo(fileStream);
                return;
            }
            catch (IOException) when (attempt < WriteRetryDelays.Length - 1)
            {
                Thread.Sleep(WriteRetryDelays[attempt + 1]);
            }
            catch (UnauthorizedAccessException) when (attempt < WriteRetryDelays.Length - 1)
            {
                Thread.Sleep(WriteRetryDelays[attempt + 1]);
            }
        }
    }
}
