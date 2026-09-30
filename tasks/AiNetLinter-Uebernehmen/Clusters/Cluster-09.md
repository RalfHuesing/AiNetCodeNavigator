# Cluster 9: MCP Server Host & Tool-Registrierungen (Ganz oben)

[Zurück zum Konzept](../Konzept.md)

- [ ] 9.1 Host-Runner & Lifecycle (`AiNetCodeNavigator`):
  - [x] CLI-Parameter und Host-Bootstrap mit Stdio-Transport (`ModelContextProtocol` SDK)
  - [x] Wartungstools: `get_server_health`, `reload_config`
  - [x] JSON-Konfiguration validieren und unterstützte Einstellungen atomar reloaden
  - [x] Health-Abfrage für globalen oder bereits residenten Zustand ohne Target-Load
  - [x] Prozessintegration: Initialize, Toolliste, Maintenance-Aufrufe, stderr/stdout und EOF
  - [ ] Review/Audit zu 9.1 durchführen; Findings ergänzen und umsetzen (audit 1/3: three P2 findings; two point audits remain).
  - [x] Audit 1 P2 remediation implemented: health returns a complete snapshot or a retryable minimum budget; a recovery envelope that cannot fit returns sanitized `InvalidParams`.
  - [x] Audit 1 P2 remediation implemented: reload preflights the complete acknowledgement under its serialized reload gate before publishing any changed setting.
  - [x] Audit 1 P2 remediation implemented: identical settings retain their version, matching the public idempotency hint.
  - [ ] Independent audit 2/3 of the three P2 remediations; implementation is complete, but acceptance and point closure remain pending.
- [ ] 9.2 Tool-Registrierungen:
  - [ ] Symbol-Tools: `find_symbol`, `get_symbol_body`
  - [ ] Struktur-Tools: `get_file_skeleton`, `get_class_structure`, `get_file_tree`, `get_namespace_tree`, `get_index_scope`
  - [ ] Beziehungs-Tools: `get_call_tree`, `find_references`, `get_type_hierarchy`, `find_implementations`, `get_impact`, `dependency_graph`, `resolve_type_origin`
  - [ ] Assembly-Tools: `get_assembly_context`, `inspect_assembly`, `search_assembly`, `find_assembly_extensions`
  - [ ] Kontext-Tools: `get_feature_context`, `get_test_context`
  - [ ] Review/Audit zu 9.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 9.3 Host- & Handshake-Integrationstests:
  - [ ] Test der Tool-Registrierungen, Argumentfilter und MCP-Handshakes
  - [ ] Review/Audit zu 9.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 9.4 Realen Host-Lebenszyklus verifizieren: Stdio-Handshake, parallele Anfragen, Cancellation, Neustart, Logging nur auf `stderr`/Datei und sauberes Herunterfahren; der bisherige Typ-Existenztest genügt dafür nicht.
  - [ ] Review/Audit zu 9.4 durchführen; Findings ergänzen und umsetzen.
