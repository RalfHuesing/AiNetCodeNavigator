using System.Text.Json;

namespace AiNetCodeNavigator.Exploration.Scenarios;

/// <summary>
/// Permanent exploration scenario that calls all 12 MCP navigation tools to verify tool dispatch and output generation.
/// </summary>
internal sealed class ExploreAllTools : IExplorationScenario
{
    public async Task RunAsync(ExplorationContext context)
    {
        var solutionTarget = context.RepositorySolution;
        var runnerAssemblyTarget = typeof(ExplorationContext).Assembly.Location;

        // 1. find_symbol
        var findResult = await context.CallAsync("find_symbol", new
        {
            targetPath = solutionTarget,
            pattern = "NavigatorHostRuntime",
            kind = "class",
            maxResults = 10,
        }).ConfigureAwait(false);

        using var findDoc = JsonDocument.Parse(findResult.Payload);
        var entry = findDoc.RootElement.GetProperty("results")[0]
            .GetProperty("entries")[0];
        var symbolId = entry.GetProperty("handoffId").GetString()
            ?? throw new InvalidOperationException("Expected handoffId for NavigatorHostRuntime.");

        // 2. get_symbol_body
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = solutionTarget,
            symbolIdentifiers = new[] { symbolId },
            maxBodyLines = 30,
        }).ConfigureAwait(false);

        // 3. browse_target
        await context.CallAsync("browse_target", new
        {
            targetPath = solutionTarget,
            view = "scope",
        }).ConfigureAwait(false);

        // 4. get_file_skeleton
        await context.CallAsync("get_file_skeleton", new
        {
            targetPath = solutionTarget,
            filePaths = new[] { "src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs" },
        }).ConfigureAwait(false);

        // 5. get_context
        await context.CallAsync("get_context", new
        {
            targetPath = solutionTarget,
            symbolIdentifier = symbolId,
            sections = new[] { "members" },
            maxResults = 10,
        }).ConfigureAwait(false);

        // 6. get_call_tree
        await context.CallAsync("get_call_tree", new
        {
            targetPath = solutionTarget,
            symbolIdentifier = symbolId,
            direction = "outgoing",
            depth = 1,
            topN = 5,
        }).ConfigureAwait(false);

        // 7. get_type_relations
        await context.CallAsync("get_type_relations", new
        {
            targetPath = solutionTarget,
            symbolIdentifier = "AiNetCodeNavigator.Mcp.NavigatorHostRuntime",
            relation = "hierarchy",
            maxResults = 10,
        }).ConfigureAwait(false);

        // 8. find_references
        await context.CallAsync("find_references", new
        {
            targetPath = solutionTarget,
            symbolIdentifier = symbolId,
            depth = 1,
            maxResults = 10,
        }).ConfigureAwait(false);

        // 9. dependency_graph
        await context.CallAsync("dependency_graph", new
        {
            targetPath = solutionTarget,
            symbolIdentifier = symbolId,
            level = "type",
            depth = 1,
            maxResults = 10,
        }).ConfigureAwait(false);

        // 10. resolve_type_origin
        await context.CallAsync("resolve_type_origin", new
        {
            targetPath = runnerAssemblyTarget,
            typeName = "AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec",
        }).ConfigureAwait(false);

        // 11. inspect_assembly
        await context.CallAsync("inspect_assembly", new
        {
            targetPath = runnerAssemblyTarget,
            includeMembers = true,
            maxResults = 10,
        }).ConfigureAwait(false);

        // 12. search_assembly
        await context.CallAsync("search_assembly", new
        {
            targetPath = runnerAssemblyTarget,
            pattern = "ExplorationContext",
            maxResults = 10,
        }).ConfigureAwait(false);
    }
}
