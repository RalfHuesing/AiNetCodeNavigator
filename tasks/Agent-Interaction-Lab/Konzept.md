---
status: draft
---

# Agent Interaction Lab

## Intention

Ein Agent soll mit den öffentlichen Navigationswerkzeugen konkrete Aufgaben auf definierten Repositories lösen. Wir wollen beobachten, ob er Werkzeugbeschreibungen und Parameter versteht, relevante Antworten erhält und mit Fehlern, Begrenzungen und Folgeaufrufen zurechtkommt. Die Ergebnisse sollen belegbare Verbesserungen an Verständlichkeit, Korrektheit und Tokeneffizienz ermöglichen.

Diese Untersuchung soll lokal gegen den aktuellen Entwicklungsstand funktionieren, ohne MCP-Deployment, MCP-Transport oder Austausch der MCP-Konfiguration in Codex. Sie ergänzt die Produktverifikation, beansprucht aber keinen Nachweis für den ausgelassenen Transport.

## Geprüfter Ausgangspunkt

Geprüfter Ausgangsstand: `9470613a57951cb5085c800ee15ce69c110948c0`; Arbeitsbaum zu Beginn sauber. Während der Konzeptarbeit sind unabhängige Änderungen unter anderem am Host hinzugekommen. Die folgenden Belege beschreiben den gelesenen Ausgangsstand, nicht einen Abschlussbefund zu diesen laufenden Änderungen. Sie werden vor Umsetzung neu geprüft.

- [`McpServerHost`](../../src/AiNetCodeNavigator/Mcp/McpServerHost.cs) und die fünf Klassen unter `Mcp/Tools/` sind leere Platzhalter. Öffentliche Registrierungen und deren endgültige Beschreibungen können deshalb heute noch nicht untersucht werden.
- [`McpArgumentValidationFilter`](../../src/AiNetCodeNavigator/Mcp/Validation/McpArgumentValidationFilter.cs) validiert gegen das registrierte SDK-Schema und prüft Bindbarkeit. Der Einstieg verwendet derzeit einen SDK-RequestContext; ein transportlos nutzbarer gemeinsamer Einstieg ist noch nicht belegt.
- [`McpResponseFormatter`](../../src/AiNetCodeNavigator/Mcp/Formatting/McpResponseFormatter.cs), [`McpToolResults`](../../src/AiNetCodeNavigator/Mcp/Formatting/McpToolResults.cs) und [`LongRunningToolCallStore`](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs) enthalten Antwort-, Budget- und Sitzungsbausteine. Der Formatter zählt mit `cl100k_base`.
- [`McpArgumentValidationFilterTests`](../../tests/AiNetCodeNavigator.FastTests/Mcp/McpArgumentValidationFilterTests.cs) verwenden echte SDK-Registrierungen für Fixture-Tools, aber einen MCP-Stream-Client und -Server. Sie sind keine transportlose Agentenuntersuchung der produktiven Tools.
- [`McpServerIntegrationTests`](../../tests/AiNetCodeNavigator.IntegrationTests/Mcp/McpServerIntegrationTests.cs) prüfen Typverfügbarkeit und Logging beim Prozessstart; sie belegen keinen nutzbaren produktiven Toolkatalog.

Die öffentlichen Verträge entstehen im bestehenden [Toolregistrierungs-Vorhaben](../AiNetLinter-Uebernehmen/Clusters/Cluster-09.md). Das Lab muss daran anschließen. Ein Zugriff nur auf Core-Scanner wäre für diese Intention unzureichend.

Zusätzlich wurde die während dieser Sitzung veränderte [`CommandLineOptions`](../../src/AiNetCodeNavigator/Cli/CommandLineOptions.cs) gelesen: Sie verwendet bereits `System.CommandLine`, bietet aber einen Stdio-MCP-Start mit `--config`, keinen transportlosen Toolaufruf oder JSON-/Markdown-Dumpmodus. Vorhandene JSON-Serialisierung, SDK-Schemavalidierung und Antwortformatierung sind wiederverwendbare Bausteine; das hier beschriebene Laufprotokoll ist geplante Infrastruktur. Die fremden laufenden Änderungen wurden nicht bearbeitet oder verifiziert.

## Empfohlene Richtung

Ein lokaler Entwicklungs- und Testzugang bietet den unveränderten produktiven Toolkatalog und führt Aufrufe durch dieselbe Verarbeitung wie der MCP-Host aus. Die zusätzliche Schnittstelle nimmt Werkzeugname und JSON-Argumente entgegen. Sie bildet keinen MCP-Client oder -Server nach.

Der Nutzer hat den Kommandozeilenzugang bestätigt. Verbraucher ist ein Agent hier in Codex, der diesen Zugang über lokale Befehle bedient. Konkret: Er liest die echten Toolbeschreibungen, wählt beispielsweise `find_symbol` und übergibt dessen JSON-Argumente an das lokale Testprogramm. Dieses führt den produktiven Aufrufpfad aus und liefert dessen Antwort. Codex muss dafür keine MCP-Verbindung aufbauen. Der Einstieg beschreibt ausschließlich die Bedienung des lokalen Zugangs, nicht die richtige Wahl von Navigationswerkzeugen.

Die Werkzeugdefinitionen, inklusive Beschreibungen, Parameterbeschreibungen und Schemas, stammen aus derselben Registrierung wie beim Produkt. Validierung, SDK-Bindesemantik, Defaults, Handler, Fehlerklassifikation, Formatierung und Budgets dürfen nicht unabhängig nachgebaut werden. Eine gemeinsame Verarbeitung muss gegebenenfalls im Hostbereich zugänglich gemacht werden; Core erhält keine MCP-Abhängigkeiten. SDK-Typen oder SDK-Metadaten lokal zu verwenden verletzt das Transport-Nicht-Ziel nicht.

### Eigenständiges Lab-Programm und Projektgrenze

Der Nutzer verlangt, dass die reguläre Anwendung keine Lab-Parameter und im Regelbetrieb unnötige Testfunktionen erhält. Empfehlung: genau ein zusätzliches .NET-Konsolenprojekt unter `tests/AiNetCodeNavigator.AgentLab/`, dessen Windows-Ausgabe `AiNetCodeNavigator.AgentLab.exe` heißt. Es ist Entwicklungs-/Testinfrastruktur im gleichen Repository und kein zweites Navigationsprodukt. Diese Packaging-Empfehlung ist noch nicht ausdrücklich bestätigt.

Das Lab besitzt seinen eigenen Einstieg, seine Kommandozeilenparameter, lokale Sitzungssteuerung, Target-/Aufgabenkonfiguration, Request-/Response-Dateien, Markdown-Renderer und Messdaten. Die reguläre `AiNetCodeNavigator.exe` erhält dafür weder einen Lab-Modus noch Dump-, Replay-, Aufgaben- oder Sitzungsparameter. Das Lab wird nicht als Bestandteil der regulären Produktverteilung benötigt.

Das Lab referenziert den produktiven Toolcode als Assembly und verwendet dessen Registrierung und Verarbeitung direkt im eigenen Prozess. Es ruft nicht `Program.Main` auf, startet nicht die reguläre Exe und baut keine MCP-Verbindung auf. Ein ProjectReference auf das vorhandene Anwendungsprojekt ist der empfohlene Einstieg; die vorhandenen FastTests und IntegrationTests verwenden bereits solche Referenzen. Dadurch ist zunächst kein zusätzliches gemeinsames Bibliotheksprojekt erforderlich. Die notwendige transportunabhängige Wiederverwendung von Registrierung, Validierung und Dispatch ist noch herzustellen und nachzuweisen; die Projektdateireferenz allein belegt sie nicht.

Die Abhängigkeitsrichtung ist ausschließlich Lab → produktiver Toolcode → Core. Produktionsprojekte dürfen das Lab nicht referenzieren. Gemeinsamer Toolcode und dessen Abhängigkeitsaufbau enthalten keine Dump-Pfade, Testaufgaben, Lab-Schalter oder eigenen Lab-Handler. Wo interne produktive Bausteine zugänglich gemacht werden müssen, geschieht das durch eine begrenzte Assembly-Freigabe entsprechend den bestehenden Testprojekten oder einen fachlich gemeinsamen Einstieg; Toolverträge werden dafür nicht kopiert.

Eine eigenständige Exe trennt Betrieb und Bedienung, nicht die Implementierung der zu prüfenden Tools. Eine Kopie von Toolbeschreibungen, Schemas oder Dispatch im Lab würde die Aussagekraft trotz getrennter Projekte zerstören.

| Form | Einschätzung für diesen Umfang |
|---|---|
| Eigenes .NET-Konsolenprojekt | Empfehlung: direkte Wiederverwendung der .NET-Toolverarbeitung, langlebiger Sitzungszustand und klare Trennung aller Lab-Funktionen. Kosten: ein weiteres kleines Projekt samt passenden Tests. |
| PowerShell als gesamte Infrastruktur | Geeignet für Start-/Build-Befehle; als Besitzer von .NET-Toolbindung, Sitzungszustand und Artefaktverträgen weniger passend als ein typisiertes .NET-Programm. |
| Python als gesamte Infrastruktur | Führt einen zusätzlichen Laufzeit-/Interop-Weg zu den .NET-Tools ein; für diese Aufgabe kein erkennbarer Vorteil. |

Ein PowerShell-Startwrapper und eine weitere gemeinsame Bibliothek gehören nicht zum vereinbarten ersten Umfang. Ein eigenes Lab-Projekt ist für Sitzung, Katalog und Dumps angemessen; mehrere neue Infrastrukturprojekte würden den Einstieg unnötig vergrößern.

Der Zugang benötigt einen langlebigen lokalen Sitzungsprozess über mehrere Agentenaufrufe. Registry, Handoff-Handles, laufende Operationen und Fortsetzungen gehören zu dieser Sitzung. Ein neuer Produktprozess pro Aufruf würde die zu untersuchenden Workflows verfälschen. Sitzungsstart und -ende sind Lab-Bedienung, keine zusätzlichen Navigationstools. Der konkrete lokale Kommunikationsmechanismus ist vor Konzeptfreigabe zu entscheiden.

Der Agent erhält die produktiven Ergebnisinhalte und Statusfelder ohne erklärende Lab-Zusätze oder nachträgliche Kürzung. Rohresultat und tatsächlich sichtbare Darstellung werden getrennt aufgezeichnet; insbesondere darf strukturierter Inhalt nicht unbemerkt verschwinden oder als zusätzlicher Text doppelt gezählt werden. Lab-Messdaten und technische Logs gelangen nicht in die Navigationsergebnisse. Ein eigener Testzugang darf das für MCP reservierte stdout des Produktstarts nicht verändern.

### Grenze der Aussagekraft

Bei lokalen Befehlen wählt der Agent ein Werkzeug aus einem gelesenen Katalog und formuliert einen Aufruf. Das prüft Aufgabenlösung und Verständlichkeit, bildet aber native Funktionsauswahl durch das Modell sowie die Darstellung eines MCP-Clients nur näherungsweise ab. Bedienfehler des lokalen Zugangs werden deshalb getrennt von Produktproblemen erfasst.

Ein eigener Modell-Runner mit API-Anbindung gehört nicht zum Einstieg. Für die gewünschte Trennung der Agentenrollen ist er nicht erforderlich.

## Vereinbarter Ablauf und erster Umfang

Die Nutzerklärung legt vier getrennte Agentenrollen fest:

1. **Aufgabenagent:** formuliert ein konkretes Navigationsziel, beispielsweise „Suche im Code nach XYZ“, und ordnet es einem definierten Repository zu.
2. **Testagent:** erhält Ziel und öffentliche Tooldefinitionen, wählt selbst Werkzeuge und Parameter und führt die lokalen Aufrufe aus. Aufrufe und Antworten werden vollständig in Markdown-Dateien festgehalten.
3. **Analyseagent:** untersucht anschließend separat die Dumps und prüft Korrektheit, Verständlichkeit, Rauschen, Nutzwert und Aufwand mit unabhängigen Belegen.
4. **Umsetzungsagent:** behebt bestätigte Findings in einem getrennten, später beauftragten Umsetzungslauf. Er verändert nichts während eines bewerteten Testlaufs.

Der erste Umfang ist eine breite Untersuchung einzelner Aufrufe über die vereinbarten Tools und Parameterfälle. Dazu kommen wenige kurze A→B-Ketten, um zu prüfen, ob ein Agent Ergebnisse aus A tatsächlich in B verwenden kann. Beispiel: Symbol finden und dessen ausgegebene `handoffId` unverändert für den Body-Aufruf verwenden. Die Abdeckung wird gegen den tatsächlichen Katalog dokumentiert; nicht untersuchte Tools werden sichtbar benannt.

Eine vollständige autonome Verbesserungsrunde und lange Aufgabenketten gehören nicht zum ersten Umfang. Die Untersuchungsmethode unten beschreibt die Beweissicherung; spätere Wiederholungen sind gezielte neue Läufe, kein jetzt einzuführender Schleifenautomat.

## JSON-Infrastruktur und Markdown-Dumps

Die Infrastruktur erzeugt das Laufprotokoll automatisch. JSON ist die maschinenlesbare Primärquelle; Markdown ist die daraus erzeugte lesbare Darstellung. Der Testagent soll weder Antworten abschreiben noch sich eigene Logging-Skripte oder Tests für jeden Fall ausdenken müssen.

Ein Lauf enthält folgende Artefakte:

| Artefakt | Inhalt und Herkunft |
|---|---|
| `run.json` | Lauf-ID, Zielzuordnung, Produkt-/Targetstand, Modellangaben und Grenzen; durch den Zugang erfasst, unbekannte Werte ausdrücklich markiert. |
| `tools.json` | Unveränderter Katalog aus den produktiven Registrierungen, einschließlich Beschreibungen und Schemas; pro Lauf exportiert. |
| `task.json` | Ziel und erlaubter Target vom Aufgabenagenten in einem validierten Lab-Format; keine vorgegebenen Aufrufsequenzen. |
| `calls/<call-id>/request.json` | Tatsächlich gewählter Toolname und JSON-Argumente, unverändert mit Herkunft und Sitzungsbezug aufgezeichnet. |
| `calls/<call-id>/response.json` | Originales produktives Ergebnis; Prozessfehler, Abbruch oder ausbleibendes Resultat werden separat als Lab-Ereignis gespeichert. |
| `calls/<call-id>/call.md` | Automatisch gerenderte Beschreibung des verwendeten Tools, tatsächliche Argumente und vollständige Antwort beziehungsweise dokumentiertes Ausbleiben. |
| `report.md` | Nachgelagerte Analyse mit Referenzen auf die Call-IDs; vom Analyseagenten erstellt. |

Das Testprogramm nimmt eine Argumentdatei entgegen und verwendet eine gemeinsame JSON-Serialisierung sowie ein versioniertes Lab-Format für Metadaten. Dadurch entfällt die fehleranfällige Verschachtelung von JSON in Shell-Argumenten. Wo der Agent eine Datei bereitstellt, validiert und archiviert die Infrastruktur deren tatsächlichen Inhalt. Sie erfindet keine Parameterwerte, injiziert keine Defaults und korrigiert keine ungültigen Toolargumente vor dem produktiven Validator. Auch fehlgeschlagene Aufrufe werden erfasst.

Vor einem Aufruf wird der Request festgehalten; danach werden Resultat oder Fehler dem gleichen Call zugeordnet. Eine unvollständige Spur bleibt sichtbar. Die Markdown-Generierung übernimmt keine freie Zusammenfassung: Alle produktiven Inhalte bleiben erhalten, Metadaten sind gesondert gekennzeichnet. Derselbe gespeicherte JSON-Dump erzeugt dieselbe Markdown-Darstellung bei gleicher Rendererversion. Die Anzeige für den Testagenten und der spätere Dump werden getrennt erfasst, damit Protokollierung dessen Wahrnehmung nicht erweitert.

„Deterministisch“ bedeutet hier verlässliche Formate, eindeutige Zuordnung, unveränderte Aufzeichnung und wiederholbare Verarbeitung festgehaltener Eingaben gegen einen festen Stand. Die Toolwahl des Agenten bleibt frei. Zeitangaben, opaque IDs, Sitzungszustand und asynchrone Abläufe verbieten eine pauschale Zusage bitweise identischer neuer Läufe. Für A→B übernimmt der Agent den Wert aus der aktuellen Antwort von A; ein früherer Token wird nicht blind erneut verwendet.

Aufgaben und ausgewählte Reproduktionen bleiben erhalten, statt pro Untersuchung neue Testskripte zu schreiben. Eine spätere gezielte Reproduktion kann identische unabhängige Argumente erneut verwenden; zustandsabhängige Folgen brauchen neue Vorgängerantworten. Ein allgemeiner Replay-Interpreter oder eine eigene Skriptsprache gehört nicht zum ersten Umfang.

## Untersuchungsmethode

### Repositories und Aufgaben

Eine versionierte Konfiguration definiert erlaubte Ziel-Repositories beziehungsweise Solutions und Szenarien. Externe Repositorypfade werden lokal zugeordnet; sie müssen nicht auf jedem Rechner identisch sein. Ein Lauf protokolliert die tatsächliche absolute Zieladresse, Repository-Commit und Änderungen beziehungsweise relevante Inhaltsfingerprints. Ein veränderliches oder nicht ladbares Ziel macht den Vergleich ungültig beziehungsweise den Lauf blockiert, nicht zu einem Tool-Verständlichkeitsfehler.

Ein Szenario enthält ein fachliches Ziel, den erlaubten Target, überprüfbare Erfolgskriterien sowie Grenzen für Aufrufe und Laufzeit. Es enthält für den Testagenten weder die Lösung noch eine Liste auszuführender Werkzeuge. Gezielte Recovery-Szenarien dürfen eine kontrollierte Ausgangslage vorgeben; diese Vorgabe wird als solche dokumentiert.

Der Aufgabenagent erzeugt die Aufgaben; der Analyseagent prüft deren Lösbarkeit und unabhängige Erfolgskriterien, ohne diese dem Testagenten zu verraten. Neue Aufgaben werden anschließend festgehalten. Auch neu erzeugte Aufgaben müssen später vergleichbar bleiben. Aufgaben werden nicht allein deshalb als Tooldefekt gewertet, weil ihr Ziel im Repository gar nicht existiert.

Der Testagent startet mit frischem Kontext: Aufgabe, Target, Lab-Bedienung und produktiver Toolkatalog. Er bekommt keine internen Implementierungshinweise, früheren Findings, Lösungsskizzen oder Referenzantworten. Zur Navigation verwendet er ausschließlich den Testzugang; direkte Datei-, Shell- oder andere MCP-Navigation ist für diesen Lauf unzulässig. Das ist in Codex zunächst eine Rollenregel, keine behauptete technische Sandbox. Zugriffe außerhalb des Zugangs werden als ungültiger Lauf markiert; unbeobachtete Zugriffe verhindern einen belastbaren Abschluss.

Der Auswerter darf den Code unabhängig lesen und mit Referenzfakten vergleichen. Er erhält Aufgabe, Verlauf und Abschlussantwort. Eine überzeugend klingende Agentenantwort oder ein zweiter Agent mit ähnlicher Meinung ist kein Korrektheitsbeleg.

Beispielaufgabe: „Finde die Verarbeitung eines ungültigen Arguments. Zeige, wodurch der eigentliche Handler nicht ausgeführt wird, und nenne Tests, die dieses Verhalten belegen.“ Die internen Anker für die Bewertung werden dem Testagenten nicht gezeigt. Das Beispiel ist eine Aufgabenform, kein Nachweis, dass die heutigen Platzhalter den Workflow ermöglichen.

### Klassische Tests und Agentenläufe

Deterministische Tests bleiben für maschinenprüfbare Verträge zuständig: Schemas, gültige und ungültige Argumente, Handoff-Ketten, Status, Budgetgrenzen und fachliche Ergebnisse. Ein reproduzierbares Fehlverhalten aus einem Agentenlauf erhält vor einer späteren Korrektur einen passenden Regressionstest gemäß Repository-Regeln.

Agentenläufe prüfen zusätzlich Toolwahl, Verständnis, Aufgabenlösung, Wiederherstellung nach Fehlern und Nutzen der Antwort. Erwartet wird ein belegtes Ergebnis, keine identische Aufrufreihenfolge oder wortgleiche Markdown-Antwort. Ein stabiler Kern von Aufgaben bleibt für Vorher-/Nachher-Vergleiche bestehen; neue Aufgaben ergänzen die Untersuchung, statt den Kern nach jedem Finding umzuschreiben.

Aufrufverläufe können ohne Agentenentscheidung erneut ausgeführt werden, um Fehler und Vertragsänderungen nachzustellen. Das ersetzt keinen frischen Agentenlauf nach geänderten Beschreibungen oder Antworten: Der Agent könnte jetzt ganz andere Werkzeuge wählen. Handoff- und Fortsetzungstokens sind sitzungsgebunden; bei Wiederholung müssen sie aus neuen Vorgängerantworten stammen. Ein Rohverlauf ist daher nicht automatisch ein direkt ausführbarer Regressionstest.

### Laufartefakte und Bewertung

Jeder Lauf erhält einen eigenen Ordner unter dem bereits ignorierten `temp/agent-interaction-lab/`. Er enthält eine maschinenlesbare Laufbeschreibung und Ereignisspur sowie einen lesbaren Markdown-Bericht. Rohartefakte bleiben erhalten; bestätigte Findings mit ausreichendem Reproduktionsbeleg werden im Taskverzeichnis versioniert, damit sie nicht allein von temporären Dateien abhängen.

Aufgezeichnet werden mindestens Toolkatalog, Produktstand, Targetstand, Aufgabenfassung, verwendetes Agentenmodell und bekannte Einstellungen, Call- und Parent-IDs, Argumente, originale Ergebnisse, sichtbare Ergebnisse, Zeitmessungen, Aufrufabbrüche und finale Agentenantwort. Es werden keine internen Gedankengänge verlangt. Nicht verfügbare Modell- oder Nutzungsdaten werden als unbekannt ausgewiesen.

Bewertet wird mit konkreten Belegen:

| Gesichtspunkt | Beobachtung |
|---|---|
| Korrektheit | Erfolgskriterien mit unabhängigen Code-/Testbelegen erfüllt; keine erfundenen Symbole oder Schlussfolgerungen. |
| Verständlichkeit | Verwechselte Werkzeuge, falsch verstandene Parameter, benötigte Korrekturaufrufe und fehlende Entscheidungshinweise. |
| Nutzbarkeit | Folgeaufrufe aus ausgegebenen IDs möglich; Aufgabenfortschritt und Fehlerbehebung ohne interne Hinweise. |
| Rauschen | Konkrete unnötige oder wiederholte Antwortteile gegenüber der Aufgabe; fehlende relevante Unterscheidungen separat erfasst. |
| Aufwand | Aufrufe, vermeidbare Wiederholungen, Katalog-/Argument-/Antwortumfang und Zeit bis zum belegten Ergebnis. |
| Stabilität | Beobachtete Fehler und erfolgreiche Wiederholung bei festgehaltenen Bedingungen; ein einzelner Lauf belegt keine allgemeine Zuverlässigkeit. |

Bytes sind exakt messbar. Vergleichbare Tokenzahlen verwenden zunächst denselben benannten `cl100k_base`-Tokenizer wie der Produktformatter und gelten nicht als exakte Abrechnung des verwendeten Agentenmodells. Produkttext, strukturierter Inhalt, tatsächlich sichtbare Darstellung und Lab-/Shell-Aufwand werden getrennt angegeben. Katalogumfang gehört zu den Kosten; reale Kontextwiederholungen werden nur gezählt, wenn beobachtbar. Unbekannter Gesamtverbrauch wird nicht aus Antworttokens erfunden.

Kürzere Antworten gelten nur bei erhaltener Korrektheit und Aufgabenlösung als Verbesserung. Ein pauschaler „Noise Score“ oder die bloße Einhaltung eines Tokenlimits genügt nicht. Fehlversuche zählen auch dann, wenn der Agent am Ende erfolgreich ist.

Ein Finding enthält Priorität, Kategorie, Aufgabe und Produkt-/Targetstand, die verursachenden Call-IDs mit Argumenten und Antwortausschnitt, Auswirkung auf die Aufgabe sowie eine überprüfbare Abnahmebedingung. Lab-Probleme, Produktprobleme, Agentenfehler und nicht entscheidbare Beobachtungen bleiben unterscheidbar.

## Scope

Die folgenden Grenzen beschreiben den Entwurf für den bestätigten Kommandozeilenzugang. Das Gesamtkonzept ist noch nicht freigegeben.

### Muss

- Transportloser lokaler Testzugang zum gemeinsamen produktiven Katalog und Aufrufpfad; keine zweite Definition der Navigations-API.
- Getrennter Lab-Einstieg: gemäß Empfehlung ein eigenes .NET-Konsolenprojekt unter `tests/AiNetCodeNavigator.AgentLab/`. Alle Lab-Parameter, Dump-Funktionen und Testkonfigurationen gehören ausschließlich dorthin; die reguläre Anwendung erhält keinen Lab-Modus.
- Zusammenhängende Sitzung mit echter produktiver Zustandsverwaltung, explizitem Ende und begrenzter Laufdauer.
- Konfigurierte Source-Solution-Ziele, einschließlich des eigenen Repositories und mindestens eines vom Nutzer benannten externen C#-Repositories; Navigation ausschließlich lesend.
- Vier getrennte Agentenrollen für Aufgabenerstellung, Test, nachgelagerte Analyse und spätere Behebung; verdeckte Referenzkriterien und begrenzte Laufbudgets.
- Breite Einzelaufrufe zu Toolwahl, Parametern, Erfolgsausgaben und relevanten Fehler-/Budgetfällen sowie wenige kurze Symbol-Folgeaufrufe. Der konkrete erste Toolumfang und Aufgabenbestand werden vor Freigabe festgelegt.
- Belegbare Laufartefakte, überprüfte Findings und Vergleiche auf festgehaltenen Produkt- und Targetständen.
- Automatisch erzeugtes JSON-Laufprotokoll mit produktivem Katalogexport, validierten Lab-Metadaten, archivierten tatsächlichen Requests und originalen Ergebnissen; vollständige Markdown-Dumps werden daraus deterministisch gerendert.
- Deterministische Tests für Zugang und geteilte Verträge; frische Agentenläufe für die qualitative Wirkung einer Korrektur.

### Nicht

- MCP-Deployment, MCP-Client/-Server, Handshake oder Protokolltransport innerhalb der Lab-Agentenläufe.
- Ersatz der produktiven MCP-Integrationstests und der bestehenden Ende-zu-Ende-Abnahme.
- Neue Navigationssemantik, Linting, Refactoring oder Schreiben in analysierte Repositories.
- Ein selbstständiges alternatives CLI-Produkt; der Zugang ist Entwicklungs-/Testinfrastruktur.
- Lab-Parameter, Dump-/Aufgabenlogik oder Abhängigkeiten auf das Lab in der regulären Anwendung oder im Core.
- Eine kopierte Toolimplementierung, ein zusätzlicher Python-Laufzeitweg, ein PowerShell-Startwrapper oder mehrere neue Infrastrukturprojekte im ersten Umfang.
- Harte Sandbox-Garantie durch bloße Agentenanweisungen.
- Assembly- und Wartungsszenarien im vorgeschlagenen ersten Source-Umfang.
- Separater Modellanbieter-Runner in der vorgeschlagenen Codex-Variante.
- Ein allgemeiner Replay-Interpreter oder eine eigene Skriptsprache für Szenarien.
- Produktänderungen innerhalb eines bewerteten Laufs. Findings werden separat analysiert und von einem anderen Agenten in einem getrennten Auftrag behoben.
- Vollständige automatische Verbesserungsrunden und lange mehrstufige Navigationsaufgaben im ersten Umfang.
- Unbegrenzte Verbesserungsrunden, absolute Fehlerfreiheitsversprechen oder eine allgemeine Modell-Benchmark-Plattform.

## Verifikation des Vorhabens

Der Zugang ist erst belastbar, wenn die beabsichtigte gemeinsame Verarbeitung im Code belegt ist. Automatisierte Vergleiche mit dem echten SDK-/MCP-Testpfad müssen den freigegebenen Toolumfang abdecken: Katalog, Defaults/Bindung, gültige und ungültige Argumente, Erfolg, Fehler, Budget/Recovery und sitzungsabhängige Folgeaufrufe. Diese Paritätsprüfungen dürfen als separate Produkt-/Integrationstests MCP verwenden; die Agentenläufe selbst verwenden es nicht. Flüchtige IDs werden über ihre Bedeutung und Folgeaufrufe verglichen, nicht über gleiche Tokenstrings.

Die Projektgrenze wird am Referenzgraphen und an den Einstiegen geprüft: Das Lab verwendet produktiven Code, Produktionsprojekte referenzieren das Lab nicht. Die reguläre CLI bekommt keine Lab-Befehle oder Lab-Optionen; ihre Startlogik registriert weder Dump-Writer noch Lab-Konfiguration. Die reguläre Produktverteilung benötigt keine Lab-Artefakte. Wiederverwendung wird durch gemeinsamen Code und passende Vertragsprüfungen nachgewiesen, nicht durch ähnliche Ausgaben zweier unabhängiger Implementierungen.

Zur Lab-Abnahme gehört ein aufgezeichneter frischer Agentenlauf auf jedem vereinbarten Repository, eine unabhängig bewertete Aufgabenlösung und ein nachvollziehbarer Fehler-/Recovery-Verlauf. Ein bewusst ausgelöster Fehler muss als solcher erkennbar bleiben. Zugriff außerhalb der erlaubten Navigation, fehlende Ereignisse oder nicht eingefrorene Vergleichsstände werden im Bericht als ungültig beziehungsweise nicht vergleichbar ausgewiesen.

Der erste Bericht liefert Beobachtungen mit Belegen und sichtbarer Toolabdeckung. Er muss keine statistische Stabilität oder fertige automatische Verbesserungsschleife nachweisen. Wird später eine Verbesserung behauptet, braucht sie dieselben Aufgaben, Targets und bekannten Agenteneinstellungen vor und nach der Änderung sowie erhaltene Korrektheit. Ein einzelner erfolgreicher Lauf ist eine Beobachtung, kein Stabilitätsnachweis.

Implementierungsabnahme verwendet die passenden offiziellen Build-/Testskripte. Für diesen Konzeptentwurf sind ausschließlich Diff-Review und `git diff --check` erforderlich; Produktverhalten wurde hier nicht geändert.

## Arbeitsgedächtnis (nur Draft)

- Nutzer hat den Kommandozeilenmodus bestätigt: vorhandene Codex-Agenten bedienen ein lokales Testprogramm; keine zusätzliche Modell-API.
- Nutzer wünscht Infrastruktur für die JSON-Dateien und deterministische Verarbeitung. Empfehlung konkretisiert: automatisch erzeugtes JSON-Laufprotokoll, daraus vollständige Markdown-Dumps; freie Agentenwahl und flüchtige IDs sind davon getrennt.
- Nutzer verlangt klare Trennung vom Regelbetrieb: keine zusätzlichen Lab-Parameter oder unnötige Lab-Funktionen in der primären Anwendung. Empfehlung: genau ein eigenes .NET-Konsolenprojekt unter `tests/AiNetCodeNavigator.AgentLab/`, das produktiven Toolcode referenziert. Noch keine ausdrückliche Bestätigung der Projektwahl; die Trennung selbst ist verbindlich.
- Nutzer hat festgelegt: Aufgabenagent → Testagent → vollständige Markdown-Dumps → separater Analyseagent → anderer Umsetzungsagent. Zunächst grob flächig untersuchen; kurze A→B-Ketten ergänzen, keine vollständige automatische Schleife.
- Vor Freigabe festzulegen: lokaler Sitzungs-/Kommunikationsmechanismus und dessen beobachtbarer Zugriffsumfang; keine transportlose Parität behaupten, bevor die gemeinsame Verarbeitung belegbar ist.
- Vor Freigabe festzulegen: erstes externes Repository, konkrete Szenarien und Toolumfang sowie Call-/Zeitgrenzen. Eine statistische Wiederholungsstudie gehört nicht zum ersten Umfang.
- Fehlende produktive Registrierungen sind eine Abhängigkeit des Labs. Dieser Entwurf startet weder deren Umsetzung noch den nächsten Agent-Workflow-Schritt.
- Das Taskverzeichnis wurde gemäß ausdrücklichem Nutzerauftrag ausgewählt: `tasks/Agent-Interaction-Lab/`.
