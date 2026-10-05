# Agent rules

The linked rules apply to work in this repository:

- [Language and repository boundaries](01-language-and-scope.mdc)
- [Documentation](02-documentation.mdc)
- [Product boundaries](03-product-boundaries.mdc)
- [Verification](04-verification.mdc)
- [Git and automatic commits](05-git.mdc)
- [Dependencies and NuGet packages](06-dependencies.mdc)
- [Code quality](07-code-quality.mdc)
- [AiNetCodeNavigator MCP navigation](08-ainetcodenavigator-mcp-navigation.mdc)

Keep rules short and enforceable. Put implementation details in `docs/` only after they exist. Put planned contracts in `tasks/`.

Keep the copyable MCP navigation rule independent of repository paths. Tool parameters, defaults, examples and protocol details belong in the [MCP tool reference](../../docs/tools/README.md) and its linked current-state documentation; use exposed schemas and response instructions while navigating.
