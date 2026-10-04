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
        };

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
            targetPath = context.RepositorySolution, symbolIdentifier = reference, sections = new[] { "callers" }, maxResults = 5,
        }).ConfigureAwait(false);
        using (var callerPage = JsonDocument.Parse(callers.Payload))
        {
            if (callerPage.RootElement.GetProperty("sections")[0].TryGetProperty("resultCursor", out var cursor))
                await context.CallAsync("get_context", new
                {
                    targetPath = context.RepositorySolution, symbolIdentifier = reference,
                    sections = new[] { "callers" }, maxResults = 5, resultCursor = cursor.GetString(),
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
