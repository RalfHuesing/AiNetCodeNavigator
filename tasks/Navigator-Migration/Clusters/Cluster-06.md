# Cluster 6: Projekt-, Datei- & Scope-Struktur (Core)

[Zurück zum Konzept](../Konzept.md)

- [x] 6.1 Dateibaum-Scanner (`get_file_tree`-Engine):
  - [x] `SolutionFileWalker` & `GetFileTreeScanner`: Schneller Scan der Solution-Dateien
  - [x] `FileTreeFilter` und `summary`-Rendering
  - [x] FastTests für File-Trees
  - [x] Reject relative `RootDirectory` values before normalizing them; cover the failure contract in a FastTest.
  - [x] Confine `RelativeRoot` through every ancestor reparse point so a nested link cannot expose files outside `RootDirectory`; cover the case in a FastTest.
  - [x] Apply `MaxResults` consistently across returned file and directory entries, and base `maxResults` truncation on the active view; cover both cases in FastTests.
  - [x] Review/Audit zu 6.1 durchführen; Findings ergänzen und umsetzen.
- [x] 6.2 Namespace-Baum (`get_namespace_tree`-Engine):
  - [x] `NamespaceTreeScanner`: Deklarierte Namespaces hierarchisch strukturieren
  - [x] FastTests für Namespace-Trees
  - [x] Namespaces über Projekt-Dokumente zusammenführen; verschachtelte, file-scoped und partielle Deklarationen abdecken.
  - [x] Tiefe und Gesamtzahl der Namespace-Knoten begrenzen; strukturierte und formatierte Ergebnisse konsistent kürzen.
  - [x] Unbekannte Projekte als Fehler ausgeben und Cancellation an den Aufrufer weiterreichen.
  - [x] Sämtliche Produkttexte englisch halten und `TotalTypes` über Namespace-Tiefen- und Ergebnisgrenzen hinweg vollständig zählen.
  - [x] Emit English error, summary, truncation, and next-action text from the namespace scanner; cover the public output paths in FastTests.
  - [x] Define and implement `TotalTypes` consistently for depth-truncated namespace scans; test a source type below `MaxDepth` and align current-state documentation.
  - [x] Review/Audit zu 6.2 durchführen; Findings ergänzen und umsetzen.
- [x] 6.3 Index-Scope (`get_index_scope`-Engine):
  - [x] `IndexScopeScanner`: Status der C#-Indizierung und Dokumentenübersicht
  - [x] FastTests für Index-Scope
  - [x] Project-/Solution-Scope, vollständige Gesamtsummen und begrenzte Projekt-/Dateityplisten mit Truncation-Metadaten abdecken.
  - [x] Unbekannte Projekte als Fehler behandeln, Cancellation weiterreichen und den Dokumentbestand unverändert lassen.
  - [x] Current-State-Dokumentation zu Roslyn-Dokumentumfang, Grenzen und lokalen Navigator-Verträgen ergänzen.
  - [x] Report generated-document and test-document totals over the selected Roslyn scope, independent of presentation limits; cover them in FastTests and documentation.
  - [x] Share generated-document classification with symbol navigation and align test-document classification with navigation scope rules.
  - [x] Review/Audit zu 6.3 durchführen; Findings ergänzen und umsetzen.

## Cluster 6 integration

- [x] Keep namespace results within the C# coverage declared by index scope when a solution contains another Roslyn project language.
- [x] Align generated-source visibility across namespace discovery and default symbol navigation; document the opt-in behavior.
- [x] Normalize project-name filters consistently between namespace tree and index scope.
- [x] Keep `find_symbol` follow-up within the C# project coverage reported by index scope and namespace tree in mixed-language solutions.
- [x] Complete the independent Cluster 6 integration review after these findings are addressed.
