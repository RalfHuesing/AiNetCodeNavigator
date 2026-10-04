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
