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

When a task needs readable source for explicitly selected managed DLLs, use the deployed or built `AiNetCodeNavigator.AssemblyExport.exe` described in the assembly export guide. Check the dump's `last-run.json` and per-DLL manifest before treating generated files as current or complete. Use MCP navigation for targeted queries; its tools remain read-only and stdout remains reserved for JSON-RPC.
