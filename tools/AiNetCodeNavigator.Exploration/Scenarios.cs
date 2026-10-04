using System.Text.Json;

namespace AiNetCodeNavigator.Exploration;

/// <summary>Add manual scenarios here, then register their names in All.</summary>
internal static class Scenarios
{
    internal static IReadOnlyDictionary<string, Func<ExplorationContext, Task>> All { get; } =
        new Dictionary<string, Func<ExplorationContext, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ExploreFindSymbol)] = ExploreFindSymbol,
            [nameof(ExploreSymbolBody)] = ExploreSymbolBody,
            [nameof(ExploreConsolidationBaseline)] = ExploreConsolidationBaseline,
            [nameof(ExploreContextUses)] = ExploreContextUses,
            [nameof(ExploreContextMembers)] = ExploreContextMembers,
            [nameof(ExploreBrowseTarget)] = ExploreBrowseTarget,
            [nameof(ExploreTypeRelations)] = ExploreTypeRelations,
            [nameof(ExploreExtensionDiscovery)] = ExploreExtensionDiscovery,
        };

    private static async Task ExploreExtensionDiscovery(ExplorationContext context)
    {
        foreach (var target in new[] { context.RepositorySolution, typeof(Scenarios).Assembly.Location })
        {
            string? cursor = null;
            do
            {
                var response = await context.CallAsync("find_symbol", new
                {
                    targetPath = target, extensionOnly = true, receiverType = "global::System.String",
                    pattern = "*Exploration*", namespaceFilter = "AINETCODENAVIGATOR.EXPLORATION", signatureFilter = "Exploration",
                    maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var items = page.RootElement.GetProperty("results")[0].GetProperty("entries");
                foreach (var item in items.EnumerateArray())
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = item.TryGetProperty("ownerTargetPath", out var owner) ? owner.GetString() : target,
                        symbolIdentifiers = new[] { item.GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                cursor = page.RootElement.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            } while (cursor is not null);
        }
    }

    private static async Task ExploreTypeRelations(ExplorationContext context)
    {
        // Declared runner fixtures exercise real Roslyn mappings and assembly ownership.
        var target = context.RepositorySolution;
        var identifier = "T:AiNetCodeNavigator.Exploration.IExplorationRelationProbe";
        foreach (var relation in new[] { "hierarchy", "implementations" })
        {
            string? cursor = null;
            do
            {
                var response = await context.CallAsync("get_type_relations", new
                {
                    targetPath = target, symbolIdentifier = identifier, relation, maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var root = page.RootElement;
                var items = root.GetProperty(relation == "hierarchy" ? "subtypes" : "implementations");
                if (cursor is null && items.GetArrayLength() > 0)
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = target, symbolIdentifiers = new[] { items[0].GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                cursor = root.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            } while (cursor is not null);
        }
        var assemblyTarget = typeof(Scenarios).Assembly.Location;
        foreach (var relation in new[] { "hierarchy", "implementations" })
        {
            var response = await context.CallAsync("get_type_relations", new
            {
                targetPath = assemblyTarget, symbolIdentifier = identifier, relation, maxResults = 1,
            }).ConfigureAwait(false);
            using var page = JsonDocument.Parse(response.Payload);
            var items = page.RootElement.GetProperty(relation == "hierarchy" ? "subtypes" : "implementations");
            if (items.GetArrayLength() > 0)
                await context.CallAsync("get_symbol_body", new
                {
                    targetPath = assemblyTarget, symbolIdentifiers = new[] { items[0].GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                }).ConfigureAwait(false);
        }
    }

    private static async Task ExploreFindSymbol(ExplorationContext context)
    {
        await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "NavigatorHostRuntime",
            kind = "class",
            scopeType = "production",
            maxResults = 10,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreConsolidationBaseline(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "StableSymbolReferenceCodec",
            kind = "class",
            scopeType = "production",
            maxResults = 10,
        }).ConfigureAwait(false);
        using var source = JsonDocument.Parse(discovery.Payload);
        var reference = source.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec")
            .GetProperty("handoffId").GetString();
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, maxBodyLines = 20,
        }).ConfigureAwait(false);
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, startLine = 21, maxBodyLines = 20,
        }).ConfigureAwait(false);
        await context.CallAsync("dependency_graph", new
        {
            targetPath = context.RepositorySolution, symbolIdentifier = reference,
            direction = "outgoing", depth = 1, scopeType = "production", maxResults = 50,
        }).ConfigureAwait(false);
        var callers = await context.CallAsync("get_context", new
        {
            targetPath = context.RepositorySolution, symbolIdentifier = reference, sections = new[] { "uses" }, maxResults = 5,
        }).ConfigureAwait(false);
        using (var callerPage = JsonDocument.Parse(callers.Payload))
        {
            if (callerPage.RootElement.GetProperty("sections")[0].TryGetProperty("resultCursor", out var cursor))
                await context.CallAsync("get_context", new
                {
                    targetPath = context.RepositorySolution, symbolIdentifier = reference,
                    sections = new[] { "uses" }, maxResults = 5, resultCursor = cursor.GetString(),
                }).ConfigureAwait(false);
        }
        var assemblyTarget = typeof(Core.Symbols.StableSymbolReferenceCodec).Assembly.Location;
        var inspection = await context.CallAsync("inspect_assembly", new
        {
            targetPath = assemblyTarget, typeName = "AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec",
            exactTypeName = true, publicOnly = true, maxResults = 10,
        }).ConfigureAwait(false);
        using var assembly = JsonDocument.Parse(inspection.Payload);
        var type = assembly.RootElement.GetProperty("types").EnumerateArray().Single();
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = type.GetProperty("ownerTargetPath").GetString(),
            symbolIdentifiers = new[] { type.GetProperty("handoffId").GetString() }, maxBodyLines = 20,
        }).ConfigureAwait(false);
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = type.GetProperty("ownerTargetPath").GetString(),
            symbolIdentifiers = new[] { type.GetProperty("handoffId").GetString() }, startLine = 21, maxBodyLines = 20,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreContextUses(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution, pattern = "TryParse", kind = "method",
            scopeType = "production", maxResults = 20,
        }).ConfigureAwait(false);
        using var symbols = JsonDocument.Parse(discovery.Payload);
        var reference = symbols.RootElement.GetProperty("results").EnumerateArray()
            .SelectMany(result => result.GetProperty("entries").EnumerateArray())
            .Single(entry => entry.GetProperty("docCommentId").GetString()!.StartsWith(
                "M:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec.TryParse(", StringComparison.Ordinal))
            .GetProperty("handoffId").GetString();
        var result = await context.CallAsync("get_context", new
        {
            targetPath = context.RepositorySolution, symbolIdentifier = reference,
            sections = new[] { "body", "uses" }, usageScope = "all", maxBodyLines = 20, maxResults = 5,
        }).ConfigureAwait(false);
        using var page = JsonDocument.Parse(result.Payload);
        var uses = page.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("name").GetString() == "uses");
        if (uses.TryGetProperty("resultCursor", out var cursor))
            await context.CallAsync("get_context", new
            {
                targetPath = context.RepositorySolution, symbolIdentifier = reference,
                sections = new[] { "body", "uses" }, usageScope = "all", maxBodyLines = 20,
                maxResults = 5, resultCursor = cursor.GetString(),
            }).ConfigureAwait(false);
        var firstUse = uses.GetProperty("items").EnumerateArray()
            .First(site => site.TryGetProperty("enclosingSymbolHandoffId", out var handoff) && handoff.ValueKind == JsonValueKind.String);
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = page.RootElement.GetProperty("target").GetProperty("ownerTargetPath").GetString(),
            symbolIdentifiers = new[] { firstUse.GetProperty("enclosingSymbolHandoffId").GetString() }, maxBodyLines = 20,
        }).ConfigureAwait(false);
        foreach (var direction in new[] { "incoming", "outgoing" })
            await context.CallAsync("get_call_tree", new
            {
                targetPath = context.RepositorySolution, symbolIdentifier = reference, direction,
                depth = 1, topN = 5, scopeType = "production",
            }).ConfigureAwait(false);
    }

    private static async Task ExploreContextMembers(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution, pattern = "StableSymbolReferenceCodec",
            kind = "class", scopeType = "production", maxResults = 10,
        }).ConfigureAwait(false);
        using var found = JsonDocument.Parse(discovery.Payload);
        var reference = found.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec")
            .GetProperty("handoffId").GetString();
        string? cursor = null;
        string? memberReference = null;
        string? owner = null;
        do
        {
            var response = await context.CallAsync("get_context", new
            {
                targetPath = context.RepositorySolution, symbolIdentifier = reference, sections = new[] { "members" },
                memberNameFilter = "Parse", memberKindFilter = "Method", memberSortBy = "name", memberScope = "production",
                maxResults = 1, resultCursor = cursor,
            }).ConfigureAwait(false);
            using var page = JsonDocument.Parse(response.Payload);
            var section = page.RootElement.GetProperty("sections")[0];
            owner ??= section.GetProperty("structure").GetProperty("targetPath").GetString();
            foreach (var member in section.GetProperty("items").EnumerateArray())
                memberReference ??= member.GetProperty("handoffId").GetString();
            cursor = section.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        } while (cursor is not null);
        if (memberReference is null || owner is null) throw new InvalidOperationException("Expected a navigable filtered codec member.");
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = owner, symbolIdentifiers = new[] { memberReference }, maxBodyLines = 20,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreBrowseTarget(ExplorationContext context)
    {
        var scope = await context.CallAsync("browse_target", new
        {
            targetPath = context.RepositorySolution, view = "scope", maxResults = 100,
        }).ConfigureAwait(false);
        using var inventory = JsonDocument.Parse(scope.Payload);
        var projectPath = inventory.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "project" && item.GetProperty("name").GetString() == "AiNetCodeNavigator.Core")
            .GetProperty("projectPath").GetString();
        var namespaces = await context.CallAsync("browse_target", new
        {
            targetPath = context.RepositorySolution, view = "namespaces", project = projectPath,
            namespacePrefix = "AiNetCodeNavigator.Core.Models", depth = 1, maxResults = 50,
        }).ConfigureAwait(false);
        using var types = JsonDocument.Parse(namespaces.Payload);
        var reference = types.RootElement.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("kind").GetString() == "type" && item.TryGetProperty("handoffId", out _))
            .GetProperty("handoffId").GetString();
        if (reference is null) throw new InvalidOperationException("Expected a navigable source type in the selected project/prefix.");
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, maxBodyLines = 20,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreSymbolBody(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "NavigatorHostRuntime",
            kind = "class",
            scopeType = "production",
            maxResults = 10,
        }).ConfigureAwait(false);
        using var document = JsonDocument.Parse(discovery.Payload);
        var references = document.RootElement.GetProperty("results").EnumerateArray()
            .SelectMany(result => result.GetProperty("entries").EnumerateArray())
            .Where(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Mcp.NavigatorHostRuntime")
            .Select(entry => entry.GetProperty("handoffId").GetString()).Where(reference => reference is not null).ToArray();
        // Discovery output remains a useful experiment if the declaration moves or disappears.
        if (references.Length != 1)
        {
            await File.WriteAllTextAsync(Path.Combine(context.OutputDirectory, "note.txt"),
                "Body follow-up skipped: discovery did not return one exact NavigatorHostRuntime reference. Inspect discovery output.").ConfigureAwait(false);
            return;
        }

        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution,
            symbolIdentifiers = references,
            maxBodyLines = 100,
        }).ConfigureAwait(false);
    }
}

internal interface IExplorationRelationProbe
{
    int Read();
}

internal sealed class ExplorationRelationFirst : IExplorationRelationProbe
{
    public int Read() => 1;
}

internal sealed class ExplorationRelationSecond : IExplorationRelationProbe
{
    int IExplorationRelationProbe.Read() => 2;
}

internal static class ExplorationStringExtensions
{
    internal static int ExplorationMark(this string value) => value.Length;
    internal static int ExplorationSize(this string value) => value.Length + 1;
}
