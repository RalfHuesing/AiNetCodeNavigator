# Manual MCP Exploration

Use this small runner to inspect actual local MCP tool output while implementing navigation tools. These scenarios are experiments: they have no expected-output assertions or snapshots, and never run automatically in the test suite or CI.

```powershell
pwsh -File ./scripts/explore.ps1 -List
pwsh -File ./scripts/explore.ps1 -Scenario ExploreFindSymbol
pwsh -File ./scripts/explore.ps1 -Scenario ExploreSymbolBody
pwsh -File ./scripts/explore.ps1 -Scenario ExploreConsolidationBaseline
```

The script builds and runs the standalone `tools/AiNetCodeNavigator.Exploration` executable. `ExploreFindSymbol` discovers the production `NavigatorHostRuntime` class in this repository. `ExploreSymbolBody` discovers that same class, selects its exact returned declaration identity, then passes its unchanged `handoffId` to `get_symbol_body`. If discovery has no unique usable match, it records a note and skips the follow-up; an empty successful result is not a technical failure.

## Adding a scenario

`ExploreConsolidationBaseline` discovers `StableSymbolReferenceCodec`, follows its source reference through two body windows, reads depth-one outgoing production dependencies, and reads two five-item direct-use context pages. It inspects the exact public type in the runner's own Core assembly and follows the returned owner/reference through two decompiled body windows. Body windows and context pages are deliberately sampled; inspect reported continuations before claiming complete content. The dependency and assembly requests provide compact fixed samples for comparing delivered output after contract changes.

Add a method to `tools/AiNetCodeNavigator.Exploration/Scenarios.cs` and register it in `Scenarios.All`:

```csharp
private static async Task ExploreMyTool(ExplorationContext context)
{
    await context.CallAsync("find_symbol", new
    {
        targetPath = context.RepositorySolution,
        pattern = "MyDeclaration",
        maxResults = 10,
    }).ConfigureAwait(false);
}
```

Use the current tool schemas and existing documentation for parameter names. `CallAsync` accepts any registered navigation tool name and a serializable argument object. It returns `Text` (all visible responses), `Payload` (the completed outer-page body), and `Response` (the final raw `CallToolResult`). Scenarios may use those returned values for follow-ups. Supply the original owner target and returned references unchanged.

## Output and failures

Each run writes below `temp/exploration/<scenario>/<UTC-run>/`. Each call has its own numbered tool directory containing:

- `request.json`: the original MCP tool name and arguments.
- `response.json`: the latest raw `CallToolResult`.
- `response.txt`: all visible response text, including progress and page headers.
- `payload.txt`: the completed payload with outer-page metadata removed.
- `attempts/<number>/`: the exact request, raw response and text for each initial call, poll, or outer page.
- `error.txt`: the exception when a technical failure occurs. The run directory also receives an error file.

Artifacts remain available after failures and are Git-ignored through the existing `temp/` rule. Inspect `response.txt` for content quality and completeness, then use the raw JSON or reconstructed payload where helpful.

A scenario exits with code 1 on exceptions, cancellation/timeout, invalid requests or unexpected MCP `IsError=true`; successful empty or partial navigation output does not fail. The default timeout is 300 seconds for the whole scenario, configurable with `-TimeoutSeconds`. Unknown scenario names exit with code 2. Content quality is evaluated manually by the agent after the run.

The runner waits for `operation=running` and `operation=retry`, respecting the reported retry interval (at least one second), and follows opaque outer `continuationToken` values. It preserves the original query and removes operation/domain cursors when requesting outer pages, as the handlers require. Domain `resultCursor` pages, body windows, omissions and semantic recovery actions are left for explicit scenario follow-ups. Budget errors remain visible failures; adjust scenario parameters and rerun when appropriate.

`ExploreContextMembers` discovers the source codec type, exhausts one-item member pages with name/kind/sort/source-scope filters, and follows a returned member reference with its unchanged owner target to a body window.

`ExploreBrowseTarget` reads the loaded source scope, passes its returned canonical Core project path to a selected namespace prefix, then follows a returned type reference to its source body.

`ExploreTypeRelations` exhausts one-item source pages for both relationship modes, follows returned references with the unchanged solution owner, and inspects both modes plus body handoffs against the runner's own assembly. Its two declared interface implementations provide real source/assembly mappings.

`ExploreExtensionDiscovery` filters two real declared string extensions by pattern, namespace, signature and receiver; exhausts one-item Source/Assembly pages; and follows every unchanged owner/reference to its body.

## Execution boundary

The runner creates the real `NavigatorHostRuntime`, loads targets through the production MSBuild/assembly infrastructure, discovers attributed production tool classes, generates their SDK schemas, runs the production argument validator and invokes the SDK binder and handler. It has no mock navigation results and uses the production output formatter and operation store.

This is a transport-free call simulation. It does not exercise JSON-RPC transport, host configuration, traffic capture or the host request-filter wrapper's final error-budget check. SDK `McpServerTool.InvokeAsync` requires a live server request context; the runner accesses its generated `AIFunction` binder at one isolated reflection boundary, matching the existing transport-free contract tests. An incompatible SDK change fails explicitly instead of substituting a different binder.

`CallAsync` optionally accepts `expectedErrorCode` for an intentional recovery probe. It preserves the error artifacts and returns only when the response is an error carrying that code; an unexpected success/error still fails. `ExploreMetadataRelations` uses the existing TestKit emitted DLL fixtures and real MSBuild source targets to show competing same-identity owners, explicit returned-owner retry, exhaustive one-item hierarchy/implementation pages, separate source/DLL body follow-ups and BCL IDisposable.Dispose mappings. The temporary analyzed fixtures are cleaned up after the scenario; saved requests/responses remain as snapshot evidence. Interface declaration text is separate from executable-body availability.
