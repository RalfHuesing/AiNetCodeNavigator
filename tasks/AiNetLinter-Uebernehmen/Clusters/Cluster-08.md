# Cluster 8: MCP Protocol Layer, Budgeting & Response-Formatting

[Zurück zum Konzept](../Konzept.md)

- [x] 8.1 Budgeting & Truncation:
  - [x] `SharpToken`-Integration für Token-Begrenzungen
  - [x] `McpTruncation`: Präzises Abschneiden mit Fortsetzungshinweisen (`RESPONSE_BUDGET_TOO_SMALL`)
  - [x] Review/Audit zu 8.1 durchführen; Findings ergänzen und umsetzen.
  - [x] Audit 1 P1: Unmögliche `minimumResponseBytes`-Retry-Werte oberhalb des öffentlichen Maximums behandeln.
  - [x] Audit 1 P2: `startOffset` auf vollständige Zeileneinheiten begrenzen oder verlustfrei kanonisieren.
  - [x] Audit 1 P2: Token-Budget-Vertrag für Fehlerantworten klären und durchsetzen/testen.
- [ ] 8.2 Standardisiertes Result-Building:
  - [x] `McpToolResults`: Einheitliche Erzeugung von `CallToolResult`, `IsError`-Policy und Statusblöcken
  - [ ] Review/Audit zu 8.2 durchführen; Findings ergänzen und umsetzen.
  - [ ] Audit 1 P2: Calculate success budget retries from the intended success projection rather than the error status prefix.
  - [ ] Audit 1 P2: Preserve required recovery and argument-correction fields when error context is shortened.
  - [ ] Audit 1 P2: Apply response budgets to loading/retry results, including their status and next action.
- [ ] 8.3 Langläufer & Paginierung:
  - [ ] `LongRunningToolCallStore`: Polling- und Fortsetzungs-Tokens (`operationToken`, `continuationToken`)
  - [ ] Review/Audit zu 8.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 8.4 Argument-Validierung:
  - [ ] `McpArgumentValidationFilter`: Schema- und Eingabevalidierung für alle Tools
  - [ ] FastTests für Budgeting, Formatting und Validierung
  - [ ] Review/Audit zu 8.4 durchführen; Findings ergänzen und umsetzen.
- [ ] 8.5 Einheitlichen öffentlichen Fehler- und Fortsetzungsvertrag pro Tool testen: `IsError`, Retry bei noch ladendem Target, `RESPONSE_BUDGET_TOO_SMALL`, `minimumResponseBytes`, stabile Pagination und Eingabegrenzen dürfen weder partielle Erfolge vortäuschen noch Daten still verlieren.
  - [ ] Review/Audit zu 8.5 durchführen; Findings ergänzen und umsetzen.
