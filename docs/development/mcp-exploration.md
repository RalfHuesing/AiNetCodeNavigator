# Manual MCP Exploration

Use this small runner to inspect actual local MCP tool output while implementing navigation tools. These scenarios are experiments: they have no expected-output assertions or snapshots, and never run automatically in the test suite or CI.

```powershell
pwsh -File ./scripts/explore.ps1 -List
pwsh -File ./scripts/explore.ps1 -Scenario ExploreAllTools
```

The script builds and runs the standalone `tools/AiNetCodeNavigator.Exploration` executable. `ExploreAllTools` is the permanent baseline scenario exercising all 12 MCP navigation tools (`find_symbol`, `get_symbol_body`, `browse_target`, `get_file_skeleton`, `get_context`, `get_call_tree`, `get_type_relations`, `find_references`, `dependency_graph`, `resolve_type_origin`, `inspect_assembly`, `search_assembly`).

## Adding a Scenario

Scenarios reside in the `AiNetCodeNavigator.Exploration.Scenarios` namespace under `tools/AiNetCodeNavigator.Exploration/Scenarios/`. Multiple scenario `.cs` files are supported.

Create a new `.cs` file with a descriptive name (e.g., `Scenarios/ExploreMyFeature.cs`) and implement `IExplorationScenario`:

```csharp
namespace AiNetCodeNavigator.Exploration.Scenarios;

internal sealed class ExploreMyTool : IExplorationScenario
{
    public async Task RunAsync(ExplorationContext context)
    {
        await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "MyDeclaration",
            maxResults = 10,
        }).ConfigureAwait(false);
    }
}
```

`ScenarioRegistry` discovers all `IExplorationScenario` implementations automatically via reflection—no central registration edits or project file modifications required.

> [!IMPORTANT]
> **Exploration Debt Rule**: Scenarios created for agent exploration are strictly temporary. Once an exploration is finished, delete the scenario `.cs` file so no exploration debt accumulates in the codebase. Only the permanent baseline scenario `ExploreAllTools` remains.

Use the current tool schemas and existing documentation for parameter names. `CallAsync` accepts any registered navigation tool name and a serializable argument object. It returns `Text` (all visible responses), `Payload` (the completed outer-page body), and `Response` (the final raw `CallToolResult`). Scenarios may use those returned values for follow-ups. Supply the original owner target and returned references unchanged.

## Output and Failures

Each run writes below `temp/exploration/<scenario>/<UTC-run>/` (created automatically if not present). Each call has its own numbered tool directory containing:

- `request.json`: the original MCP tool name and arguments.
- `response.json`: the latest raw `CallToolResult`.
- `response.txt`: all visible response text, including progress and page headers.
- `payload.txt`: the completed payload with outer-page metadata removed.
- `attempts/<number>/`: the exact request, raw response and text for each initial call, poll, or outer page.
- `error.txt`: the exception when a technical failure occurs. The run directory also receives an error file.

Artifacts remain available after failures and are Git-ignored through the existing `temp/` rule. Inspect `response.txt` for content quality and completeness, then use the raw JSON or reconstructed payload where helpful.

A scenario exits with code 1 on exceptions, cancellation/timeout, invalid requests or unexpected MCP `IsError=true`; successful empty or partial navigation output does not fail. The default timeout is 300 seconds for the whole scenario, configurable with `-TimeoutSeconds`. Unknown scenario names exit with code 2. Content quality is evaluated manually by the agent after the run.

The runner waits for `operation=running` and `operation=retry`, respecting the reported retry interval (at least one second), and follows opaque outer `continuationToken` values. It preserves the original query and removes operation/domain cursors when requesting outer pages, as the handlers require. Domain `resultCursor` pages, body windows, omissions and semantic recovery actions are left for explicit scenario follow-ups. Budget errors remain visible failures; adjust scenario parameters and rerun when appropriate.

## Execution Boundary

The runner creates the real `NavigatorHostRuntime`, loads targets through the production MSBuild/assembly infrastructure, discovers attributed production tool classes, generates their SDK schemas, runs the production argument validator and invokes the SDK binder and handler. It has no mock navigation results and uses the production output formatter and operation store.

This is a transport-free call simulation. It does not exercise JSON-RPC transport, host configuration, traffic capture or the host request-filter wrapper's final error-budget check. SDK `McpServerTool.InvokeAsync` requires a live server request context; the runner accesses its generated `AIFunction` binder at one isolated reflection boundary, matching the existing transport-free contract tests. An incompatible SDK change fails explicitly instead of substituting a different binder.

`CallAsync` optionally accepts `expectedErrorCode` for an intentional recovery probe. It preserves the error artifacts and returns only when the response is an error carrying that code; an unexpected success/error still fails.
