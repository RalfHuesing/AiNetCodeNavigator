# Cluster 8: MCP Protocol Layer, Budgeting & Response-Formatting

[Zurück zum Konzept](../Konzept.md)

- [x] 8.1 Budgeting & Truncation:
  - [x] `SharpToken`-Integration für Token-Begrenzungen
  - [x] `McpTruncation`: Präzises Abschneiden mit Fortsetzungshinweisen (`RESPONSE_BUDGET_TOO_SMALL`)
  - [x] Review/Audit zu 8.1 durchführen; Findings ergänzen und umsetzen.
  - [x] Audit 1 P1: Unmögliche `minimumResponseBytes`-Retry-Werte oberhalb des öffentlichen Maximums behandeln.
  - [x] Audit 1 P2: `startOffset` auf vollständige Zeileneinheiten begrenzen oder verlustfrei kanonisieren.
  - [x] Audit 1 P2: Token-Budget-Vertrag für Fehlerantworten klären und durchsetzen/testen.
- [x] 8.2 Standardisiertes Result-Building:
  - [x] `McpToolResults`: Einheitliche Erzeugung von `CallToolResult`, `IsError`-Policy und Statusblöcken
  - [x] Review/Audit zu 8.2 durchführen; Findings ergänzen und umsetzen (accepted in final independent audit 3/3).
  - [x] Audit 1 P2: Calculate success budget retries from the intended success projection rather than the error status prefix.
  - [x] Audit 1 P2: Preserve required recovery and argument-correction fields when error context is shortened.
  - [x] Audit 1 P2: Apply response budgets to loading/retry results, including their status and next action.
  - [x] Audit 2 P2: Make the advertised truncated-success byte/token retry executable with the final status projection.
  - [x] Audit 2 P2: Preserve the exact required error envelope when optional context follows multiline correction fields.
- [x] 8.3 Langläufer & Paginierung:
  - [x] `LongRunningToolCallStore`: Polling- und Fortsetzungs-Tokens (`operationToken`, `continuationToken`)
  - [x] Lifecycle-, Cancellation-, Tokenbindungs-, Capacity- und Pagination-Vertrag dokumentieren und testen.
  - [x] Review/Audit zu 8.3 durchführen; Findings ergänzen und umsetzen (accepted in final independent audit 3/3).
  - [x] Audit 1 P2 (accepted in audit 2/3): Keep continuation retry minima executable across token projections and exact budget retries.
  - [x] Audit 1 P2 (accepted in audit 2/3): Replay completed operation pages without allocating new immutable snapshots.
  - [x] Audit 1 P2 (accepted in audit 2/3): Enforce completed retention when background operations finish without a poll.
  - [x] Audit 1 P2 (accepted in audit 2/3): Coordinate expiration cancellation and completion cleanup without disposed-CTS races.
  - [x] Audit 2 P2 (accepted in audit 3/3): Keep replayed cached final pages consistent with continuation snapshot lifetime.
  - [x] Audit 2 P2 (accepted in audit 3/3): Preserve delegate loading/retry control semantics instead of projecting complete success.
- [x] 8.4 Argument-Validierung:
  - [x] `McpArgumentValidationFilter`: Schema- und Eingabevalidierung für registrierte SDK-Tools
  - [x] FastTests mit SDK-Stream-Fixture für Schema, Budgeting, Formatting und Validierung
  - [x] Review/Audit zu 8.4 durchführen; Findings ergänzen und umsetzen (accepted in final independent audit 3/3).
  - [x] Audit 1 P2 (accepted in audit 2/3): Reject unknown top-level keys for referenced/composed root schemas.
  - [x] Audit 1 P2 (explicit rename accepted in audit 2/3): Match SDK-advertised parameter names in binding compatibility checks.
  - [x] Audit 1 P2 (accepted in audit 2/3): Apply error budgets and protocol fallback to unavailable schema responses.
  - [x] Audit 1 P2 (accepted in audit 3/3): Resolve precise safe required-field paths inside inline/reference dictionary values and preserve unsafe-ancestor fallback.
  - [x] Audit 2 P2 (accepted in audit 3/3): Match SDK wire names using SDK parameter semantics through referenced/composed roots without implicit naming-policy renames.
- [ ] 8.5 Einheitlichen öffentlichen Fehler- und Fortsetzungsvertrag pro Tool testen: `IsError`, Retry bei noch ladendem Target, `RESPONSE_BUDGET_TOO_SMALL`, `minimumResponseBytes`, stabile Pagination und Eingabegrenzen dürfen weder partielle Erfolge vortäuschen noch Daten still verlieren.
  - [x] Production host and all twenty navigation registrations are available; focused real-stdio implementation evidence is tracked under Cluster 9.2.
  - [x] Implementation matrix records concrete real-host success and tool-specific invalid-input evidence for all twenty navigation tools, shared malformed-envelope cases, selected actual minimum-budget retry, stable outer/domain page reconstruction where supported, truthful domain truncation, and lifecycle ownership: [public host and tool contract matrix](../Reviews/public-contract-matrix.md).
  - [ ] Review/Audit zu 8.5 durchführen; Findings ergänzen und umsetzen (0/3 point audits; exact byte/token recovery minima and every loading/success retry projection are not verified per tool).

Initial [Cluster 8 integration review 1](../Reviews/Cluster-08.md#cluster-8-integration-review-1--partial-scope-public-contracts-pending) verified the earlier internal scope without new interface findings (83 focused regressions passed). The public host matrix is now implemented; Cluster 8 remains open for the documented remaining 8.5 coverage and independent review. Point 8.1 was accepted at 2/3; 8.2-8.4 at 3/3. The current implementation phase consumes no 8.5 point audit (0/3). Work was stopped at the user's request; see the [remaining-work and verification snapshot](../Findings.md#user-directed-stop--2026-10-01).
