---
status: draft
---

# Funktionsfähiger MCP-Server mit abschließendem Agent Interaction Lab

## Intention

AiNetCodeNavigator muss einem AI-Agenten zuverlässige, ausschließlich lesende C#-Navigation durch Source-Solutions und verwaltete Assemblies ermöglichen. Das Ergebnis ist der funktionsfähige MCP-Stdio-Server mit allen 20 Navigations- und zwei Wartungstools, verständlichen öffentlichen Verträgen, korrekten Folgeaufrufen und beherrschbaren Antwortgrößen. Danach untersucht das Agent Interaction Lab genau dieses Produkt auf dem eigenen Repository und verbessert belegte Defekte und Verständlichkeitsprobleme in begrenzten, getrennt bewerteten Runden.

Die Priorität ist: nutzbare Produktfunktionen und Korrektheit, dann deren Vertragsnachweis und Dokumentation, zuletzt das Lab und seine Verbesserungsschleife. Ein zusätzliches E2E-Testvorhaben gehört nicht zum Ergebnis.

## Geltung und Lesereihenfolge

Dieses Dokument und die drei Kapitel unter `konzept/` bilden zusammen genau ein Konzept. Die Kapitel sind vollständig zu lesen; das Lab-Kapitel ist verbindlicher Bestandteil, kein Vorschlag. Dieser Planungsauftrag erstellt keine Roadmap, erteilt keine Implementierungsfreigabe und führt keine Produktprüfung aus.

| Dokument | Verbindlicher Inhalt |
|---|---|
| [01-server](konzept/01-server.md) | Toolmenge, bestehende Semantik, Restdefekte, Handoffs, Runtime, Budgets und Latenz |
| [02-verifikation](konzept/02-verifikation.md) | Testgrenzen, konkrete öffentliche Abnahmezellen, Dokumentation und Abschlussnachweise |
| [03-agent-lab](konzept/03-agent-lab.md) | Vollständiger Lab-Vertrag sowie abschließende Agenten- und Verbesserungsrunden |
| [Quellenabdeckung](Quellenabdeckung.md) | Herkunft und disposition aller Altaufgaben einschließlich ausdrücklich ersetzter Anforderungen |

Die aktuelle Nutzerentscheidung ersetzt drei alte Vorgaben: E2E-Integrationstests entfallen; der Audit-Agent verwendet `gpt-6.1-sol` mit `high`; die abschließende iterative Verbesserung ist Bestandteil dieses Vorhabens. Die Alttexte im Quellenarchiv sind ausschließlich historische Evidenz. Ihr Ausführungsauftrag, Stopvermerk, Status, Medium-Auditvorgabe, separate Lab-Freigabesperre und Verbot automatischer Fixrunden gelten nicht als Anweisung für dieses neue Vorhaben. Abgeschlossene historische Audits werden dadurch weder erneut durchgeführt noch als neue Freigabe gezählt.

Bei einer echten, durch dieses Konzept und die lokalen Produktverträge nicht entscheidbaren Produkt- oder Architekturfrage muss der Agent nur die davon abhängige Arbeit anhalten und den Nutzer fragen. Er darf keine alternative Produktentscheidung erfinden. Unabhängige Arbeit geht weiter.

## Geprüfte Planungsgrundlage

Planungsstand: 2026-10-01, Ausgangs-HEAD `a27bdda444d3648c0a927605dc55734f4c253ff6`, sauberer Arbeitsbaum vor Beginn. Gelesen wurden die Planungsrolle und Repository-Regeln, Altaufgaben und deren aktuelle Befund-/Abnahmeübersichten, aktuelle `docs/` sowie konkrete Produktionsregistrierung, Runtime, Routing, Validierung und MSBuild-Strukturerfassung. Historische Detailnachweise werden vollständig und bytegetreu mit Prüfsummen konserviert. Es wurden für die Konzeptarbeit keine Builds, Produkt- oder E2E-Tests ausgeführt.

Die Registrierung der 22 Namen ist in `McpServerHost.cs` vorhanden. Der bisherige Zustand ist deshalb kein leeres Servergerüst. Historische erfolgreiche Tests sind vorhandene Evidenz; sie sind keine selbst ausgeführten neuen Gates und keine vollständige öffentliche Abnahme. Der zuletzt angefangene Gesamttest wurde im alten Auftrag unterbrochen. Seine FastTests waren laut Stopnachweis erfolgreich; ein erfolgreicher Abschluss des Gesamtlaufs ist nicht belegt.

Eine zusätzliche konkrete Vertragslücke wurde am Quelltext festgestellt: `StructureTools.GetIndexScope` veröffentlicht derzeit nur `targetPath` und einen CancellationToken. Es setzt das Bytebudget intern und bietet keine öffentlichen Budget-, Operations- oder Fortsetzungsparameter. Die alte allgemeine Budget-/Pagingmatrix ist für diesen Toolaufruf deshalb noch nicht ausführbar. Kapitel 01 legt die vollständige Ergänzung fest.

## Scope

### Muss

- Die exakt festgelegte 22er-Toolmenge und alle in Kapitel 01 beschriebenen, bereits vorgesehenen Source-/Assembly-/Git-Funktionen vervollständigen und erhalten.
- Alle alten offenen Sachaufgaben und Befundgrenzen übernehmen: MSBuild-Strukturinvalidierung, öffentliche Parameter-/Default-/Filter-/Cap-Verträge, exakte Budget-Recovery, Loading/Operations/Fortsetzungen, Assembly-Membersortierung, ausstehende Host-/Vertragsaudits, Read-only-Grenzen, Laufzeituntersuchung und Abschlussdokumentation.
- Bestehende korrekte Implementierungen wiederverwenden. Ein historisches `[x]` ist weder Anlass zur Neuimplementierung noch allein ein Abschlussbeweis.
- Produktvertrag durch automatisierte Unit-, Komponenten- und eng begrenzte Nicht-E2E-Integrationstests nachweisen. Den ausgeschlossenen E2E-Anteil in Gates und Berichten ausdrücklich abgrenzen.
- Harte Byte-/Tokengrenzen und passende Antwortseiten beibehalten. Explizite Budgets niemals automatisch anheben. Vermeidbare Budgetfehler und Folgeaufrufe anhand konkreter Ergebnisprojektionen beheben und anschließend im Lab messen.
- Vollständigen englischen Toolkatalog und Setupdokumentation für Claude Desktop, Cursor und Antigravity liefern. Dokumentation entsteht mit der tatsächlich implementierten Funktion.
- Die separate Lab-Exe und den gemeinsamen produktiven Aufrufpfad gemäß Kapitel 03 liefern. Das Lab kommt erst nach der Serverabnahme; im bewerteten Run wird nicht gefixt.
- Das eigene Repository mit Source-Solution und gebauter Core-Assembly als verbindliche Lab-Basis benutzen. Beide Ziele gehören zu einem Repository; ein zweites Repository ist keine Voraussetzung.
- Mindestens zwei und höchstens drei bewertete Lab-/Audit-Runden mit frischen Agentenkontexten durchführen, dazwischen bestätigte Findings korrigieren, die Wirkung vergleichen und Restbefunde ehrlich ausweisen.
- Alle Nachweise des alten Aufgabenbestands in diesem neuen Ordner erhalten. Vor Entfernung der Altordner alle aktiven Repositorylinks und Referenzen auf die neue Ablage umstellen.

### Nicht

- Neue oder ausgeführte E2E-Integrationstests, reale MCP-Testclients, JSON-RPC-/Handshake-Testläufe, MCP-Loopback im Lab oder eine als Test verkleidete Gesamtsystemprüfung.
- Löschen oder Abschwächen vorhandener Tests, um grüne Gates zu erzeugen. Bestehende E2E-Tests werden erhalten und aus der Auswahl dieses Vorhabens ausgeschlossen.
- Linting, Qualitätsmetriken, Code-Smells, Diagnose-Regeln, Clone-Erkennung, automatisches Refactoring oder produktive Code-Auditingtools. Die Audit-Agenten prüfen die Umsetzung; das macht den MCP-Server nicht zu einem Auditprodukt.
- Schreiben in analysierte Source-/Assemblyziele, Ausführen analysierter Binaries, Lab-Modus im Produkt, Lab-Verteilung als Produktvoraussetzung, zweite Navigation-API oder kopierte Tooldefinitionen.
- Unbegrenzte Audit-/Fixschleifen, statistische Modellbenchmarks, exhaustive Kreuzprodukte aller Parameter oder eine numerische 100%-Code-Coverage-Zusage.
- Deployment, tatsächliche Änderungen an Client-/Codex-MCP-Konfigurationen, neue Modell-API, Weboberfläche, allgemeine Agenten-Sandbox oder ein zusätzlicher Bibliotheks-/Testprojektbaum.

## Umsetzung und unabhängiger Audit

Die spätere Umsetzung verwendet `gpt-6-luna` mit Reasoning `high`; der unabhängige Audit und die Lab-Analyse verwenden `gpt-6.1-sol` mit Reasoning `high`. Aufgabenautoren und Testagenten verwenden ebenfalls `gpt-6-luna/high`. Der koordinierende Agent startet und beendet die Rollen; die Lab-Exe führt keine Modelle aus. Diese Angaben gelten auch gegenüber alten abweichenden Modellvorgaben.

Schreibende Arbeit ist strikt seriell. Dazu gehören Code, Dokumentation, Reviews, Builds, Tests, Restores, statische Logs und Git-Aktionen. Ein Auditor mit eigenen Gates oder Reviewdateiänderungen benötigt dieselbe exklusive Schreibphase. Rein lesende Recherche darf parallel erfolgen; ein Audit des veränderlichen Produkts beginnt erst an einem festen, unbeanspruchten Stand.

Für übernommene Restaufgaben gelten die historischen Punkt-Auditgrenzen weiter. Keine Umbenennung oder Bündelung setzt einen ausgeschöpften Zähler zurück. Die folgende Tabelle ist eine Zustandsübernahme, keine neue Aufgabenroadmap:

| Alte Identität | Ausgangsstatus | Auditstand und neue Disposition |
|---|---|---|
| 2.3 | Strukturgrenze offen | 3/3 ausgeschöpft; konkrete Restsemantik ergänzen, Abnahme im verbleibenden übergreifenden Review, kein viertes Punktaudit |
| 8.5 | Vertragsmatrix unvollständig | 0/3; fehlende Zellen transportlos nachweisen und unabhängig auditieren |
| Cluster 8 | Öffentliche Zusammenschaltung offen | bisher ein partielles Integrationsreview, 0 Fixrunden; verbleibende gemeinsame Pfade prüfen |
| 9.2 | Registrierung vorhanden; P2-Fix unbestätigt, P3 offen | 1/3; Fix unabhängig bestätigen und die P3-Sortierung schließen |
| 9.3 | Hostimplementierung vorhanden; Audit fehlt | 0/3; Registrierung, Adapter und Fehlerüberleben durch Code-/Komponentennachweise auditieren |
| 9.4 | Lifecycleimplementierung vorhanden; Audit fehlt | 0/3; Parallelität, Cancellation, Kindprozess-Ownership, Neustart und Disposal auf Komponentenebene auditieren |
| Cluster 9 | Gemeinsamer Review fehlt | 0 Integrationsreviews/Fixrunden; gemeinsamen Produktionspfad prüfen |
| 11.1 | Nicht umgesetzt | E2E-Anforderung durch Nutzer ausgeschlossen; nicht als bestanden markieren |
| 11.2 | Dokumentation offen | Katalog und drei Setupseiten bleiben Muss |
| 11.3 | Abschlussgate offen | Nicht-E2E-Gates und ehrlicher Abschluss bleiben Muss |
| 11.4 | 22er-Abnahmematrix offen | vollständige relevante Vertragsmatrix bleibt Muss; Transportbeweis ist ausgeschlossen |

Bereits abgeschlossene Punkte 1.*, 3.*, 4.*, 5.*, 6.*, 7.*, 8.1–8.4 und 9.1 werden als Grundlage erhalten; historische Zähler stehen im Quellenarchiv. Neue belegte Regressionen werden als neue Defekte geprüft, ohne abgeschlossene Alt-Punktaudits routinemäßig zu wiederholen. Pro noch offenem Altpunkt maximal drei Punkt-Audits insgesamt; pro noch offenem Altcluster maximal drei Fixrunden. Erreichtes Limit beendet nur die Schleife, es erklärt keinen Defekt für behoben.

Die abschließende Lab-Schleife besitzt die eigene, eindeutig begrenzte Rundenzahl aus Kapitel 03. Sie bewertet die Agentenbenutzbarkeit des vervollständigten Produkts und ist kein zusätzliches viertes Audit desselben historischen Punkts. Dieselbe unveränderte Alt-Finding-Reproduktion wird nicht erneut als neue Aufgabe gezählt. Ein verbleibender Korrektheitsblocker verhindert den vollständigen Abschluss auch nach Ausschöpfung eines Limits.

## Abschlussdefinition

Der Serverteil ist fertig, wenn alle Muss-Verträge aus Kapitel 01, die vollständige relevante Nicht-E2E-Abnahmematrix, die zulässigen Gates, die unabhängigen Rest-/Zusammenschaltungsreviews und die englische Dokumentation abgeschlossen sind. Ausgeschlossene E2E-Nachweise werden ausdrücklich als nicht durchgeführt geführt. Ein transportloser Erfolg darf nicht als selbst geprüfter Stdio-Erfolg bezeichnet werden.

Das Gesamtvorhaben ist fertig, wenn zusätzlich die Lab-Infrastruktur, die erste vollständige Aufgabenuntersuchung, mindestens zwei bewertete Audit-Runden, notwendige Korrekturen und der Vergleich gemäß Kapitel 03 vorliegen. Es gibt keine unbearbeiteten oder blockierten Pflichtaufgaben und keine offenen P0/P1/P2-Defekte, die Navigation, Read-only, Bindung, Recovery oder benötigte Funktionen verletzen. Dokumentierte qualitative not_observed-Spezialzellen nach gültigem Aufgabenversuch sind ausschließlich unter den Bedingungen aus Kapitel 03 zulässig; sie belegen keinen autonomen Erfolg. Neue P3-Verbesserungsbefunde dürfen nach dem Rundengrenzwert nur mit belegter Auswirkung, verbleibender Einschränkung und konkreter Abnahmebedingung offen bleiben; die ausdrücklich übernommene Assembly-Sortierung ist vorher zu schließen. Der Bericht nennt jede solche Restgrenze und beansprucht keine fehlerfreie Software.

Dieses Konzept bleibt `draft`. Prüfung/Freigabe, Roadmap und Umsetzung werden ausschließlich durch den jeweiligen späteren Nutzerauftrag gestartet.
