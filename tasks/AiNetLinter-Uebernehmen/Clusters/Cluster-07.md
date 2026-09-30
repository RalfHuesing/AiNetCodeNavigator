# Cluster 7: Assembly-Dekompilierung & Binary-Navigation (Core)

[Zurück zum Konzept](../Konzept.md)

- [x] 7.1 Decompiler & virtueller Roslyn-Workspace:
  - [x] `ICSharpCode.Decompiler`-Adapter (`AssemblyDecompilationAdapter`)
  - [x] `AssemblyDecompilationCache`: On-the-Fly-Dekompilierung und Caching
  - [x] `AssemblyRoslynWorkspaceFactory`: Erzeugung eines virtuellen Roslyn-Workspaces aus Dekompilaten
  - [x] FastTests für Dekompilierung und virtuellen Workspace
  - [x] Review/Audit zu 7.1 durchführen; Findings ergänzen und umsetzen.
- [x] 7.2 Assembly-Navigations-Backends:
  - [x] `inspect_assembly`: Öffentliche API und Typdefinitionen extrahieren
  - [x] `get_assembly_context`: Zusammenfassung von Assemblies
  - [x] `search_assembly`: Text-, Aufruf- und Datenzugriffssuche im Dekompilat
  - [x] `find_assembly_extensions`: Auffinden von Extension Methods in Binaries
  - [x] `resolve_type_origin`: DLL-Pfad und NuGet-Herkunft externer Typen ermitteln
  - [x] FastTests für Assembly-Navigation
  - [x] Review/Audit zu 7.2 durchführen; Findings ergänzen und umsetzen.
- [x] 7.3 Assembly-Folgeaufrufe und Lebenszyklus prüfen: `inspect_assembly`-Handoffs aus der formatierten und strukturierten Antwort müssen mit passender Assembly-Session bei Folge-Tools auflösbar sein; Cache-/Session-Wiederverwendung, geänderte DLL, abgelaufene Tokens, fehlende Referenzen und native Dateien testen.
  - [x] Residente Session-Registry mit Snapshot-Leases, Wiederverwendung, Idle-Ablauf und Begrenzung auf 32 Targets.
  - [x] Handoff-Resolver mit Assembly-/Target-/Snapshot-Bindung und `get_symbol_body`-Consumer für Typen und Member.
  - [x] Folge-Tool-Werbung für Assembly-Handoffs auf `get_symbol_body` begrenzen; source-only Structure- und Relationship-Scanner nicht als Assembly-Follow-ups ausgeben.
  - [x] FastTests für Roundtrips, Cache-Session-Wiederverwendung, unbekannte/fremde/veraltete Handles, fehlende Referenzen und native Dateien.
  - [x] Reject a failed refresh of an already resident target instead of exposing the previous generation through a new inspect call.
  - [x] Refresh the reference set when dependencies change without changing the target DLL, and cover the resulting handoff/body behavior.
  - [x] Enforce the 32-target resident limit when all existing sessions have active accesses.
  - [ ] Review/Audit zu 7.3 durchführen; Findings ergänzen und umsetzen.
