# Navigator component map

This map locates the local implementation and its verification sources. The binding references are [current-state documentation](../../docs/README.md), the [local public contract matrix](Reviews/public-contract-matrix.md), and the code and tests below. Historical roadmap checkboxes are not proof that a public contract is accepted.

## Product boundaries

The catalog contains twenty read-only navigation tools and two maintenance tools. Source solutions and managed assemblies are navigation targets. Linting, quality metrics, code-smell detection, duplicate detection, and automatic refactoring are outside the product.

## Components and evidence

| Area | Local implementation | Local verification | Contract documentation |
|---|---|---|---|
| Target routing and resident solutions | [Workspace](../../src/AiNetCodeNavigator.Core/Workspace/) | [Workspace FastTests](../../tests/AiNetCodeNavigator.FastTests/Workspace/) | [Build and tests](../../docs/development/build-and-tests.md) |
| Compilation and syntax-tree cache | [Caching](../../src/AiNetCodeNavigator.Core/Caching/) | [Caching FastTests](../../tests/AiNetCodeNavigator.FastTests/Caching/) | [Cache contract](../../docs/development/build-and-tests.md) |
| Test builders and fixtures | [TestKit](../../tests/AiNetCodeNavigator.TestKit/) | [TestKit FastTests](../../tests/AiNetCodeNavigator.FastTests/TestKit/) | [Verification entry points](../../docs/development/build-and-tests.md) |
| Logging and application lifecycle | [Logging](../../src/AiNetCodeNavigator/Logging/) and [Program](../../src/AiNetCodeNavigator/Program.cs) | [FastTests](../../tests/AiNetCodeNavigator.FastTests/) and [integration tests](../../tests/AiNetCodeNavigator.IntegrationTests/) | [Host](../../docs/mcp-host.md) |
| Handoffs, symbol resolution, body, structure, references, impact, and context | [Symbols](../../src/AiNetCodeNavigator.Core/Symbols/) | [Symbol FastTests](../../tests/AiNetCodeNavigator.FastTests/Symbols/) | [Symbol resolution](../../docs/navigation/symbol-resolution.md) and [relationship contracts](../../docs/navigation/relationship-contracts.md) |
| Skeletons | [Skeletons](../../src/AiNetCodeNavigator.Core/Skeletons/) | [FastTests](../../tests/AiNetCodeNavigator.FastTests/) | [File skeleton](../../docs/navigation/get-file-skeleton.md) |
| Call graphs | [CallTree](../../src/AiNetCodeNavigator.Core/CallTree/) | [Call-tree FastTests](../../tests/AiNetCodeNavigator.FastTests/CallTree/) | [Call tree](../../docs/navigation/get-call-tree.md) |
| Type hierarchy and implementation discovery | [Hierarchy](../../src/AiNetCodeNavigator.Core/Hierarchy/) and [Symbols](../../src/AiNetCodeNavigator.Core/Symbols/) | [FastTests](../../tests/AiNetCodeNavigator.FastTests/) | [Type hierarchy](../../docs/navigation/get-type-hierarchy.md) and [references and implementations](../../docs/navigation/find-references-and-implementations.md) |
| Project and namespace dependencies | [Dependencies](../../src/AiNetCodeNavigator.Core/Dependencies/) | [FastTests](../../tests/AiNetCodeNavigator.FastTests/) | [Dependency graph](../../docs/navigation/dependency-graph.md) |
| File, namespace, and index scope | [FileStructure](../../src/AiNetCodeNavigator.Core/FileStructure/) | [FastTests](../../tests/AiNetCodeNavigator.FastTests/) | [File tree](../../docs/navigation/get-file-tree.md), [namespace tree](../../docs/navigation/get-namespace-tree.md), and [index scope](../../docs/navigation/get-index-scope.md) |
| Decompilation, assembly navigation, and type origins | [Assemblies](../../src/AiNetCodeNavigator.Core/Assemblies/) | [Assembly FastTests](../../tests/AiNetCodeNavigator.FastTests/Assemblies/) | [Decompilation](../../docs/navigation/assembly-decompilation.md), [assembly navigation](../../docs/navigation/assembly-navigation.md), and [type origin](../../docs/navigation/resolve-type-origin.md) |
| Results, budgets, validation, and operation lifecycle | [MCP layer](../../src/AiNetCodeNavigator/Mcp/) | [MCP FastTests](../../tests/AiNetCodeNavigator.FastTests/Mcp/) | [Results](../../docs/mcp-tool-results.md), [budgets](../../docs/mcp-response-budgets.md), [validation](../../docs/mcp-argument-validation.md), and [long-running calls](../../docs/mcp-long-running-calls.md) |
| Stdio host, tool registrations, and maintenance | [Host](../../src/AiNetCodeNavigator/Mcp/McpServerHost.cs), [runtime](../../src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs), and [tools](../../src/AiNetCodeNavigator/Mcp/Tools/) | [Real stdio integration tests](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/McpServerIntegrationTests.cs) | [Host](../../docs/mcp-host.md) and [registration evidence](../../docs/navigation/mcp-registration-status.md) |

## Acceptance records

- [Roadmap and local reference policy](Konzept.md)
- [Findings and technical debt](Findings.md)
- [Cluster reviews](Reviews/)

Read the public contract matrix alongside the documentation and tests when assessing acceptance. Preserve its distinctions between implementation evidence, independent audits, and remaining contract cases.
