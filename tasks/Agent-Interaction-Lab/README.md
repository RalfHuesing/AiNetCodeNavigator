# Agent Interaction Lab

Concept for evaluating agentic navigation through the production tool surface without MCP transport or a deployed Codex MCP connection.

The proposed lab is a separate .NET console project under `tests/AiNetCodeNavigator.AgentLab/`. Lab arguments, recording, and report rendering belong there; the regular application receives no lab mode or lab options. The lab reuses production tool code.

- [Concept draft](Konzept.md): intent, proposed boundaries, evaluation method, and unresolved decisions.
- [Existing public-tool work](../AiNetLinter-Uebernehmen/Clusters/Cluster-09.md): planned host and tool registrations; this lab must share those contracts rather than introduce a second navigation API.

This task is in concept planning. No implementation or roadmap is authorized by this document.
