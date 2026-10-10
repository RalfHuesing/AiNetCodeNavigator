# MCP Tools

Use these tools through a connected MCP client. They are read-only navigation operations, not command-line subcommands. Targets are absolute existing `.sln` / `.slnx` paths for source or managed `.dll` / `.exe` paths for assemblies. Assembly navigation reads decompiled code and supported owner references; it never executes analyzed binaries.

This page is available offline with `AiNetCodeNavigator.exe --doc tools`. Read `--doc setup` to connect the server and `--doc overview` for installation and product boundaries. Server documentation uses stderr; save it with `2> navigator-tools.md` if useful. Repository links below are optional deeper reading.

## First steps through your client

1. Configure the installed server as a local stdio process using `--doc setup`, then connect your MCP client.
2. Ask the client to discover the server's tools. MCP `tools/list` is the actual source for exposed names, descriptions, and `inputSchema` in the installed build. Read parameter descriptions alongside schema defaults and bounds: nullable parameters can advertise `default: null` while their description explains the effective default; conditional options and analysis limits may be described rather than encoded as schema constraints. Follow discovery pagination if the client reports it. Client prefixes and server labels may differ from the tool names shown here.
3. Select the owning target and a narrow question. Use the discovered schema when preparing arguments; omit optional settings until needed.
4. Send a `tools/call` through the client and inspect its text content, errors, scope, and omissions. Copy returned references and owner paths into follow-up calls.

The examples below show MCP requests after the client's connection and initialization. A client may expose an equivalent tool UI instead of raw JSON-RPC. Do not paste these JSON requests into executable arguments.

```json
{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}
```

## Choose a tool

This is a navigation guide, not a second input-schema catalog. Use `tools/list` and its parameter descriptions for wire arguments, defaults, validation, and per-tool budgets.

| Question | Tool and practical behavior |
|---|---|
| Where is a declaration? | `find_symbol`: source or assembly. Patterns use case-insensitive substring matching, anchored `*` / `?` wildcards, or automatically detected regex; dot-separated patterns match qualified type/member names. Use `pattern` or `namePatterns`, not both. Narrow by kind, namespace, signature, or exact source project. `scopeType` selects `all`, `production`, or `tests`; assembly scope classifies owners. Extension-only discovery finds declared extension methods, without proving expression applicability. |
| What is its implementation? | `get_symbol_body`: source or decompiled declaration bodies, including batches. `symbolIdentifiers` accepts returned references. Body windows use one-based declaration-relative lines, not physical file lines. Inspect each item's success or failure. |
| What declarations are in this file? | `get_file_skeleton`: outlines without bodies or executable initializers. Accepts indexed source paths or references identifying declaration files; assembly files belong to the selected binary's decompiled source. |
| What is indexed or in this namespace? | `browse_target`: select exactly one `view`, `scope` or `namespaces`. Scope is source-only and reports loaded projects/documents, frameworks, and exclusions. Namespace browsing supports source and assembly; it is not a physical-file inventory. |
| Who calls it, or what does it call? | `get_call_tree`: incoming, outgoing, or both; bounded static traversal with ASCII or Mermaid output. Graph/fanout limits report truncation rather than a result-list cursor. |
| Where is it used? | `find_references`: direct or bounded transitive uses. Optional summary reports discovered reference-site totals. Paging and analysis coverage are separate. |
| What inherits or implements it? | `get_type_relations`: explicitly select `relation=hierarchy` or `implementations`. Contract/override roots and named type roots differ; follow unsupported-root recovery. |
| What depends on this type or file? | `dependency_graph`: select a file or symbol root and type, file, namespace, or source-only project view. Member roots select the owning type. |
| Where does this type come from? | `resolve_type_origin`: source project or metadata/framework/NuGet assembly; ambiguity can return candidates instead of one owner. |
| Which context do I need? | `get_context`: explicitly select `sections` from `body`, `members`, `uses`, `tests`. Assembly supports the first three only. Tests are static source candidates with an independent scope; they do not prove coverage. Section failures can preserve useful partial results. |
| What public API does this binary expose? | `inspect_assembly`: compact type overview; opt into `includeMembers=true` for member declarations. Keep each returned `handoffId` with its `ownerTargetPath`. |
| Where is text in a binary? | `search_assembly`: literal decompiled-text search by default; `isRegex=true` explicitly enables regex. Declaration/kind/file filters narrow the query. A file analysis limit differs from result pagination. |

## Source walkthrough: find and read a method

Assume your application has a solution at `C:\work\App\App.slnx` and a method named `LoadOrders`. Substitute your real target and search term.

```json
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"find_symbol","arguments":{"targetPath":"C:\\work\\App\\App.slnx","pattern":"LoadOrders","kind":"method","maxResults":10}}}
```

The following is an **illustrative excerpt of JSON inside the response's text content**, with omitted fields and explicit placeholders; it is not captured output. Matching declarations are under `results[].entries[]`:

```json
{
  "results": [
    {
      "pattern": "LoadOrders",
      "entries": [
        {
          "name": "<returned declaration name>",
          "signature": "<returned signature>",
          "projectName": "<returned project name>",
          "filePath": "<returned source path>",
          "handoffId": "<returned src: reference>"
        }
      ]
    }
  ]
}
```

Resolve multiple matches by signature, project, and file; do not select the first name match blindly. Replace the placeholder below with the exact selected `handoffId`. Keep the same solution target:

```json
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_symbol_body","arguments":{"targetPath":"C:\\work\\App\\App.slnx","symbolIdentifiers":["<returned src: reference>"],"startLine":1,"maxBodyLines":40}}}
```

Read the returned body and each item's status. If it offers another body window, finish any outer response pages first, then use the returned reference and next declaration-relative `startLine`, omitting `endLine`. For direct uses of the same selected method:

```json
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"find_references","arguments":{"targetPath":"C:\\work\\App\\App.slnx","symbolIdentifier":"<returned src: reference>","depth":1,"maxResults":10}}}
```

When the physical file and line are already known, an external bounded file read can answer a source-text question without symbol discovery. Source references remain usable across body edits or server restart when the owner path and declaration ID still select one declaration. Rediscover after renames, signature changes, or project moves.

## Assembly walkthrough: inspect and read a type

Select your managed binary, for example `C:\work\App\bin\App.Library.dll`. Begin with a small public type page:

```json
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"inspect_assembly","arguments":{"targetPath":"C:\\work\\App\\bin\\App.Library.dll","maxResults":10}}}
```

The following is an **illustrative excerpt of JSON inside text content**, with omitted fields and placeholders; it is not captured output:

```json
{
  "types": [
    {
      "name": "<returned type name>",
      "signature": "<returned type signature>",
      "handoffId": "<returned asm: reference>",
      "ownerTargetPath": "<returned absolute owner path>"
    }
  ]
}
```

After recovering all pages needed to choose the type, copy its exact `handoffId` and `ownerTargetPath` into the follow-up. They are a pair; the owner may differ from your initial target. Read the selected decompiled declaration:

```json
{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"get_symbol_body","arguments":{"targetPath":"<returned absolute owner path>","symbolIdentifiers":["<returned asm: reference>"],"maxBodyLines":40}}}
```

For a member list rather than a whole declaration body, use the same pair:

```json
{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"get_context","arguments":{"targetPath":"<returned absolute owner path>","symbolIdentifier":"<returned asm: reference>","sections":["members"],"maxResults":10}}}
```

Use returned member references for subsequent body or relationship queries. Never construct an `asm:` reference from a type's display name. If a result has no handoff reference, use its reported recovery or refine discovery. Native binaries are unsupported. Decompiled text and static relationships are evidence about the selected binary snapshot, not proof of runtime behavior.

## Shared request and response behavior

Keep the tool, target, query options, and page size unchanged during polling and paging. Treat all tokens as opaque. A successful response normally omits affirmative status and complete-analysis defaults; inspect actual item/section status, omissions, and truncation before claiming completeness or absence. Responses can contain JSON in text content, body text, or recovery text; do not assume every text block is standalone JSON, especially when outer paging splits it.

| Returned control | What to do next |
|---|---|
| `Status: operation=running` and `operationToken=...` | Running is not an analysis result. Wait at least `retryAfterMilliseconds`, then repeat the same tool, target, and query with the returned `operationToken`. Retain an active `resultCursor`; omit `continuationToken`. Inspect progress rather than repeatedly starting new work. |
| `continuationToken=...` in the response preamble | Read the next stored outer text page: repeat the same query with `continuationToken`, omitting `operationToken` and `resultCursor`. Collect all outer pages before using result-list cursors or body windows. |
| `resultCursor` in the result | After all outer pages, repeat the same query with this cursor and no other token. This pages known results in the same snapshot. If that call returns running, poll with its `operationToken` and retain this cursor. A `get_context` cursor continues one section but requires the original full `sections`, options, and page size. |
| `RESPONSE_BUDGET_TOO_SMALL` | Follow the reported `minimumResponseBytes` / `minimumResponseTokens` and recovery instructions. Repeat the unchanged query with the supported budget increase; preserve active tokens as directed. |
| Expired/invalid token or `STALE_SNAPSHOT` | Follow `nextAction`. Start a fresh query against the current target when required, then use only its new tokens and results. Do not combine body windows or result pages from different snapshots. |
| `TARGET_MISMATCH` or ambiguous/missing symbol | Use the reported owner target for an unchanged reference; resolve ambiguity using returned candidates. Rediscover renamed, removed, or changed declarations. |

For example, polling the initial source search adds one argument to that same request:

```json
{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"find_symbol","arguments":{"targetPath":"C:\\work\\App\\App.slnx","pattern":"LoadOrders","kind":"method","maxResults":10,"operationToken":"<returned operationToken>"}}}
```

For outer paging replace that `operationToken` argument with `continuationToken`; for result-list paging replace it with `resultCursor`. Use only the combination required by the preceding response.

`maxResponseBytes` bounds UTF-8 response text; `maxResponseTokens` optionally bounds text using `cl100k_base`. Discover per-tool defaults and supported values through `tools/list`. Budgets constrain delivery; they do not enlarge graph depth, scanned scope, or other analysis limits. Domain truncation without a cursor requires following `nextAction` or starting a new query with a supported analysis limit changed. Restoring missing owner/reference evidence can also be required.

Navigation tools do not edit analyzed source or binaries. Use external tools for edits, builds, and tests. Source loading uses MSBuild design-time evaluation and is not a sandbox for custom build targets. The server's stdout is reserved for MCP transport.

Optional repository details: [symbol resolution](../navigation/symbol-resolution.md), [response budgets](../mcp-response-budgets.md), [long-running calls](../mcp-long-running-calls.md), [tool result formatting](../mcp-tool-results.md), and [navigation context](../navigation/get-context.md).
