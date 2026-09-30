# Cluster 7: Assembly-Dekompilierung & Binary-Navigation (Core)

[Zurück zum Konzept](../Konzept.md)

- [x] 7.1 Decompiler & virtueller Roslyn-Workspace:
  - [x] `ICSharpCode.Decompiler`-Adapter (`AssemblyDecompilationAdapter`)
  - [x] `AssemblyDecompilationCache`: On-the-Fly-Dekompilierung und Caching
  - [x] `AssemblyRoslynWorkspaceFactory`: Erzeugung eines virtuellen Roslyn-Workspaces aus Dekompilaten
  - [x] FastTests für Dekompilierung und virtuellen Workspace
  - [x] Review/Audit zu 7.1 durchführen; Findings ergänzen und umsetzen.
- [ ] 7.2 Assembly-Navigations-Backends:
  - [x] `inspect_assembly`: Öffentliche API und Typdefinitionen extrahieren
  - [ ] `get_assembly_context`: Zusammenfassung von Assemblies
  - [ ] `search_assembly`: Text-, Aufruf- und Datenzugriffssuche im Dekompilat
  - [ ] `find_assembly_extensions`: Auffinden von Extension Methods in Binaries
  - [ ] `resolve_type_origin`: DLL-Pfad und NuGet-Herkunft externer Typen ermitteln
  - [ ] FastTests für Assembly-Navigation
  - [ ] Review/Audit zu 7.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 7.3 Assembly-Folgeaufrufe und Lebenszyklus prüfen: `inspect_assembly`-Handoffs aus der formatierten und strukturierten Antwort müssen mit passender Assembly-Session bei Folge-Tools auflösbar sein; Cache-/Session-Wiederverwendung, geänderte DLL, abgelaufene Tokens, fehlende Referenzen und native Dateien testen.
  - [ ] Review/Audit zu 7.3 durchführen; Findings ergänzen und umsetzen.
