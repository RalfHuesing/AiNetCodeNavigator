# Cluster 5: Call Graph, Beziehungen & Hierarchien (Core)

[Zurück zum Konzept](../Konzept.md)

- [x] 5.1 Call-Tree-Builder (`get_call_tree`-Engine):
  - [x] `CallTreeBuilder`: Traversierung eingehender (`incoming`) und ausgehender (`outgoing`) Aufrufe via Roslyn-AST
  - [x] `CallGraphTextRenderer` (ASCII) & `CallTreeMermaidRenderer` (Mermaid-Diagramme)
  - [x] FastTests für Call-Trees
  - [x] Preserve non-BCL metadata callees with the default `IncludeBcl` setting and test both settings.
  - [x] Apply `TopN` to the combined incoming/outgoing expansion and cover `Both`.
  - [x] Report node-cap completeness accurately, including exact-cap and pending-work cases.
  - [ ] Preserve source-backed callees in framework-named namespaces with default `IncludeBcl`.
  - [ ] Review/Audit zu 5.1 durchführen; Findings ergänzen und umsetzen.
- [x] 5.2 Referenzen & Implementierungen (`find_references`, `find_implementations`-Engine):
  - [x] `FindReferencesResolver`: AST-Aufrufstellensuche über Solution-Grenzen
  - [x] Interface- und abstrakte Methoden-Implementierungssuche
  - [x] FastTests für Referenzen und Implementierungen
  - [ ] Review/Audit zu 5.2 durchführen; Findings ergänzen und umsetzen.
- [x] 5.3 Typ-Hierarchien (`get_type_hierarchy`-Engine):
  - [x] `TypeHierarchyScanner`: Basisklassen, Schnittstellen und abgeleitete Typen ermitteln
  - [x] `GetTypeHierarchyFormatter`: Formatierung als Baumstruktur
  - [x] FastTests für Typ-Hierarchien
  - [ ] Review/Audit zu 5.3 durchführen; Findings ergänzen und umsetzen.
- [x] 5.4 Transitive Impact-Analyse (`get_impact`-Engine):
  - [x] Ermittlung des transitiven Blast Radius bei Änderungen an Symbolen
  - [x] FastTests für Impact-Berechnung
  - [ ] Review/Audit zu 5.4 durchführen; Findings ergänzen und umsetzen.
- [x] 5.5 Projekt- & Namespace-Abhängigkeiten (`dependency_graph`-Engine):
  - [x] `DependencyGraphScanner`: Projektabhängigkeiten und Namespace-Referenzen
  - [x] FastTests für Dependency-Graphen
  - [ ] Review/Audit zu 5.5 durchführen; Findings ergänzen und umsetzen.
- [ ] 5.6 Beziehungen über ein gemeinsames Test-Szenario prüfen: Aufrufe, Referenzen, Overrides, Interface-Implementierungen und transitive Auswirkungen über mehrere Projekte hinweg mit identischer Semantik und stabilen Handoffs testen; Grenzen und Kürzungen der Ergebnisse ausgeben.
  - [ ] Review/Audit zu 5.6 durchführen; Findings ergänzen und umsetzen.
