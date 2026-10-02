# Roadmap: Verlässliche und tokeneffiziente MCP-Navigation

Verbindliche Spezifikation: [freigegebenes Konzept](Konzept.md), `status: ready`, Freigabe-Commit `0a3f989`. Diese Roadmap ordnet den freigegebenen Scope; sie entscheidet keine weiteren Funktionen oder Produktverträge. Schritt 3 erstellt ausschließlich Planung. Die Umsetzung beginnt erst mit dem gesonderten Nutzeraufruf von Schritt 4.

## Ausführung und Nachweise

Die Aufgaben stehen vollständig in dieser Datei. Jeder nummerierte Punkt ist ein eigener, begrenzter Arbeitsauftrag; zusätzliche Leaf-Dateien sind nicht erforderlich. In Schritt 4 werden die Punkte in der angegebenen Reihenfolge bearbeitet. Resume ist der erste offene ausführbare Punkt. Die Milestone-Checkbox wird erst geschlossen, wenn ihre Aufgaben einschließlich Audit abgeschlossen sind.

Für jeden Codepunkt gelten dieselben Anforderungen: betroffenen Ist-Stand und Verbraucher lesen, bei einem reproduzierbaren Defekt zuerst einen fehlgeschlagenen Nachweis herstellen, den vollständigen betroffenen Vertrag implementieren, gezielt verifizieren, aktuelle Dokumentation im selben Slice aktualisieren und ausschließlich die eigenen Änderungen committen. Anforderungen und Grenzwerte werden nicht zugunsten grüner Checks abgeschwächt. Der Abschlussnachweis wird direkt beim jeweiligen Punkt ergänzt: Commit, ausgeführte Checks/Ergebnisse, belegte Analysegrenzen und tatsächlich unerfüllte Nachweise. Keine separate Fortschritts-, Audit- oder Schuldenverwaltung.

Die [Verifikationsregeln](../../.agents/rules/04-verification.mdc) und [Build-/Testanleitung](../../docs/development/build-and-tests.md) bestimmen die späteren Gates. Code-Slices verwenden das offizielle Buildskript sowie die engste betroffene Fast-/Integration-Auswahl; relevante Extended-Fälle werden gezielt ausgewählt. Das vollständige eligible Routine-Gate liegt in M3-T1. Reine Dokumentations-Slices brauchen Diffprüfung und `git diff --check`. In dieser Roadmap-Erstellung werden weder Builds noch Tests ausgeführt.

Alle Aufgaben übernehmen die [Nicht-Ziele des Konzepts](Konzept.md#nicht): read-only Targets, kein administrativer Ersatzkatalog, keine Ausführung analysierter Bibliotheken, keine Erweiterung zur Compilerdiagnostik-/Refactoringfunktion, keine Legacy-Aliase und keine neuen universellen Toolketten. Core bleibt frei von MCP-Transport und Tokenisierung. Analysierte Anwendungsdateien werden nicht verändert. Technische Tool-/Komponentennamen sind zulässig; Produkt-, Hersteller- und Kundennamen sowie reale Anwendungspfade gehören weder in diese Planung noch in Commit-Nachrichten. Lokale Messdetails bleiben in den bestehenden, nicht versionierten Auditartefakten; versionierte Nachweise verwenden neutrale Aliase.

Ein Audit arbeitet nur lesend und bewertet den Milestone-Diff gegen das Konzept und seine Abnahme. In Schritt 4 ist danach höchstens ein gezielter Korrektur-Leaf je Milestone vorgesehen; offene Abweichungen bleiben sichtbar und erlauben keinen unbelegten Abschluss.

## Reihenfolge

- [ ] **M1 — Vergleichsgrundlage und konsistente Analyse**
- [ ] **M2 — Nachladbare, kompakte Navigation und gemeinsamer Kontext**
- [ ] **M3 — Gesamtabnahme und begrenzter Praxisvergleich**

## M1 — Vergleichsgrundlage und konsistente Analyse

### M1-T1 — Ausgangsmessung festschreiben

- [x] **M1-T1 abschließen**

**Intention:** Die spätere Umstellung mit einer nachvollziehbaren Ausgangsaufnahme vergleichen, bevor Servercode oder Verträge verändert werden.

**Scope:** Die vorhandenen Belege für Repository R sowie Assembly A/B lesen und die sechs Szenarien M1–M6 aus dem [begrenzten Messumfang](Konzept.md#begrenzter-messumfang) festschreiben. Queries, deklarative Auswahl, erforderliche Information, Target-/Referenzfingerprints und tatsächliche Prozess-/Buildzuordnung erfassen. Passende vorhandene vollständige Messungen übernehmen; die nötige Ausgangsmessung einmal read-only aufnehmen. Die Baseline-Artefakte über die späteren Codeänderungen hinaus verfügbar halten. Handles je Prozess neu entdecken. Pro festgelegter Variante ein kalter und zwei warme Durchläufe in isolierter Zuordnung, maximal zwei Varianten je Szenario und 36 vollständige Workflows insgesamt, höchstens 60 Minuten.

**Nicht:** Kein neuer Zielkorpus, kein Benchmarkframework, keine Serveränderung zur Baseline, keine Beendigung bestehender Clientprozesse und keine Gleichsetzung des abweichenden Audit-Deployments mit dem lokalen Quellstand. Die Messung ist keine zusätzliche Konzeptfreigabe.

**Abnahme:** Für jedes Szenario sind Ausgangsargumente, Ergebnismenge, Build-/Targetzuordnung und vollständig rekonstruierte Antwortkosten vorhanden oder der konkrete fehlende Nachweis innerhalb des Zeitlimits dokumentiert. Messwerte enthalten Bytes, Antworttokens, Requests, Polls, Seiten und Laufzeiten; unzugängliche Clientkosten sind ausdrücklich nicht gemessen. Targetdateien bleiben unverändert. Unverfügbarkeit wird nicht als bestandene Baseline behauptet und eröffnet keine unbegrenzte Wartephase. Vor dem nächsten Codepunkt liegen die vorhandenen Baselinebelege gesichert vor.

**Abschlussnachweis (2026-10-02, anonymisierte Targets A/B/R):** Die sechs Konzept-Szenarien und ihre Varianten sind festgeschrieben. Auf sauberem HEAD `ac149ad5812dcd00ff35fa698006159bd9859ef2` bestand `pwsh -File ./scripts/build.ps1` mit 0 Warnungen und 0 Fehlern. 27 dreifach ausgeführte Abfrage-Workflows sowie ein abgebrochener M5-Versuch blieben unter 36 begonnenen Workflows und unter 60 Minuten. M1 lieferte vollständig eine Antwortseite mit 10.873 UTF-8-Bytes / 3.263 `cl100k_base`-Antworttokens; die drei Antwortzeiten waren 1.783/77/69 ms. M6 rekonstruierte jeweils alle 32 eindeutigen Treffer: beim 4-KiB-Budget 18 Seiten und 57.854 Bytes / 17.590 Tokens je Workflow, beim 16-KiB-Budget 8 Seiten und 55.681 Bytes / 17.102 Tokens. M2s frische Memberauswahl entsprach nicht dem archivierten fünfzeiligen Getter; M3- und M4-Antworten sind gespeichert, aber Prozess-Discoverykosten fehlen und die rekursive Caller-Gleichwertigkeit von M4 ist nicht belegt. M5 blieb offen: Das frühere Prozess-Handle ist keinem Symbolnamen zugeordnet; eine separate breite Source-Abfrage lieferte nach 5 Minuten 44 Sekunden keine Antwort und zählt nicht als Baseline. Diese Punkte gelten nicht als bestandene Vergleichsmessung. Originalnamen, Pfade, Target-/Referenzfingerprints, Rohantworten und die sechs konkreten Queries sind in den ignorierten lokalen Belegen `temp/usage-audit/m1-baseline-results.md`, `m1-baseline-raw.json`, `m6-baseline-raw.json` und `m1-baseline.ps1` gesichert; die abschließenden Hashes bestätigten unveränderte Targets. Die Prozesszuordnung erfolgte über den offiziellen Build auf diesem HEAD, den gestarteten lokalen stdio-Prozess und seine lokalen DLL-Hashes; das Build besitzt keine eingebettete Commitkennung und der verbundene 21-Tool-Client wurde nicht als Repository-Baseline verwendet. Schema-/Requestkosten sind nicht gemessen. M2/M4-Auswahl und M5-Root-Zuordnung bleiben für M3-T2 ausdrücklich offen.

### M1-T2 — Snapshot, Referenzidentität und Owner-Leases härten

- [x] **M1-T2 abschließen**

**Intention:** Innerhalb einer Analyse einen verlässlichen Stand verwenden und dessen Owner während der Analyse schützen.

**Scope:** Muss 8 und 12 sowie den Lease-Anteil von Muss 6 umsetzen. `ResidentSolution`, gemeinsame Source-Routen und Batchaufrufe auf einen einmal bezogenen Snapshot/Identität unter Source-Lease ausrichten. Hash und Text aus denselben Dateibytes ableiten; Refresh-Lesefehler strukturiert melden. Assembly-Batches und Owner-Analysen an einen festen Root-/Referenzstand unter aktiver Lease binden. Referenzresolver und Residency mit fehlenden DLLs, falschen Identitäten, Closure-Grenzen und Sessiondruck prüfen und nachgewiesene Fehler korrigieren. Vorhandene Registry-/Handoff-/Resolvermechanismen verwenden.

**Nicht:** Keine globale atomare Dateisystemaufnahme, kein Refresh-Tool, keine dauerhaften Handles und keine pauschale Erhöhung der 128-/32-Grenzen. Keine geratenen DLL-Ersatzidentitäten.

**Abnahme:** Deterministische Änderungen zwischen Batchteilen liefern keine gemischten Symbolstände; gesperrte Dateien ergeben einen wiederholbaren Fehler statt scheinbar aktueller alter Bodies. Hash/Text stimmen überein. Falsche Referenzversionen werden nicht erfolgreich aufgelöst. Aktive Owner bleiben geleast; legitime Closure-/Residency-Grenzen ergeben explizite Grenzen oder ausführbare Recovery. Entsprechende Source-/Assembly-Fixtures und relevante bestehende Snapshot-/Lifecycle-Prüfungen bestehen.

**Abschlussnachweis (2026-10-02):** `pwsh -File ./scripts/build.ps1` bestand mit 0 Warnungen und 0 Fehlern. Die Fast-Auswahl `ResidentSolutionStalenessTests|AssemblyFingerprintAndReferenceTests|AssemblySymbolHandoffResolverTests.SessionRegistry|AssemblyNavigationScannerTests.FindSymbol_RejectsOwner|AssemblyDecompilationCacheTests` bestand mit 26/26; sie deckt atomaren Refresh samt wiederholbarem Lese-Fehler, Hash/Text-Bytes, PublicKeyToken-Identität, aktive Owner-Leases unter 32-Session-Druck, Closure-/transitive-Referenzgrenzen und Cache-Schema v5 ab. Die abschließende Integrationsauswahl `FindSymbolPatternBatch_UsesOneSnapshotAcrossPatternParts|AssemblyToolsContractTests` bestand mit 11/11. Die Regression `AssemblyBodyBatch_UsesOneSnapshotAcrossItems` wies im tatsächlich ausgeführten alten Einzelöffnungs-Handler eine vollständige gemischte Antwort nach (`First() => 101`, `Second() => 202`); sie prüft nach dem Fix sowohl rohe IDs als auch Handoffs und besteht. Die Skeleton-Regressionsprüfung wies im tatsächlich ausgeführten alten per-Handoff-Resolver nach DLL-Wechsel vor Item 2 `completeness=truncated` nach; mit Handoff-Auflösung im gepinnten Root-Scope ist die Batchantwort vollständig. Die Workspace-Refresh-/Retry-Integration bestand mit 2/2; die Extended-Auswahl `MSBuildSolutionLoader_CustomTargetsAndScratchCleanupPreserveWorkspaceSnapshot|MSBuildSolutionLoader_ColdSolutionsWithSameNamedProjectsHaveIsolatedSnapshots` bestand mit 2/2. Gesperrte Refresh-Datei und falscher PublicKeyToken wurden jeweils vor der Korrektur mit einer fehlschlagenden Regression nachgewiesen; die Transitivitätsregression wurde zusätzlich mit deaktivierter Subgraph-Prüfung als Negativkontrolle fehlschlagend ausgeführt und danach mit aktiver Prüfung bestanden. Die vollständige Routine-Suite bleibt gemäß Prüfplan bis M3-T1 ausstehend.

### M1-T3 — Statische Beziehungen korrekt belegen

- [ ] **M1-T3 abschließen**

**Intention:** Rekursion und unklare Bindungen so ausgeben, dass Agenten daraus keine falschen Abwesenheits- oder Aufrufbeweise ableiten.

**Scope:** Muss 1 in den vorhandenen Relationship-Scannern und betroffenen Projektionen umsetzen. Direkte rekursive Kanten erhalten, Expansion begrenzen, mehrfache Fundstellen mit Zeile/Spalte bewahren und dieselbe rekursive Stelle zwischen Referenzen, Call-Tree und Impact konsistent behandeln. Erste Kandidaten bei mehrdeutiger Symbolbindung nicht als exakte Ziele übernehmen. Aufruf, Memberzugriff sowie statisch gebundene virtuelle/interfacebasierte Ziele unterscheiden; mögliche Ziele und unaufgelöste Stellen kennzeichnen. Gemeinsame direkte Referenzsemantik wiederverwenden.

**Nicht:** Keine Zusammenführung der öffentlichen Impact-/Call-Tree-Workflows und keine Laufzeitbehauptungen über DI, Reflection oder dynamischen Dispatch. Kein zusätzliches Redesign von Graphformaten oder Traversierungsargumenten.

**Abnahme:** Source- und Assembly-Fixtures für direkte/gegenseitige Rekursion und mehrere Callsites liefern übereinstimmende Fundstellen und terminierende Traversierung. Zwei Aufrufe auf derselben Zeile bleiben unterscheidbar. Eine mehrdeutige Überladung erscheint nicht als geratener exakter Call. Grenzen und Belegarten sind im öffentlichen Ergebnis sichtbar.

### M1-T4 — Ergebnisstatus und Fortsetzungsgrundlage vereinheitlichen

- [ ] **M1-T4 abschließen**

**Intention:** Analysestand, Analysegrenzen und Antwort-/Ergebnisfortsetzung unabhängig voneinander verständlich machen.

**Scope:** Die gemeinsame Grundlage aus Muss 3 und 5 in den vorhandenen Ergebnis-, Operations- und Antwortmechanismen schaffen. `snapshotId`, tatsächlich analysierten Scope und konkrete Auslassungsgründe knapp ausweisen. `operationToken` für Polling, `continuationToken` ausschließlich für unveränderliche äußere Seiten und `resultCursor` für Domänenseiten unterscheiden. Query-/Target-/Snapshot-/Abschnittsbindung, Replay, TTL, Cancellation, begrenzte Retention und Budget-Recovery bewahren. Die nachfolgenden Scanner-/Toolpunkte verwenden diese Grundlage; keine zweite parallele Cursorengine. Gemeinsame Verträge und Verbraucher in diesem Slice konsistent halten.

**Nicht:** Keine unbegrenzte Trefferhaltung, keine Erweiterung einer Analyse durch einen Cursor, keine globale Vollständigkeitszusage und kein Rückfall in die Stringformat-Erkennung verschiedener Tokenarten. Keine neue Refresh- oder Messroute.

**Abnahme:** Transportfreie Handler-/Store-Prüfungen zeigen verschachtelte äußere/Domänenseiten, Bindung und Replay, Ablauf und Kapazitätsgrenzen, Cancellation sowie exakte Retry-Minima. Budgetwechsel ändern nicht die Query. Äußere Seiten dürfen ihren sichtbaren alten Stand fertig ausliefern; neue Domänen-/Handoff-Verwendung gegen veränderte Targets wird strukturiert abgewiesen. Leere, begrenzte und fehlgeschlagene Ergebnisse sind unterscheidbar; keine Teiltextseite steht neben scheinbar vollständigem StructuredContent.

### M1-A — Audit der Analysegrundlage

- [ ] **M1-Audit abschließen**

**Intention/Scope:** Baselinezuordnung, Snapshot-/Ownerkonsistenz, korrekte Beziehungen sowie neue gemeinsame Ergebnis-/Fortsetzungsverträge unabhängig lesen und gegen Muss 1, 3, 5, 6, 8 und 12 prüfen. Implementierung, Verbraucher, Tests und aktuelle Dokumentation einbeziehen.

**Nicht:** Keine zusätzliche Messaufnahme, kein Produktionscode durch den Auditor und keine Scope-Erweiterung.

**Abnahme:** Keine offene blockierende Abweichung; Findings und deren belegte Behandlung sind am Punkt dokumentiert. Fehlende externe Baselinewerte werden als fehlend weitergegeben. Alle M1-Aufgaben und ihr Audit sind nachgewiesen, bevor der Aggregate-Haken gesetzt wird.

## M2 — Nachladbare, kompakte Navigation und gemeinsamer Kontext

### M2-T1 — Source- und gemeinsame Trefferlisten vollständig nachladen

- [ ] **M2-T1 abschließen**

**Intention:** Bekannte Treffer nicht durch Anzeigecaps verlieren und den tatsächlichen Source-Scope nachvollziehbar machen.

**Scope:** Muss 3 und 5 für Symbolsuche, direkte Referenzlisten, Implementierungen, Typ-/Dateistruktur, Projekt-/Namespaceinventare, Hierarchielisten und Impact-Fundstellen umsetzen. `get_index_scope` zeigt stabile Projektidentität, geladene Framework-Kontexte und Ausschlüsse. Begrenzte ermittelte Listen deterministisch über `resultCursor` nachladen; positives `maxResults` dort als Seitengröße verwenden und bereits bekannte Einträge bewahren. Vorhandene Scanner-/Traversalgrenzen separat ausweisen. Source- und Assembly-Routen der gemeinsamen Tools abdecken, ohne ihre fachlichen Unterschiede zu beseitigen.

**Nicht:** Keine Erweiterung zur Analyse aller Target Frameworks, kein Entfernen unentschiedener Toolargumente und keine Umdeutung von Call-Tree-/Dependency-Graphgrenzen oder Body-Fenstern in Trefferseiten. Nicht begrenzte Listen erhalten keine künstliche Ergebnisgrenze.

**Abnahme:** Jede betroffene Listenfamilie wird mit mehr ermittelten Treffern als Seitengröße durchlaufen: alle Einträge innerhalb desselben ausgewiesenen Analysescopes sind exakt einmal erreichbar. Scopefilter gelten vor Zählung und Paging; Owner und Handoffs bleiben korrekt. Nicht analysierte Frameworks, ausgeschlossene Dokumente und erreichte Scannergrenzen sind sichtbar. Targetänderungen, ungültige Cursor und äußere Budgetseiten funktionieren gemäß M1-T4.

### M2-T2 — Assembly-Ausgaben und Suchvertrag vereinfachen

- [ ] **M2-T2 abschließen**

**Intention:** API-/Suchnavigation ohne doppelte Volltextausgabe und implizite Inventare liefern.

**Scope:** Muss 2 und 4 sowie den Assembly-Anteil von Muss 5 umsetzen. `inspect_assembly`, `search_assembly` und `find_assembly_extensions` erhalten eine kompakte Projektion mit öffentlichen Owner-Handoffs direkt am Eintrag und ehrlichen Closure-/Semantikgrenzen. `formattedText`-Doppelungen und `detailLevel` entfernen; kurze Diagnosekategorien/Zählwerte standardmäßig und vorhandene ausführliche Hinweise über `includeDiagnostics=true` liefern. Das vorhandene gleichnamige Call-Tree-Argument verwendet dieselbe Bedeutung. `inspect_assembly.includeReferences=false` unabhängig von Filtern. `searchKind` entfernen; explizite Literal-/Regex-Auswahl mit `isRegex=false` als Default verwenden, Deklarations-/Dateifilter bewahren. Assembly-Inventar-/Suchseiten und begrenzte Extension-Treffer verwenden `resultCursor`.

**Nicht:** Keine semantische Netzwerk-/SQL-Analyse, keine Extension-Umbenennung oder Erweiterung, keine internen `i:`-Identitäten als zweiter öffentlicher Handle-Vertrag. Keine automatische Closure-Ausweitung aufgrund ausgeschalteter Diagnoseausgabe.

**Abnahme:** API-/Such-/Extension-Ergebnisse erlauben den Owner-Handoff zu Body/Struktur; alle ermittelten Einträge bleiben nachladbar. Leere, diagnostisch begrenzte Such-/Extension-Fixtures mit kurzem Targetpfad brauchen standardmäßig höchstens 256 Antworttokens und nennen dennoch Scope/Grenzen. Missing-/Wrong-Version-/Closurelimit-Fixtures lassen eigene Deklarationen nutzbar und versprechen keine vollständigen Beziehungen. Literal/Regex und Deklarationsfilter werden getrennt geprüft; Kommentar-/Texttreffer sind keine semantischen Calls. Der volle Tokenvergleich für Messszenario M1 erfolgt erst in M3-T2, nicht in zusätzlichen Zwischenkampagnen.

### M2-T3 — Body- und Namespace-Recovery präzisieren

- [ ] **M2-T3 abschließen**

**Intention:** Folgeaktionen an den tatsächlichen Fehler oder das nächste Fenster binden.

**Scope:** Den verbleibenden Recovery-Anteil von Muss 6 implementieren. Body-Batches geben pro Item Auflösungsstatus und nächste Deklarationsfensterposition aus. Erfolgreiche Fenster verlangen keine Fehlerbehebung; mixed Batches erhalten erfolgreiche Items. Namespace-Recovery berücksichtigt Targetart, Projektselektion, Präfix und erlaubte Tiefe. Owner-Mismatch, unbekanntes Handle, Generationwechsel und unresidenter Owner bleiben unterscheidbar und führen zu ausführbaren Folgeaktionen. Äußere Seiten sind vor dem nächsten Body-/Domänenschritt zu lesen.

**Nicht:** Kein Fallback auf erratene Namen/alte Bodies, keine neue Residency-/Refreshroute und kein unabhängiges Redesign der Body-Argumente.

**Abnahme:** Erfolgreiches Body-Fenster, mixed Batch und tatsächlicher Itemfehler liefern jeweils passende Recovery. Ausgewählte Source-Projekte werden nicht erneut unnötig verlangt; Assemblies erhalten keine Source-Projektanweisung. Wrong-Owner-/Unknown-/Stale-/Unresident-Fälle liefern unterscheidbare Gründe und ausführbare Neuentdeckung. Budget-Recovery und neue Cursorbegriffe bleiben konsistent.

### M2-T4 — Testkandidaten semantisch ergänzen

- [ ] **M2-T4 abschließen**

**Intention:** Vom Vertrag aus statisch belegte Testkandidaten finden und Heuristik von Beleg unterscheiden.

**Scope:** Muss 7 im vorhandenen Testkandidaten-Owner implementieren und für die spätere Kontextabfrage wiederverwenden. Neben der gekennzeichneten Namensheuristik direkte Verwendungen in erkannten Testmethoden und passende Implementierungen von Interface-/abstrakten Typ-/Memberzielen berücksichtigen. Expansion begrenzen, an Projekt/Snapshot binden und deduplizieren. Herkunft mit Fundstelle/Belegart für direkte Verwendung, Implementierungsbezug und Namensheuristik ausgeben. Die Zieldeklaration darf Production-Code sein; Testkandidaten werden unabhängig vom späteren Caller-Scope gesammelt.

**Nicht:** Keine transitive Traversierung beliebiger Testhelfer, keine Coverage-/Testpass-Zusage und keine Umdeutung einer ähnlich benannten Implementierungsfixture zum bewiesenen Membertest.

**Abnahme:** Fixtures decken Interface/Implementierung, anders benannte direkte Tests, reine Namensähnlichkeit, doppelte Typnamen in verschiedenen Projekten sowie erreichte Expansion-/Seitenlimits ab. Kandidaten und Belege sind dedupliziert und statisch gekennzeichnet; außerhalb des analysierten Scopes wird nichts behauptet. Listen nutzen die gemeinsame Cursor-/Statusgrundlage und verlieren ermittelte Kandidaten nicht.

### M2-T5 — Gemeinsamen Kontext einführen und drei Routen entfernen

- [ ] **M2-T5 abschließen**

**Intention:** Eine begrenzte Kontextfrage mit gemeinsamem Analysestand und ausschließlich angeforderten Abschnitten beantworten.

**Scope:** Muss 11 und den [gemeinsamen Kontextvertrag](Konzept.md#gemeinsamer-kontextvertrag) vollständig über Scanner, Host, Schema, Registrierung und Verbraucher umsetzen. `get_context` unterstützt Source und Assembly mit den ausdrücklich gewählten Abschnitten `body`, `members`, `callers`, `tests`; die kompakte Zieldeklaration steht einmal im Kopf. Abschnittsstatus und -cursor, Scope-Vorprüfung, gemeinsame Symbolauflösung/Identität/Lease, unabhängiger Test-Scope sowie Fehler-/Teilergebnisverhalten gemäß Konzept implementieren. Die drei bisherigen Kontexttools im selben vollständigen Slice aus dem öffentlichen Katalog entfernen und exklusive ungenutzte Pfade bereinigen. Bibliotheksüberblick über `inspect_assembly`, gezielte Einzeltools und unterschiedliche Relationship-Aufgaben erhalten.

**Nicht:** Keine öffentlichen Aliase, Presets als zweite Abschnittsauswahl, automatischen Inventare, beliebigen Toolketten oder zusätzlichen Abschnitte für transitive Impactanalyse. Nicht gewählte Abschnitte führen weder Scannerarbeit noch Ausgabe aus.

**Abnahme:** Source-/Assembly-Fälle mit einzelnen und kombinierten Abschnitten belegen einen gemeinsamen Analysestand ohne doppelte Targetöffnung. Assembly plus `tests`, Nicht-Typ plus `members` und ausdrücklich wirkungslose Argumente ergeben klare Eingabefehler; weggelassene Defaults tun dies nicht. Caller-Scope `production` unterdrückt keine Tests. Abschnittsfehler liefern keine vollständige Gesamterfolgsmeldung. Ein Abschnittscursor setzt nur diesen Abschnitt fort und wiederholt keine andere Analyse/Ausgabe. Registrierter Katalog enthält exakt 17 Navigationstools; alte Kontextrouten sind nicht als Alias verfügbar.

### M2-T6 — Releasezuordnung, Katalog und Agentenanleitung abschließen

- [ ] **M2-T6 abschließen**

**Intention:** Gemessenen Serverstand identifizierbar machen und einen durchgängig passenden Agentenworkflow anbieten.

**Scope:** Muss 9 und den Anleitungsanteil von Muss 10 umsetzen. Buildabgeleitete Release-/Commitkennung in Standardinitialisierung und vorhandener Startup-Protokollierung ausgeben. Katalog, Schemas, aktuelle Dokumentation und lokale Navigationsregel auf dieselben 17 Tools ausrichten. Kurze Toolbeschreibungen und konkrete Folgeoperationen beschreiben Source-/Assembly-Einstieg, Owner-Handoff, explizite Kontextabschnitte, gezielte Einzeltools, direktes Lesen bekannter Source-Fenster sowie Testsuche und Recovery. Verbleibende aktive Verweise auf entfernte Argumente/Routen bereinigen; historische Belege und negative Assertions als solche erhalten.

**Nicht:** Kein Health-/Reload-Ersatz, keine Kennung pro Symboleintrag, keine Loggingänderung über Laufzeittools und keine Aktualisierung aktueller Dokumentation auf einen noch nicht implementierten Stand. Keine Produktnamen in neu abgeleiteten Beispielen oder Commits.

**Abnahme:** Standard-MCP-Initialisierung/Startup ordnen den Prozess nachvollziehbar dem Build zu; stdout enthält nur MCP. Exakter Katalog-/Schematest, Dokumentations-/Regelabgleich und navigierbare Beispiele stimmen überein. Neue Namen/Argumente sind erklärt, entfernte Verträge tauchen in aktiven Anleitungen nicht auf. Startup-Konfiguration mit Prozessneustart bleibt der dokumentierte Mechanismus.

### M2-A — Audit der Agentenoberfläche

- [ ] **M2-Audit abschließen**

**Intention/Scope:** Alle betroffenen Source-/Assembly-Routen, Projektionen, Cursor-/Budgetverträge, Testkandidaten, Kontextabschnitte, Ersatzwege, den exakten 17-Tool-Katalog und aktuelle Agentenanleitung unabhängig gegen Muss 2–7 und 9–11 prüfen. Nicht gewählte Kontextarbeit und entfernte exklusive Pfade besonders beachten.

**Nicht:** Kein Benchmark der Zwischenstände, keine Scope-Erweiterung und keine Implementierung durch den Auditor.

**Abnahme:** Keine offene blockierende Abweichung. Findings und belegte Behandlung stehen direkt am Punkt. Die fachlichen Voraussetzungen für den finalen Vorher-/Nachher-Abgleich sind hergestellt; erst dann wird der M2-Aggregate-Haken gesetzt.

## M3 — Gesamtabnahme und begrenzter Praxisvergleich

### M3-T1 — Konsolidierte technische Abnahme

- [ ] **M3-T1 abschließen**

**Intention:** Das Zusammenspiel der geänderten Analyse-/Host-/Toolverträge mit eigenständigen, reproduzierbaren Nachweisen abnehmen.

**Scope:** Die [Abnahmematrix des Konzepts](Konzept.md#verifikation-und-abnahme) vollständig gegen die bereits implementierten Slices prüfen. Verbleibende Integrationslücken in deren Scope schließen. Das vollständige eligible Routine-Solution-Gate einmal ausführen; relevante Extended-Beziehungs-/Host-/Workspace-/Response-Fälle gezielt auswählen. Stdio-/Katalog- und transportfreie Handlerbelege sauber trennen. Projekt-/Frameworkidentität, Referenz-Closure, Fehler, Cancellation, Residency, nested Paging, Read-only und Protokollausgabe zusammen betrachten. Nach späteren Änderungen die betroffenen Checks erneut ausführen, ohne unveränderte Gates routinemäßig zu wiederholen.

**Nicht:** Keine proprietären CI-Abhängigkeiten, keine Umgehung vorhandener E2E-Ausschlüsse und keine Gleichsetzung eines kompilierten oder ausgeschlossenen Checks mit einem ausgeführten Nachweis.

**Abnahme:** Die technischen Kriterien einschließlich Rekursion, unsicherer Bindung, vollständiger Listenfortsetzung, Snapshot, Kontextvorprüfung, Testprovenienz, Recovery, Diagnosegrenzen und kurzem leeren Ergebnis sind durch tatsächlich ausgeführte passende Nachweise erfüllt. Offizielle Gates bestehen; nicht ausgeführte Checks und ihre praktische Grenze sind ausdrücklich benannt. Katalog/Schema/Dokumentation passen zum implementierten Ergebnis. Kein Praxisvergleich wird dadurch vorweg als bestanden behauptet.

### M3-T2 — Abschlussmessung und Effizienzbefund

- [ ] **M3-T2 abschließen**

**Intention:** Das konkrete Ergebnis anhand der festgeschriebenen Workflows bewerten und die Messarbeit abschließen.

**Scope:** Die in M1-T1 festgeschriebenen sechs Szenarien M1–M6 gegen den endgültigen, eindeutig zugeordneten Stand aufnehmen. Der [begrenzte Messumfang](Konzept.md#begrenzter-messumfang) gilt unverändert: ein kalter und zwei warme Durchläufe je Variante, höchstens zwei Varianten je Szenario/36 vollständige Workflows, maximal 60 Minuten. Vorher/Nachher nach deklarativer Information, Scope und Vollständigkeit vergleichen; sämtliche benötigten Antwort- und Domänenseiten mitzählen. Den breiten Dependency-Fall einmal innerhalb dieses Umfangs einordnen. Neue Belege/Testkandidaten und korrigierte Rekursion als Korrektheitsänderungen ausweisen; nicht gegen unvollständige Altdaten als reine Tokensteigerung werten. Ergebnisse und verbleibenden Mehraufwand anonym zusammenfassen. Bei verfehltem Tokenkriterium oder konkretem Messfehler ausschließlich die betroffenen Szenarien einmal gezielt nachmessen, maximal weitere 30 Minuten.

**Nicht:** Keine weiteren Targets, Statistik-, Last- oder Monitoringkampagnen, Modellkosten aus Tokenisierung, positiven Latenzversprechen ohne kontrollierten Beleg oder Messschleifen nach Ablauf der festgelegten Runde.

**Abnahme:** Der Bericht nennt Build/Prozess, Target-/Referenzzuordnung, Semantik/Grenzen, vollständige Antworttokens/Bytes, Requests/Polls/Seiten und Kalt-/Warmlaufzeiten. Verfügbare Schema-/Requestkosten sind getrennt von Antworttokens; fehlende Clientdaten bleiben als nicht gemessen sichtbar. Der schmale Assembly-A-Fall erfüllt das 1.600-Token-Kriterium bzw. das im Konzept definierte 50-Prozent-Kriterium bei veränderten Targetbytes/Referenzumgebungen. Eine kürzere erste Seite zählt nicht. Unverfügbare Targets, Timeouts, unvollständige Seiten oder verletzte Kriterien bleiben ausdrücklich fehlende/verfehlte Nachweise; damit wird kein entsprechender Erfolgshaken gesetzt. Messarbeit endet nach der begrenzten Runde mit konkreten Befunden. Technische Fixture-Abnahme und tatsächlich erreichter externer Praxisnachweis bleiben getrennt.

### M3-A — Finale Abnahmeprüfung

- [ ] **M3-Audit abschließen**

**Intention/Scope:** Den vollständigen Task-Diff, Scopeabdeckung, technischen Abschluss, unveränderte Targets und begrenzten Vorher-/Nachher-Bericht unabhängig gegen das freigegebene Konzept prüfen. Alle zwölf Muss-Anforderungen und ihre Ergebnisse, entfernte Routen/Argumente, positive/negative Schemafälle und verbleibende Nachweisgrenzen berücksichtigen.

**Nicht:** Keine erneute vollständige Messkampagne und keine automatische Freigabe unerfüllter Kriterien wegen abgelaufener Messzeit.

**Abnahme:** Keine offene blockierende Abweichung; keine offenen Nutzerentscheidungen; Ergebnisse werden nur entsprechend ihren tatsächlich erbrachten Belegen abgeschlossen. Nach einer erforderlichen gezielten Korrektur werden ausschließlich die betroffenen technischen Nachweise wiederholt, mit erneuter Messung nur innerhalb der in M3-T2 festgelegten Runde. Ein offener externer Praxisnachweis bleibt ausdrücklich offen und wird nicht durch technische Fixture-Ergebnisse ersetzt. Der Gesamtabschluss enthält überprüfbare Commits/Checks und den konkreten Effizienzbefund.

## Abdeckung des freigegebenen Scopes

| Muss im Konzept | Verantwortliche Aufgaben |
|---|---|
| 1 — Beziehungen | M1-T3; Gesamtabnahme M3-T1 |
| 2 — Kompakte Darstellung | M2-T2, M2-T5 |
| 3 — Unvollständigkeit/Scope | M1-T4, M2-T1, M2-T2, M2-T5 |
| 4 — Diagnosen | M2-T2; Kontextverbrauch M2-T5 |
| 5 — Fortsetzungen | M1-T4, M2-T1, M2-T2, M2-T5 |
| 6 — Recovery/Leases | M1-T2, M2-T3, M2-T5 |
| 7 — Testkandidaten | M2-T4; Kontextverbrauch M2-T5 |
| 8 — Resolver/Residency | M1-T2 |
| 9 — Release/Katalog | M2-T5, M2-T6 |
| 10 — Effizienz/Anleitung | M1-T1, M2-T6, M3-T2 |
| 11 — Gemeinsamer Kontext | M2-T5 |
| 12 — Snapshot/Batch | M1-T2; Verbraucher M1-T3, M2-T1, M2-T5 |

Alle Aufgaben bleiben bis zur tatsächlich ausgeführten Umsetzung und Verifikation offen. Konzept und Messgrenzen werden durch diese Roadmap nicht geändert.
