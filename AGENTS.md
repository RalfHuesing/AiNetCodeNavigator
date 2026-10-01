# AiNetCodeNavigator agent map

This repository contains product specifications, an autonomous MCP server for agentic C# code navigation, and automated test infrastructure. Its binding product references are `docs/`, the local public contract matrix, and Navigator code and tests. Verify implementation claims against these local sources; specifications under `tasks/` describe work that may still be planned.

## Where to look

- [Project status and entry points](README.md)
- [Current-state documentation index](docs/README.md)
- [Public host and tool contract matrix](tasks/Navigator-Migration/Reviews/public-contract-matrix.md)
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
- [MCP navigation workflow](.agents/rules/08-mcp-navigation.mdc)

Read the relevant specification and rules before changing files. Ask when a decision is missing or sources conflict.
