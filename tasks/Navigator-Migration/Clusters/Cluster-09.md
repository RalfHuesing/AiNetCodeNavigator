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
  - [x] Removed Git change discovery, Git-only `get_impact` parameters, Git production handlers/processes, and Git-specific fixtures per the updated product scope; symbol `get_impact` remains.
  - [x] Verified the registered navigation purpose/enum descriptions, selected assembly zero-default equivalence, assembly `includeDiagnostics`, large line-safe find/body/context paging, and bounded call-tree recovery guidance.
  - [x] Verified sanitized one-token `InvalidParams` for schema-valid scope, direction, target-routing, and assembly detail errors; scope/detail also pass at 16 and 32 tokens.
  - [x] Added one concrete invalid-input result for every navigation tool, shared actual-host malformed/missing/unknown/type/range/semantic input cases, continuation-after-error proof, and a linked case matrix for success, paging/truncation applicability, and remaining budget limits.
  - [x] Remediated audit-1 P2: omitted/false assembly `get_impact` resolves raw method Doc-ID, qualified name, and emitted position through the selected target; omitted/false match opaque `h:` caller payloads and caller handles follow to the body. Real stdio regression was red before the production change.
  - [ ] Complete exact byte/token recovery minima, all supported per-tool field/default/cap/filter parity, and remaining listed public contract boundaries.
  - [x] Beziehungs-Tools: `get_call_tree`, `find_references`, `get_type_hierarchy`, `find_implementations`, `get_impact`, `dependency_graph`, `resolve_type_origin` (registration and connected source/assembly routes; remaining contract finding below).
  - [x] Assembly-Tools: `get_assembly_context`, `inspect_assembly`, `search_assembly`, `find_assembly_extensions` (registration and connected assembly routes).
  - [x] Kontext-Tools: `get_feature_context`, `get_test_context` (registered source-only contracts).
  - [x] Implemented audit-1 P2 remediation; independent audit confirmation remains pending.
  - [ ] Deferred nonblocking audit-1 P3: align assembly class structure's omitted/lines member ordering before `maxMembers`; retain as visible technical debt.
  - [ ] Review/Audit zu 9.2 durchführen; Findings ergänzen und umsetzen (independent audit 1/3 performed; P2 implementation remediation awaits independent confirmation; P3 remains deferred).
- [ ] 9.3 Host- & Handshake-Integrationstests:
  - [x] Actual stdio initialize and `tools/list` assert 20 navigation plus 2 maintenance names and selected wire-schema bindings; real-host malformed/unknown/missing/type/range/semantic calls return expected protocol or typed errors, and subsequent valid calls prove host survival.
  - [ ] Review/Audit zu 9.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 9.4 Realen Host-Lebenszyklus verifizieren: Stdio-Handshake, parallele Anfragen, Cancellation, Neustart, Logging nur auf `stderr`/Datei und sauberes Herunterfahren; der bisherige Typ-Existenztest genügt dafür nicht.
  - [x] Real stdio acceptance covers handshake, tool listing, ordinary calls, clean EOF, and protocol-only stdout. Component tests cover operation polling, cancellation, and disposal without a Git child.
  - [ ] Review/Audit zu 9.4 durchführen; Findings ergänzen und umsetzen.

Point 9.1 is closed after [independent audit 2/3](../Reviews/Cluster-09.md#point-91-independent-audit-23--accepted) accepted fixed commit `3cd6919e9c2c8f2c543082cd93fb662f2e406ae5`. All twenty navigation registrations and their connected handlers are independently checked at `2da5c6d3bd4d3c2e139c9d4bbe0048f1d67b1bfe`. [Point 9.2 audit 1/3](../Reviews/Cluster-09.md#point-92-independent-audit-13--registration-verified-contract-finding-open) originally found the default-false raw assembly Impact defect; its implementation remediation and red-before-fix regression are recorded above, pending independent confirmation. The three tool-category registration children are closed; they do not imply complete per-field or end-to-end acceptance. Point 8.5 remains open at 0/3 for its remaining exact per-tool budget/recovery matrix and independent audit. Points 9.3/9.4 implementation evidence is recorded above and in the [public host and tool contract matrix](../Reviews/public-contract-matrix.md), pending independent acceptance. The user ordered a full stop and authorized committing without a completed green full gate; see the [remaining-work and verification snapshot](../Findings.md#user-directed-stop--2026-10-01).
