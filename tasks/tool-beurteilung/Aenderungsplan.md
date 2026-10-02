# Änderungsvorschlag für die Agentenoberfläche

Stand: 2026-10-02. **Brainstorming und Umsetzungsplan, noch kein implementierter Vertrag.** Grundlage: [ursprüngliche Beurteilung](Konzept.md), `docs/`, öffentliche Vertragsmatrix sowie lokale Implementierung und Tests; Ausgangs-HEAD `f406a94`. Vorhandene Testfälle wurden gelesen, für diese Dokumentationsaufgabe nicht ausgeführt. Die bereits fremd geänderte `AssemblyToolsContractTests.cs` ist kein neu bestätigter Testnachweis.

**Ausgewählte Umsetzungsentscheidung:** `get_server_health` und `reload_config` werden vollständig aus dem öffentlichen MCP-Vertrag entfernt. Es gibt keinen Administrationskatalog oder Ersatztool. Die einzige unterstützte Servereinstellung bleibt `minimumLogLevel` aus der JSON-Datei beim Start; Änderungen erfordern einen Prozessneustart. Diese Entscheidung gilt für die tatsächliche Umsetzung unabhängig von den breiteren, weiterhin vorgeschlagenen Arbeitspaketen unten.

## Meine Entscheidung

Die Beurteilung setzt bei Tool-Auswahl und nachvollziehbaren Ergebnissen richtig an. Ihre Zieloberfläche mit 16 Tools würde ich aber nicht übernehmen: **`get_impact` und `resolve_type_origin` bleiben eigenständige Tools.** Das breitere, noch nicht ausgewählte Redesign plante **18 Navigationstools**. Die aktuell ausgewählte Entfernung setzt vor diesem Redesign einen 19-Tool-Katalog ohne administrative Laufzeitwerkzeuge um. Zuerst Verlässlichkeit und Anschlussfähigkeit verbessern, danach Verträge vereinfachen. Keine neuen Linter-, Refactoring- oder Testausführungsfunktionen.

Wichtige Korrekturen gegenüber der Beurteilung:

- **Aktualisierung existiert bereits.** `ResidentSolution` aktualisiert Inhalte per Hash und lädt Strukturänderungen neu; Integrationstests behandeln auch externe Compile-Globs und Imports. Source-Handoffs sind snapshot- und projektgebunden; Projektmarker berücksichtigen Referenzen und verfügbaren Framework-Kontext. Das muss gezielt gehärtet und sichtbar gemacht werden, nicht neu erfunden werden.
- **`get_impact` hat einen anderen Auswahlvertrag als `get_call_tree`.** Es sammelt direkte/transitive Referenzstellen und Datei-/Projektsummen vor dem Anzeigelimit. Der Call-Graph begrenzt dagegen Nachbarn mit lokalem `topN`. Eine Zusammenführung könnte Änderungsstellen verstecken. Allerdings beweist auch Impact keinen vollständigen fachlichen Änderungsumfang.
- **Herkunft ist mehr als Namenssuche.** `resolve_type_origin` sucht im Source-Fall auch Metadatenreferenzen und liefert den exakten DLL-Pfad. Der allgemeine Source-Resolver ist deklarationsorientiert. Die Zusammenführung würde `find_symbol` zu einem zusätzlichen Such-/Auflösungs-Mischvertrag machen.
- **`partial` und Heuristikkennzeichnung sind bereits vorhanden.** Die Typstruktur enthält Member über mehrere Deklarationen; Testkontexte tragen `static-test-candidates-only`. Es fehlen vor allem bessere Belege pro Kandidat und bessere Suchwege.
- **Beide spezialisierten Assembly-Suchmodi sind Regex-Voreinstellungen.** `external_calls` erkennt eine Liste von HTTP-, Socket- und ähnlichen Namen, nicht allgemein Aufrufe fremder Assemblies. Auch diesen Modus streichen; deklarationsbezogene Suche dagegen behalten.

## Entscheidung für jedes heutige Tool

| Tool | Entscheidung | Konkrete Änderung bzw. Zuständigkeit |
|---|---|---|
| `find_symbol` | Vereinfachen | Namenssuche behalten; nur `namePatterns`, ein Snapshot für den gesamten Batch, Treffer vollständig nachladbar. |
| `get_symbol_body` | Vereinfachen | Batch behalten; ausschließlich `startLine` + `maxBodyLines`, Fortsetzung pro Symbol. |
| `get_file_skeleton` | Behalten | Dateiperspektive für mehrere Typen; Handoffs auch als Dateiselektion behalten. |
| `get_class_structure` | Vereinfachen | Typ-/Memberperspektive über alle `partial`-Teile; feste Deklarationsreihenfolge statt `sortBy`. |
| `get_index_scope` | Erweitern | Analysierte Projekte, Framework-Kontexte, Ausschlüsse und Snapshot sichtbar machen; größere Inventare nachladbar machen. |
| `get_file_tree` | Entfernen | Physische Dateisuche extern; Assembly-Einstieg über paginierte `inspect_assembly`-Typen und deren Handoffs zu Skeleton/Body. Internen virtuellen Dateibegriff erhalten. |
| `get_namespace_tree` | Behalten | Logischer Einstieg über Projekte/Namespaces; Handoffs und eindeutige Projektselektion erhalten. |
| `get_call_tree` | Vereinfachen | Ablaufverfolgung; kompakte Knoten-/Kantenliste mit Fundstellen und Belegart statt ASCII/Mermaid-Auswahl. |
| `find_references` | Vereinfachen | Ausschließlich direkte Verwendungen; `depth` entfernen. Änderungsstellen vollständig nachladbar machen. |
| `get_type_hierarchy` | Behalten | Basistypen, Interfaces und Subtypen; Typbeziehungen verständlich unterscheiden. |
| `find_implementations` | Behalten | Interface-Member und Overrides zentral behalten; vorhandene Typabfragen als bequemen Einstieg ebenfalls erhalten. |
| `get_impact` | Behalten/härten | Transitive Verwendungsstellen und aggregierter Überblick; kein lokales `topN`, Zählwerte ausdrücklich als Fundstellen benennen. |
| `dependency_graph` | Behalten | Typ-/Dateiabhängigkeiten zusätzlich zu Aufrufen; keine Qualitätsbewertung. |
| `resolve_type_origin` | Erweitern | Herkunftsauflösung behalten und sicheren Übergang zur exakt aufgelösten externen API anbieten. |
| `get_feature_context` | Vereinfachen | Fester Einstieg aus Deklaration, direkten Aufrufern und Testkandidaten; Abschnitte mit eigenem Limit und Status. |
| `get_test_context` | Erweitern | Semantisch belegte statische Beziehungen zusätzlich zur Namensheuristik suchen. |
| `get_assembly_context` | Zusammenführen | Identität/Referenzübersicht zu `inspect_assembly`; Body, Member und Beziehungen über Einzeltools. Freie Kombinationsschalter entfallen. |
| `inspect_assembly` | Vereinfachen/erweitern | Ein Bibliotheksprofil plus paginierte API; nur `memberNames`, keine `detailLevel`-Varianten. |
| `search_assembly` | Vereinfachen | Text/ausdrückliche Regex und Deklarationsfilter behalten; `searchKind`, `data_access`, `external_calls` und Regex-Automatik entfernen. |
| `find_assembly_extensions` | Erweitern/umbenennen | Ziel `find_extensions`: Source-Projekt oder Assembly, tatsächliche Receiver-Verträglichkeit, Herkunft und Body-Handoff. |
| `get_server_health` | Vollständig entfernen | Kein öffentliches Health-/Admin-Tool und kein Ersatzkatalog. |
| `reload_config` | Vollständig entfernen | Logging-Level weiter beim Prozessstart aus JSON lesen; Änderungen erfordern Neustart. |

Die Zieloberfläche ergibt sich vollständig aus dieser Tabelle: vier heutige Namen entfallen aus dem Standardkatalog, die Extension-Suche wird umbenannt. Dateiskelett und Typstruktur bleiben, weil Datei- und Typgrenzen insbesondere bei `partial` verschieden sind. Keine zusätzliche Universalabfrage.

## Konkrete Umsetzung in dieser Reihenfolge

1. **P0 – Einen geprüften Snapshot pro neuer Analyse verwenden.** In `ResidentSolution` verschluckte `IOException` beim Lesen bekannter Dateien in einen strukturierten, wiederholbaren Fehler überführen; Hash und gelesener Text müssen denselben Inhalt repräsentieren. In `SymbolTools.FindSymbol` die Source-Lease und den Snapshot außerhalb der Pattern-Schleife halten; Assembly-Batches ebenfalls an einen festen Owner-/Referenzstand binden. Snapshot einmal ermitteln und wiederverwenden. **Abnahme:** Gesperrte/wechselnde Datei liefert keine scheinbar aktuelle Vollantwort; ein Edit zwischen Batch-Teilabfragen erzeugt keine gemischten Handoffs. Vor Fehlerbehebung deterministische Regression herstellen.

2. **P0 – Unklare Beziehungen ehrlich ausgeben.** `CallTreeBuilder` darf nicht `CandidateSymbols[0]` bzw. den ersten Member-Group-Eintrag als gesichertes Ziel wählen. Unaufgelöste Stellen und mögliche Ziele markieren; Kanten unterscheiden Aufruf, Memberzugriff und statisch gebundenes virtuelles/interfacebasiertes Ziel. Fundstellen um Spalten ergänzen. Graphen gelten nur für den ausgewählten Scope und die angefragte Tiefe; DI/Reflection/dynamische Dispatch-Ziele werden nicht behauptet. **Abnahme:** Mehrdeutige Überladung erscheint nicht als exakter Call; zwei Aufrufe auf einer Zeile bleiben unterscheidbar. Keine Compilerdiagnostik- oder Auditfunktion ergänzen.

3. **P0 – Analyseumfang und Ergebnisfortsetzung vereinheitlichen.** Gemeinsame Metadaten: `snapshotId`, tatsächlich analysierter Scope, Vollständigkeit und konkrete Auslassungsgründe; in Verbünden pro Abschnitt. `get_index_scope` zeigt stabile Projektidentität und tatsächlich geladene Framework-Kontexte; keine Behauptung, alle Target Frameworks seien analysiert. Für `find_references`, `find_symbol`, Struktur-/Inventarlisten und Impact-Fundstellen snapshotgebundene Ergebniscursor ergänzen. Graph-/Traversierungslimits bleiben separate Analysegrenzen mit konkretem Eingrenzungsweg. **Abnahme:** Auch jenseits heutiger Anzeigecaps sind alle ermittelten Listeneinträge ohne Duplikate erreichbar; leeres, begrenztes und fehlgeschlagenes Ergebnis unterscheiden sich.

   Bestehendes Byte-/Token-Paging beibehalten: Es kann nur bereits erzeugten Antworttext nachladen, keine zuvor durch `maxResults` verworfenen Einträge. Domänencursor ausdrücklich als `resultCursor` von `continuationToken` für Textseiten trennen. Textseiten dürfen ihren alten unveränderlichen Snapshot weiterliefern, müssen ihn aber sichtbar nennen; neue Analyse mit altem Domänencursor oder Handoff meldet strukturiert einen veralteten Stand. TTL, Replay, Query-Bindung und Budget-Recovery erhalten. Kein neues Refresh-Tool.

4. **P1 – Testsuche auf Beziehungen erweitern.** `TestRecommendationBuilder` zusätzlich von direkten Symbolverwendungen zu erkannten Testmethoden führen; für Typziele relevante Member einbeziehen. Danach begrenzte Wege über Test-Helfer ergänzen, unter Wiederverwendung vorhandener Beziehungsscanner. Pro Kandidat Grund und Fundstelle/Belegpfad liefern: Namensähnlichkeit, direkte Verwendung oder indirekter statischer Pfad. `get_feature_context.scopeType` nur auf Caller anwenden; Tests unabhängig im Test-Scope suchen. `get_test_context.scopeType` entfällt, das Ausgangssymbol darf aus Production stammen. **Abnahme:** Anders benannter Test mit direkter Verwendung wird gefunden; namensähnliche, unverbundene Fixtures bleiben klar heuristisch. Keine Ausführungs-/Coverage-Zusage.

5. **P1 – Source und externe Bibliotheken verbinden.** `resolve_type_origin` soll zur bewiesenen Referenzidentität `ownerTargetPath` und einen Symbol-Handoff oder einen ausdrücklichen Nichtverfügbarkeitsgrund liefern. Die allgemeinen Body-/Strukturtools konsumieren das über ihre bestehende Assembly-Route. Projektkontext über den Source-Handoff bestimmen; bei mehrdeutigen Roh-Namen auswählbare Projekt-/Framework-Kandidaten zurückgeben. **Abnahme:** Zwei Projekte mit unterschiedlichen Paketständen führen jeweils zur richtigen API; ausgetauschte DLLs machen alte Handles ungültig. Keine Suche nach einer beliebigen gleichnamigen DLL.

6. **P1 – Extensions semantisch auffindbar machen.** `FindAssemblyExtensionsScanner` in einen gemeinsamen Source-/Assembly-Scanner für `find_extensions` überführen. Im Source-Fall den aufgelösten Referenzsatz des gewählten Projektkontexts und Source-Extensions verwenden. Stringvergleich durch Receiver-Auflösung und Roslyn-Verträglichkeits-/Generics-Prüfung ersetzen; ungeklärte Constraints als Kandidatengrenze ausgeben. Sichtbarkeit, nötigen Namespace-Import, Signatur, Herkunft und navigierbaren Handoff liefern; ohne Aufrufposition keine vollständige Auflösbarkeit behaupten. **Abnahme:** `List<T>` findet passende `IEnumerable<T>`-Extensions aus anderer DLL, inkompatible Constraints werden nicht als verwendbar ausgegeben, Body-Follow-up trifft denselben Paketstand.

7. **P2 – Standardkatalog und Parameter umbauen.** Erst die Ersatzwege vervollständigen, dann File-Tree und Assembly-Verbund aus der Registrierung entfernen und die Tabellenentscheidungen umsetzen. `get_server_health` und `reload_config` bleiben vollständig entfernt; keine Host-Option, kein Administrationskatalog und kein Ersatztool. `detailLevel` überall entfernen; feste Ergebnisform, explizite Budgets. Für nachladbare Listen positives `maxResults` als Seitengröße definieren; Weglassen nutzt Default, `0` entfällt als zweite Default-Schreibweise. Roh-Symbolnamen, Dokumentations-IDs, Positionsauflösung, Batching, Filter und Budgets behalten. `search_assembly.declarationOnly` bleibt, weil es Kommentar-/Body-Treffer ausschließt und auch Feld-/Event-/Enum-Deklarationen erschließt. **Abnahme für die hier ausgewählte Tool-Entfernung:** 19 registrierte Navigationstools und kein öffentliches Maintenance-Tool; `inspect_assembly` → Handoff → Skeleton/Body funktioniert ohne File-Tree.

8. **P2 – Überlappende Implementierungen bereinigen und Verträge abnehmen.** Gemeinsame direkte Referenzsammlung samt Owner-, Fundstellen- und Filtersemantik für Referenzen/Impact nutzen; die öffentlichen Arbeitsabläufe bleiben getrennt. Impact-Summen als direkte/transitive Verwendungsstellen ausweisen und Owner-Identität auch bei gleichnamigen Projekten erhalten. Toolbeschreibungen nennen jeweils den Anlass und sinnvolle Folgeoperationen. Entfernte Verträge in Schema, Validator, Registrierungen, Tests, Doku und Agentenregeln gemeinsam bereinigen; keine dauerhaften öffentlichen Alias-Tools. Breaking Changes als zusammenhängende versionierte Umstellung dokumentieren.

## Nachweise und Prüfung

Die wesentlichen Befunde sind lokal nachvollziehbar:

- [Snapshot-Aktualisierung und Lesefehler](../../src/AiNetCodeNavigator.Core/Workspace/ResidentSolution.cs), [Strukturtests](../../tests/AiNetCodeNavigator.IntegrationTests/Workspace/WorkspaceLoadingIntegrationTests.cs), [Snapshot-/Projektidentität](../../src/AiNetCodeNavigator.Core/Symbols/AnalysisSymbolIdentity.cs).
- [Batch-Symbolsuche](../../src/AiNetCodeNavigator/Mcp/Tools/Symbols/SymbolTools.cs), [Call-Graph und Kandidatenauswahl](../../src/AiNetCodeNavigator.Core/CallTree/CallTreeBuilder.cs), [Impact-Sammlung](../../src/AiNetCodeNavigator.Core/Symbols/ImpactAnalyzer.cs).
- [Testheuristik](../../src/AiNetCodeNavigator.Core/Symbols/TestRecommendationBuilder.cs), [Extension-Namensvergleich](../../src/AiNetCodeNavigator.Core/Assemblies/FindAssemblyExtensionsScanner.cs), [Assembly-Regex-Voreinstellungen](../../src/AiNetCodeNavigator.Core/Assemblies/AssemblySearchScanner.cs).
- [Herkunftsvertrag](../../docs/navigation/resolve-type-origin.md), [vorhandene Typstruktur](../../docs/navigation/get-class-structure.md), [Paging-Vertrag](../../docs/mcp-long-running-calls.md).

Jeden Umsetzungsschritt mit den offiziellen Build-/Testskripten und der engsten betroffenen Auswahl prüfen; relevante Extended-Beziehungstests gezielt auswählen. Abschließend Routine-Solution-Gate und Vertragsprüfung durchführen. Die sechs Aufgaben aus dem ursprünglichen Prompt als Ablaufchecks verwenden: Fehlerverfolgung, Feature-Erweiterung, Signaturänderung, Testauswahl, NuGet-/Extension-Erkundung und Weiterarbeit nach Edits. Dabei Korrektheit, Nachladbarkeit, zusätzliche Abrufe und erhaltenen Kontext festhalten; hier werden keine Laufzeit- oder Tokengewinne behauptet. `docs/` jeweils erst mit der tatsächlich implementierten Änderung aktualisieren.

## Historical implementation record: physical file-tree removal (2026-10-02)

The user's implementation request selects only the removal of the physical file-tree tool from this broader proposal. It retires the public route, SDK schema, handler, Core scanner, exclusive models/filter, scanner tests, current contract rows, documentation, and agent guidance. The remaining catalog has 19 navigation tools and two maintenance tools. Other proposed changes above remain proposals.

Assembly discovery continues through `inspect_assembly` type handoffs to `get_file_skeleton`, followed by member handoffs to `get_symbol_body`. Decompiled document identities and the shared materialized source-root infrastructure remain in use. Source inventory uses `get_index_scope`; physical file discovery uses external file-search tools. Historical migration/review records and the original decision rationale retain explicitly historical references rather than rewriting earlier evidence.

Verification and the independent `gpt-6.1-sol/medium` audit are recorded here after completion. Starting HEAD: `80ddeddf44e03e94d5958a68b82d3ec76958154c`; the initial working tree and index were clean.

Verification completed before the final routine gate:

- `pwsh -File ./scripts/build.ps1`: passed, exit 0, 0 warnings/errors after test corrections.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~IndexScopeScannerTests|FullyQualifiedName~NamespaceTreeScannerTests|FullyQualifiedName~FileSkeletonTests|FullyQualifiedName~SkeletonMapTests|FullyQualifiedName~AssemblyInspectScannerTests'`: 42 passed, 0 failed/skipped, exit 0.
- The targeted Source/Assembly/IndexScope selection passed 10 of 11 cases initially; the new assembly workflow case exposed test-only selector/format assumptions. The corrected `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyNavigationHandlersReturnOwnerResultsAcrossAllSixteenRoutes'` passed 1/1, 0 skipped, exit 0. The initial build also exposed two incomplete test edits, which were corrected before the successful build.
- No ExtendedIntegration case exercises the removed physical scanner or changed registration; the only extended tests cover unchanged MSBuild loader guards/isolation. E2E remains excluded by the official scripts. Retained E2E tests were updated and compiled, but were not executed.

Final routine gate: `pwsh -File ./scripts/test.ps1` passed on the working-tree implementation based on `80ddeddf44e03e94d5958a68b82d3ec76958154c`: FastTests 541/541 and IntegrationTests 29/29, 0 failed/skipped, exit 0. This includes the original SDK 21-name catalog assertion and the corrected inspection-type-handoff → skeleton-member-handoff → body workflow. `git diff --check` passed.

Independent final audit (`gpt-6.1-sol`, reasoning `medium`): one P3 finding, an unchanged active intention sentence in former task concept documentation still demanding 20 navigation tools. The documentation fix round changed it to 19 navigation plus two maintenance tools. The auditor then rechecked the corrected sentence against the exact catalog and reported no open findings. No production or test changes were needed after the green routine gate; the final documentation diff and local links were checked instead of repeating unchanged tests.

Audit coverage: production registrations and original SDK schemas/catalog; exclusive scanner/model/filter removal and orphan callers; retained decompiled document/source-root infrastructure; the exact Inspect→Skeleton→member-body test; source index boundaries; active tool counts, current contract rows, documentation, agent rules, and historical-record labeling. Historical rationale and negative catalog assertions are the only retained mentions of the retired tool. Final `git diff --check` passed; no push or history rewrite is part of this task.

## Selected implementation scope: maintenance-tool removal (2026-10-02)

The user selected complete removal of `get_server_health` and `reload_config`, with no administrative catalog or replacement. The public catalog is the 19 navigation tools listed in `docs/mcp-host.md`; logging configuration remains startup-only and a process restart is required after changing the JSON file. Historical assessment and prior implementation records above remain labeled as records of their earlier decisions and evidence.

Baseline HEAD: `53424948eac6d671d730ed3bb44f54ebcf6a7483`; the initial working tree and index were clean. Production registration, the SDK catalog, tests, current documentation, and agent guidance now use the 19-tool navigation surface. The maintenance handler, configuration reload/version/publication machinery, and health-only runtime state were deleted. Startup configuration and logging remain; navigation runtime construction no longer depends on configuration. Assembly lifecycle tests retain only an internal active-access count instead of the former health snapshot collector and DTO. Independent Core cache and resident-solution infrastructure remain outside the removed tools' implementation.

Verification completed on the final code:

- `pwsh -File ./scripts/build.ps1`: passed, exit 0, 0 warnings/errors. The first build exposed a missing import in the retained shutdown test; it was corrected before the successful build.
- `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~NavigatorHostConfigurationTests|FullyQualifiedName~LoggingSetupTests'`: 22 passed, 0 failed/skipped, exit 0.
- `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~IndexScopeContractTests|FullyQualifiedName~SourceToolsContractTests|FullyQualifiedName~AssemblyToolsContractTests|FullyQualifiedName~RelationshipToolsContractTests|FullyQualifiedName~SourceRelationshipToolsContractTests'`: 15 passed, 0 failed/skipped, exit 0.
- `pwsh -File ./scripts/test.ps1`: FastTests 553/553 and IntegrationTests 29/29 passed, 0 failed/skipped, exit 0. This includes startup validation/default/unavailable-file cases, the exact 19-name SDK catalog, negative assertions for the retired tools, and source/assembly navigation and disposal contracts.

The two extended cases, `MSBuildSolutionLoader_CustomTargetsAndScratchCleanupPreserveWorkspaceSnapshot` and `MSBuildSolutionLoader_ColdSolutionsWithSameNamedProjectsHaveIsolatedSnapshots`, exercise unchanged loader guards, scratch cleanup, and isolated snapshots; neither uses the changed runtime/configuration/assembly-observation paths. No additional extended selection is affected. Retained E2E tests were updated and compiled, but not executed; official gates exclude `E2EIntegration`.

Independent final review (`gpt-6.1-sol`, reasoning `medium`) covered the entire diff, production wiring, SDK discovery, exclusive-state removal, startup configuration, navigation/lifecycle contracts, retained E2E code, documentation, and historical-record boundaries. One P3 documentation finding concerned a dangling response-budget reference; the orchestrator replaced it with the explicit 512–65,536-byte range, and the reviewer confirmed the fix. No open findings remain. Only documentation corrections and this verification record followed the green final code gate. Final `git diff --check` passed. No push or history rewrite is part of this task.
