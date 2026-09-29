AiNetCodeNavigator ist die Ablösug von AiNetLinter.

Ich will alle Features von AiNetLinter die für Code-Navigation zuständig sind in diesem Projekt haben.

siehe Landkarte [CodeMap-AiNetLinter.md](CodeMap-AiNetLinter.md)


Inklusive voller test abdeckung.

AiNetlinter ist erprobt und getestet - wir sollen sehr viel davon übernehmen.

wir übernehmen NICHT den kompletten Linter Part.

AiNetCodeNavigator ist ausschließlich ein MCP Server für agentische navigation in c# code.

bei unklarheiten immer in AiNetLinter nachschauen und nach AiNetCodeNavigator adaptieren

bei entscheidungsfragen -> blocken und nutzer fragen!


roadmap:

grundlagen schaffen:
1. [x] .agents\rules anpassen (habe ich aus anderem repo kopiert)
2. [x] solution/projekt skelette anlegen (projekte, core, tests, usw. was wir so brauchen)
3. [x] namespaces mit gitkeep und klassen hüllen
4. [x] pwsh scripte für build und die tests - diese müssen dann immer aufgerufen werden (in rules oder docs erankern), output nach temp\ (sieht in docs dann das der agent dort nachlesen soll, evtl. gibt das script das auch so aus). temp\ in gitignore
5. [x] ergänze hier alle weiteren punkte in sinnvollen clustern (siehe Landkarte [CodeMap-AiNetLinter.md](CodeMap-AiNetLinter.md))

---

## Detaillierte Umsetzungs-Roadmap (Von "unten" nach "oben")

### Cluster 1: Fundamentale Basis- & Test-Infrastruktur
- [x] 1.1 TestKit-Basisinfrastruktur aufbauen:
  - [x] `TestWorkspaceBuilder`: Dynamischer `AdhocWorkspace` für In-Memory-Projekte, SyntaxTrees und Compilations
  - [x] `SampleCodeFixtures`: Realistische C#-Codevorlagen (Klassen, Interfaces, Vererbung, Records, Extensions)
  - [x] Semantische Assertions & Result-Prüfhilfen in TestKit
- [x] 1.2 Logging-Setup in Host (`AiNetCodeNavigator.Logging`):
  - [x] Serilog-Konfiguration mit täglicher Rotation und Dateiausgabe unter Host-Pfad
  - [x] `stderr`-Fehlerkanal, striktes Verbot von Ausgaben auf `stdout`
- [x] 1.3 Caching-Infrastruktur (`AiNetCodeNavigator.Core.Caching`):
  - [x] `CompilationCacheManager`: In-Memory- und MTime-basierter Cache für SyntaxTrees & Compilations
  - [x] FastTests für Cache-Hit/Miss und Invalidierung

### Cluster 2: Workspace- & Resident-Solution-Engine
- [x] 2.1 Target-Erkennung & Validierung (`AiNetCodeNavigator.Core.Workspace`):
  - [x] `AnalysisTarget` & `AnalysisTargetResolver`: Unterscheidung Source-Modus (`.sln`/`.slnx`) vs. Assembly-Modus (`.dll`/`.exe`)
  - [x] Pfadnormalisierung und Sicherheitsprüfung
  - [x] FastTests für Target-Resolver
- [x] 2.2 Resident Solution Registry (`ProjectRegistry`):
  - [x] `ProjectDefinition` und `ProjectDefinitionLoader`
  - [x] Hintergrund-Laden via MSBuild-Locator (`operation=retry`-Verhalten)
  - [x] `ProjectLease` & Nebenläufigkeits-Schutz
  - [x] Staleness-Erkennung bei geänderten Quelldateien
  - [x] FastTests und IntegrationTests für residenten Solution-Lebenszyklus

### Cluster 3: Kompaktes Handoff- & Symbol-Identitätssystem
- [x] 3.1 Symbol-Identität (`AiNetCodeNavigator.Core.Symbols`):
  - [x] `AnalysisSymbolIdentity`: Normalisierung von `ISymbol` zu kanonischen Identifikatoren (Doc-Comment-ID, File/Line)
  - [x] FastTests für Symbol-Identitätsabbildung
- [x] 3.2 Handoff-Tokensystem (`AiNetCodeNavigator.Core.Models` / `Symbols`):
  - [x] `HandoffCounterAlphabet` & `HandoffCounterStore`: Kompakte ID-Generierung (`h:...`)
  - [x] `HandoffHandleRegistry` & `SymbolHandoffIdentifier`: Bidirektionale Zuordnung von Token zu Symbol/Speicherort
  - [x] FastTests für Handoff-Erzeugung, Token-Auflösung und Thread-Sicherheit

### Cluster 4: Semantische Symbol- & Code-Inspektions-Engine (Core)
- [ ] 4.1 Symbolsuche (`find_symbol`-Engine):
  - [ ] `FindSymbolScanner`: Filter nach Namen/Patterns, `SymbolKind`, `scopeType` (`production`, `tests`, `all`)
  - [ ] FastTests für Symbolsuche
- [ ] 4.2 Symbol-Body-Extraktion (`get_symbol_body`-Engine):
  - [ ] `SourceSymbolBodyResolver`: Syntax-Extraktion aus AST mit Paginierung (`startLine`, `maxBodyLines`)
  - [ ] Batch-Extraktion für mehrere Symbole in einem Aufruf
  - [ ] FastTests für Symbol-Body-Lesen
- [ ] 4.3 File-Skeletons (`get_file_skeleton`-Engine):
  - [ ] `SkeletonSyntaxWalker` & `SkeletonMapBuilder`: Syntax-Knoten ohne Methodenrümpfe erfassen
  - [ ] `SkeletonMarkdownRenderer`: Formatierte Markdown-Ausgabe mit Handoff-IDs
  - [ ] FastTests für File-Skeletons
- [ ] 4.4 Klassen-Struktur (`get_class_structure`-Engine):
  - [ ] `ClassStructureScanner`: Vollständige Member-Übersicht (Properties, Methoden, Konstruktoren, Sichtbarkeiten)
  - [ ] FastTests für Class-Structure
- [ ] 4.5 Test-Erkennung & Test-Kontext (`get_test_context`-Engine):
  - [ ] `TestDetector`: Erkennung von Testprojekten und Testframeworks (xUnit, NUnit, MSTest)
  - [ ] `TestRecommendationBuilder`: Verknüpfung von Produktionscode mit abdeckenden Tests
  - [ ] FastTests für Test-Kontext
- [ ] 4.6 Feature-Kontext (`get_feature_context`-Engine):
  - [ ] `FeatureContextScanner`: Bündelung von Symbol, Signatur, Aufrufern und Tests *(ohne Linter-Violations!)*
  - [ ] FastTests für Feature-Kontext

### Cluster 5: Call Graph, Beziehungen & Hierarchien (Core)
- [ ] 5.1 Call-Tree-Builder (`get_call_tree`-Engine):
  - [ ] `CallTreeBuilder`: Traversierung eingehender (`incoming`) und ausgehender (`outgoing`) Aufrufe via Roslyn-AST
  - [ ] `CallGraphTextRenderer` (ASCII) & `CallTreeMermaidRenderer` (Mermaid-Diagramme)
  - [ ] FastTests für Call-Trees
- [ ] 5.2 Referenzen & Implementierungen (`find_references`, `find_implementations`-Engine):
  - [ ] `FindReferencesResolver`: AST-Aufrufstellensuche über Solution-Grenzen
  - [ ] Interface- und abstrakte Methoden-Implementierungssuche
  - [ ] FastTests für Referenzen und Implementierungen
- [ ] 5.3 Typ-Hierarchien (`get_type_hierarchy`-Engine):
  - [ ] `TypeHierarchyScanner`: Basisklassen, Schnittstellen und abgeleitete Typen ermitteln
  - [ ] `GetTypeHierarchyFormatter`: Formatierung als Baumstruktur
  - [ ] FastTests für Typ-Hierarchien
- [ ] 5.4 Transitive Impact-Analyse (`get_impact`-Engine):
  - [ ] Ermittlung des transitiven Blast Radius bei Änderungen an Symbolen
  - [ ] FastTests für Impact-Berechnung
- [ ] 5.5 Projekt- & Namespace-Abhängigkeiten (`dependency_graph`-Engine):
  - [ ] `DependencyGraphScanner`: Projektabhängigkeiten und Namespace-Referenzen
  - [ ] FastTests für Dependency-Graphen

### Cluster 6: Projekt-, Datei- & Scope-Struktur (Core)
- [ ] 6.1 Dateibaum-Scanner (`get_file_tree`-Engine):
  - [ ] `SolutionFileWalker` & `GetFileTreeScanner`: Schneller Scan der Solution-Dateien
  - [ ] `FileTreeFilter` und `summary`-Rendering
  - [ ] FastTests für File-Trees
- [ ] 6.2 Namespace-Baum (`get_namespace_tree`-Engine):
  - [ ] `NamespaceTreeScanner`: Deklarierte Namespaces hierarchisch strukturieren
  - [ ] FastTests für Namespace-Trees
- [ ] 6.3 Index-Scope (`get_index_scope`-Engine):
  - [ ] `IndexScopeScanner`: Status der C#-Indizierung und Dokumentenübersicht
  - [ ] FastTests für Index-Scope

### Cluster 7: Assembly-Dekompilierung & Binary-Navigation (Core)
- [ ] 7.1 Decompiler & virtueller Roslyn-Workspace:
  - [ ] `ICSharpCode.Decompiler`-Adapter (`AssemblyDecompilationAdapter`)
  - [ ] `AssemblyDecompilationCache`: On-the-Fly-Dekompilierung und Caching
  - [ ] `AssemblyRoslynWorkspaceFactory`: Erzeugung eines virtuellen Roslyn-Workspaces aus Dekompilaten
  - [ ] FastTests für Dekompilierung und virtuellen Workspace
- [ ] 7.2 Assembly-Navigations-Backends:
  - [ ] `inspect_assembly`: Öffentliche API und Typdefinitionen extrahieren
  - [ ] `get_assembly_context`: Zusammenfassung von Assemblies
  - [ ] `search_assembly`: Text-, Aufruf- und Datenzugriffssuche im Dekompilat
  - [ ] `find_assembly_extensions`: Auffinden von Extension Methods in Binaries
  - [ ] `resolve_type_origin`: DLL-Pfad und NuGet-Herkunft externer Typen ermitteln
  - [ ] FastTests für Assembly-Navigation

### Cluster 8: MCP Protocol Layer, Budgeting & Response-Formatting
- [ ] 8.1 Budgeting & Truncation:
  - [ ] `SharpToken`-Integration für Token-Begrenzungen
  - [ ] `McpTruncation`: Präzises Abschneiden mit Fortsetzungshinweisen (`RESPONSE_BUDGET_TOO_SMALL`)
- [ ] 8.2 Standardisiertes Result-Building:
  - [ ] `McpToolResults`: Einheitliche Erzeugung von `CallToolResult`, `IsError`-Policy und Statusblöcken
- [ ] 8.3 Langläufer & Paginierung:
  - [ ] `LongRunningToolCallStore`: Polling- und Fortsetzungs-Tokens (`operationToken`, `continuationToken`)
- [ ] 8.4 Argument-Validierung:
  - [ ] `McpArgumentValidationFilter`: Schema- und Eingabevalidierung für alle Tools
  - [ ] FastTests für Budgeting, Formatting und Validierung

### Cluster 9: MCP Server Host & Tool-Registrierungen (Ganz oben)
- [ ] 9.1 Host-Runner & Lifecycle (`AiNetCodeNavigator`):
  - [ ] CLI-Parameter und Host-Bootstrap mit Stdio-Transport (`ModelContextProtocol` SDK)
  - [ ] Wartungstools: `get_server_health`, `reload_config`
- [ ] 9.2 Tool-Registrierungen:
  - [ ] Symbol-Tools: `find_symbol`, `get_symbol_body`
  - [ ] Struktur-Tools: `get_file_skeleton`, `get_class_structure`, `get_file_tree`, `get_namespace_tree`, `get_index_scope`
  - [ ] Beziehungs-Tools: `get_call_tree`, `find_references`, `get_type_hierarchy`, `find_implementations`, `get_impact`, `dependency_graph`, `resolve_type_origin`
  - [ ] Assembly-Tools: `get_assembly_context`, `inspect_assembly`, `search_assembly`, `find_assembly_extensions`
  - [ ] Kontext-Tools: `get_feature_context`, `get_test_context`
- [ ] 9.3 Host- & Handshake-Integrationstests:
  - [ ] Test der Tool-Registrierungen, Argumentfilter und MCP-Handshakes

### Cluster 10: End-to-End Verifikation, Dokumentation & Abnahme
- [ ] 10.1 E2E-Integrationstests:
  - [ ] Stdio-Kommunikation gegen echte Solution und echte Assemblies
  - [ ] Verifikation aller 22 Navigationstools
- [ ] 10.2 Dokumentation & Tool-Katalog:
  - [ ] `docs/tools/`: Vollständiger Tool-Katalog mit Schemas und Parametern
  - [ ] `docs/setup/`: Konfiguration für Claude Desktop, Cursor, Antigravity
- [ ] 10.3 Finale Abnahme:
  - [ ] `pwsh -File ./scripts/build.ps1` (0 Warnungen, 0 Fehler)
  - [ ] `pwsh -File ./scripts/test.ps1` (100% bestandene Tests)
