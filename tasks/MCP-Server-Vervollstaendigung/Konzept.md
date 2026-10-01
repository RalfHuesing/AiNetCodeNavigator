---
status: draft
---

# Funktionsfähiger MCP-Server für C#-Navigation

## Intention

AiNetCodeNavigator ermöglicht AI-Agenten zuverlässige, ausschließlich lesende C#-Navigation durch Source-Solutions und verwaltete Assemblies. Das Ergebnis ist ein funktionsfähiger MCP-Stdio-Server mit genau 20 Navigations- und zwei Wartungstools, korrekten Folgeaufrufen, verständlichen öffentlichen Verträgen und begrenzten Antwortgrößen.

Oberstes Ziel ist, den funktionsfähigen Server fertigzustellen. Dieses Vorhaben vervollständigt benötigte Funktionen und behebt belegte Fehler. Korrekte Komponenten werden wiederverwendet; Verbesserungen ohne erforderlichen Beitrag zur Abnahme erweitern den Auftrag nicht.

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
- Abnahme und Dokumentation knapp halten: eine Nachweiszeile je Tool, gemeinsame Mechanismusnachweise einmal, eine gemeinsame Toolreferenz und eine gemeinsame Setupseite. Kein zusätzlicher Schemaexport oder Dokumentationsgenerator.

## Scope

### Muss

- Genau die 22 Tools mit sämtlichen in Kapitel 01 festgelegten Source-, Assembly-, Git- und Wartungsfunktionen liefern.
- MSBuild-Strukturinvalidierung, Symbol-/Ownerauflösung, Handoffs, öffentliche Parameter/Defaults/Filter/Caps und Assembly-Membersortierung korrekt umsetzen.
- Loading, Operationen, Fortsetzungen, Cancellation, Disposal, Konfigurationsreload und Read-only-Grenzen zuverlässig erfüllen.
- Harte Byte-/Tokengrenzen und automatisch passende Antwortseiten anwenden. Explizite Budgets niemals automatisch anheben. Mindestwerte und Recovery müssen ausführbar sein; vermeidbare Fehlerwiederholungen und erneute Analyse gespeicherter Ergebnisse entfallen.
- Die relevanten Verträge durch Unit-, Komponenten- und eng begrenzte Nicht-E2E-Integrationstests gemäß Kapitel 02 nachweisen.
- Die langen Integrationstestläufe vor Git-Commits gemäß Kapitel 02 anpassen: betroffene Tests gezielt ausführen, die abschließende Suite nicht mehrfach wiederholen und unnötige Fixturearbeit entfernen. Für die Source-Git-Fälle entfallen unbenutzte Assemblybuilds und unnötige Restores. Assertions und fachliche Aussagekraft erhalten; E2E-Fälle nicht ausführen.
- Eine kompakte englische Toolreferenz und eine Setupseite mit Abschnitten für Claude Desktop, Cursor und Antigravity anhand der implementierten Verträge liefern.

### Nicht

- Neue oder ausgeführte E2E-Integrationstests, reale MCP-Testclients, JSON-RPC-/Handshake-Testläufe oder als Komponententest bezeichnete Gesamtsystemprüfungen.
- Löschen oder Abschwächen vorhandener Tests, um grüne Gates zu erzeugen. Bestehende E2E-Tests bleiben erhalten und sind aus den Gates dieses Vorhabens ausgeschlossen.
- Produktfeatures für Linting, Qualitätsmetriken, Code-Smells, Diagnose-Regeln, Clone-Erkennung, automatisches Refactoring oder Code-Auditing.
- Navigator-Schreibzugriffe auf analysierte Source-/Assemblyziele, Ausführen analysierter Binaries oder Ändern echter Benutzerkonfigurationen.
- Deployment, zusätzliche Produktprogramme, neue Bibliotheks-/Testprojekte, allgemeine Frameworks, statistische Benchmarks, exhaustive Parameterkreuzprodukte oder eine numerische 100%-Code-Coverage-Zusage.

## Umsetzung und Audit

Der Implementierer verwendet `gpt-6-luna/high`; der unabhängige Auditor verwendet `gpt-6.1-sol/high`. Die spätere Umsetzung folgt dem Repository-Workflow: fachliche Arbeitsschritte seriell ausführen, nach einem abgeschlossenen Milestone unabhängig prüfen und bei Findings höchstens einen Korrekturauftrag ausführen. Ein verbleibender Pflichtdefekt verhindert die Abnahme; ein ausgeschöpfter Prüfablauf erklärt ihn nicht für behoben.

Codeänderungen, Dokumentation, Builds, Tests, Restores und Git-Aktionen laufen seriell. Der Auditor prüft einen festen Stand ausschließlich lesend. Belege nennen den geprüften Commit, konkrete Tests und deren Ergebnisse.

### Begrenzte Refactorings während der Umsetzung

Notwendige Änderungen innerhalb eines fachlichen Auftrags dürfen Code verbessern und halten die festgelegten Verantwortlichkeiten und Soll-Verträge ein. Bei unabhängigem Codeverstoß meldet der Implementierer Fundstelle, verletzte Vorgabe und Auswirkung, statt eigenständig ein Refactoring zu beginnen. Unabhängige beauftragte Arbeit geht weiter.

Der Audit-Agent legt zusätzliche strukturelle Refactorings vor Umsetzung fest, auch im gerade bearbeiteten Pfad. Lokale Änderungen zur beauftragten Funktionskorrektur benötigen keinen separaten Refactoringauftrag. Zulässiger Anlass ist ein verbindlicher Regelverstoß im Änderungspfad oder eine konkrete Behinderung eines Muss-Vertrags, keine Stilpräferenz oder Bereinigung anderer Bereiche. Der Auditor definiert:

- betroffene Komponente und erlaubten Änderungsbereich;
- belegten Verstoß, erforderliche Änderung und unverändert zu erhaltende Verträge;
- konkrete Abschlussprüfung und erforderliche betroffene Tests.

Diese einmalige Festlegung darf bei notwendiger Meldung vor Abschluss des Arbeitspunkts erfolgen. Der Auftrag gehört in den bestehenden Arbeitspunkt oder den einen Korrekturauftrag des Milestones; kein zusätzlicher Milestone oder höheres Korrekturlimit. Nach einmaliger Umsetzung prüft die Abschlusskontrolle die vereinbarten Kriterien und Regressionen, eröffnet aber keine neue Stil-/Umstrukturierungsrunde. Verbleibende Pflichtdefekte werden als Blockade mit Fundstelle gemeldet, ohne automatische Wiederholung oder stillschweigende Abnahme. Nicht erforderliche Beobachtungen stehen knapp in vorhandenen Abschlussnotizen, ohne neue Aufgaben/Berichtsdateien.

Nur eine tatsächlich fehlende Produkt- oder Architekturentscheidung wird dem Nutzer vorgelegt. Dafür wird die abhängige Arbeit angehalten; unabhängige Arbeit kann weitergehen. Routineentscheidungen innerhalb der festgelegten Verträge trifft der Implementierer.

## Abschlussdefinition

Das Vorhaben ist fertig, wenn alle Muss-Verträge erfüllt sind, die kompakte Abnahmematrix aus Kapitel 02 vollständig belegt ist, die vorgeschriebenen Nicht-E2E-Gates bestanden sind, der unabhängige Audit keine offenen Pflichtdefekte feststellt und Toolreferenz sowie Setupseite mit dem implementierten Stand übereinstimmen. Dann wird abgeschlossen; weitere Optimierung oder umfassende Bereinigung ist kein Abschlusskriterium.

Unzulässig sind offene P0/P1/P2-Defekte an Navigation, Read-only, Bindung, Recovery oder benötigten Funktionen. Die konkret festgelegte Assembly-Sortierung muss erfüllt sein. Sonstige P3-Befunde dürfen nur dokumentiert offen bleiben, wenn sie keinen Muss-Vertrag verletzen; Auswirkung und Abnahmebedingung werden genannt.

Ausgeschlossene Prüfungen werden als nicht durchgeführt ausgewiesen. Komponentenprüfungen belegen keinen ausgeführten Stdio-Handshake oder Clientstart. Der Abschlussbericht nennt die tatsächlichen Nachweise und verbleibenden Einschränkungen.

Der Status ist `draft`. Prüfung/Freigabe, Roadmap und Umsetzung beginnen jeweils erst durch den entsprechenden Nutzerauftrag.
