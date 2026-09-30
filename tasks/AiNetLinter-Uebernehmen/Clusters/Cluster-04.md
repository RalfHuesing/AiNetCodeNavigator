# Cluster 4: Semantische Symbol- & Code-Inspektions-Engine (Core)

[Zurück zum Konzept](../Konzept.md)

- [x] 4.1 Symbolsuche (`find_symbol`-Engine):
  - [x] `FindSymbolScanner`: Filter nach Namen/Patterns, `SymbolKind`, `scopeType` (`production`, `tests`, `all`), generated source standardmäßig ausschließen und gezielt einbeziehen
  - [x] FastTests für Symbolsuche (Name/Pattern, `SymbolKind` einschließlich Delegate/Record-Varianten, Produktions-/Testprojekt-Scope, generated source und Mehrprojektfall)
  - [x] Complete the public kind vocabulary (`delegate`, `record class`, `record struct`) and make plain `struct` exclude record structs; cover each kind and mismatch with FastTests.
  - [x] Apply the reference `includeGenerated` default and opt-in to source locations across all scopes; cover generated path/header and mixed-source declarations with FastTests.
  - [x] Review/Audit zu 4.1 durchführen; Findings ergänzen und umsetzen.
- [x] 4.2 Symbol-Body-Extraktion (`get_symbol_body`-Engine):
  - [x] `SourceSymbolBodyResolver`: Syntax-Extraktion aus AST mit Paginierung (`startLine`, `maxBodyLines`)
  - [x] Batch-Extraktion für mehrere Symbole in einem Aufruf
  - [x] FastTests für Symbol-Body-Lesen
  - [ ] Review/Audit zu 4.2 durchführen; Findings ergänzen und umsetzen.
- [x] 4.3 File-Skeletons (`get_file_skeleton`-Engine):
  - [x] `SkeletonSyntaxWalker` & `SkeletonMapBuilder`: Syntax-Knoten ohne Methodenrümpfe erfassen
  - [x] `SkeletonMarkdownRenderer`: Formatierte Markdown-Ausgabe mit Handoff-IDs
  - [x] FastTests für File-Skeletons
  - [ ] Review/Audit zu 4.3 durchführen; Findings ergänzen und umsetzen.
- [x] 4.4 Klassen-Struktur (`get_class_structure`-Engine):
  - [x] `ClassStructureScanner`: Vollständige Member-Übersicht (Properties, Methoden, Konstruktoren, Sichtbarkeiten)
  - [x] FastTests für Class-Structure
  - [ ] Review/Audit zu 4.4 durchführen; Findings ergänzen und umsetzen.
- [x] 4.5 Test-Erkennung & Test-Kontext (`get_test_context`-Engine):
  - [x] `TestDetector`: Erkennung von Testprojekten und Testframeworks (xUnit, NUnit, MSTest)
  - [x] `TestRecommendationBuilder`: Verknüpfung von Produktionscode mit abdeckenden Tests
  - [x] FastTests für Test-Kontext
  - [ ] Review/Audit zu 4.5 durchführen; Findings ergänzen und umsetzen.
- [x] 4.6 Feature-Kontext (`get_feature_context`-Engine):
  - [x] `FeatureContextScanner`: Bündelung von Symbol, Signatur, Aufrufern und Tests *(ohne Linter-Violations!)*
  - [x] FastTests für Feature-Kontext
  - [ ] Review/Audit zu 4.6 durchführen; Findings ergänzen und umsetzen.
- [ ] 4.7 Gemeinsame Symbolauflösung für Folge-Tools fertigstellen: eindeutige qualifizierte Namen, Doc-IDs, Positionen und `h:...`-IDs unterstützen; bei mehrdeutigen Kurznamen auswählbare Treffer statt eines zufälligen ersten Symbols liefern. Die Tool-Verträge und Fehlerfälle mit FastTests belegen.
  - [ ] Review/Audit zu 4.7 durchführen; Findings ergänzen und umsetzen.
- [ ] 4.8 Test-Kontext fachlich absichern: gleichnamige Testklassen in verschiedenen Projekten getrennt erhalten, xUnit/NUnit/MSTest korrekt klassifizieren und Empfehlungen als Heuristik ausweisen; Tests für Mehrprojektfälle und `TestMethodAttribute` ergänzen.
  - [ ] Review/Audit zu 4.8 durchführen; Findings ergänzen und umsetzen.
