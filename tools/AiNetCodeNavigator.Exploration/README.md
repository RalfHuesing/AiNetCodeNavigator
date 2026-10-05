# AiNetCodeNavigator.Exploration

A standalone CLI tool for transport-free manual MCP tool output inspection against local production handlers.

## Usage

See [Scenarios README](Scenarios/README.md) for full agent guidelines, scenario authoring, and lifecycle instructions.

- **List Scenarios**: `pwsh -File ./scripts/explore.ps1 -List`
- **Run Baseline Scenario**: `pwsh -File ./scripts/explore.ps1 -Scenario ExploreAllTools`
- **Output Artifacts**: `temp/exploration/<ScenarioName>/<UTC-timestamp>/`
