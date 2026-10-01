# Cluster 9: MCP Server Host & Tool-Registrierungen (Ganz oben)

[Zurück zum Konzept](../Konzept.md)

- [x] 9.1 Host-Runner & Lifecycle (`AiNetCodeNavigator`):
  - [x] CLI-Parameter und Host-Bootstrap mit Stdio-Transport (`ModelContextProtocol` SDK)
  - [x] Wartungstools: `get_server_health`, `reload_config`
  - [x] JSON-Konfiguration validieren und unterstützte Einstellungen atomar reloaden
  - [x] Health-Abfrage für globalen oder bereits residenten Zustand ohne Target-Load
  - [x] Prozessintegration: Initialize, Toolliste, Maintenance-Aufrufe, stderr/stdout und EOF
  - [x] Review/Audit zu 9.1 durchführen; Findings ergänzen und umsetzen (accepted in independent audit 2/3; all three P2 findings closed).
  - [x] Audit 1 P2 remediation implemented: health returns a complete snapshot or a retryable minimum budget; a recovery envelope that cannot fit returns sanitized `InvalidParams`.
  - [x] Audit 1 P2 remediation implemented: reload preflights the complete acknowledgement under its serialized reload gate before publishing any changed setting.
  - [x] Audit 1 P2 remediation implemented: identical settings retain their version, matching the public idempotency hint.
  - [x] Independent audit 2/3 accepted all three P2 remediations and closed point 9.1; no third audit is required without a new concrete finding.
- [ ] 9.2 Tool-Registrierungen:
  - [x] Initial symbol slice: `find_symbol`, `get_symbol_body` (source and assembly routing; assembly follow-up via `get_symbol_body`).
  - [x] Initial structure slice: `get_file_skeleton`, `get_class_structure`, `get_file_tree`, `get_namespace_tree`, `get_index_scope`.
  - [x] Register the remaining thirteen navigation handlers and exercise their source/assembly happy paths through real MCP stdio; full public contracts remain open below.
  - [x] Verified source and assembly ownership, namespace/class structure, dependency edge, and body/skeleton consumer slices with real stdio handoffs.
  - [x] Verified Root → B → C relationship traversal through depth 2, fixture depth-3 queries, and owner-bound relationship handoffs for references, call tree, impact, and assembly context.
  - [x] Verified raw managed-assembly symbol inputs (names, documentation IDs, positions, line-only) and selectable owner-bound ambiguity results through public consumers.
  - [x] Verified Git change-context hunk mapping, sibling declarations, staged/unstaged/untracked changes, deletion completeness, repository status, and invalid refs through real stdio fixtures.
  - [x] Verified sanitized one-token `InvalidParams` for schema-valid scope, direction, target-routing, and assembly detail errors; scope/detail also pass at 16 and 32 tokens.
  - [ ] Complete all twenty navigation contracts, including public budget/error recovery, assembly-reference consumer closure, and full parameter/filter parity.
  - [ ] Beziehungs-Tools: `get_call_tree`, `find_references`, `get_type_hierarchy`, `find_implementations`, `get_impact`, `dependency_graph`, `resolve_type_origin`
  - [ ] Assembly-Tools: `get_assembly_context`, `inspect_assembly`, `search_assembly`, `find_assembly_extensions`
  - [ ] Kontext-Tools: `get_feature_context`, `get_test_context`
  - [ ] Review/Audit zu 9.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 9.3 Host- & Handshake-Integrationstests:
  - [ ] Test der Tool-Registrierungen, Argumentfilter und MCP-Handshakes
  - [ ] Review/Audit zu 9.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 9.4 Realen Host-Lebenszyklus verifizieren: Stdio-Handshake, parallele Anfragen, Cancellation, Neustart, Logging nur auf `stderr`/Datei und sauberes Herunterfahren; der bisherige Typ-Existenztest genügt dafür nicht.
  - [ ] Review/Audit zu 9.4 durchführen; Findings ergänzen und umsetzen.

Point 9.1 is closed after [independent audit 2/3](../Reviews/Cluster-09.md#point-91-independent-audit-23--accepted) accepted fixed commit `3cd6919e9c2c8f2c543082cd93fb662f2e406ae5`. All twenty navigation tools are now registered beside the two maintenance tools. Several source/assembly, relationship, raw-input, and Git change-context contract slices have verified real-stdio evidence, but the full parameter/default/filter, cancellation, budget, and rendering matrix remains open. Point 9.2 and its independent audit remain open at 0/3. Point 8.5 remains 0/3 pending full per-tool acceptance, and later lifecycle/product acceptance remains separate work.
