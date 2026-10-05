# MCP Exploration Scenarios

Transport-free manual inspection harness for local MCP tool output and handler behavior.

## Core Rules for Agents

1. **Exploration Only**: Scenarios are experiments without assertions or snapshots; they never run in test suites or CI.
2. **Prevent Exploration Debt**: Scenarios created during a task or investigation are **strictly temporary**. Once an exploration is finished, **delete** your scenario `.cs` file before completing the task.
3. **Permanent Scenarios**: `ExploreAllTools` is the sole permanent baseline scenario exercising all 12 MCP navigation tools. Do not delete it.

## Adding a Scenario

- **Multiple Files Supported**: Create a new `.cs` file under `Scenarios/` with a descriptive, distinguishable name (e.g. `Explore<FeatureOrIssue>.cs`).
- **Namespace**: `AiNetCodeNavigator.Exploration.Scenarios`
- **Contract**: Implement `IExplorationScenario`. `ScenarioRegistry` discovers implementations automatically via reflection—no registration edits required:

```csharp
namespace AiNetCodeNavigator.Exploration.Scenarios;

internal sealed class ExploreFeatureX : IExplorationScenario
{
    public async Task RunAsync(ExplorationContext context)
    {
        await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "MyTargetType",
            maxResults = 10,
        }).ConfigureAwait(false);
    }
}
```

## Running Scenarios

```powershell
# List available scenarios
pwsh -File ./scripts/explore.ps1 -List

# Run a specific scenario (default timeout: 300s)
pwsh -File ./scripts/explore.ps1 -Scenario ExploreAllTools
pwsh -File ./scripts/explore.ps1 -Scenario ExploreFeatureX -TimeoutSeconds 180
```

## Artifacts and Output

Outputs are stored under `temp/exploration/<ScenarioName>/<UTC-timestamp>/` (git-ignored, ensured automatically):
- `##-tool_name/request.json`: Input arguments.
- `##-tool_name/response.json`: Latest raw `CallToolResult`.
- `##-tool_name/response.txt`: All delivered text with initial-call, poll, and outer-page attempt boundaries.
- `##-tool_name/payload.txt`: Reconstructed body with transport/page headers stripped.
- `##-tool_name/attempts/`: Individual polling/paging attempt logs.
- `error.txt`: Captured exception on failure.

## Supported MCP Tools (12 Tools)

- **Source / Scope**: `browse_target` (views: `scope`, `namespaces`), `get_file_skeleton`
- **Symbols**: `find_symbol`, `get_symbol_body`
- **Context & Structure**: `get_context` (sections: `body`, `members`, `uses`, `tests`)
- **Relationships**: `get_call_tree`, `get_type_relations`, `find_references`, `dependency_graph`, `resolve_type_origin`
- **Assembly**: `inspect_assembly`, `search_assembly`
