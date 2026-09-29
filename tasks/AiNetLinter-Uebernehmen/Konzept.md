AiNetCodeNavigator ist die Ablösug von AiNetLinter.

Ich will alle Features von AiNetLinter die für Code-Navigation zuständig sind in diesem Projekt haben.

siehe Landkarte [CodeMap-AiNetLinter.md](CodeMap-AiNetLinter.md)

Review der bisherigen Umsetzung und Belege für ergänzte Aufgaben: [Review-2026-09-30.md](Review-2026-09-30.md).


Inklusive automatisierter Tests für jeden öffentlichen Tool-Vertrag und dessen wichtige Fehlerfälle; die konkrete Abnahme steht in Cluster 11.

AiNetlinter ist erprobt und getestet - wir sollen sehr viel davon übernehmen.

wir übernehmen NICHT den kompletten Linter Part.

AiNetCodeNavigator ist ausschließlich ein MCP Server für agentische navigation in c# code.

bei unklarheiten immer in AiNetLinter nachschauen und nach AiNetCodeNavigator adaptieren

Bei echten, nicht aus Referenz und Regeln lösbaren Produkt- oder Architekturentscheidungen nur die abhängige Arbeit blockieren und den Nutzer fragen; unabhängige Arbeit fortsetzen.

## Zielbild und Abnahme

AiNetCodeNavigator soll als eigenständiger, ausschließlich lesender MCP-Server die C#-Navigationswerkzeuge von AiNetLinter für Solutions und verwaltete Assemblies anbieten. Ein Agent muss von einem gefundenen Symbol über die ausgegebene `h:...`-ID zu Body, Struktur, Referenzen, Aufrufern, Implementierungen und Kontext navigieren können. Die ID darf bei gleichem Namen in verschiedenen Projekten nicht auf das falsche Symbol zeigen; ungültige oder veraltete IDs sollen eine verständliche, wiederherstellbare Fehlermeldung liefern.

Zur Abnahme gehören 20 Navigationswerkzeuge und zwei Wartungswerkzeuge (`get_server_health`, `reload_config`) über den realen MCP-Stdio-Transport, begrenzte und fortsetzbare Antworten, sichere Fehlerfälle sowie Tests für jeden öffentlichen Tool-Vertrag und dessen wichtige Fehlerfälle. Navigation verändert weder den analysierten Code noch den Benutzer-Workspace. Linting, Qualitätsmetriken, Diagnose- und Refactoring-Werkzeuge bleiben außerhalb des Produkts. Alle von diesem Repository verfassten Inhalte und alle Produkt-Ausgaben sollen auf Englisch sein; die Kommunikation mit dem Nutzer bleibt gemäß `.agents/rules/01-language-and-scope.mdc` auf Deutsch. Die `[x]`-Markierung eines Core-Bausteins belegt noch keinen funktionsfähigen MCP-Aufruf; die Ende-zu-Ende-Abnahme erfolgt in Cluster 11.

## Ausführungsauftrag an den Orchestrator

Wenn der Auftrag lautet, dieses Konzept umzusetzen, führe die Arbeit selbstständig bis zum bestmöglichen implementierten Stand aus. Das oberste Arbeitsziel ist, möglichst viel **funktionsfähigen, getesteten Produktcode** aus der Roadmap zu liefern. Bleibe nicht wegen eines lokal begrenzten Problems stehen: dokumentiere es und bearbeite alle davon unabhängigen Aufgaben weiter. Halte die Produktgrenzen und verbindlichen Repository-Regeln ein; hake keine Aufgabe allein aufgrund vorhandener Dateien oder grüner, aber unpassender Tests ab.

### Rollen und Reihenfolge

1. Du bist der Orchestrator. Lies vor Beginn die Repository-Regeln, dieses Konzept, die Code-Map und den aktuellen Git-Stand. Plane die Cluster 1 bis 11 in Roadmap-Reihenfolge. Bereits gesetzte `[x]` sind Vorarbeiten, keine Review-Freigabe. Beginne beim ersten Cluster; für einen Cluster ohne offene Implementierungspunkte folgt direkt der Cluster-Review. Vermeide erneute Implementierung bereits belegter Funktionen.
2. Beauftrage für Implementierungsarbeit je aktivem Cluster einen **Implementierer-Subagenten mit `gpt-6-luna` und Reasoning Effort `high`**. Gib ihm das konkrete Cluster, die offenen Punkte, relevante Findings aus früheren Reviews, Repository-Regeln und die Abnahmekriterien. Er soll AiNetLinter nur lesend als Referenz prüfen, passende Tests ergänzen, die offiziellen PowerShell-Gates ausführen, betroffene `docs/`-Seiten bei geänderter Implementierung aktualisieren und abgeschlossene, verifizierte Slices gemäß Git-Regeln committen. Er soll innerhalb des Clusters zuerst unabhängige, wertvolle Funktionen fertigstellen und lokale Probleme samt Belegen melden.
3. **Nach jedem Cluster** beauftrage einen unabhängigen **Review-Subagenten mit `gpt-6-sol` und Reasoning Effort `medium`**. Das gilt auch für bereits abgehakte Cluster. Der Reviewer prüft den aktuellen Code und die Tests gegen dieses Konzept, die AiNetLinter-Referenz und die öffentlichen Tool-Verträge. Er prüft besonders Ausführbarkeit, Fehlerfälle, Handoff-Folgeaufrufe, Testaussagekraft und Produktgrenzen. Er ändert keinen Code und liefert priorisierte Findings mit Datei/Zeile, Reproduktion oder begründeter Evidenz sowie einer konkreten Abnahmebedingung. Ein Testlauf ist nur dann als bestanden zu melden, wenn er tatsächlich ausgeführt wurde.
4. Übergib behebbare Review-Findings an den Implementierer. **Maximal drei Fixrunden je Cluster**: Eine Fixrunde ist ein Implementierungsdurchlauf für die priorisierten Findings, gefolgt von einem erneuten unabhängigen Review. Die erste Implementierung und der erste Review zählen noch nicht als Fixrunde. Nach einem Review ohne offene relevante Findings oder nach der dritten Fixrunde schließe die Cluster-Schleife und gehe weiter. Verlängere die Schleife nicht stillschweigend.
5. Arbeite die Cluster 1 bis 11 durch. Führe am Ende die vorgesehenen Gesamt-Gates und die End-to-End-Abnahme aus, soweit technisch möglich. Gib einen Abschlussbericht mit implementierten Funktionen, bestanden/nicht bestandenen Gates, verbliebenen Findings, Tech-Debt und nicht umgesetzten Roadmap-Punkten. Melde keinen vollständigen Produktabschluss, solange öffentliche MCP-Verträge oder erforderliche Gates offen sind.

### Umgang mit Findings und Blockern

- Halte pro Cluster den Implementierungsstand und jede Review-/Fixrunde unter `tasks/AiNetLinter-Uebernehmen/Reviews/Cluster-XX.md` fest: geprüfter Commit, Reviewer-Modell, Findings mit Priorität, zugehörige Fixes, ausgeführte Gates und verbleibende Risiken. Nutze `tasks/AiNetLinter-Uebernehmen/Findings.md` als clusterübergreifendes Register für offene Findings und Tech-Debt; verlinke auf die Detailstelle statt Befunde mehrfach auszuschreiben. Wenn Cluster 10 deutschsprachige Datei- oder Verzeichnisnamen ändert, aktualisiere diese Pfade und alle Verweise darauf.
- Ein **lokaler Blocker** betrifft eine einzelne Funktion oder einen klar isolierten Slice. Halte ihn mit Ursache, Auswirkung, versuchten Lösungswegen und nächstem Schritt fest, lasse dessen Checkbox offen und fahre mit unabhängiger Arbeit fort. Dasselbe gilt für nicht kritische Review-Findings nach ausgeschöpften Fixrunden.
- Ein **globaler Blocker** liegt nur vor, wenn ohne Nutzerentscheidung, fehlende externe Voraussetzung oder grundlegende technische Reparatur keine sinnvolle unabhängige Implementierung mehr möglich ist. Frage bei echten Produkt-/Architekturentscheidungen oder widersprüchlichen Quellen den Nutzer; blockiere bis zur Antwort nur die davon abhängigen Arbeiten. Nutze die Zeit für unabhängige Clusterpunkte.
- Behandle fehlgeschlagene Builds, relevante Tests, Protokollfehler, Schreibzugriffe auf analysierte Workspaces und unzulässige Linter-Funktionen nicht als erledigt. Trenne lokal betroffene Slices sauber ab. Committe Code nur nach den vorgeschriebenen Verifikations-Gates; dokumentiere unvollständige Arbeiten und bearbeite andere Slices weiter.
- Priorisiere zuerst fehlende nutzbare Navigationsfunktionen und ihre Integration, dann Defekte, die Folgeaufrufe oder mehrere Tools betreffen, danach eng begrenzte Qualitätsverbesserungen. Bei gleicher Priorität bevorzuge einen vollständigen vertikalen Pfad vom MCP-Aufruf bis zum getesteten Ergebnis gegenüber zusätzlicher interner Infrastruktur.


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
- [ ] 2.3 Staleness und Ladefehler über echte Solutions absichern:
  - [ ] Änderungen an vorhandenen Dateien, neu hinzugefügte/entfernte Dateien sowie geänderte Projekt- und Referenzstruktur in nachfolgenden Navigationsaufrufen korrekt abbilden; Snapshot-/Reload-Verhalten festlegen und testen.
  - [ ] Lade- und MSBuild-Fehler mit Ursache und erneuter Versuchsmöglichkeit an den MCP-Aufrufer melden; Integrationstest mit realer `.slnx` statt nur in-memory-Workspace.

### Cluster 3: Kompaktes Handoff- & Symbol-Identitätssystem
- [x] 3.1 Symbol-Identität (`AiNetCodeNavigator.Core.Symbols`):
  - [x] `AnalysisSymbolIdentity`: Normalisierung von `ISymbol` zu kanonischen Identifikatoren (Doc-Comment-ID, File/Line)
  - [x] FastTests für Symbol-Identitätsabbildung
- [x] 3.2 Handoff-Tokensystem (`AiNetCodeNavigator.Core.Models` / `Symbols`):
  - [x] `HandoffCounterAlphabet` & `HandoffCounterStore`: Kompakte ID-Generierung (`h:...`)
  - [x] `HandoffHandleRegistry` & `SymbolHandoffIdentifier`: Bidirektionale Zuordnung von Token zu Symbol/Speicherort
  - [x] FastTests für Handoff-Erzeugung, Token-Auflösung und Thread-Sicherheit
- [ ] 3.3 Handoff-Vertrag über Producer und Consumer schließen:
  - [ ] Alle ausgegebenen `h:...`-IDs aus Source- und Assembly-Tools auf dieselbe kanonische, ziel- und snapshotgebundene Identität zurückführen; rohe DocumentationCommentIds nicht als scheinbar gültige Handoffs ausgeben.
  - [ ] Roundtrip-Tests von `find_symbol`, `get_file_skeleton` und `inspect_assembly` zu den jeweils erlaubten Folge-Tools ergänzen; unbekannte, fremde und nach Änderung veraltete Handles als typisierte Fehler behandeln.

### Cluster 4: Semantische Symbol- & Code-Inspektions-Engine (Core)
- [x] 4.1 Symbolsuche (`find_symbol`-Engine):
  - [x] `FindSymbolScanner`: Filter nach Namen/Patterns, `SymbolKind`, `scopeType` (`production`, `tests`, `all`)
  - [x] FastTests für Symbolsuche
- [x] 4.2 Symbol-Body-Extraktion (`get_symbol_body`-Engine):
  - [x] `SourceSymbolBodyResolver`: Syntax-Extraktion aus AST mit Paginierung (`startLine`, `maxBodyLines`)
  - [x] Batch-Extraktion für mehrere Symbole in einem Aufruf
  - [x] FastTests für Symbol-Body-Lesen
- [x] 4.3 File-Skeletons (`get_file_skeleton`-Engine):
  - [x] `SkeletonSyntaxWalker` & `SkeletonMapBuilder`: Syntax-Knoten ohne Methodenrümpfe erfassen
  - [x] `SkeletonMarkdownRenderer`: Formatierte Markdown-Ausgabe mit Handoff-IDs
  - [x] FastTests für File-Skeletons
- [x] 4.4 Klassen-Struktur (`get_class_structure`-Engine):
  - [x] `ClassStructureScanner`: Vollständige Member-Übersicht (Properties, Methoden, Konstruktoren, Sichtbarkeiten)
  - [x] FastTests für Class-Structure
- [x] 4.5 Test-Erkennung & Test-Kontext (`get_test_context`-Engine):
  - [x] `TestDetector`: Erkennung von Testprojekten und Testframeworks (xUnit, NUnit, MSTest)
  - [x] `TestRecommendationBuilder`: Verknüpfung von Produktionscode mit abdeckenden Tests
  - [x] FastTests für Test-Kontext
- [x] 4.6 Feature-Kontext (`get_feature_context`-Engine):
  - [x] `FeatureContextScanner`: Bündelung von Symbol, Signatur, Aufrufern und Tests *(ohne Linter-Violations!)*
  - [x] FastTests für Feature-Kontext
- [ ] 4.7 Gemeinsame Symbolauflösung für Folge-Tools fertigstellen: eindeutige qualifizierte Namen, Doc-IDs, Positionen und `h:...`-IDs unterstützen; bei mehrdeutigen Kurznamen auswählbare Treffer statt eines zufälligen ersten Symbols liefern. Die Tool-Verträge und Fehlerfälle mit FastTests belegen.
- [ ] 4.8 Test-Kontext fachlich absichern: gleichnamige Testklassen in verschiedenen Projekten getrennt erhalten, xUnit/NUnit/MSTest korrekt klassifizieren und Empfehlungen als Heuristik ausweisen; Tests für Mehrprojektfälle und `TestMethodAttribute` ergänzen.

### Cluster 5: Call Graph, Beziehungen & Hierarchien (Core)
- [x] 5.1 Call-Tree-Builder (`get_call_tree`-Engine):
  - [x] `CallTreeBuilder`: Traversierung eingehender (`incoming`) und ausgehender (`outgoing`) Aufrufe via Roslyn-AST
  - [x] `CallGraphTextRenderer` (ASCII) & `CallTreeMermaidRenderer` (Mermaid-Diagramme)
  - [x] FastTests für Call-Trees
- [x] 5.2 Referenzen & Implementierungen (`find_references`, `find_implementations`-Engine):
  - [x] `FindReferencesResolver`: AST-Aufrufstellensuche über Solution-Grenzen
  - [x] Interface- und abstrakte Methoden-Implementierungssuche
  - [x] FastTests für Referenzen und Implementierungen
- [x] 5.3 Typ-Hierarchien (`get_type_hierarchy`-Engine):
  - [x] `TypeHierarchyScanner`: Basisklassen, Schnittstellen und abgeleitete Typen ermitteln
  - [x] `GetTypeHierarchyFormatter`: Formatierung als Baumstruktur
  - [x] FastTests für Typ-Hierarchien
- [x] 5.4 Transitive Impact-Analyse (`get_impact`-Engine):
  - [x] Ermittlung des transitiven Blast Radius bei Änderungen an Symbolen
  - [x] FastTests für Impact-Berechnung
- [x] 5.5 Projekt- & Namespace-Abhängigkeiten (`dependency_graph`-Engine):
  - [x] `DependencyGraphScanner`: Projektabhängigkeiten und Namespace-Referenzen
  - [x] FastTests für Dependency-Graphen
- [ ] 5.6 Beziehungen über ein gemeinsames Test-Szenario prüfen: Aufrufe, Referenzen, Overrides, Interface-Implementierungen und transitive Auswirkungen über mehrere Projekte hinweg mit identischer Semantik und stabilen Handoffs testen; Grenzen und Kürzungen der Ergebnisse ausgeben.

### Cluster 6: Projekt-, Datei- & Scope-Struktur (Core)
- [x] 6.1 Dateibaum-Scanner (`get_file_tree`-Engine):
  - [x] `SolutionFileWalker` & `GetFileTreeScanner`: Schneller Scan der Solution-Dateien
  - [x] `FileTreeFilter` und `summary`-Rendering
  - [x] FastTests für File-Trees
- [x] 6.2 Namespace-Baum (`get_namespace_tree`-Engine):
  - [x] `NamespaceTreeScanner`: Deklarierte Namespaces hierarchisch strukturieren
  - [x] FastTests für Namespace-Trees
- [x] 6.3 Index-Scope (`get_index_scope`-Engine):
  - [x] `IndexScopeScanner`: Status der C#-Indizierung und Dokumentenübersicht
  - [x] FastTests für Index-Scope

### Cluster 7: Assembly-Dekompilierung & Binary-Navigation (Core)
- [x] 7.1 Decompiler & virtueller Roslyn-Workspace:
  - [x] `ICSharpCode.Decompiler`-Adapter (`AssemblyDecompilationAdapter`)
  - [x] `AssemblyDecompilationCache`: On-the-Fly-Dekompilierung und Caching
  - [x] `AssemblyRoslynWorkspaceFactory`: Erzeugung eines virtuellen Roslyn-Workspaces aus Dekompilaten
  - [x] FastTests für Dekompilierung und virtuellen Workspace
- [ ] 7.2 Assembly-Navigations-Backends:
  - [x] `inspect_assembly`: Öffentliche API und Typdefinitionen extrahieren
  - [ ] `get_assembly_context`: Zusammenfassung von Assemblies
  - [ ] `search_assembly`: Text-, Aufruf- und Datenzugriffssuche im Dekompilat
  - [ ] `find_assembly_extensions`: Auffinden von Extension Methods in Binaries
  - [ ] `resolve_type_origin`: DLL-Pfad und NuGet-Herkunft externer Typen ermitteln
  - [ ] FastTests für Assembly-Navigation
- [ ] 7.3 Assembly-Folgeaufrufe und Lebenszyklus prüfen: `inspect_assembly`-Handoffs aus der formatierten und strukturierten Antwort müssen mit passender Assembly-Session bei Folge-Tools auflösbar sein; Cache-/Session-Wiederverwendung, geänderte DLL, abgelaufene Tokens, fehlende Referenzen und native Dateien testen.

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
- [ ] 8.5 Einheitlichen öffentlichen Fehler- und Fortsetzungsvertrag pro Tool testen: `IsError`, Retry bei noch ladendem Target, `RESPONSE_BUDGET_TOO_SMALL`, `minimumResponseBytes`, stabile Pagination und Eingabegrenzen dürfen weder partielle Erfolge vortäuschen noch Daten still verlieren.

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
- [ ] 9.4 Realen Host-Lebenszyklus verifizieren: Stdio-Handshake, parallele Anfragen, Cancellation, Neustart, Logging nur auf `stderr`/Datei und sauberes Herunterfahren; der bisherige Typ-Existenztest genügt dafür nicht.

### Cluster 10: Repository und Produkt-Ausgaben vollständig auf Englisch umstellen
- [ ] 10.1 Alle versionierten, selbst verfassten Inhalte inventarisieren: `rg --files` und beispielsweise `rg -n '[ÄÖÜäöüß]|\b(Fehler|Keine|Bitte|ungültig|gefunden|Zeilen|Vollständigkeit|Starte|Bestanden)\b'` für typische deutsche Wörter und Meldungen einsetzen; auch ASCII-only-Texte manuell prüfen. `src/`, `tests/`, `scripts/`, `docs/`, `tasks/`, `.agents/`, Konfigurationsdateien und README-/AGENTS-Dateien einbeziehen. Generierte, ignorierte oder externe Dateien nicht als zu bearbeitenden Repository-Inhalt zählen.
- [ ] 10.2 Sämtliche deutschsprachigen Kommentare, XML-Dokumentation, Fehlermeldungen, Hinweise, MCP-Tool-Antworten, CLI-/Log-Ausgaben, PowerShell-Skriptausgaben, Testnamen/-Assertions/-Fixtures, Regeln und Dokumentation ins Englische übertragen. Auch bestehende Aufgabenbeschreibungen einschließlich dieses Konzepts und der Review-Dateien übersetzen. `.agents/rules/01-language-and-scope.mdc` danach auf Englisch als verbindliche Repository-Sprache für künftige Inhalte festlegen. Die Kommunikation des Agenten mit dem Nutzer bleibt Deutsch.
- [ ] 10.3 Deutschsprachige selbst verfasste Bezeichner, Datei- und Verzeichnisnamen auf Englisch bringen und alle Verweise/Links nachziehen. Etablierte MCP-Toolnamen, JSON-Feldnamen, Fehlercodes, Handoff-Formate und andere Maschinenverträge nur bei fachlich nötiger, getesteter Vertragsänderung ändern; Übersetzungen dürfen Navigationssemantik und read-only-Verhalten nicht verschlechtern.
- [ ] 10.4 Betroffene Tests auf englische Ausgaben aktualisieren und reale MCP-Aufrufe für Erfolg, Fehler und Retry prüfen. Nach der Migration die offiziellen Build-, Fast-, Integrations- und Gesamttest-Skripte ausführen. Die gezielte `rg`-Suche wiederholen und jeden verbleibenden Treffer prüfen; für selbst verfasste deutsche Texte dürfen keine offenen Treffer bleiben.

### Cluster 11: End-to-End Verifikation, Dokumentation & Abnahme
- [ ] 11.1 E2E-Integrationstests:
  - [ ] Stdio-Kommunikation gegen echte Solution und echte Assemblies
  - [ ] Verifikation aller 20 Navigations- und zwei Wartungswerkzeuge
- [ ] 11.2 Dokumentation & Tool-Katalog:
  - [ ] `docs/tools/`: Vollständiger Tool-Katalog mit Schemas und Parametern
  - [ ] `docs/setup/`: Konfiguration für Claude Desktop, Cursor, Antigravity
- [ ] 11.3 Finale Abnahme:
  - [ ] `pwsh -File ./scripts/build.ps1` (0 Warnungen, 0 Fehler)
  - [ ] `pwsh -File ./scripts/test.ps1` (100% bestandene Tests)
- [ ] 11.4 Abnahmematrix aus AiNetLinter-Verhalten und Navigator-Vertrag erstellen: für jedes der 22 Werkzeuge mindestens Erfolg, relevante Filter, Handoff/Folgeaufruf, Pagination/Budget und Fehlerszenarien nachweisen; ausdrücklich ausgeschlossene Linter-, Metrik- und Schreibwerkzeuge dürfen nicht registriert sein.
