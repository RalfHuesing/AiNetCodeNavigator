# Cluster 10: Repository und Produkt-Ausgaben vollständig auf Englisch umstellen

[Zurück zum Konzept](../Konzept.md)

- [ ] 10.1 Alle versionierten, selbst verfassten Inhalte inventarisieren: `rg --files` und beispielsweise `rg -n '[ÄÖÜäöüß]|\b(Fehler|Keine|Bitte|ungültig|gefunden|Zeilen|Vollständigkeit|Starte|Bestanden)\b'` für typische deutsche Wörter und Meldungen einsetzen; auch ASCII-only-Texte manuell prüfen. `src/`, `tests/`, `scripts/`, `docs/`, `tasks/`, `.agents/`, Konfigurationsdateien und README-/AGENTS-Dateien einbeziehen. Generierte, ignorierte oder externe Dateien nicht als zu bearbeitenden Repository-Inhalt zählen.
  - [ ] Review/Audit zu 10.1 durchführen; Findings ergänzen und umsetzen.
- [ ] 10.2 Sämtliche deutschsprachigen Kommentare, XML-Dokumentation, Fehlermeldungen, Hinweise, MCP-Tool-Antworten, CLI-/Log-Ausgaben, PowerShell-Skriptausgaben, Testnamen/-Assertions/-Fixtures, Regeln und Dokumentation ins Englische übertragen. Auch bestehende Aufgabenbeschreibungen einschließlich des Konzepts, aller Clusterdateien und der Review-Dateien übersetzen. `.agents/rules/01-language-and-scope.mdc` danach auf Englisch als verbindliche Repository-Sprache für künftige Inhalte festlegen. Die Kommunikation des Agenten mit dem Nutzer bleibt Deutsch.
  - [ ] Review/Audit zu 10.2 durchführen; Findings ergänzen und umsetzen.
- [ ] 10.3 Deutschsprachige selbst verfasste Bezeichner, Datei- und Verzeichnisnamen auf Englisch bringen und alle Verweise/Links nachziehen. Etablierte MCP-Toolnamen, JSON-Feldnamen, Fehlercodes, Handoff-Formate und andere Maschinenverträge nur bei fachlich nötiger, getesteter Vertragsänderung ändern; Übersetzungen dürfen Navigationssemantik und read-only-Verhalten nicht verschlechtern.
  - [ ] Review/Audit zu 10.3 durchführen; Findings ergänzen und umsetzen.
- [ ] 10.4 Betroffene Tests auf englische Ausgaben aktualisieren und reale MCP-Aufrufe für Erfolg, Fehler und Retry prüfen. Nach der Migration die offiziellen Build-, Fast-, Integrations- und Gesamttest-Skripte ausführen. Die gezielte `rg`-Suche wiederholen und jeden verbleibenden Treffer prüfen; für selbst verfasste deutsche Texte dürfen keine offenen Treffer bleiben.
  - [ ] Review/Audit zu 10.4 durchführen; Findings ergänzen und umsetzen.
