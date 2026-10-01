# Cluster 2: Workspace- & Resident-Solution-Engine

[Zurück zum Konzept](../Konzept.md)

- [x] 2.1 Target-Erkennung & Validierung (`AiNetCodeNavigator.Core.Workspace`):
  - [x] `AnalysisTarget` & `AnalysisTargetResolver`: Unterscheidung Source-Modus (`.sln`/`.slnx`) vs. Assembly-Modus (`.dll`/`.exe`)
  - [x] Pfadnormalisierung und Sicherheitsprüfung
  - [x] FastTests für Target-Resolver
  - [x] Handle file access and race failures during target fingerprinting as structured, recoverable resolution errors; cover a locked or vanished target.
  - [x] Review/Audit zu 2.1 durchführen; Findings ergänzen und umsetzen.
- [x] 2.2 Resident Solution Registry (`ProjectRegistry`):
  - [x] `ProjectDefinition` und `ProjectDefinitionLoader`
  - [x] Hintergrund-Laden via MSBuild-Locator (`operation=retry`-Verhalten)
  - [x] `ProjectLease` & Nebenläufigkeits-Schutz
  - [x] Staleness-Erkennung bei geänderten Quelldateien
  - [x] FastTests und IntegrationTests für residenten Solution-Lebenszyklus
  - [x] Refresh every Roslyn document that shares a changed on-disk source path, including linked files in multiple projects.
  - [x] Prevent in-flight creation from publishing a resident entry after registry disposal and verify lease/disposal concurrency.
  - [x] Review/Audit zu 2.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 2.3 Staleness und Ladefehler über echte Solutions absichern:
  - [x] Änderungen an vorhandenen Dateien, neu hinzugefügte/entfernte Dateien sowie geänderte Projekt- und Referenzstruktur in nachfolgenden Navigationsaufrufen korrekt abbilden; Snapshot-/Reload-Verhalten festlegen und testen.
  - [x] Lade- und MSBuild-Fehler mit Ursache und erneuter Versuchsmöglichkeit an den Aufrufer melden; Integrationstest mit realer `.slnx` statt nur in-memory-Workspace.
  - [x] Detect newly added C# files matched by project Compile globs outside the project directory and include them in the next snapshot.
  - [x] Detect structural changes from custom MSBuild imports, including changed project references, and reload the affected solution.
  - [x] Detect activation of a previously absent conditional MSBuild import when its file appears, then reload the project structure.
  - [x] Detect activation of a previously absent conditional import nested in an MSBuild `ImportGroup` or other supported import container.
  - [x] Review/Audit zu 2.3 durchführen; Findings ergänzen und umsetzen.
