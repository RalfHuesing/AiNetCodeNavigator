# Agent Interaction Lab

Concept for evaluating agentic navigation through the production tool surface without MCP transport or a deployed Codex MCP connection.

The lab is a separate .NET console project under `tests/AiNetCodeNavigator.AgentLab/`. Lab arguments, recording, and report rendering belong there; the regular application receives no lab mode or lab options. The lab reuses a shared production tool registration and dispatcher. The agreed evaluation covers AiNetCodeNavigator, AiNetLinter, and all 22 product tools.

- [Concept draft](Konzept.md): binding boundaries, shared dispatch, CLI/session and artifact contracts, agent roles, coverage, and verification. Roadmap and implementation are blocked pending the mandatory concept review described at the beginning of the document.
- [Existing public-tool work](../AiNetLinter-Uebernehmen/Clusters/Cluster-09.md): planned host and tool registrations; this lab must share those contracts rather than introduce a second navigation API.

Infrastructure acceptance may use internal fixture tools. Full completion also requires production registration parity and the first documented agent investigation; incomplete product prerequisites must be reported as blocked or incomplete.

This task is in concept planning. No implementation or roadmap is authorized by this document.

After AiNetLinter-Uebernehmen is completed, a request to sharpen this concept means checking its assumptions against the implemented code and tests, updating integration points and evidence, and conducting a fresh Luna comprehension review. The resulting concept requires explicit user approval before the block is lifted; subsequent workflow steps still require separate requests. A generic implementation request does not bypass the review block.
