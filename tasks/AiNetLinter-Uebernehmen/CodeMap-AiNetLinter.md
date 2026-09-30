# Code-Map: AiNetLinter zu AiNetCodeNavigator

Diese Landkarte dokumentiert alle wiederverwendbaren Komponenten, Quellpfade und zugehörigen Tests aus `AiNetLinter` (`C:\Daten\Entwicklung\Ralf\AiNetLinter`) sowie ihre Zielorte und Anpassungen in `AiNetCodeNavigator`.

---

## 1. Übersicht & Abgrenzung (Scope)

| Bereich | In AiNetLinter | In AiNetCodeNavigator | Status / Begründung |
|---|---|---|---|
| **C#-Navigation via AST** | `Mcp/Tools/...` | `AiNetCodeNavigator.Core` + `AiNetCodeNavigator.Mcp.Tools` | **Vollständig übernehmen** (Symbol-, Referenz-, Call-, Hierarchie-Tools). |
| **Assembly-Dekompilierung** | `Mcp/Assemblies/...` | `AiNetCodeNavigator.Core.Assemblies` + Tools | **Vollständig übernehmen** (`ICSharpCode.Decompiler` zu virtuellem Roslyn-Workspace). |
| **Handoff- & Token-System** | `Mcp/Handoffs/...` | `AiNetCodeNavigator.Core.Models` / `AiNetCodeNavigator.Core.Symbols` | **Vollständig übernehmen** (Kompakte `h:...`-IDs für idempotente Folgeaufrufe). |
| **Workspace- & Resident-Cache**| `Mcp/Projects/...` | `AiNetCodeNavigator.Core.Workspace` & `Caching` | **Vollständig übernehmen** (MSBuildWorkspace, Projekt-Leases, Retry bei Ladezustand). |
| **MCP-Protokoll & Host** | `Commands/McpServerCommand` | `src/AiNetCodeNavigator/Mcp` | **Vollständig übernehmen** (Stdio-Transport, Budgeting, Truncation). |
| **Linter & Rule Engine** | `Core/Linter*.cs`, `Rules` | **Ausgeschlossen** | Nicht im Scope. Kein `verify`, keine Code-Smells, keine Linter-Regeln. |
| **Metriken & Hotspots** | `Metrics/*`, `Diagnostics` | **Ausgeschlossen** | Nicht im Scope. Keine Komplexitäts-/Coupling-Berechnungen. |
| **Code-Duplikate** | `DuplicateDetection/*` | **Ausgeschlossen** | Nicht im Scope. |

---

## 2. Detaillierte Komponenten-Zuordnung

### 2.1 Infrastruktur, Logging & Workspace ("Von unten")

| Komponente in AiNetLinter | Pfad in AiNetLinter | Zielpfad in AiNetCodeNavigator | Tests in AiNetLinter |
|---|---|---|---|
| Target-Erkennung & Routing | `src/AiNetLinter/Mcp/AnalysisTarget.cs`, `AnalysisTargetResolver.cs` | `src/AiNetCodeNavigator.Core/Workspace/` | `src/AiNetLinter.FastTests/Mcp/AnalysisTargetResolverTests.cs` |
| Resident Solution Loader & Registry | `src/AiNetLinter/Mcp/Projects/ProjectRegistry.cs`, `ProjectInstanceFactory.cs`, `ProjectLease.cs`, `ProjectDefinitionLoader.cs` | `src/AiNetCodeNavigator.Core/Workspace/` | `src/AiNetLinter.FastTests/Mcp/Projects/*` |
| Compilation & File Cache | `src/AiNetLinter/Cache/AnalysisCacheManager.cs` | `src/AiNetCodeNavigator.Core/Caching/` | `src/AiNetLinter.FastTests/Cache/*` |
| TestKit Builders & Fixtures | `src/AiNetLinter.TestKit/*`, `TestHelper.cs` | `tests/AiNetCodeNavigator.TestKit/` | Alle FastTests |
| Serilog Host Logging (Stdio-Safe) | `src/AiNetLinter/Logging/*`, `Program.cs` | `src/AiNetCodeNavigator/Logging/` | `src/AiNetLinter.IntegrationTests/Mcp/McpServerLifetimeTests.cs` |

### 2.2 Handoffs & Symbol-Identität

| Komponente in AiNetLinter | Pfad in AiNetLinter | Zielpfad in AiNetCodeNavigator | Tests in AiNetLinter |
|---|---|---|---|
| Symbol-Identität & Keying | `src/AiNetLinter/Mcp/AnalysisSymbolIdentity.cs` | `src/AiNetCodeNavigator.Core/Symbols/SymbolIdentity.cs` | `src/AiNetLinter.FastTests/Mcp/Tools/SymbolGraph/*` |
| Handoff Token & Store | `src/AiNetLinter/Mcp/Handoffs/HandoffHandleRegistry.cs`, `HandoffCounterStore.cs`, `HandoffCounterAlphabet.cs`, `SymbolHandoffIdentifier.cs` | `src/AiNetCodeNavigator.Core/Models/` & `Symbols/` | `src/AiNetLinter.FastTests/Mcp/Handoffs/*` |

### 2.3 Semantische Navigation (Symbol, Body, Skeleton, Structure)

| Feature / MCP-Tool | Quellcode in AiNetLinter | Ziel in AiNetCodeNavigator | Relevante Tests in AiNetLinter |
|---|---|---|---|
| `find_symbol` | `Mcp/Tools/SymbolGraph/FindSymbolTool.cs`, `FindSymbolScanner.cs`, `FindSymbolResponseBudget.cs` | `Core/Symbols/` + `Mcp/Tools/Symbols/` | `FastTests/Mcp/Tools/SymbolGraph/FindSymbolToolTests.cs` |
| `get_symbol_body` | `Mcp/Tools/GetSymbolBodyTool.cs`, `SourceSymbolBodyResolver.cs` | `Core/Symbols/` + `Mcp/Tools/Symbols/` | `FastTests/Mcp/Tools/SymbolGraph/GetSymbolBodyToolTests.cs` |
| `get_file_skeleton` | `Maps/Skeleton/SkeletonSyntaxWalker.cs`, `SkeletonMapBuilder.cs`, `SkeletonMarkdownRenderer.cs`, `Mcp/Tools/FileStructure/GetFileSkeletonTool.cs` | `Core/Skeletons/` + `Mcp/Tools/Structure/` | `FastTests/Maps/SkeletonMapTests.cs` |
| `get_class_structure` | `Mcp/Tools/FileStructure/GetClassStructureTool.cs`, `GetClassStructureModels.cs`, `GetClassStructureResponseBudget.cs` | `Core/Symbols/` + `Mcp/Tools/Structure/` | `FastTests/Mcp/Tools/FileStructure/GetClassStructureToolTests.cs` |
| `get_test_context` | `Core/TestDetector.cs`, `Mcp/Tools/TestContext/GetTestContextTool.cs`, `TestRecommendationBuilder.cs` | `Core/Symbols/` + `Mcp/Tools/Structure/` | `FastTests/Mcp/Tools/TestContext/GetTestContextToolTests.cs` |
| `get_feature_context` | `Mcp/Tools/FeatureContext/GetFeatureContextTool.cs`, `FeatureContextScanner.cs`, `FeatureContextFormatter.cs` *(ohne Linter-Violations!)* | `Core/Symbols/` + `Mcp/Tools/Structure/` | `FastTests/Mcp/Tools/FeatureContext/GetFeatureContextToolTests.cs` |

### 2.4 Graphen, Beziehungen & Hierarchien

| Feature / MCP-Tool | Quellcode in AiNetLinter | Ziel in AiNetCodeNavigator | Relevante Tests in AiNetLinter |
|---|---|---|---|
| `get_call_tree` | `Mcp/Tools/CallTree/GetCallTreeTool.cs`, `CallGraphTextRenderer.cs`, `CallTreeMermaidRenderer.cs`, `CallGraphResponseBudget.cs` | `Core/CallTree/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/CallTree/GetCallTreeToolTests.cs` |
| `find_references` | `Mcp/Tools/SymbolGraph/FindReferencesTool.cs`, `FindReferencesTool.Resolver.cs` | `Core/Symbols/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/SymbolGraph/FindReferencesToolTests.cs` |
| `get_type_hierarchy` | `Mcp/Tools/SymbolGraph/GetTypeHierarchyTool.cs`, `GetTypeHierarchyFormatter.cs` | `Core/Hierarchy/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/TypeHierarchy/GetTypeHierarchyToolTests.cs` |
| `find_implementations` | `Mcp/Tools/SymbolGraph/FindSymbolScanner.cs`, `FindSymbolTool.cs` | `Core/Hierarchy/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/SymbolGraph/FindImplementationsTests.cs` |
| `get_impact` | `Core/DiffImpactAnalyzer.cs`, `Mcp/Tools/SymbolGraph/GetImpactTool.cs` | `Core/Symbols/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/SymbolGraph/GetImpactToolTests.cs` |
| `dependency_graph` | `Mcp/Tools/DependencyGraph/DependencyGraphTool.cs`, `DependencyGraphScanner.cs` | `Core/Dependencies/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/DependencyGraph/DependencyGraphToolTests.cs` |
| `resolve_type_origin` | `Mcp/Tools/TypeResolution/ResolveTypeOriginTool.cs`, `ResolveTypeOriginHandoffResolver.cs` | `Core/Assemblies/` + `Mcp/Tools/Relationships/` | `FastTests/Mcp/Tools/TypeResolution/ResolveTypeOriginToolTests.cs` |

### 2.5 Struktur & Scope

| Feature / MCP-Tool | Quellcode in AiNetLinter | Ziel in AiNetCodeNavigator | Relevante Tests in AiNetLinter |
|---|---|---|---|
| `get_file_tree` | `Mcp/Tools/FileStructure/GetFileTreeTool.cs`, `GetFileTreeScanner.cs`, `SolutionFileWalker.cs`, `FileTreeFilter.cs` | `Core/Workspace/` + `Mcp/Tools/Structure/` | `FastTests/Mcp/Tools/FileStructure/GetFileTreeToolTests.cs` |
| `get_namespace_tree` | `Mcp/Tools/FileStructure/GetNamespaceTreeTool.cs`, `GetNamespaceTreeScanner.cs`, `GetNamespaceTreeResponseBudget.cs` | `Core/Workspace/` + `Mcp/Tools/Structure/` | `FastTests/Mcp/Tools/FileStructure/GetNamespaceTreeToolTests.cs` |
| `get_index_scope` | `Mcp/Tools/FileStructure/GetIndexScopeTool.cs`, `GetIndexScopeScanner.cs` | `Core/Workspace/` + `Mcp/Tools/Structure/` | `FastTests/Mcp/Tools/FileStructure/GetIndexScopeToolTests.cs` |

### 2.6 Assembly-Dekompilierung & Binary-Analyse

| Feature / MCP-Tool | Quellcode in AiNetLinter | Ziel in AiNetCodeNavigator | Relevante Tests in AiNetLinter |
|---|---|---|---|
| Decompiler & Virtual Workspace | `Mcp/Assemblies/Analysis/AssemblyDecompilationCache.cs`, `AssemblyDecompilationAdapter.cs`, `AssemblyRoslynWorkspaceFactory.cs`, `AssemblyAnalysisSession.cs` | `Core/Assemblies/` | `FastTests/Mcp/Assemblies/AssemblyDecompilationCacheTests.cs` |
| `inspect_assembly` | `Mcp/Tools/AssemblyAnalysis/InspectAssemblyTool.cs`, `InspectAssemblyFormatter.cs` | `Core/Assemblies/` + `Mcp/Tools/Assemblies/` | `FastTests/Mcp/Tools/AssemblyAnalysis/InspectAssemblyToolTests.cs` |
| `get_assembly_context` | `Mcp/Tools/AssemblyAnalysis/AssemblyAnalysisContextTool.cs`, `AssemblyAnalysisService.cs` | `Core/Assemblies/` + `Mcp/Tools/Assemblies/` | `FastTests/Mcp/Tools/AssemblyAnalysis/AssemblyAnalysisContextToolTests.cs` |
| `search_assembly` | `Mcp/Tools/AssemblyAnalysis/AssemblySearchTool.cs`, `AssemblySearchDeclarationFilter.cs` | `Core/Assemblies/` + `Mcp/Tools/Assemblies/` | `FastTests/Mcp/Tools/AssemblyAnalysis/AssemblySearchToolTests.cs` |
| `find_assembly_extensions` | `Mcp/Tools/AssemblyAnalysis/FindAssemblyExtensionsTool.cs` | `Core/Assemblies/` + `Mcp/Tools/Assemblies/` | `FastTests/Mcp/Tools/AssemblyAnalysis/FindAssemblyExtensionsToolTests.cs` |

### 2.7 Protokoll, Budgeting, Validierung & Host

| Feature / MCP-Tool | Quellcode in AiNetLinter | Ziel in AiNetCodeNavigator | Relevante Tests in AiNetLinter |
|---|---|---|---|
| Tool-Result Building & Error Policy | `Mcp/McpToolResults.cs`, `McpToolResults.NavigationText.cs`, `IsErrorPolicy.md` | `AiNetCodeNavigator/Mcp/Formatting/` | `FastTests/Mcp/Results/McpToolResultsContentTests.cs` |
| Budgeting & Truncation | `Mcp/McpTruncation.cs`, `SharpToken`-Nutzung | `AiNetCodeNavigator/Mcp/Formatting/` | `FastTests/Mcp/Results/McpTruncationTests.cs` |
| Argument- & Schema-Validierung | `Mcp/Registration/McpArgumentValidationFilter.cs` | `AiNetCodeNavigator/Mcp/` | `FastTests/Mcp/Registration/McpArgumentValidationFilterTests.cs` |
| Tool-Registrierungen | `Mcp/Registration/*ToolRegistrations.cs` (SymbolGraph, FileStructure, Assembly, Maintenance) | `AiNetCodeNavigator/Mcp/Tools/` | `IntegrationTests/Mcp/McpServerIntegrationTests.cs` (current: two maintenance + twenty navigation tools; the thirteen Cluster 9.2 handlers have real stdio fixture paths, while full reference-contract parity remains open) |
| Maintenance: `get_server_health` | `Mcp/Tools/ServerMaintenance/*` | `AiNetCodeNavigator/Mcp/Tools/Maintenance/` | `FastTests/Mcp/Tools/ServerMaintenance/GetServerHealthToolTests.cs` |
| Stdio-Server Lifetime & Host | `Mcp/Daemon/ThinClientProxy.cs`, `Commands/McpServerCommand.cs` | `AiNetCodeNavigator/Program.cs`, `McpServerHost.cs` | `IntegrationTests/Mcp/McpServerLifetimeTests.cs` |
