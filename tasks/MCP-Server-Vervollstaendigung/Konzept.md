---
status: draft
---

# Funktionsfähiger MCP-Server für C#-Navigation

## Intention

AiNetCodeNavigator ermöglicht AI-Agenten zuverlässige, ausschließlich lesende C#-Navigation durch Source-Solutions und verwaltete Assemblies. Das Ergebnis ist ein funktionsfähiger MCP-Stdio-Server mit genau 20 Navigations- und zwei Wartungstools, korrekten Folgeaufrufen, verständlichen öffentlichen Verträgen und begrenzten Antwortgrößen.

Dieses Vorhaben vervollständigt die benötigten Funktionen und behebt belegte Fehler. Maßgeblich sind die fachlichen Ergebnisse. Korrekte Komponenten werden wiederverwendet.

## Verbindliche Spezifikation

Dieses Dokument und die beiden Kapitel unter `konzept/` bilden das vollständige Konzept. Implementierer und Auditor lesen alle drei Dokumente.

| Dokument | Inhalt |
|---|---|
| [01-server](konzept/01-server.md) | Toolmenge, Navigationssemantik, öffentliche Parameter, Handoffs, Runtime, Budgets und Read-only-Verträge |
| [02-verifikation](konzept/02-verifikation.md) | Zulässige Tests, konkrete Abnahmefälle, erforderliche Gates und Dokumentation |

Die Anforderungen stehen vollständig in diesen drei Dokumenten. Code, Tests und `docs/` belegen den tatsächlichen Implementierungsstand. Vor Änderungen erfasst der Implementierer HEAD und Arbeitsbaum und prüft die betroffenen Definitionen, Aufrufer und Tests. Repository-Regeln gelten.

## Umsetzung ohne Overengineering

- Vorhandene Verantwortlichkeiten, SDK-Registrierung, Runtime und Testprojekte verwenden. Änderungen erfolgen bei der Komponente, die das fachliche Verhalten besitzt.
- Neue Abstraktionen nur einführen, wenn ein konkreter Vertrag sonst nicht korrekt umgesetzt oder geprüft werden kann. Keine Erweiterungspunkte für mögliche spätere Anforderungen bauen.
- Testzugänge intern und auf den konkreten Prüfbedarf begrenzen. Keine zusätzliche öffentliche API, generische Aufrufinfrastruktur oder zweite Implementierung der Navigationslogik schaffen.
- Gemeinsame Validierung, Formatierung und Zustandslogik einmal gründlich prüfen. Zusätzliche Tooltests prüfen die tatsächlichen fachlichen Resultate und abweichenden Verträge; identische Mechanismustests werden nicht für jedes Tool wiederholt.
- Bereits korrekte Funktionen durch passende Nachweise bestätigen. Weder neu implementieren noch umstrukturieren, nur um die Arbeit neu aufzuteilen.

## Scope

### Muss

- Genau die 22 Tools mit sämtlichen in Kapitel 01 festgelegten Source-, Assembly-, Git- und Wartungsfunktionen liefern.
- MSBuild-Strukturinvalidierung, Symbol-/Ownerauflösung, Handoffs, öffentliche Parameter/Defaults/Filter/Caps und Assembly-Membersortierung korrekt umsetzen.
- Loading, Operationen, Fortsetzungen, Cancellation, Disposal, Konfigurationsreload und Read-only-Grenzen zuverlässig erfüllen.
- Harte Byte-/Tokengrenzen und automatisch passende Antwortseiten anwenden. Explizite Budgets niemals automatisch anheben. Mindestwerte und Recovery müssen ausführbar sein; vermeidbare Fehlerwiederholungen und erneute Analyse gespeicherter Ergebnisse entfallen.
- Die relevanten Verträge durch Unit-, Komponenten- und eng begrenzte Nicht-E2E-Integrationstests gemäß Kapitel 02 nachweisen.
- Laufzeitursachen zulässiger Tests gezielt untersuchen und unnötige wiederholte Fixturearbeit reduzieren. Assertions und fachliche Aussagekraft erhalten.
- Vollständige englische Toolreferenz und Setupdokumentation für Claude Desktop, Cursor und Antigravity anhand der implementierten Verträge liefern.

### Nicht

- Neue oder ausgeführte E2E-Integrationstests, reale MCP-Testclients, JSON-RPC-/Handshake-Testläufe oder als Komponententest bezeichnete Gesamtsystemprüfungen.
- Löschen oder Abschwächen vorhandener Tests, um grüne Gates zu erzeugen. Bestehende E2E-Tests bleiben erhalten und sind aus den Gates dieses Vorhabens ausgeschlossen.
- Linting, Qualitätsmetriken, Code-Smells, Diagnose-Regeln, Clone-Erkennung, automatisches Refactoring oder produktive Code-Auditingtools.
- Schreiben in analysierte Source-/Assemblyziele, Ausführen analysierter Binaries oder Ändern echter Benutzerkonfigurationen.
- Deployment, zusätzliche Produktprogramme, neue Bibliotheks-/Testprojekte, allgemeine Frameworks, statistische Benchmarks, exhaustive Parameterkreuzprodukte oder eine numerische 100%-Code-Coverage-Zusage.

## Umsetzung und Audit

Der Implementierer verwendet `gpt-6-luna/high`; der unabhängige Auditor verwendet `gpt-6.1-sol/high`. Die spätere Umsetzung folgt dem Repository-Workflow: fachliche Arbeitsschritte seriell ausführen, nach einem abgeschlossenen Milestone unabhängig prüfen und bei Findings höchstens einen Korrekturauftrag ausführen. Ein verbleibender Pflichtdefekt verhindert die Abnahme; ein ausgeschöpfter Prüfablauf erklärt ihn nicht für behoben.

Codeänderungen, Dokumentation, Builds, Tests, Restores und Git-Aktionen laufen seriell. Der Auditor prüft einen festen Stand ausschließlich lesend. Belege nennen den geprüften Commit, konkrete Tests und deren Ergebnisse.

Nur eine tatsächlich fehlende Produkt- oder Architekturentscheidung wird dem Nutzer vorgelegt. Dafür wird die abhängige Arbeit angehalten; unabhängige Arbeit kann weitergehen. Routineentscheidungen innerhalb der festgelegten Verträge trifft der Implementierer.

## Abschlussdefinition

Das Vorhaben ist fertig, wenn alle Muss-Verträge erfüllt sind, die relevante Abnahmematrix aus Kapitel 02 vollständig belegt ist, die vorgeschriebenen Nicht-E2E-Gates bestanden sind, der unabhängige Audit keine offenen Pflichtdefekte feststellt und Toolreferenz sowie Setupseiten mit dem implementierten Stand übereinstimmen.

Unzulässig sind offene P0/P1/P2-Defekte an Navigation, Read-only, Bindung, Recovery oder benötigten Funktionen. Die konkret festgelegte Assembly-Sortierung muss erfüllt sein. Sonstige P3-Befunde dürfen nur dokumentiert offen bleiben, wenn sie keinen Muss-Vertrag verletzen; Auswirkung und Abnahmebedingung werden genannt.

Ausgeschlossene Prüfungen werden als nicht durchgeführt ausgewiesen. Komponentenprüfungen belegen keinen ausgeführten Stdio-Handshake oder Clientstart. Der Abschlussbericht nennt die tatsächlichen Nachweise und verbleibenden Einschränkungen.

Der Status ist `draft`. Prüfung/Freigabe, Roadmap und Umsetzung beginnen jeweils erst durch den entsprechenden Nutzerauftrag.
