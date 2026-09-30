# Cluster 3: Kompaktes Handoff- & Symbol-Identitätssystem

[Zurück zum Konzept](../Konzept.md)

[Implementierungsreview 3.1](../Reviews/Cluster-03.md)

- [x] 3.1 Symbol-Identität (`AiNetCodeNavigator.Core.Symbols`):
  - [x] `AnalysisSymbolIdentity`: Normalisierung von `ISymbol` zu kanonischen Identifikatoren (Doc-Comment-ID, File/Line)
  - [x] FastTests für Symbol-Identitätsabbildung
  - [ ] Review/Audit zu 3.1 durchführen; Findings ergänzen und umsetzen.
- [x] 3.2 Handoff-Tokensystem (`AiNetCodeNavigator.Core.Models` / `Symbols`):
  - [x] `HandoffCounterAlphabet` & `HandoffCounterStore`: Kompakte ID-Generierung (`h:...`)
  - [x] `HandoffHandleRegistry` & `SymbolHandoffIdentifier`: Bidirektionale Zuordnung von Token zu Symbol/Speicherort
  - [x] FastTests für Handoff-Erzeugung, Token-Auflösung und Thread-Sicherheit
  - [ ] Review/Audit zu 3.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 3.3 Handoff-Vertrag über Producer und Consumer schließen:
  - [ ] Alle ausgegebenen `h:...`-IDs aus Source- und Assembly-Tools auf dieselbe kanonische, ziel- und snapshotgebundene Identität zurückführen; rohe DocumentationCommentIds nicht als scheinbar gültige Handoffs ausgeben.
  - [ ] Roundtrip-Tests von `find_symbol`, `get_file_skeleton` und `inspect_assembly` zu den jeweils erlaubten Folge-Tools ergänzen; unbekannte, fremde und nach Änderung veraltete Handles als typisierte Fehler behandeln.
  - [ ] Review/Audit zu 3.3 durchführen; Findings ergänzen und umsetzen.
