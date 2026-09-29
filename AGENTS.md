# AiNetCodeNavigator agent map

This repository contains product specifications, an MCP server for agentic C# code navigation, and automated test infrastructure. Treat `docs/` as the current-state reference once implemented and verify implementation claims against code and tests; specifications under `tasks/` describe work that may still be planned.

## Where to look

- [Project status and entry points](README.md)
- [Agent rules](.agents/rules/README.md)
- [Optional task workflow](.agents/agent-workflow/README.md); use a step only when the task invokes it

## Required rules

- [Language and repository boundaries](.agents/rules/01-language-and-scope.mdc)
- [Documentation](.agents/rules/02-documentation.mdc)
- [Product boundaries](.agents/rules/03-product-boundaries.mdc)
- [Verification](.agents/rules/04-verification.mdc)
- [Git and automatic commits](.agents/rules/05-git.mdc)
- [Dependencies and NuGet packages](.agents/rules/06-dependencies.mdc)
- [Code practices](.agents/rules/07-code-practices.mdc)
- [MCP navigation workflow](.agents/rules/08-mcp-navigation.mdc)

Read the relevant specification and rules before changing files. Ask when a decision is missing or sources conflict.
