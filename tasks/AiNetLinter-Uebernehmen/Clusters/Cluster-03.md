# Cluster 3: Kompaktes Handoff- & Symbol-Identitätssystem

[Zurück zum Konzept](../Konzept.md)

[Implementierungsreview 3.1](../Reviews/Cluster-03.md)

- [x] 3.1 Symbol-Identität (`AiNetCodeNavigator.Core.Symbols`):
  - [x] `AnalysisSymbolIdentity`: Normalisierung von `ISymbol` zu kanonischen Identifikatoren (Doc-Comment-ID, File/Line)
  - [x] FastTests für Symbol-Identitätsabbildung
  - [x] Gleichpfadige Projekte mit verschiedenen Referenzen sicher disambiguieren oder mehrdeutige Handoffs unterdrücken.
  - [x] Bei Pfadvarianten-Tests echte Source- und Assembly-Handoffs vor dem Vergleich nachweisen.
  - [x] Review/Audit zu 3.1 durchführen; Findings ergänzen und umsetzen.
- [x] 3.2 Handoff-Tokensystem (`AiNetCodeNavigator.Core.Models` / `Symbols`):
  - [x] `HandoffCounterAlphabet` & `HandoffCounterStore`: Kompakte ID-Generierung (`h:...`)
  - [x] `HandoffHandleRegistry` & `SymbolHandoffIdentifier`: Bidirektionale Zuordnung von Token zu Symbol/Speicherort
  - [x] FastTests für Handoff-Erzeugung, Token-Auflösung und Thread-Sicherheit
  - [x] Neu ausgegebene Handles bei parallelem Zugriff sofort rückwärts auflösen können.
  - [x] Formatierung öffentlich konstruierter Identifier gegen ungültige Tokens und DocIDs absichern.
  - [x] Review/Audit zu 3.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 3.3 Handoff-Vertrag über Producer und Consumer schließen:
  - [x] Source-Produzenten binden `h:...` an kanonische Lösungssnapshots und stabile Projektmarker; rohe DocumentationCommentIds werden nicht als Handoffs ausgegeben.
  - [x] Roundtrips von `find_symbol` und `get_file_skeleton` zu Source-Folge-Tools sowie typisierte Fehler für unbekannte, fremde und veraltete Source-Handles ergänzen.
  - [x] Source-Snapshot-Pfade normalisieren und übergebene `find_symbol`-Identitäten gegen die aktuelle Lösung prüfen.
  - [x] Übergebene Feature-/Class-Structure-Identitäten validieren und Source-Roundtrips über Mehrprojekt- und Pfadvarianten abdecken.
  - [ ] Assembly-Folge-Tools und Roundtrips für `inspect_assembly` abschließen (Auflösung und Sitzungslebenszyklus siehe Cluster 7).
  - [x] Assembly-DTOs nur bei kanonischer Identität als Handoff mit Folge-Tools kennzeichnen.
  - [x] Review/Audit zu 3.3 durchführen; Findings ergänzen und umsetzen.
- [x] Cluster-Integration: Handoff-artige Eingaben in Feature Context und Class Structure konsistent als typisierte Fehler behandeln.
