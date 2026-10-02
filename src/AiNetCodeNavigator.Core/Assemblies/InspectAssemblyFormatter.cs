#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Assemblies;

public static partial class InspectAssemblyFormatter
{
    public const int MaxDisplayedNamespaces = 10;
    public const string NamespaceSummaryPrefix = "Top 10 Namespaces and ";

    private static readonly Regex BodyLocationPathRegex = new(
        "(?i)(?<=— `)(?:[A-Z]:[\\\\/]|\\\\\\\\|/)[^`]+(?=`)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex AbsolutePathRegex = new(
        "(?i)(?:[A-Z]:[\\\\/]|\\\\\\\\|/)(?:[^\\\\/\\s`]+[\\\\/])+[^\\\\/\\s`]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static IReadOnlyList<string> CompactNamespaces(IReadOnlyList<string> namespaces)
    {
        if (namespaces.Count <= MaxDisplayedNamespaces) return namespaces;

        return namespaces
            .Take(MaxDisplayedNamespaces)
            .Append($"{NamespaceSummaryPrefix}{namespaces.Count - MaxDisplayedNamespaces} more")
            .ToList();
    }

    public static string FormatText(InspectAssemblyPayload payload, bool publicOnly)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, payload);
        AppendNamespaces(builder, payload.Namespaces, payload.TotalNamespaces, publicOnly);
        AppendTypes(builder, payload, publicOnly);
        AppendContinuation(builder, payload.ResultCursor);
        AppendReferences(builder, payload.References, payload.ReferenceSummary);
        AppendDiagnostics(builder, payload.Diagnostics);

        return SanitizeText(builder.ToString().TrimEnd());
    }

    private static void AppendHeader(StringBuilder builder, InspectAssemblyPayload payload)
    {
        builder.AppendLine($"Assembly: `{payload.Identity?.Name ?? "unknown"}`");
        AppendDecompileRoot(builder, payload.DecompiledSourceRoot);
        builder.AppendLine($"Completeness: `{payload.Completeness}`");
        if (payload.Origin is { } origin)
        {
            AppendOrigin(builder, origin);
        }
        builder.AppendLine();
        if (payload.Identity is { } identity)
        {
            builder.AppendLine($"Identity: {identity.Name}, Version {identity.Version}, Culture {identity.Culture}");
        }
    }

    private static void AppendOrigin(StringBuilder builder, AssemblyOrigin origin)
    {
        if (origin.IsDecompiled)
        {
            builder.AppendLine("Source: Decompilation");
            return;
        }

        builder.AppendLine($"Source: `{origin.OriginKind}`");
    }

    private static void AppendDecompileRoot(StringBuilder builder, string? decompiledSourceRoot)
    {
        if (!string.IsNullOrWhiteSpace(decompiledSourceRoot))
        {
            builder.AppendLine($"decompileRoot: `{decompiledSourceRoot}` (locally readable; generated; session-bound)");
        }
    }

    private static void AppendNamespaces(
        StringBuilder builder,
        IReadOnlyList<string> namespaces,
        int totalNamespaces,
        bool publicOnly)
    {
        var count = totalNamespaces > 0 ? totalNamespaces : namespaces.Count;
        builder.AppendLine($"{VisibilityLabel(publicOnly)}Namespaces: {count}");
        foreach (var namespaceName in namespaces) builder.AppendLine($"- `{namespaceName}`");
    }

    private static void AppendTypes(StringBuilder builder, InspectAssemblyPayload payload, bool publicOnly)
    {
        var typesTruncated = payload.ShownCount < payload.TotalTypes;
        builder.AppendLine($"{VisibilityLabel(publicOnly)}API types: {payload.ShownCount} of {payload.TotalTypes}{FormatTruncation(typesTruncated, payload.TruncatedBy)}");
        foreach (var type in payload.Types) AppendType(builder, type);
    }

    private static void AppendType(StringBuilder builder, AssemblyTypeDto type)
    {
        var qualifiedName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
        var memberCount = type.MembersTruncated
            ? $", Members {type.Members.Count} of {type.TotalMembers} shown{FormatTruncation(true, type.TruncatedBy)}"
            : $", {type.TotalMembers} members";
        builder.AppendLine($"- `{qualifiedName}`{FormatHandoffId(type.Id)} ({type.Kind}, {type.Accessibility}{memberCount})");
        foreach (var member in type.Members)
        {
            builder.AppendLine($"  - {member.Kind}: `{member.Signature}`{FormatHandoffId(member.Id)}");
        }
    }

    private static void AppendContinuation(StringBuilder builder, string? continuationToken)
    {
        if (!string.IsNullOrWhiteSpace(continuationToken))
        {
            builder.AppendLine($"Continuation: resultCursor: `{continuationToken}` reuse unchanged with the same query.");
        }
    }

    private static void AppendReferences(
        StringBuilder builder,
        IReadOnlyList<AssemblyReferenceDto> references,
        AssemblyReferenceSummary? summary)
    {
        var referenceCount = summary is null
            ? references.Count.ToString()
            : $"{summary.ShownReferenceCount} of {summary.TotalReferenceCount}";
        builder.AppendLine($"References: {referenceCount}{(summary?.ReferencesTruncated == true ? " (truncated)" : string.Empty)}");
        if (summary?.ReferencesTruncated == true && references.Count == 0)
        {
            builder.AppendLine("- Reference details not requested; includeReferences=true for the list");
        }
        foreach (var reference in references)
        {
            var diagnostic = string.IsNullOrWhiteSpace(reference.Diagnostic) ? string.Empty : $": {reference.Diagnostic}";
            builder.AppendLine($"- {reference.Name}, Version {reference.Version} (Depth {reference.Depth}, State {reference.ResolutionState}, {(reference.Resolved ? "resolved" : "unresolved")}{diagnostic})");
        }

        builder.AppendLine();
    }

    private static void AppendDiagnostics(StringBuilder builder, IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0) return;
        builder.AppendLine($"Diagnostics: {diagnostics.Count}");
        foreach (var diagnostic in diagnostics) builder.AppendLine($"- {diagnostic}");
    }

    private static string VisibilityLabel(bool publicOnly) => publicOnly ? "Public " : string.Empty;

    private static string FormatHandoffId(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? string.Empty
            : $"; handoffId: `{HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(id)}`";

    private static string FormatTruncation(bool truncated, IReadOnlyList<string>? reasons) =>
        truncated
            ? $" (truncated{(reasons is { Count: > 0 } ? $": {string.Join(", ", reasons)}" : string.Empty)})"
            : string.Empty;

    internal static string SanitizeText(string text)
    {
        var lines = text.Split('\n')
            .Where(line => !line.Contains("Paths: decompiledProject", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.TrimStart().StartsWith("- Generation:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.TrimStart().StartsWith("- GeneratedPath:", StringComparison.OrdinalIgnoreCase))
            .Select(line => SanitizeLine(line
                .Replace("generatedPath=", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("generation=", string.Empty, StringComparison.OrdinalIgnoreCase)));
        return string.Join('\n', lines);
    }

    private static string SanitizeLine(string line)
    {
        if (line.TrimStart().StartsWith("decompileRoot:", StringComparison.Ordinal)) return line;
        var withoutBodyLocation = BodyLocationPathRegex.Replace(line, "<decompiled-source>");
        return AbsolutePathRegex.Replace(withoutBodyLocation, match => Path.GetFileName(match.Value));
    }
}
