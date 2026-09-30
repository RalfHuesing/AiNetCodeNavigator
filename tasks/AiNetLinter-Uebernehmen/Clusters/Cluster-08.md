# Cluster 8: MCP Protocol Layer, Budgeting & Response-Formatting

[Zurück zum Konzept](../Konzept.md)

- [ ] 8.1 Budgeting & Truncation:
  - [x] `SharpToken`-Integration für Token-Begrenzungen
  - [x] `McpTruncation`: Präzises Abschneiden mit Fortsetzungshinweisen (`RESPONSE_BUDGET_TOO_SMALL`)
  - [ ] Review/Audit zu 8.1 durchführen; Findings ergänzen und umsetzen.
- [ ] 8.2 Standardisiertes Result-Building:
  - [ ] `McpToolResults`: Einheitliche Erzeugung von `CallToolResult`, `IsError`-Policy und Statusblöcken
  - [ ] Review/Audit zu 8.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 8.3 Langläufer & Paginierung:
  - [ ] `LongRunningToolCallStore`: Polling- und Fortsetzungs-Tokens (`operationToken`, `continuationToken`)
  - [ ] Review/Audit zu 8.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 8.4 Argument-Validierung:
  - [ ] `McpArgumentValidationFilter`: Schema- und Eingabevalidierung für alle Tools
  - [ ] FastTests für Budgeting, Formatting und Validierung
  - [ ] Review/Audit zu 8.4 durchführen; Findings ergänzen und umsetzen.
- [ ] 8.5 Einheitlichen öffentlichen Fehler- und Fortsetzungsvertrag pro Tool testen: `IsError`, Retry bei noch ladendem Target, `RESPONSE_BUDGET_TOO_SMALL`, `minimumResponseBytes`, stabile Pagination und Eingabegrenzen dürfen weder partielle Erfolge vortäuschen noch Daten still verlieren.
  - [ ] Review/Audit zu 8.5 durchführen; Findings ergänzen und umsetzen.
