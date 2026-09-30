# AiNetCodeNavigator – Architektonische Notizen & Differenzen zu AiNetLinter

## MCP-Server & Prozessmodell

- **Nur als direkter Stdio-MCP-Host implementiert (kein ThinClient / Daemon wie bei AiNetLinter):**
  - Kein zentraler Daemon über Named Pipes: Jeder gestartete Stdio-Prozess hält seine eigene `NavigatorHostRuntime` und lädt die Solution separat via `MSBuildWorkspace`.
  - **Risiko bei Multi-Agenten-Betrieb:** Wenn ein Agentensystem mehrere parallele Subagenten mit jeweils eigener Stdio-Instanz startet, vervielfachen sich RAM-Verbrauch, Startup-Ladezeiten und parallele MSBuild-Dateizugriffe.
  - *Praxisprüfung erforderlich:* Da der Linter- und Metrik-Teil entfallen ist, ist der Speicherbedarf pro Instanz geringer als bei AiNetLinter (~1,5–3 GB statt ~10 GB bei 180k LOC), aber bei mehreren parallelen Prozessen summiert es sich dennoch. AiNetLinter hatte mit Daemon + ThinClient am Ende keine Probleme; technisch ist der direkte Stdio-Host zwar einfacher, bei parallelen Prozessen aber potenziell ressourcenhungriger.

- **Kaltstart-Latenz zwischen separaten Agenten-Sessions:**
  - Da kein langlebiger Daemon im Hintergrund weiterläuft, erlischt der In-Memory-Cache beim Beenden der Stdio-Session (EOF). Eine spätere neue Session muss MSBuild neu initialisieren und die Solution erneut laden (5–20 s Initialisierungszeit bei großen Projekten).

## Tool-Verhalten & Protokoll-Trade-Offs

- **Zusätzliche Roundtrips / Latenz durch hartes Response-Budgeting:**
  - Jedes Tool erzwingt `maxResponseBytes` und `maxResponseTokens`. Überschreitet ein Ergebnis das Budget, liefert der Server `RESPONSE_BUDGET_TOO_SMALL` mit exakten Mindestwerten (`minimumResponseBytes`, `minimumResponseTokens`).
  - *Trade-Off:* Schützt das Kontextfenster vor Overflow, zwingt den Agenten aber zu einem zweiten LLM-Turn / API-Aufruf mit korrigiertem Budget (kostet Latenz und Token).

- **Rigide Handoff-Invalidierung (kein tolerantes Heuristik-Fallback):**
  - Handoff-IDs (`h:...`) sind kryptografisch an den Target-Hash, Snapshot und Projektkontext gebunden.
  - Wenn sich der Code ändert oder ein Snapshot stale wird, bricht der Server mit `STALE_HANDOFF` bzw. `INVALID_HANDOFF` ab. Es gibt kein stillschweigendes Raten/Namensauflösen mehr – der Agent muss über `find_symbol` neu suchen.

- **Keine Volltext- / Nicht-C#-Suche (`search_pattern`) – *(Einstufung: fraglich)*:**
  - In AiNetLinter gab es `search_pattern` für Regex-/Textsuche in Nicht-C#-Dateien (`.json`, `.xml`, `.sql`, etc.).
  - In AiNetCodeNavigator entfallen (reine C#-Semantik).
  - *Bewertung: Fraglich als echter Nachteil*, da moderne Agentensysteme bereits eigene, optimierte Volltext-Werkzeuge (Ripgrep, Grep-Tools, IDE-File-Search) standardmäßig mitbringen.
