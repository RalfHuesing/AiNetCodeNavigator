# AiNetCodeNavigator agent map

This repository contains product specifications, a read-only MCP server for agentic C# code navigation, a separate offline assembly export CLI, and automated test infrastructure. Its binding product references are `docs/` and Navigator code and tests. Verify implementation claims against current local sources; specifications under `tasks/` describe acceptance requirements that may still be planned.

## Where to look

- [Project status and entry points](README.md)
- [Current-state documentation index](docs/README.md)
- [Assembly export CLI and agent usage](docs/assembly-export.md)
- [Agent rules](.agents/rules/README.md)
- [Optional task workflow](.agents/agent-workflow/README.md); use a step only when the task invokes it

## Required rules

- [Language and repository boundaries](.agents/rules/01-language-and-scope.mdc)
- [Documentation](.agents/rules/02-documentation.mdc)
- [Product boundaries](.agents/rules/03-product-boundaries.mdc)
- [Verification](.agents/rules/04-verification.mdc)
- [Git and automatic commits](.agents/rules/05-git.mdc)
- [Dependencies and NuGet packages](.agents/rules/06-dependencies.mdc)
- [Code quality](.agents/rules/07-code-quality.mdc)
- [AiNetCodeNavigator MCP navigation](.agents/rules/08-ainetcodenavigator-mcp-navigation.mdc)

Read the relevant specification and rules before changing files. Ask when a decision is missing or sources conflict.

Before changing documentation, consult the [embedded documentation map and maintenance workflow](docs/README.md#embedded-documentation-map-and-maintenance). It identifies the canonical pages embedded in each executable through `--doc` and the repository-only references; follow its maintenance and verification steps without creating separate CLI text copies.

When a task needs readable source for selected managed DLLs or EXEs, use the deployed or built `AiNetCodeNavigator.AssemblyExport.exe` described in the assembly export guide. Use named `--output`, repeatable `--source`, and optional `--include`/`--exclude` filename patterns; see the guide for selection, dependency and dry-run contracts. The marked dump is fully replaced on every export run. Check the dump's `last-run.log` and per-assembly manifest before treating generated files as current or complete. Use MCP navigation for targeted queries; its tools remain read-only and stdout remains reserved for JSON-RPC.
