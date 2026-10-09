# Agent-led MCP navigation improvement

This task runs bounded cycles in which independent agents try realistic C# navigation questions, identify friction in actual Explorer output, and implement small evidence-backed improvements. The active [charter](charter.md), [protocol](protocol.md), and [roadmap](roadmap.md) are the operating instructions. Repository rules remain binding; the optional `.agents/agent-workflow/` is not invoked.

The user authorized exploratory cycle 2. Use only `scripts/explore.ps1` and `tools/AiNetCodeNavigator.Exploration` for navigation experiments. Do not use direct MCP JSON calls, a live MCP client/server for measurement, or an alternate simulator. The server already running in another process may be stale after code edits.

Keep external targets and all identifying details local and ignored. Tracked text uses R01–R04/A01 aliases. Current decisions go in [results.md](results.md); raw evidence stays under ignored `temp/external-repos/mcp-usage-audit/round2/`. The [first-round report](archive/round1/results.md) and instructions remain archived with their partial acceptance.

For execution, use [prompts/orchestrator.md](prompts/orchestrator.md). The [Explorer guide](../../docs/development/mcp-exploration.md), [tool contracts](../../docs/tools/README.md), [response budgets](../../docs/mcp-response-budgets.md), and [continuations](../../docs/mcp-long-running-calls.md) describe current behavior. Task plans are not evidence of implementation.
