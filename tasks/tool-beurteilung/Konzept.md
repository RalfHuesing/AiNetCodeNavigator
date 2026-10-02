Bewerte den beigefügten Funktionsumfang von AiNetCodeNavigator und entscheide konkret, welche Features bleiben, welche wegkönnen und welche fehlen.

Produktziel:
AiNetCodeNavigator ist ein MCP-Server für autonome AI-Agenten, die C#/.NET-Software entwickeln. Er soll ihnen helfen, relevanten Code zu finden, Verhalten und Beziehungen zu verstehen, Änderungen vorzubereiten und passende Tests zu identifizieren.

Produktgrenzen:
- Read-only Code-Navigation und semantische Erkundung.
- Keine Codeänderungen, automatischen Refactorings, Linter, Code-Audits oder Testausführung.
- Der Agent nutzt für Bearbeitung, Builds und Testausführung andere Werkzeuge.

Deine Aufgabe ist eine Produktentscheidung, keine Zusammenfassung der Featureliste. Bewerte den Nutzen für einen handelnden AI-Agenten, nicht für einen Menschen in einer IDE.

Arbeitsgrundlage:
Du erhältst die Featureliste, keine verifizierte Implementierung. Behandle Aussagen wie „exakt“, „essenziell“, „hocheffizient“ oder „eliminiert Ambiguitäten vollständig“ als Behauptungen. Erfinde keine Messwerte zu Geschwindigkeit, Tokenverbrauch oder Zuverlässigkeit. Trenne belegten Funktionsumfang, Annahmen und offene Implementierungsfragen. Triff trotzdem für jedes Tool eine klare Empfehlung.

Bewertungsmaßstab:
Vergleiche mit einem Agenten, der bereits Dateien lesen, ripgrep verwenden, Code bearbeiten sowie Builds und Tests ausführen kann.

Ein Feature verdient seinen Platz, wenn es beispielsweise:
- semantische Fragen beantwortet, die Textsuche nicht zuverlässig löst;
- wichtige Informationen liefert, die sonst schwer zugänglich sind;
- die Erfolgswahrscheinlichkeit einer Entwicklungsaufgabe erhöht;
- Kontextmenge oder notwendige Interaktionen sinnvoll reduziert;
- falsche Schlussfolgerungen durch unvollständige oder veraltete Ergebnisse verhindert.

Berücksichtige auch Kosten:
- zusätzliche Toolbeschreibungen im Agentenkontext;
- schwierige Auswahl zwischen ähnlichen Tools;
- Parameterkomplexität;
- Ladezeit und Rechenaufwand;
- Wartungsaufwand;
- irreführende Sicherheit durch Heuristiken oder unvollständige Graphen.

Weniger Tools sind kein Selbstzweck. Ein zusammengesetztes Tool kann sinnvoll sein, wenn sein konkreter Arbeitsablauf den zusätzlichen Vertrag rechtfertigt. Ähnliche Ausgaben beweisen noch keine Redundanz.

Prüfe die Features anhand dieser Aufgaben:
1. Einen Fehler über mehrere Methoden und Projekte verfolgen.
2. Ein Feature in bestehendem Code erweitern.
3. Eine Interface- oder Methodensignatur ändern und betroffene Stellen finden.
4. Relevante Tests für eine Änderung auswählen.
5. Eine unbekannte NuGet-API einschließlich Extension-Methods verstehen.
6. Nach eigenen Codeänderungen mit einem weiterhin gültigen Analysezustand weiterarbeiten.

Untersuche ausdrücklich:
- get_file_skeleton gegenüber get_class_structure.
- get_call_tree gegenüber find_references und get_impact.
- get_type_hierarchy gegenüber find_implementations, auch auf Member-Ebene.
- get_feature_context gegenüber seinen Einzeloperationen.
- get_assembly_context gegenüber inspect_assembly und den allgemeinen Symboltools.
- Den Zusatznutzen von get_file_tree und get_namespace_tree gegenüber vorhandenen Dateisuchwerkzeugen.
- Ob get_server_health und reload_config zur öffentlich sichtbaren Arbeitsoberfläche eines Entwicklungsagenten gehören.
- Einzelne Parameter, Ausgabeformate und Suchmodi: Ein nützliches Tool kann unnötige Teilfeatures besitzen.

Prüfe mögliche Lücken, ohne automatisch neue Tools vorzuschlagen:
- Aktualität des Index nach Änderungen durch den Agenten.
- Gültigkeit von Handoff-IDs und Fortsetzungstokens nach Änderungen.
- Erkennbare Unvollständigkeit durch Limits, Paging, Ladefehler oder fehlende Projekte.
- Unterschied zwischen „keine Treffer“ und „nicht vollständig analysiert“.
- Grenzen statischer Analyse bei virtuellen Aufrufen, DI, Reflection und dynamischem Verhalten.
- Unterscheidung heuristischer Testkandidaten von nachgewiesenen Beziehungen.
- Eindeutige Symbolauflösung bei Überladungen, Generics und mehreren Target Frameworks.
- Anschluss von Source-Navigation an externe Bibliotheken.

Verlange nicht für jede Lücke ein neues MCP-Tool. Entscheide, ob sie durch einen bestehenden Vertrag, Ergebnis-Metadaten, internes Verhalten oder ein neues Tool gelöst werden sollte.

Antworte auf Deutsch in dieser Struktur:

1. ENTSCHEIDUNG
Beginne mit drei kurzen Listen:
- Entfernen.
- Zusammenführen oder aus der Agentenoberfläche auslagern.
- Neu ergänzen beziehungsweise bestehende Verträge erweitern.

Nenne konkrete Namen. Keine Einleitung.

2. ENTSCHEIDUNG FÜR ALLE 22 TOOLS
Eine Tabelle mit genau einer Zeile je Tool:
Tool | Entscheidung | Konkreter Agentennutzen oder Streichgrund | Ersatz/Ziel

Zulässige Entscheidungen:
BEHALTEN, VEREINFACHEN, ZUSAMMENFÜHREN, AUSLAGERN, ENTFERNEN.

Bei ZUSAMMENFÜHREN: Nenne das Zieltool und welche Fähigkeiten dort erhalten bleiben.
Bei AUSLAGERN: Nenne den Zielbereich und begründe, warum die Fähigkeit intern oder administrativ erhalten bleibt.
Bei ENTFERNEN: Nenne den Ersatz oder ausdrücklich, welcher Anwendungsfall entfällt und warum das akzeptabel ist.

3. TEILFEATURES, DIE WEGKÖNNEN
Eine kurze Tabelle:
Tool/Parameter/Modus | Änderung | Praktische Folge

Unterscheide das Entfernen eines öffentlichen Tools vom Entfernen seiner zugrunde liegenden Fähigkeit.

4. FEHLENDE FÄHIGKEITEN
Höchstens fünf priorisierte Ergänzungen:
Priorität | Fähigkeit | Konkreter Fehler im heutigen Ablauf | Minimale Lösung | Prüfkriterium

P0 = für verlässliches autonomes Arbeiten erforderlich.
P1 = hoher praktischer Zusatznutzen.
P2 = optional.

Kennzeichne jede Ergänzung als „laut Liste fehlend“ oder „in der Liste nicht ausreichend beschrieben“. Beschreibe nur das minimal nötige Verhalten. Wenn keine Ergänzung ausreichend begründet ist, sage das.

5. EMPFOHLENE ZIEL-OBERFLÄCHE
Liste die endgültigen öffentlichen MCP-Tools mit je einem Satz zu ihrer Zuständigkeit. Berücksichtige alle vorherigen Streichungen und Zusammenführungen. Vermeide Überschneidungen und ein universelles Tool mit unübersichtlichen Modusschaltern.

6. ENTSCHEIDENDE UNSICHERHEITEN
Höchstens drei offene Fragen oder gezielte Vergleichstests, deren Ergebnis eine Empfehlung ändern könnte. Nenne jeweils die betroffene Empfehlung und das Entscheidungskriterium. Schiebe die Entscheidung nicht pauschal auf spätere Benchmarks.

Stil:
- Maximal 1.500 Wörter.
- Keine Marketingprosa, Höflichkeitseinleitung oder Wiederholung der Aufgabenstellung.
- Keine allgemeinen Aussagen wie „Semantik ist wichtig“ ohne konkrete Konsequenz.
- Kein „kommt darauf an“ ohne Entscheidung und benannte Bedingung.
- Keine erfundenen Prozentwerte, Punktescores oder Leistungsversprechen.
- Begründe Streichungen ebenso konkret wie Ergänzungen.
- Empfiehl keine neuen Features, die nur bereits vorhandene Fähigkeiten umbenennen.

---

## 1. ENTSCHEIDUNG

- **Entfernen:** `get_file_tree`.
- **Zusammenführen oder auslagern:** `get_impact` → `get_call_tree`; `resolve_type_origin` → `find_symbol`; `get_assembly_context` → `inspect_assembly` und Symboltools; `get_server_health`, `reload_config` → Administration.
- **Neu ergänzen beziehungsweise Verträge erweitern:** Keine neuen Tools. Aktualität, Symbolidentitäten, Vollständigkeit und Belegarten verbindlich machen; `find_assembly_extensions` um den Referenzsatz eines Source-Projekts erweitern.

## 2. ENTSCHEIDUNG FÜR ALLE 22 TOOLS

Grundlage ist der dokumentierte Funktionsumfang, nicht eine geprüfte Implementierung. Die folgenden Zielverträge sind Empfehlungen; beworbene Effizienz und Zuverlässigkeit bleiben unbestätigt. :chatgpt-content-reference{index="0"}

| Tool | Entscheidung | Konkreter Agentennutzen oder Streichgrund | Ersatz/Ziel |
|---|---|---|---|
| `find_symbol` | VEREINFACHEN | Semantischer Einstieg statt bloßer Namensfundstellen; übernimmt Herkunftsauflösung. | Namensmuster oder eindeutiger Identifikator; Herkunft und Folge-Handoff. |
| `get_symbol_body` | VEREINFACHEN | Symbolbezogenes Lesen und Dekompilieren; Batch-Abrufe behalten. | Eindeutiges Windowing mit sichtbarer Fortsetzung. |
| `get_file_skeleton` | BEHALTEN | Überblick über mehrere Typen innerhalb ausgewählter Dateien. | Dateibezogene Signaturen mit Handoffs. |
| `get_class_structure` | VEREINFACHEN | Typbezogene Memberansicht statt Dateiansicht. | Soll sämtliche `partial`-Teile verbinden; deklarierende Datei je Member ausgeben. |
| `get_index_scope` | BEHALTEN | Verhindert Schlussfolgerungen über nicht geladene Projekte. | Analyseumfang, Ladeprobleme und Aktualität. |
| `get_file_tree` | ENTFERNEN | Physische Dateisuche ist vorausgesetzt; virtuelle Dateibäume rechtfertigen keinen zusätzlichen Einstieg. | Dateisuchwerkzeuge; DLL-Navigation über `inspect_assembly`; Indexzugehörigkeit über `get_index_scope`. |
| `get_namespace_tree` | BEHALTEN | Logische, projektübergreifende Gliederung ohne bekannten Symbolnamen, unabhängig von Ordnern. | Namespace-Navigation; keine Gleichsetzung mit Architekturgrenzen. |
| `get_call_tree` | VEREINFACHEN | Mehrstufige Fehlerverfolgung einschließlich ausgehender Aufrufe. | Strukturierte Kanten, Fundstellen und aggregierte Dateien/Projekte. |
| `find_references` | VEREINFACHEN | Konkrete Änderungsstellen einschließlich Nicht-Aufruf-Verwendungen. | Direkte Referenzen, vollständig nachladbar. |
| `get_type_hierarchy` | BEHALTEN | Basistypen, Interfaces, Subtypen und Implementierungstypen als Beziehungen. | Ausschließlich Typ-Ebene. |
| `find_implementations` | VEREINFACHEN | Member-Zuordnung für Interface-Signaturänderungen und Overrides. | Member-Ebene behalten; reine Typabfragen → `get_type_hierarchy`. |
| `get_impact` | ZUSAMMENFÜHREN | Laut Liste Aufruferketten plus Datei-/Projektaggregation, kein eigenständiger Änderungsvertrag. | `get_call_tree` erhält transitive Aufrufer und Aggregation. :chatgpt-content-reference{index="1"} |
| `dependency_graph` | BEHALTEN | Typ-/Dateiabhängigkeiten ergänzen Aufrufketten bei Änderungsvorbereitung. | Typisierte Abhängigkeitskanten, keine Audit-Urteile. |
| `resolve_type_origin` | ZUSAMMENFÜHREN | Herkunft gehört zur Symbolauflösung, nicht zu einem separaten Arbeitsablauf. | `find_symbol`: Source-Projekt/Datei, Assembly-/Paketzuordnung, DLL-Pfad und Handoff erhalten. :chatgpt-content-reference{index="2"} |
| `get_feature_context` | VEREINFACHEN | Sinnvoller fester Startablauf: Deklaration, direkte Aufrufer, Testkandidaten. | Verbund behalten; gemeinsame Analyseimplementierung, getrennte Abschnittsbudgets. :chatgpt-content-reference{index="3"} |
| `get_test_context` | VEREINFACHEN | Eigenständige Testauswahl nach Änderungen, ohne erneut Feature-Kontext abzurufen. | Kandidaten mit Testidentität, Projekt und Auswahlgrund. |
| `get_assembly_context` | ZUSAMMENFÜHREN | Bibliotheksprofil behalten; frei kombinierbare Detailabfragen rechtfertigen keinen zweiten Einstieg. | Profil → `inspect_assembly`; Body → `get_symbol_body`; Member → `get_class_structure`; Aufrufer/Impact → `get_call_tree`. :chatgpt-content-reference{index="4"} |
| `inspect_assembly` | VEREINFACHEN | API-Erkundung ohne bekannten Symbolnamen und ohne vollständige Dekompilation. | Metadatenprofil und paginierte Signaturen; keine Body-/Graph-Schalter. :chatgpt-content-reference{index="5"} |
| `search_assembly` | VEREINFACHEN | Suche im sonst nicht zugänglichen Dekompilat; externe API-Nutzung ohne bekannten Aufrufer entdecken. | `text` und assemblyweites `external_calls` behalten. |
| `find_assembly_extensions` | BEHALTEN | Receiver-basierte Suche statt Raten statischer Containerklassen. | Projektbezogenen Suchraum und Receiver-Verträglichkeit ergänzen. |
| `get_server_health` | AUSLAGERN | Cache-/GC-Diagnose betrifft Serverbetrieb, nicht Codeentscheidungen. | Administrationsoberfläche/Logs behalten; relevante Analysefehler in Ergebnissen. |
| `reload_config` | AUSLAGERN | Logging-Konfiguration ist Betriebsverwaltung, keine Code-Navigation. | Administrative CLI oder Betreiberoberfläche; Fähigkeit bleibt. |

**Dateigrenze und Typgrenze sind unterschiedliche Einstiege:** Das Skelett erschließt Dateien, die Strukturansicht einen Typ. Die Liste beschreibt beide Perspektiven, aber keine ausdrückliche Garantie für zusammengeführte `partial`-Deklarationen. :chatgpt-content-reference{index="6"}

**Für Signaturänderungen reichen Aufrufketten nicht:** Referenzen liefern Verwendungsstellen; Member-Implementierungen erschließen zusätzlich anzupassende Verträge und Überschreibungen. Anschließend sind auch deren Referenzen zu prüfen. :chatgpt-content-reference{index="7"} :chatgpt-content-reference{index="8"}

Der Feature-Verbund bleibt wegen seines festen Arbeitsablaufs. Der Assembly-Verbund verliert dagegen seine frei kombinierbare Zweitoberfläche.

## 3. TEILFEATURES, DIE WEGKÖNNEN

| Tool/Parameter/Modus | Änderung | Praktische Folge |
|---|---|---|
| `find_symbol.pattern`; `inspect_assembly.memberName` | Jeweils nur Array-Variante behalten. | Batch bleibt; alternative Schreibweisen entfallen. |
| `get_symbol_body.endLine` | `startLine` + `maxBodyLines` behalten. | Keine konkurrierenden Fenstergrenzen. |
| `get_class_structure.sortBy` | Öffentliche Sortierwahl entfernen; feste dokumentierte Reihenfolge. | Weniger Präsentationsparameter; stabile Fortsetzungen. |
| `find_references.depth` | Entfernen. | Referenzen bleiben direkt; Aufruftraversierung übernimmt `get_call_tree`. |
| `get_call_tree.format` | ASCII/Mermaid durch eine kompakte Kantenliste ersetzen. | Beziehungsinformationen bleiben; Diagrammerzeugung entfällt. |
| `get_call_tree.includeDiagnostics` | Vollständigkeitsrelevantes immer ausgeben; technische Details administrativ. | Analyseprobleme nicht versehentlich ausblenden. |
| `get_feature_context/get_test_context.scopeType` | Entfernen; Ausgangssymbol und Test-Suchraum getrennt behandeln. | Produktionssymbole verlieren nicht ihre Testkandidaten. |
| Assemblytools: `detailLevel` | Eine Ergebnisspezifikation; Paging und Filter behalten. | Keine parallelen Ausgabevarianten pflegen. |
| `search_assembly`: `data_access`, `declarationOnly`, Regex-Autodetektion | Entfernen. | DB-Suche über Text/externe Aufrufe; Deklarationen über Symboltools; Regex ausdrücklich wählen. |

`data_access` verliert seine spezialisierte Suchheuristik, nicht die Möglichkeit, Datenzugriffe zu untersuchen. Die Liste beschreibt dafür keinen nachvollziehbaren Erkennungs- oder Belegvertrag. :chatgpt-content-reference{index="9"}

**Batching, Budgets und Tiefenlimits bleiben.** Sie begrenzen reale Arbeit; ihre Einheiten und Auslassungen müssen eindeutig sein. Budgetüberschreitungen sollen bevorzugt kleinere fortsetzbare Ergebnisse erzeugen, nicht nur größere Budgets verlangen.

## 4. FEHLENDE FÄHIGKEITEN

Die Liste nennt residente Caches, Handoffs und Fortsetzungen, beschreibt aber deren Zusammenspiel mit externen Änderungen nicht ausreichend. :chatgpt-content-reference{index="10"}

| Priorität | Fähigkeit | Konkreter Fehler im heutigen Ablauf | Minimale Lösung | Prüfkriterium |
|---|---|---|---|---|
| **P0** | Aktualität — **in der Liste nicht ausreichend beschrieben** | Nach Agenten-Edits könnte der Index alte Aufrufe liefern. | Intern Datei-, Projekt- und Referenzänderungen nachführen; ein Snapshot je Antwort. Aktualisierung oder Fehler mit Wiederholungsweg melden. | Body ändern, Datei hinzufügen/löschen, Projektverweis ändern: nächste Abfrage aktuell oder ausdrücklich nicht auswertbar. |
| **P0** | Identitäten/Fortsetzungen — **in der Liste nicht ausreichend beschrieben** | Alte Handoffs oder Cursor könnten andere Symbole beziehungsweise gemischte Ergebnisse liefern. | Identitäten an Projekt, Target Framework, generische Konstruktion/Überladung und Revision binden; Mehrdeutigkeit melden; veraltete IDs/Tokens strukturiert ablehnen. | Überladung ändern, Framework wechseln, während Paging editieren: niemals stille Umdeutung; Wiederauflösung möglich. |
| **P0** | Vollständigkeit — **in der Liste nicht ausreichend beschrieben** | „Keine Treffer“ könnte Limit, Ladefehler oder ausgeschlossene Projekte bedeuten. | Je Ergebnis/Verbundabschnitt analysierten Scope, Vollständigkeit, Auslassungsgründe und Fortsetzung ausweisen; Generated-Filter sowie Tiefen-/Verzweigungsgrenzen sichtbar machen. | Überfüllte Seite und fehlgeschlagenes Projekt unterscheiden sich von vollständig leerem Ergebnis. |
| **P0** | Belegarten — **in der Liste nicht ausreichend beschrieben** | Virtuelle Ziele, DI-/Reflection-Verhalten oder ähnliche Testnamen könnten als bewiesene Ausführung erscheinen. | Statische Bindung, mögliche Implementierung, Heuristik und unbekannte Grenze unterscheiden; Tests mit Auswahlgrund/Belegpfad, niemals Abdeckungsversprechen. | Zwei Implementierungen und ein namensähnlicher Test werden nicht als sicher ausgeführt dargestellt. |
| **P1** | Extension-Suche vom Source-Projekt — **laut Liste fehlend** | Die Receiver-DLL allein erschließt möglicherweise keine Extensions anderer Projektabhängigkeiten. | `find_assembly_extensions` nutzt den aufgelösten Projekt-/Framework-Referenzsatz; liefert Signatur, Constraints, Namespace und Herkunft. Externe Handoffs funktionieren in allgemeinen Symboltools; Original/Dekompilat unterscheiden. | Extension aus anderer DLL und generischer Receiver auffindbar; Body-Abruf trifft denselben Paketstand oder meldet fehlenden Body. |

Der letzte Punkt erweitert den derzeit ausschließlich assemblybezogenen Einstieg, nicht bloß dessen Namen. :chatgpt-content-reference{index="11"}

Technischer Hintergrund, kein Implementierungsnachweis: Roslyn-Solutions sind unveränderliche Modelle; ihre Existenz garantiert keine Dateisystemaktualität. Extension-Verwendbarkeit hängt außerdem von Receiver-Kompatibilität und Namensraumkontext ab. :chatgpt-content-reference{index="12"}

Referenz- und Aufrufantworten dürfen ihren bekannten Suchraum nicht als sämtliche externen Verbraucher darstellen.

## 5. EMPFOHLENE ZIEL-OBERFLÄCHE

**16 öffentliche Tools:**

- `find_symbol` findet beziehungsweise identifiziert Symbole samt Herkunft.
- `get_symbol_body` liefert begrenzte Original- oder dekompilierte Deklarationen.
- `get_file_skeleton` erschließt ausgewählte Dateien ohne Bodies.
- `get_class_structure` zeigt deklarierte Member über sämtliche Teile eines Typs.
- `get_index_scope` erklärt analysierten Umfang und Zustand.
- `get_namespace_tree` erschließt logische Namespace-Gruppen.
- `get_call_tree` verfolgt Aufrufketten samt Datei-/Projektaggregation.
- `find_references` liefert direkte semantische Verwendungsstellen.
- `get_type_hierarchy` liefert ausschließlich Typbeziehungen.
- `find_implementations` ordnet Member ihren Implementierungen und Overrides zu.
- `dependency_graph` liefert typisierte Typ-/Dateiabhängigkeiten.
- `get_feature_context` liefert den festen Einstieg Deklaration–Aufrufer–Tests.
- `get_test_context` liefert begründete Testkandidaten.
- `inspect_assembly` erschließt Bibliotheksidentität und Metadaten-API.
- `search_assembly` durchsucht Dekompilate und externe API-Aufrufe.
- `find_assembly_extensions` findet receiverbezogene Extensions im gewählten Referenzraum.

## 6. ENTSCHEIDENDE UNSICHERHEITEN

1. **`get_impact` zusammenführen:** Ermittelt die Implementierung zusätzliche Änderungsbeziehungen außerhalb des Aufrufgraphs? Ein eigener, nachvollziehbarer Änderungsablauf würde die Empfehlung auf **BEHALTEN** ändern.
2. **`get_feature_context` behalten:** Ersetzt der Verbund bei repräsentativen Feature-Aufgaben tatsächlich notwendige Abrufe? Fügt er nur unbenötigte Abschnitte hinzu, ohne Abrufe einzusparen, entfällt er; Einzelanalysen bleiben.
3. **`data_access` entfernen:** Findet der Modus belegbare Datenzugriffsketten, die Textsuche und `external_calls` nicht gezielt erschließen? Dann als begründeten Suchmodus behalten, nicht als neues Tool.
