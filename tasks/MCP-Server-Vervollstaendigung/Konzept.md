---
status: ready
---

# Funktionsfähiger MCP-Server mit abschließendem Agent Interaction Lab

## Intention

AiNetCodeNavigator muss einem AI-Agenten zuverlässige, ausschließlich lesende C#-Navigation durch Source-Solutions und verwaltete Assemblies ermöglichen. Das Ergebnis ist ein funktionsfähiger MCP-Stdio-Server mit allen 20 Navigations- und zwei Wartungstools, verständlichen öffentlichen Verträgen, korrekten Folgeaufrufen und beherrschbaren Antwortgrößen. Danach untersucht das Agent Interaction Lab dieses Produkt auf dem eigenen Repository und verbessert belegte Defekte und Verständlichkeitsprobleme in begrenzten, getrennt bewerteten Runden.

Die Priorität ist: nutzbare Produktfunktionen und Korrektheit, dann Vertragsnachweise und Dokumentation, zuletzt das Lab und seine Verbesserungsschleife. E2E-Integrationstests gehören nicht zu diesem Vorhaben.

## Verbindliche Spezifikation und Lesereihenfolge

Dieses Dokument und die drei Kapitel unter `konzept/` bilden zusammen genau ein vollständiges Konzept. Implementierer und Auditor lesen alle vier Dokumente. Das Lab-Kapitel ist verbindlicher Bestandteil. Die Konzeptfreigabe startet keine Roadmap und erteilt keine Implementierungsfreigabe; sie belegt keine ausgeführte Produktprüfung.

| Dokument | Verbindlicher Inhalt |
|---|---|
| [01-server](konzept/01-server.md) | Toolmenge, Navigationssemantik, öffentliche Parameter, Handoffs, Runtime, Budgets und Latenz |
| [02-verifikation](konzept/02-verifikation.md) | Testgrenzen, konkrete öffentliche Abnahmefälle, Dokumentation und Abschlussnachweise |
| [03-agent-lab](konzept/03-agent-lab.md) | Vollständiger Lab-Vertrag sowie abschließende Agenten- und Verbesserungsrunden |

Repository-Regeln gelten. Vor Änderungen erfasst der Implementierer HEAD und Arbeitsbaum und prüft die produktiven Definitionen, Aufrufer, Runtime und betroffenen Tests. Code, Tests und `docs/` belegen den tatsächlichen Implementierungsstand; die Anforderungen dieses Auftrags stehen vollständig in diesen vier Konzeptdokumenten. Ein bereits korrekt implementierter Vertrag wird wiederverwendet und am geprüften Stand nachgewiesen. Eine Implementierungsbehauptung ohne passenden Nachweis ist keine Abnahme.

Bei einer echten, durch dieses Konzept und die lokalen Produktverträge nicht entscheidbaren Produkt- oder Architekturfrage hält der Agent nur die davon abhängige Arbeit an und fragt den Nutzer. Er darf keine alternative Produktentscheidung erfinden. Unabhängige Arbeit geht weiter.

## Scope

### Muss

- Die exakt festgelegte 22er-Toolmenge und sämtliche Source-/Assembly-/Git-Funktionen aus Kapitel 01 liefern.
- MSBuild-Strukturinvalidierung, öffentliche Parameter-/Default-/Filter-/Cap-Verträge, exakte Budget-Recovery, Loading/Operations/Fortsetzungen, Assembly-Membersortierung, Host-/Runtimeverträge, Read-only-Grenzen und Abschlussdokumentation vollständig erfüllen und prüfen.
- Korrekte Implementierungen wiederverwenden; fehlende oder fehlerhafte Verträge bei den zuständigen Komponenten ergänzen beziehungsweise korrigieren.
- Den Produktvertrag durch automatisierte Unit-, Komponenten- und eng begrenzte Nicht-E2E-Integrationstests nachweisen. Den ausgeschlossenen E2E-Anteil in Gates und Berichten ausdrücklich abgrenzen.
- Harte Byte-/Tokengrenzen und passende Antwortseiten beibehalten. Explizite Budgets niemals automatisch anheben. Vermeidbare Budgetfehler und Folgeaufrufe anhand konkreter Ergebnisprojektionen beheben und anschließend im Lab messen.
- Den gemeinsamen produktiven Katalog, Validator und Dispatcher gemäß Kapitel 03 schon im Serverteil bereitstellen. Host, Vertragsprüfungen und Lab verwenden denselben Aufrufpfad.
- Einen vollständigen englischen Toolkatalog und Setupdokumentation für Claude Desktop, Cursor und Antigravity liefern. Dokumentation entsteht mit der tatsächlich implementierten Funktion.
- Laufzeitursachen zulässiger Tests und Navigation nach Phasen untersuchen und unnötige wiederholte Fixturearbeit reduzieren. Assertions bleiben erhalten; ausgeschlossene Tests werden dafür nicht ausgeführt.
- Die separate Lab-Exe gemäß Kapitel 03 liefern. Das Lab kommt erst nach der Serverabnahme; im bewerteten Run wird nicht gefixt.
- Das eigene Repository AiNetCodeNavigator mit Source-Solution und gebauter Core-Assembly als verbindliche Lab-Basis benutzen. Beide Ziele gehören zu einem Repository.
- Mindestens zwei und höchstens drei bewertete Lab-/Audit-Runden mit frischen Agentenkontexten durchführen, dazwischen bestätigte Findings korrigieren, die Wirkung vergleichen und Restbefunde ausweisen.

### Nicht

- Neue oder ausgeführte E2E-Integrationstests, reale MCP-Testclients, JSON-RPC-/Handshake-Testläufe, MCP-Loopback im Lab oder eine als Komponententest bezeichnete Gesamtsystemprüfung.
- Löschen oder Abschwächen vorhandener Tests, um grüne Gates zu erzeugen. Bestehende E2E-Tests bleiben erhalten und werden aus der Auswahl dieses Vorhabens ausgeschlossen.
- Linting, Qualitätsmetriken, Code-Smells, Diagnose-Regeln, Clone-Erkennung, automatisches Refactoring oder produktive Code-Auditingtools. Die Audit-Agenten prüfen die Umsetzung; der MCP-Server dient der Navigation.
- Schreiben in analysierte Source-/Assemblyziele, Ausführen analysierter Binaries, Lab-Modus im Produkt, Lab-Verteilung als Produktvoraussetzung, zweite Navigation-API oder kopierte Tooldefinitionen.
- Unbegrenzte Audit-/Fixschleifen, statistische Modellbenchmarks, exhaustive Kreuzprodukte aller Parameter oder eine numerische 100%-Code-Coverage-Zusage.
- Deployment, tatsächliche Änderungen an Client-/Codex-MCP-Konfigurationen, neue Modell-API, Weboberfläche, allgemeine Agenten-Sandbox oder ein zusätzlicher Bibliotheks-/Testprojektbaum.

## Umsetzung und unabhängiger Audit

Die spätere Umsetzung verwendet `gpt-6-luna` mit Reasoning `high`; der unabhängige Audit und die Lab-Analyse verwenden `gpt-6.1-sol` mit Reasoning `high`. Aufgabenautoren und Testagenten verwenden ebenfalls `gpt-6-luna/high`. Der koordinierende Agent startet und beendet die Rollen; die Lab-Exe führt keine Modelle aus.

Schreibende Arbeit ist strikt seriell. Dazu gehören Code, Dokumentation, Reviews, Builds, Tests, Restores, statische Logs und Git-Aktionen. Ein Auditor mit eigenen Gates oder Reviewdateiänderungen benötigt dieselbe exklusive Schreibphase. Rein lesende Recherche darf parallel erfolgen; ein Audit des veränderlichen Produkts beginnt erst an einem festen, unbeanspruchten Stand.

Der unabhängige Audit prüft fachlich zusammengehörende Abnahmeeinheiten und abschließend ihre Zusammenschaltung. Eine Abnahmeeinheit ist ein vor ihrem ersten Audit festgelegter zusammenhängender Vertrag mit den betroffenen Produktionspfaden und erforderlichen Nachweisen. Pro Einheit sind einschließlich Erstprüfung höchstens drei Audits zulässig. Korrekturen und Nachprüfungen desselben Vertrags zählen zur gleichen Einheit; Umbenennen oder Aufteilen eröffnet keine zusätzlichen Versuche. Jeder Audit protokolliert Commit, Scope, Gates, Findings und Abnahmestatus. Ein erreichtes Limit beendet nur die Schleife und erklärt keinen Defekt für behoben.

Die abschließende Lab-Schleife hat die gesonderte Rundenzahl aus Kapitel 03. Sie bewertet die Agentenbenutzbarkeit des abgenommenen Produkts. Ein verbleibender Korrektheitsblocker verhindert den vollständigen Abschluss auch nach Ausschöpfung eines Limits.

## Abschlussdefinition

Der Serverteil ist fertig, wenn alle Muss-Verträge aus Kapitel 01, die vollständige relevante Nicht-E2E-Abnahmematrix, die zulässigen Gates, die unabhängigen Vertrags-/Zusammenschaltungsreviews und die englische Dokumentation abgeschlossen sind. Ausgeschlossene E2E-Nachweise werden ausdrücklich als nicht durchgeführt geführt. Ein transportloser Erfolg darf nicht als selbst geprüfter Stdio-Erfolg bezeichnet werden.

Das Gesamtvorhaben ist fertig, wenn zusätzlich die Lab-Infrastruktur, die vollständige Aufgabenuntersuchung, mindestens zwei bewertete Audit-Runden, notwendige Korrekturen und der Vergleich gemäß Kapitel 03 vorliegen. Es gibt keine unbearbeiteten oder blockierten Pflichtaufgaben und keine offenen P0/P1/P2-Defekte, die Navigation, Read-only, Bindung, Recovery oder benötigte Funktionen verletzen. Dokumentierte qualitative `not_observed`-Spezialzellen nach gültigem Aufgabenversuch sind ausschließlich unter den Bedingungen aus Kapitel 03 zulässig; sie belegen keinen autonomen Erfolg. P3-Verbesserungsbefunde dürfen nach dem Rundengrenzwert nur mit belegter Auswirkung, verbleibender Einschränkung und konkreter Abnahmebedingung offen bleiben. Die in Kapitel 01 verbindlich festgelegte Assembly-Sortierung muss erfüllt sein. Der Bericht nennt jede zulässige Restgrenze und beansprucht keine fehlerfreie Software.

Dieses Konzept ist nach unabhängiger Prüfung mit `gpt-6.1-sol/high` und Korrektur der Source-Baseline-Vorbereitung `ready` (Freigabe: 2026-10-01). Die Vorbereitung umfasst ausdrücklich Restore/Build vor dem Einfrieren, Prüfung der Source-Metadatenreferenzen sowie den unveränderlichen Restore-/Referenzbestand für den Vergleich. Die abschließende unabhängige Prüfung hat keine weiteren handlungsrelevanten Findings oder notwendigen Nutzerentscheidungen festgestellt. Intention, Scope, Nicht-Ziele und Verifikation erfüllen die Freigabekriterien. Roadmap und Umsetzung werden ausschließlich durch den jeweiligen späteren Nutzerauftrag gestartet.
