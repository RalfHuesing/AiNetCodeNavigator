# Cluster 6: Projekt-, Datei- & Scope-Struktur (Core)

[Zurück zum Konzept](../Konzept.md)

- [x] 6.1 Dateibaum-Scanner (`get_file_tree`-Engine):
  - [x] `SolutionFileWalker` & `GetFileTreeScanner`: Schneller Scan der Solution-Dateien
  - [x] `FileTreeFilter` und `summary`-Rendering
  - [x] FastTests für File-Trees
  - [ ] Review/Audit zu 6.1 durchführen; Findings ergänzen und umsetzen.
- [x] 6.2 Namespace-Baum (`get_namespace_tree`-Engine):
  - [x] `NamespaceTreeScanner`: Deklarierte Namespaces hierarchisch strukturieren
  - [x] FastTests für Namespace-Trees
  - [ ] Review/Audit zu 6.2 durchführen; Findings ergänzen und umsetzen.
- [x] 6.3 Index-Scope (`get_index_scope`-Engine):
  - [x] `IndexScopeScanner`: Status der C#-Indizierung und Dokumentenübersicht
  - [x] FastTests für Index-Scope
  - [ ] Review/Audit zu 6.3 durchführen; Findings ergänzen und umsetzen.
