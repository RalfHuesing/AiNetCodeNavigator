# Cluster 11: End-to-End Verifikation, Dokumentation & Abnahme

[Zurück zum Konzept](../Konzept.md)

- [ ] 11.1 E2E-Integrationstests:
  - [ ] Stdio-Kommunikation gegen echte Solution und echte Assemblies
  - [ ] Verifikation aller 20 Navigations- und zwei Wartungswerkzeuge
  - [ ] Review/Audit zu 11.1 durchführen; Findings ergänzen und umsetzen.
- [ ] 11.2 Dokumentation & Tool-Katalog:
  - [ ] `docs/tools/`: Vollständiger Tool-Katalog mit Schemas und Parametern
  - [ ] `docs/setup/`: Konfiguration für Claude Desktop, Cursor, Antigravity
  - [ ] Review/Audit zu 11.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 11.3 Finale Abnahme:
  - [ ] `pwsh -File ./scripts/build.ps1` (0 Warnungen, 0 Fehler)
  - [ ] `pwsh -File ./scripts/test.ps1` (100% bestandene Tests)
  - [ ] Review/Audit zu 11.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 11.4 Abnahmematrix aus lokalen Navigator-Spezifikationen, öffentlicher Vertragsmatrix und Tests erstellen: für jedes der 22 Werkzeuge mindestens Erfolg, relevante Filter, Handoff/Folgeaufruf, Pagination/Budget und Fehlerszenarien nachweisen; ausdrücklich ausgeschlossene Linter-, Metrik- und Schreibwerkzeuge dürfen nicht registriert sein.
  - [ ] Review/Audit zu 11.4 durchführen; Findings ergänzen und umsetzen.
