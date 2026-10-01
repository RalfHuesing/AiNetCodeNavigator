# Navigator migration and acceptance

AiNetCodeNavigator is an autonomous MCP server for read-only C# code navigation. Its current-state documentation in [docs/](../../docs/README.md), the [local public contract matrix](Reviews/public-contract-matrix.md), and its code and tests are the binding product references. Task checklists record acceptance work and historical evidence; they do not override implemented contracts or establish completion by themselves.

The [local component map](CodeMap-Navigator.md) locates Navigator infrastructure, navigation engines, host integration, and test suites. Consult these local sources when implementing or reviewing behavior. Ask the user when a product decision is missing or local sources conflict.

Historical implementation review and additional acceptance work: [Review-2026-09-30.md](Review-2026-09-30.md).

Bei echten, nicht aus lokalen Verträgen und Regeln lösbaren Produkt- oder Architekturentscheidungen nur die abhängige Arbeit blockieren und den Nutzer fragen; unabhängige Arbeit fortsetzen.

## Zielbild und Abnahme

AiNetCodeNavigator soll als eigenständiger, ausschließlich lesender MCP-Server die lokal spezifizierten C#-Navigationswerkzeuge für Solutions und verwaltete Assemblies anbieten. Ein Agent muss von einem gefundenen Symbol über die ausgegebene `h:...`-ID zu Body, Struktur, Referenzen, Aufrufern, Implementierungen und Kontext navigieren können. Die ID darf bei gleichem Namen in verschiedenen Projekten nicht auf das falsche Symbol zeigen; ungültige oder veraltete IDs sollen eine verständliche, wiederherstellbare Fehlermeldung liefern.

Zur Abnahme gehören 20 Navigationswerkzeuge und zwei Wartungswerkzeuge (`get_server_health`, `reload_config`) über den realen MCP-Stdio-Transport, begrenzte und fortsetzbare Antworten, sichere Fehlerfälle sowie Tests für jeden öffentlichen Tool-Vertrag und dessen wichtige Fehlerfälle. Navigation verändert weder den analysierten Code noch den Benutzer-Workspace. Linting, Qualitätsmetriken, Diagnose- und Refactoring-Werkzeuge bleiben außerhalb des Produkts. Alle von diesem Repository verfassten Inhalte und alle Produkt-Ausgaben sollen auf Englisch sein; die Kommunikation mit dem Nutzer bleibt gemäß `.agents/rules/01-language-and-scope.mdc` auf Deutsch. Die `[x]`-Markierung eines Core-Bausteins belegt noch keinen funktionsfähigen MCP-Aufruf; die Ende-zu-Ende-Abnahme erfolgt in Cluster 11.

## Ausführungsauftrag an den Orchestrator

Wenn der Auftrag lautet, dieses Konzept umzusetzen, führe die Arbeit selbstständig bis zum bestmöglichen implementierten Stand aus. Das oberste Arbeitsziel ist, möglichst viel **funktionsfähigen, getesteten Produktcode** aus der Roadmap zu liefern. Bleibe nicht wegen eines lokal begrenzten Problems stehen: dokumentiere es und bearbeite alle davon unabhängigen Aufgaben weiter. Halte die Produktgrenzen und verbindlichen Repository-Regeln ein; hake keine Aufgabe allein aufgrund vorhandener Dateien oder grüner, aber unpassender Tests ab.

### Rollen und Reihenfolge

1. Du bist der Orchestrator mit **`gpt-6.1-sol`**. Lies vor Beginn die Repository-Regeln, dieses Konzept, die Code-Map, die unten verlinkten Clusterdateien und den aktuellen Git-Stand. Plane die Cluster 1 bis 11 und darin jeden nummerierten Punkt in Roadmap-Reihenfolge. Bereits gesetzte `[x]` sind Vorarbeiten, keine Audit-Freigabe. Prüfe auch diese Punkte; vermeide erneute Implementierung bereits belegter Funktionen.
2. Beauftrage für Implementierungsarbeit je aktivem Cluster einen **Implementierer-Subagenten mit `gpt-6-luna` und Reasoning Effort `high`**. Gib ihm den konkreten Punkt, relevante Findings aus früheren Reviews, Repository-Regeln und die Abnahmekriterien. Er soll die lokalen Navigator-Spezifikationen, Verträge, Implementierungen und Tests als Referenz prüfen, passende Tests ergänzen, die offiziellen PowerShell-Gates ausführen, betroffene `docs/`-Seiten bei geänderter Implementierung aktualisieren und abgeschlossene, verifizierte Slices gemäß Git-Regeln committen. Er soll innerhalb des Clusters zuerst unabhängige, wertvolle Funktionen fertigstellen und lokale Probleme samt Belegen melden.
3. **Nach jedem nummerierten Roadmap-Punkt**, auch nach bereits abgehakten Punkten, beauftrage einen unabhängigen **Review-/Audit-Subagenten mit `gpt-6.1-sol` und Reasoning Effort `medium`**. Der Auditor prüft Code und Tests gegen diesen Punkt, das Konzept, die lokalen Navigator-Spezifikationen und Tests und die öffentlichen Tool-Verträge. Er ändert keinen Code und liefert priorisierte Findings mit Datei/Zeile, Reproduktion oder begründeter Evidenz sowie konkreten Abnahmebedingungen. Er darf neue `[ ]`-Arbeit unter dem betroffenen Punkt ergänzen; jeder neue eigenständige nummerierte Punkt erhält ebenfalls eine Audit-Checkbox. Ein Testlauf ist nur dann als bestanden zu melden, wenn er tatsächlich ausgeführt wurde.
   - Der Auditor **kann eigene Recherche-Subagenten mit `gpt-6-luna` und Reasoning Effort `high` starten**, wenn mehrere klar abgegrenzte, unabhängige Such- oder Vergleichsaufgaben dadurch besser abgedeckt werden, etwa lokale Navigator-Verträge, relevante Tests oder betroffene Aufrufpfade. Er nutzt dafür nur freie Agent-Slots und startet keine Recherche-Subagenten für kleine oder aufeinander aufbauende Schritte. Diese Subagenten arbeiten ausschließlich lesend: keine Dateiänderungen, Builds, Tests, Restores, Commits oder weiteren Subagenten. Sie liefern Belege an den Auditor zurück. Der Auditor prüft und gewichtet ihre Ergebnisse selbst; ihre Arbeit ist kein zusätzliches Audit und erhöht nicht das Drei-Audit-Limit.
4. Übergib Audit-Findings dem Implementierer und lasse sie bearbeiten. Danach erfolgt bei Bedarf ein erneutes Audit. **Maximal drei Audits pro nummeriertem Punkt insgesamt**, einschließlich des ersten Audits; keine vierte Prüfung desselben Punkts durch Umbenennung oder Aufspaltung der Findings. Markiere die Audit-Checkbox erst als `[x]`, wenn das Audit durchgeführt und die Findings umgesetzt oder nach Ausschöpfung des Limits als offene Findings/Tech-Debt dokumentiert sind. Ein Audit von bereits abgehakten Punkten kann deren Status wieder öffnen.
5. **Nach jedem Cluster** beauftrage denselben Review-Modelltyp mit einer unabhängigen Integrationsprüfung der Punkte und ihrer Zusammenschaltung. Die Regel für optionale Recherche-Subagenten gilt auch hier. Der Cluster-Review fasst die Punkt-Audits zusammen und prüft Schnittstellen zwischen Punkten, ohne einen Punkt über sein Drei-Audit-Limit hinaus erneut zu auditieren. Neue clusterübergreifende Findings werden als `[ ]` ergänzt und implementiert. **Maximal drei Fixrunden je Cluster**: Eine Fixrunde ist ein Implementierungsdurchlauf für priorisierte Cluster-Findings, gefolgt von einem erneuten Integrationsreview. Die erste Implementierung und der erste Cluster-Review zählen noch nicht als Fixrunde. Nach einem Review ohne offene relevante Findings oder nach der dritten Fixrunde schließe die Cluster-Schleife und gehe weiter.
6. Arbeite die Cluster durch (Cluster 1 bis 9 und 11). Führe am Ende die vorgesehenen Gesamt-Gates und die End-to-End-Abnahme aus, soweit technisch möglich. Gib einen Abschlussbericht mit implementierten Funktionen, bestanden/nicht bestandenen Gates, verbliebenen Findings, Tech-Debt und nicht umgesetzten Roadmap-Punkten. Melde keinen vollständigen Produktabschluss, solange öffentliche MCP-Verträge oder erforderliche Gates offen sind.

### Nebenläufigkeit im gemeinsamen Workspace

- **Schreibende Subagenten laufen immer seriell, niemals parallel** — auch bei verschiedenen Dateien, Clustern oder Worktrees. Als schreibend gelten insbesondere Code-/Dokumentationsänderungen, Checklisten- und Review-Dateien, Formatierung, Paket-Restore, Build- und Testläufe samt generierten Artefakten und statischen Logs sowie Git-Staging und Commits. Ein Agent mit einem solchen Auftrag belegt die Schreibphase bis zum Ende seines Turns und seiner gestarteten Prozesse.
- Der Orchestrator vergibt die Schreibphase exklusiv. Er startet den nächsten schreibenden Subagenten erst, wenn der vorherige samt Build-/Testprozessen vollständig beendet ist. Eigene Schreibaktionen des Orchestrators dürfen ebenfalls nicht mit einem schreibenden Subagenten überlappen. Das gilt auch für einen Auditor, sobald er Checklisten/Review-Dateien ändert oder Tests ausführt.
- Rein lesende Subagenten dürfen gleichzeitig laufen. Für einen Audit des veränderlichen Repositorys warte dennoch auf das Ende der laufenden Schreibphase und prüfe einen festen Git-Stand. Lesende Recherchen an unveränderten Referenzen können parallel stattfinden; sie dürfen keine Build-/Testskripte oder andere indirekt schreibende Befehle starten.

### Umgang mit Findings und Blockern

- Halte pro Cluster den Implementierungsstand, die Audit-Zählung jedes Punkts und jede Cluster-Review-/Fixrunde unter `tasks/Navigator-Migration/Reviews/Cluster-XX.md` fest: geprüfter Commit, Reviewer-Modell, Findings mit Priorität, zugehörige Fixes, ausgeführte Gates und verbleibende Risiken. Nutze `tasks/Navigator-Migration/Findings.md` als clusterübergreifendes Register für offene Findings und Tech-Debt; verlinke auf die Detailstelle statt Befunde mehrfach auszuschreiben.
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
5. [x] ergänze hier alle weiteren punkte in sinnvollen clustern (siehe Landkarte [CodeMap-Navigator.md](CodeMap-Navigator.md))

---

## Detaillierte Umsetzungs-Roadmap (Von "unten" nach "oben")

Die verlinkten Clusterdateien sind die verbindlichen Checklisten für die nummerierten Clusterpunkte. Bearbeite sie in dieser Reihenfolge; deren Status und Audit-Punkte stehen ausschließlich dort.

1. [Cluster 1: Fundamentale Basis- & Test-Infrastruktur](Clusters/Cluster-01.md)
2. [Cluster 2: Workspace- & Resident-Solution-Engine](Clusters/Cluster-02.md)
3. [Cluster 3: Kompaktes Handoff- & Symbol-Identitätssystem](Clusters/Cluster-03.md)
4. [Cluster 4: Semantische Symbol- & Code-Inspektions-Engine (Core)](Clusters/Cluster-04.md)
5. [Cluster 5: Call Graph, Beziehungen & Hierarchien (Core)](Clusters/Cluster-05.md)
6. [Cluster 6: Projekt-, Datei- & Scope-Struktur (Core)](Clusters/Cluster-06.md)
7. [Cluster 7: Assembly-Dekompilierung & Binary-Navigation (Core)](Clusters/Cluster-07.md)
8. [Cluster 8: MCP Protocol Layer, Budgeting & Response-Formatting](Clusters/Cluster-08.md)
9. [Cluster 9: MCP Server Host & Tool-Registrierungen (Ganz oben)](Clusters/Cluster-09.md)
11. [Cluster 11: End-to-End Verifikation, Dokumentation & Abnahme](Clusters/Cluster-11.md)
