# Cluster 1: Fundamentale Basis- & Test-Infrastruktur

[Zurück zum Konzept](../Konzept.md)

- [x] 1.1 TestKit-Basisinfrastruktur aufbauen:
  - [x] `TestWorkspaceBuilder`: Dynamischer `AdhocWorkspace` für In-Memory-Projekte, SyntaxTrees und Compilations
  - [x] `SampleCodeFixtures`: Realistische C#-Codevorlagen (Klassen, Interfaces, Vererbung, Records, Extensions)
  - [x] Semantische Assertions & Result-Prüfhilfen in TestKit
  - [x] Handoff-Assertion an das tatsächliche Counter-Alphabet angleichen und Grenzfälle testen.
  - [x] Exponierte `Solution` und `Workspace.CurrentSolution` konsistent halten; Projekte, Dokumente und Referenzen prüfen.
  - [x] Fehlerpfade der öffentlichen Builder-Eingaben einschließlich Fluent-API testen.
  - [x] Review/Audit zu 1.1 durchführen; Findings ergänzen und umsetzen.
- [x] 1.2 Logging-Setup in Host (`AiNetCodeNavigator.Logging`):
  - [x] Serilog-Konfiguration mit täglicher Rotation und Dateiausgabe unter Host-Pfad
  - [x] `stderr`-Fehlerkanal, striktes Verbot von Ausgaben auf `stdout`
  - [x] Review/Audit zu 1.2 durchführen; Findings ergänzen und umsetzen.
- [x] 1.3 Caching-Infrastruktur (`AiNetCodeNavigator.Core.Caching`):
  - [x] `CompilationCacheManager`: In-Memory- und MTime-basierter Cache für SyntaxTrees & Compilations
  - [x] FastTests für Cache-Hit/Miss und Invalidierung
  - [x] Gespeicherte Hashes dürfen nicht durch hashlose Lookups umgangen werden; beide Cache-Arten testen.
  - [x] Compilations bei jeder semantisch relevanten Eingabeänderung invalidieren oder mit vollständigem Fingerprint prüfen.
  - [x] Review/Audit zu 1.3 durchführen; Findings ergänzen und umsetzen.
