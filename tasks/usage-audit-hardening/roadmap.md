# Roadmap: Verlässliche und tokeneffiziente MCP-Navigation

Verbindliche Spezifikation: [freigegebenes Konzept](Konzept.md), `status: ready`, Freigabe-Commit `0a3f989`. Diese Roadmap ordnet den freigegebenen Scope; sie entscheidet keine weiteren Funktionen oder Produktverträge. Schritt 3 erstellt ausschließlich Planung. Die Umsetzung beginnt erst mit dem gesonderten Nutzeraufruf von Schritt 4.

## Ausführung und Nachweise

Diese Datei ist der Index mit Reihenfolge, verbindlichen Status-Checkboxen und Scopeabdeckung. Jeder Aufgaben- und Auditpunkt steht vollständig in einer eigenen Datei unter `roadmap/`; dort werden auch seine Nachweise gepflegt. Gemeinsame Ausführungsregeln stehen hier und gelten für alle Punkt-Dateien. In Schritt 4 werden die Punkte in der angegebenen Reihenfolge bearbeitet. Resume ist der erste offene ausführbare Aufgabenpunkt im Index; offene Milestone- oder Aufgaben-Checkboxen mit Unterpunkten sind Aggregate und keine eigenen Arbeitsaufträge. Bei M2-T1 wird jeweils der erste offene Unterpunkt bearbeitet. Status-Checkboxen werden ausschließlich in diesem Index gepflegt. Die Milestone-Checkbox wird erst geschlossen, wenn ihre Aufgaben einschließlich Audit abgeschlossen sind.

Für jeden Codepunkt gelten dieselben Anforderungen: betroffenen Ist-Stand und Verbraucher lesen, bei einem reproduzierbaren Defekt zuerst einen fehlgeschlagenen Nachweis herstellen, den vollständigen betroffenen Vertrag implementieren, gezielt verifizieren, aktuelle Dokumentation im selben Slice aktualisieren und ausschließlich die eigenen Änderungen committen. Anforderungen und Grenzwerte werden nicht zugunsten grüner Checks abgeschwächt. Der Abschlussnachweis wird direkt beim jeweiligen Punkt ergänzt: Commit, ausgeführte Checks/Ergebnisse, belegte Analysegrenzen und tatsächlich unerfüllte Nachweise. Keine separate Fortschritts-, Audit- oder Schuldenverwaltung.

Die [Verifikationsregeln](../../.agents/rules/04-verification.mdc) und [Build-/Testanleitung](../../docs/development/build-and-tests.md) bestimmen die späteren Gates. Code-Slices verwenden das offizielle Buildskript sowie die engste betroffene Fast-/Integration-Auswahl; relevante Extended-Fälle werden gezielt ausgewählt. Das vollständige eligible Routine-Gate liegt in [M3-T1](roadmap/M3-T1.md). Reine Dokumentations-Slices brauchen Diffprüfung und `git diff --check`. In dieser Roadmap-Erstellung werden weder Builds noch Tests ausgeführt.

Alle Aufgaben übernehmen die [Nicht-Ziele des Konzepts](Konzept.md#nicht): read-only Targets, kein administrativer Ersatzkatalog, keine Ausführung analysierter Bibliotheken, keine Erweiterung zur Compilerdiagnostik-/Refactoringfunktion, keine Legacy-Aliase und keine neuen universellen Toolketten. Core bleibt frei von MCP-Transport und Tokenisierung. Analysierte Anwendungsdateien werden nicht verändert. Technische Tool-/Komponentennamen sind zulässig; Produkt-, Hersteller- und Kundennamen sowie reale Anwendungspfade gehören weder in diese Planung noch in Commit-Nachrichten. Lokale Messdetails bleiben in den bestehenden, nicht versionierten Auditartefakten; versionierte Nachweise verwenden neutrale Aliase.

Bei langsamen oder hängenden Tests gilt verbindlich die [gesonderte Behandlung nach fünf Minuten](../../.agents/rules/04-verification.mdc#slow-or-stalled-tests). Der Orchestrator vergibt den eigenständigen Diagnose-/Korrekturpunkt vor weiterer Implementierung; unveränderte breite Wiederholungsläufe und Verschiebung auf einen späteren Milestone sind kein zulässiger Umgang. Für den bereits bekannten Sammellauf ist [M2-T1.1](roadmap/M2-T1.1.md) zuständig.

Ein Audit arbeitet nur lesend und bewertet den Milestone-Diff gegen das Konzept und seine Abnahme. In Schritt 4 ist danach höchstens ein gezielter Korrektur-Leaf je Milestone vorgesehen; offene Abweichungen bleiben sichtbar und erlauben keinen unbelegten Abschluss.

## Reihenfolge

- [x] **[M1 — Vergleichsgrundlage und konsistente Analyse](#m1--vergleichsgrundlage-und-konsistente-analyse)**
- [ ] **[M2 — Nachladbare, kompakte Navigation und gemeinsamer Kontext](#m2--nachladbare-kompakte-navigation-und-gemeinsamer-kontext)**
- [ ] **[M3 — Gesamtabnahme und begrenzter Praxisvergleich](#m3--gesamtabnahme-und-begrenzter-praxisvergleich)**

## M1 — Vergleichsgrundlage und konsistente Analyse

- [x] **[M1-T1 — Ausgangsmessung festschreiben](roadmap/M1-T1.md)**
- [x] **[M1-T2 — Snapshot, Referenzidentität und Owner-Leases härten](roadmap/M1-T2.md)**
- [x] **[M1-T3 — Statische Beziehungen korrekt belegen](roadmap/M1-T3.md)**
- [x] **[M1-T4 — Ergebnisstatus und Fortsetzungsgrundlage vereinheitlichen](roadmap/M1-T4.md)**
- [x] **[M1-A — Audit der Analysegrundlage](roadmap/M1-A.md)**

## M2 — Nachladbare, kompakte Navigation und gemeinsamer Kontext

- [ ] **[M2-T1 — Source- und gemeinsame Trefferlisten vollständig nachladen](roadmap/M2-T1.md)**
  - [x] **[M2-T1.1 — Hängenden Integrationslauf untersuchen und stabilisieren](roadmap/M2-T1.1.md)**
  - [x] **[M2-T1.2 — Symbolsuche vollständig nachladen](roadmap/M2-T1.2.md)**
  - [x] **[M2-T1.3 — Referenzen und Implementierungen vollständig nachladen](roadmap/M2-T1.3.md)**
  - [ ] **[M2-T1.4 — Typ-, Datei- und Namespaceinventare vollständig nachladen](roadmap/M2-T1.4.md)**
  - [ ] **[M2-T1.5 — Hierarchie- und Impactlisten samt Budgetregression abschließen](roadmap/M2-T1.5.md)**
  - [ ] **[M2-T1.6 — Indexscope und Projektidentität belegen](roadmap/M2-T1.6.md)**
  - [ ] **[M2-T1.7 — Unterpunkte und gemeinsamen Abschlussnachweis abgleichen](roadmap/M2-T1.7.md)**
- [ ] **[M2-T2 — Assembly-Ausgaben und Suchvertrag vereinfachen](roadmap/M2-T2.md)**
- [ ] **[M2-T3 — Body- und Namespace-Recovery präzisieren](roadmap/M2-T3.md)**
- [ ] **[M2-T4 — Testkandidaten semantisch ergänzen](roadmap/M2-T4.md)**
- [ ] **[M2-T5 — Gemeinsamen Kontext einführen und drei Routen entfernen](roadmap/M2-T5.md)**
- [ ] **[M2-T6 — Releasezuordnung, Katalog und Agentenanleitung abschließen](roadmap/M2-T6.md)**
- [ ] **[M2-A — Audit der Agentenoberfläche](roadmap/M2-A.md)**

## M3 — Gesamtabnahme und begrenzter Praxisvergleich

- [ ] **[M3-T1 — Konsolidierte technische Abnahme](roadmap/M3-T1.md)**
- [ ] **[M3-T2 — Abschlussmessung und Effizienzbefund](roadmap/M3-T2.md)**
- [ ] **[M3-A — Finale Abnahmeprüfung](roadmap/M3-A.md)**

## Abdeckung des freigegebenen Scopes

| Muss im Konzept | Verantwortliche Aufgaben |
|---|---|
| 1 — Beziehungen | [M1-T3](roadmap/M1-T3.md); Gesamtabnahme [M3-T1](roadmap/M3-T1.md) |
| 2 — Kompakte Darstellung | [M2-T2](roadmap/M2-T2.md), [M2-T5](roadmap/M2-T5.md) |
| 3 — Unvollständigkeit/Scope | [M1-T4](roadmap/M1-T4.md), [M2-T1](roadmap/M2-T1.md) ([M2-T1.2](roadmap/M2-T1.2.md), [M2-T1.3](roadmap/M2-T1.3.md), [M2-T1.4](roadmap/M2-T1.4.md), [M2-T1.5](roadmap/M2-T1.5.md), [M2-T1.6](roadmap/M2-T1.6.md), [M2-T1.7](roadmap/M2-T1.7.md)), [M2-T2](roadmap/M2-T2.md), [M2-T5](roadmap/M2-T5.md) |
| 4 — Diagnosen | [M2-T2](roadmap/M2-T2.md); Kontextverbrauch [M2-T5](roadmap/M2-T5.md) |
| 5 — Fortsetzungen | [M1-T4](roadmap/M1-T4.md), [M2-T1](roadmap/M2-T1.md) ([M2-T1.2](roadmap/M2-T1.2.md), [M2-T1.3](roadmap/M2-T1.3.md), [M2-T1.4](roadmap/M2-T1.4.md), [M2-T1.5](roadmap/M2-T1.5.md), [M2-T1.6](roadmap/M2-T1.6.md), [M2-T1.7](roadmap/M2-T1.7.md)), [M2-T2](roadmap/M2-T2.md), [M2-T5](roadmap/M2-T5.md) |
| 6 — Recovery/Leases | [M1-T2](roadmap/M1-T2.md), [M2-T3](roadmap/M2-T3.md), [M2-T5](roadmap/M2-T5.md) |
| 7 — Testkandidaten | [M2-T4](roadmap/M2-T4.md); Kontextverbrauch [M2-T5](roadmap/M2-T5.md) |
| 8 — Resolver/Residency | [M1-T2](roadmap/M1-T2.md) |
| 9 — Release/Katalog | [M2-T5](roadmap/M2-T5.md), [M2-T6](roadmap/M2-T6.md) |
| 10 — Effizienz/Anleitung | [M1-T1](roadmap/M1-T1.md), [M2-T6](roadmap/M2-T6.md), [M3-T2](roadmap/M3-T2.md) |
| 11 — Gemeinsamer Kontext | [M2-T5](roadmap/M2-T5.md) |
| 12 — Snapshot/Batch | [M1-T2](roadmap/M1-T2.md); Verbraucher [M1-T3](roadmap/M1-T3.md), [M2-T1](roadmap/M2-T1.md) ([M2-T1.2](roadmap/M2-T1.2.md), [M2-T1.3](roadmap/M2-T1.3.md), [M2-T1.4](roadmap/M2-T1.4.md), [M2-T1.5](roadmap/M2-T1.5.md), [M2-T1.6](roadmap/M2-T1.6.md)), [M2-T5](roadmap/M2-T5.md) |

Der Prüfweg für den ursprünglichen M2-T1-Klassenumfang wird in [M2-T1.1](roadmap/M2-T1.1.md) stabilisiert und in [M2-T1.7](roadmap/M2-T1.7.md) abschließend vollständig nachgewiesen.

Alle Aufgaben bleiben bis zur tatsächlich ausgeführten Umsetzung und Verifikation offen. Konzept und Messgrenzen werden durch diese Roadmap nicht geändert.
