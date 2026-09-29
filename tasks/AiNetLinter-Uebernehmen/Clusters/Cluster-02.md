# Cluster 2: Workspace- & Resident-Solution-Engine

[Zurück zum Konzept](../Konzept.md)

- [x] 2.1 Target-Erkennung & Validierung (`AiNetCodeNavigator.Core.Workspace`):
  - [x] `AnalysisTarget` & `AnalysisTargetResolver`: Unterscheidung Source-Modus (`.sln`/`.slnx`) vs. Assembly-Modus (`.dll`/`.exe`)
  - [x] Pfadnormalisierung und Sicherheitsprüfung
  - [x] FastTests für Target-Resolver
  - [x] Handle file access and race failures during target fingerprinting as structured, recoverable resolution errors; cover a locked or vanished target.
  - [ ] Review/Audit zu 2.1 durchführen; Findings ergänzen und umsetzen.
- [x] 2.2 Resident Solution Registry (`ProjectRegistry`):
  - [x] `ProjectDefinition` und `ProjectDefinitionLoader`
  - [x] Hintergrund-Laden via MSBuild-Locator (`operation=retry`-Verhalten)
  - [x] `ProjectLease` & Nebenläufigkeits-Schutz
  - [x] Staleness-Erkennung bei geänderten Quelldateien
  - [x] FastTests und IntegrationTests für residenten Solution-Lebenszyklus
  - [ ] Review/Audit zu 2.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 2.3 Staleness und Ladefehler über echte Solutions absichern:
  - [ ] Änderungen an vorhandenen Dateien, neu hinzugefügte/entfernte Dateien sowie geänderte Projekt- und Referenzstruktur in nachfolgenden Navigationsaufrufen korrekt abbilden; Snapshot-/Reload-Verhalten festlegen und testen.
  - [ ] Lade- und MSBuild-Fehler mit Ursache und erneuter Versuchsmöglichkeit an den MCP-Aufrufer melden; Integrationstest mit realer `.slnx` statt nur in-memory-Workspace.
  - [ ] Review/Audit zu 2.3 durchführen; Findings ergänzen und umsetzen.
