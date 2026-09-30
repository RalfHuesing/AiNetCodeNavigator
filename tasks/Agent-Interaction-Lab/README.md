# Agent Interaction Lab

Concept for evaluating agentic navigation through the production tool surface without MCP transport or a deployed Codex MCP connection.

The lab is a separate .NET console project under `tests/AiNetCodeNavigator.AgentLab/`. Lab arguments, recording, and report rendering belong there; the regular application receives no lab mode or lab options. The lab reuses a shared production tool registration and dispatcher. The agreed evaluation covers AiNetCodeNavigator, AiNetLinter, and all 22 product tools.

- [Concept draft](Konzept.md): binding boundaries, shared dispatch, CLI/session and artifact contracts, agent roles, coverage, and verification. The draft awaits explicit final approval.
- [Existing public-tool work](../AiNetLinter-Uebernehmen/Clusters/Cluster-09.md): planned host and tool registrations; this lab must share those contracts rather than introduce a second navigation API.

Infrastructure acceptance may use internal fixture tools. Full completion also requires production registration parity and the first documented agent investigation; incomplete product prerequisites must be reported as blocked or incomplete.

This task is in concept planning. No implementation or roadmap is authorized by this document.
