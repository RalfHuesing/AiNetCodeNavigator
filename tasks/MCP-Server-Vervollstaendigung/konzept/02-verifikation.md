# 02 — Verifikation, Dokumentation und Abnahme ohne E2E

Dieses Kapitel gehört zu [Konzept.md](../Konzept.md). Es definiert die verbindliche Abnahme aller fachlichen Verträge mit zulässigen Nicht-E2E-Nachweisen.

## Ausgeschlossene und zulässige Testwege

Ausgeschlossen sind vollständige Produktabläufe über den äußeren Einstieg, insbesondere Child-Process-MCP-Stdio, MCP-Client/Server-Handshake und JSON-RPC über Streams einschließlich in-memory. Name, Fixturetarget und Testprojekt ändern diese E2E-Grenze nicht.

Zulässig sind Tests originaler Registrierungsdefinitionen, Validierung/SDK-Bindung, Handler, Formatter, Stores und Runtime ohne MCP-Server/-Client/-Transport sowie begrenzte Komponentenintegrationen mit Roslyn/MSBuild, Dateien, Decompiler oder Git. SDK-Adapter dürfen direkt aufgerufen werden. Keine Gesamtproduktkette; interne Testzugänge bleiben gemäß Kapitel 01 begrenzt.

Vor Gates werden existierende Fälle nach ihrem Ablauf inventarisiert. Ausgeschlossene Stdio-/SDK-Stream-/Gesamt-Reportpublikationsfälle erhalten `Category=E2EIntegration`, bei gemischten Klassen am einzelnen Fall oder nach Trennung bei unveränderten Assertions. Bestehende Tests bleiben ausführbar und erhalten ihre Assertions.

Jeder Testlauf verwendet offizielle Skripte mit `Category!=E2EIntegration`; engere Filter werden per AND kombiniert. Die Auswahl wird gegen das Inventar geprüft. Build läuft ohne Testfilter. `ExtendedIntegration` bleibt standardmäßig ausgeschlossen; `-IncludeExtended` ist nur für betroffene nachweisliche Nicht-E2E-Fälle mit E2E-Ausschluss erlaubt. Kein unselektierter Suite- oder Release-E2E-Lauf.

## Nachweismatrix für alle 21 Tools

`Abnahmematrix.md` im Task hat genau eine Zeile je Tool: unterstützte Targets, konkrete fachliche Testnamen und Ergebnis. Gemeinsam werden Mechanismusnachweise, Gates, geprüfter Commit und Passed/Failed/Skipped genannt. Keine kopierten Requests/Logs je Tool. `docs/` enthält nur implementierte Fakten.

Ergebnisse: `passed`, `failed`, `not_run`, `not_applicable` mit Vertragsgrund. Fehlende/fehlgeschlagene Pflichtnachweise werden benannt. Der E2E-Ausschluss steht einmal mit Inventar als `not_run: excluded_by_user` und blockiert diese Abnahme nicht. Passende vorhandene Tests zählen; keine neuen Tests allein zur Matrixbefüllung.

Die Abnahme prüft:

1. Fachliches normales Handlerresultat je Tool/unterstütztem Target und die Fehlerfälle unten. Weitere Fehler je Target nur bei anderer Semantik. Schemafilter allein genügt nicht.
2. Parameterabgleich mit originalen SDK-Definitionen, einschließlich `get_index_scope`. Gemeinsame Validierung einmal; konkrete Handlerfälle für Defaults, Zero, Filter-before-count/cap, Sortierung und Selektoren. Genau-einer-Selektoren: beide/keinen und gültige Einzelargumente. Kombinationen nur für abweichende Verträge/Regressionen.
3. Jede Handoffart einmal bis zur echten Consumerantwort derselben Runtime. Unknown/foreign/stale/Ambiguität am gemeinsamen Resolver; weitere Fälle nur für abweichende Handlerwege.
4. Budgetierte Toolprojektionen und gemeinsame Fehler-/Recovery-/Paging-/Replayregeln gemäß folgendem Abschnitt. Keine identischen Fehlerprüfungen je Tool/Target.
5. Deterministisches Loading/Poll/Cancellation an echten Source-/Assembly-/Operationsbesitzern; Folgeaufruf bis zum Ergebnis je unterschiedlichem Routingweg. Bei übrigen Tools Anbindung an diesen Weg kontrollieren, keine identischen kalten Ladungen wiederholen.

Kein dauerhafter JSON-Katalog, Exportframework oder separate Parametermatrix.

## Konkrete fachliche Fehlerfälle

Pflichtfälle am produktiven Handler ohne MCP-Transport: andere Pflichtfelder gültig, Budgets ausreichend. Unbekanntes `h:` bedeutet syntaktisch gültig, aber nicht registriert. Ein früher Fehler in einem anderen Feld ersetzt den Fall nicht.

| Tool | Ungültiger beziehungsweise fehlerhafter fachlicher Fall | Erwartung |
|---|---|---|
| `find_symbol` | Weder `pattern` noch `namePatterns` angegeben | `INVALID_ARGUMENT` |
| `get_symbol_body` | Unbekanntes Assembly-`h:` als Symbolauswahl | `HANDOFF_UNKNOWN` |
| `get_file_skeleton` | Source-Dateiauswahl `Missing.cs`, die nicht im Target existiert | `INVALID_ARGUMENT` |
| `get_class_structure` | Unbekanntes Assembly-`h:` als Typauswahl | `HANDOFF_UNKNOWN` |
| `get_namespace_tree` | Nicht unterstütztes `kind` | `INVALID_ARGUMENT` |
| `get_index_scope` | Existierende verwaltete Assembly als Target | `INVALID_ARGUMENT` |
| `get_call_tree` | Nicht unterstützte `direction` und unbekanntes `h:` | `INVALID_ARGUMENT`; Richtungsprüfung vor Symbolauflösung |
| `find_references` | Unbekanntes Source-`h:` als Symbolauswahl | `HANDOFF_UNKNOWN` |
| `get_type_hierarchy` | Unbekanntes Assembly-`h:` als Typauswahl | `HANDOFF_UNKNOWN` |
| `find_implementations` | Unbekanntes Assembly-`h:` als Symbolauswahl | `HANDOFF_UNKNOWN` |
| `get_impact` | Leerer erforderlicher `symbolIdentifier` | `INVALID_ARGUMENT` vor Symbolauflösung |
| `dependency_graph` | Weder Datei- noch Symbolauswahl gesetzt | `INVALID_ARGUMENT` |
| `resolve_type_origin` | `symbolIdentifier` und `typeName` beide leer | `INVALID_ARGUMENT` |
| `get_assembly_context` | Nicht unterstütztes `detailLevel` und unbekanntes `h:` | `INVALID_ARGUMENT`; Detailprüfung vor Symbolauflösung |
| `inspect_assembly` | Nicht unterstütztes `detailLevel` | `INVALID_ARGUMENT` |
| `search_assembly` | Nicht unterstütztes `searchKind` | `INVALID_ARGUMENT` |
| `find_assembly_extensions` | Nicht unterstütztes `detailLevel` | `INVALID_ARGUMENT`; fehlender/leerer Receiver bleibt dagegen gültig |
| `get_feature_context` | Leerer Symbolidentifier | `INVALID_ARGUMENT` vor Source-Scan |
| `get_test_context` | Leerer Symbolidentifier | `INVALID_ARGUMENT` vor Source-Scan |
| `get_server_health` | Absoluter Pfad zu einem nicht existierenden Target | `INVALID_ARGUMENT`; keine Targetladung |
| `reload_config` | Konfigurierte Datei mit fehlerhaftem JSON beziehungsweise unzulässigem `minimumLogLevel` | `CONFIG_INVALID`; Settings und Version unverändert |

Fehler: `IsError`, sichere Ursache, ausführbare nächste Aktion; gültiger Folgeaufruf bleibt möglich. Ungültige Enums zusätzlich direkt am Handler prüfen, damit Schemaabweisung die Reihenfolge nicht verdeckt. Tiny-Budget-`InvalidParams` ist ein eigener Budgetfall.

## Verbindliche Budget-, Loading- und Pagingfälle

Jedes Navigationstool liefert eine tatsächlich gemessene Projektion unter separat begrenzten Bytes/Tokens. Gemeinsame Formatter-/Storefälle und repräsentative Handler je unterschiedlichem Routing-/Antwortweg prüfen Fehler und Recovery einmal; zusätzliche Fälle betreffen zusammengesetzte Antworten, Domaincursor, atomare Wartung und Regressionen.

Bei kleinster Bytegrenze bereits passende Projektionen erzeugen keinen künstlichen Fehler durch Bytes unter 512. Passt der Pflichtfehler nicht ins Tokencap, gilt `InvalidParams`.

Recovery wiederholt beide angebotenen Minima unverändert bis zu einer ausführbaren Success-/Page-/Error-/Controlantwort. Immutable Minima entsprechen exakt der gemessenen finalen Projektion; dynamisches Health prüft die konservative Snapshotgarantie aus Kapitel 01. Eine erste Recoveryseite muss fortsetzbar sein.

| Toolgruppe | Verbindliche konkrete Projektionen |
|---|---|
| `find_symbol` | begrenzte Trefferprojektion; vollständige Rekonstruktion einer mehrseitigen Trefferliste |
| `get_symbol_body` | line-safe mehrseitiger Body und Batch-/Fenstergrenzen |
| `get_file_skeleton` | Skeletonprojektion mit tatsächlicher äußerer Fortsetzung |
| `get_class_structure` | direkte Structureprojektion unabhängig vom Context; Sortierung vor Cap |
| `get_namespace_tree`, `get_index_scope` | je eigene begrenzte Textprojektion; fachliche Tiefe/Caps bleiben truncation; ausführbare Fortsetzung des Indexscopeberichts |
| `get_call_tree` | begrenzte Graphprojektion in ASCII und Mermaid; ausführbare Token-Recovery statt ausschließlich one-token fallback |
| `find_references`, `get_type_hierarchy`, `find_implementations` | je eigene begrenzte Projektion mit unveränderten fachlichen Grenzen |
| `get_impact` | Source-/Assembly-Symbolresultat; gemeinsame Operation-/Budget-/Fortsetzungsmechanismen |
| `dependency_graph`, `resolve_type_origin` | eigene begrenzte Graph-/Originprojektion; keine neuen Domaincursor |
| `get_assembly_context` | gesamter gewählter Sectionmix einschließlich mehrseitigem Body; Sectionfehler und exakte Recovery der zusammengesetzten Antwort |
| `inspect_assembly` | 512-Byte-Anfrage mit ausführbarer Seite beziehungsweise Recovery; äußere Pages und `v1.`-Domainpages getrennt und ineinander geschachtelt |
| `search_assembly` | begrenzte Searchprojection; Domainreplay plus äußere Pages; finale matched-file-Truncation ohne falschen Cursor |
| `find_assembly_extensions` | begrenzte Extensionprojektion; Receiver optional und Resultcaps |
| `get_feature_context`, `get_test_context` | eigene begrenzte Contextprojektion; leere Identifier bleiben INVALID_ARGUMENT vor Scan |
| `get_server_health` | vollständiger atomarer Snapshot mit konservativ ausführbarer Paar-Recovery und winzigem Envelopefallback; Targetstatus ohne Load; Loading/Paging nicht anwendbar |
| `reload_config` | Acknowledgement und Configerrors unter byte-/tokenseitiger Grenze; exakte Paar-Recovery wo darstellbar; kein Settingspublish bei abgelehntem Budget; Loading/Paging nicht anwendbar |

Gemeinsame Pagination: vollständige immutable Rekonstruktion ohne Auslassungen/Doppelungen/Unicodeschäden; Replay, Budgetänderung, fremder Target/Query, Tokenablauf und Nachfolgetokenexpiry am Store. Anbindung aller pagingfähigen Tools kontrollieren; keine identischen Storetests je Tool. Fachliche Domaintruncation bleibt zulässig.

Kaltes Source-/Assemblyloading bis zur fachlichen Antwort, Operationskontrolle je unterschiedlichem Weg und mindestens zwei unabhängige Waiter mit Cancellation eines Waiters prüfen. Wartung lädt keine Targets.

## Testauswahl und Fixturegrenzen

Allgemeine Testkategorisierung und offizielle Filter werden an den tatsächlichen Testablauf angepasst. Git-Änderungsermittlung, Git-Fixtures, Git-spezifische Antwortbudgets und der Git-Prozesslebenszyklus sind kein Produktvertrag.

## Host-, Lifecycle-, Read-only- und Assemblynachweise

Registrierung: exakt 21 Tools, korrekte ReadOnly/Destructive/Idempotent/OpenWorld-Metadaten und vollständige Purpose-/Parameter-/Schemas aus originalen annotierten Methoden. Read-only Audit des realen Hostaufbaus bestätigt Registrierung, Runtime und Filter, ohne Handshake.

Validierung einmal: Required, unbekannte Felder einschließlich `$ref`-/composed Roots, Null, Arrays, Typen, fractional/out-of-range int, Enums, Bindbarkeit. Wire-Namen nach `AIParameterNameAttribute`/CLR, nicht Serializer-Policy. Externe Schemareferenzen ohne Netzwerk abweisen; sichere `fieldPath`, unsafe-key-Fallback `$`. Abweisung führt keinen Handler aus; gültiger Folgeaufruf bleibt möglich. Interne Extraktion ändert SDK-Binder/Regeln nicht.

Lifecycle komponentennah: Disposal, parallele unabhängige Aufträge, initiale/Pollwaiter-Cancellation, Ablauf/Capacity, Stopmitteilung, frische Runtime ohne alte Handles. Stdoutsauberkeit am Logging-/CLI-Writer und im Host-Codeaudit; kein JSON-RPC-/EOF-Gesamtprozess.

Read-only: echter Loader mit kalten Fixtures, zwei Solutions mit gleichen Projektnamen, Datei-/Verzeichnissnapshots einschließlich obj/bin, Custom-Targets aus Kapitel 01. Endvergleich nach Dispose/Scratchcleanup. Navigation/Wartung schreiben weder Inputs noch Benutzerconfig. Bezeichnete Fixtureänderungen für Staleness sind keine Navigatorwrites.

Assembly: reale Root→B→C-PEidentitäten, gleiche/mehrdeutige Namen, direkt referenzierender Root, same-identity Ersatz, Missing/Native/Locked, Rawinputs/strikte opake Owner, Implementierungen/Overrides und Handoff→Body. Hierarchie/Implementierungen bleiben rootbezogen; Closure nur bei referenzfähigen Tools. Keine falschen Metadata-Sourcehandoffs oder verborgenen Context-Sectionfehler.

## Offizielle Gates und Nachweise

Nach abgeschlossener Testkategorisierung gelten folgende Gates für die spätere Umsetzung und deren Commits:

Pro Code-Slice: offizieller Build und engste betroffene Auswahl über `test-fast.ps1`/`test-integration.ps1`, bei beiden betroffenen Bereichen beide Auswahlen. Filter anhand geänderter Komponenten/Aufrufer begründen. Keine betroffene Pflichtprüfung weglassen, kein vollständiger Integrationstestlauf je kleinem Commit. Diese auftragsbezogene Regel bei Umsetzung in Git-/Verifikationsregeln und Build-/Testdokumentation festhalten.

Abschluss: einmal Build und ganze zulässige Routineauswahl über `test.ps1`, das FastTests und IntegrationTests enthält. Kein zusätzlicher vollständiger Lauf der Einzelskripte. Wiederholung nur nach betroffenen Änderungen, Fehlern oder konkreten Bedenken. Dokumentationsänderungen: Diffreview und `git diff --check`.

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test.ps1 -Filter 'Category!=E2EIntegration'
```

Fokussiert beispielsweise: `pwsh -File ./scripts/test-integration.ps1 -Filter '(FullyQualifiedName~AffectedComponent)&(Category!=E2EIntegration)'`, bei FastTests mit `test-fast.ps1`. Platzhalter durch echte Auswahl ersetzen. Keine Direkt-Dotnet-Testabkürzung. Build: 0 Warnungen/Fehler; erforderliche Tests bestanden, keine unbeabsichtigten Skips oder leere Auswahl. Betroffene zulässige Extendedfälle gezielt mit `-IncludeExtended` plus E2E-Ausschluss nachweisen.

Reproduzierbarer Defekt: geeigneter Nicht-E2E-Test zuerst rot, nach Fix grün. Korrekten Code nicht künstlich zurücknehmen. Nicht deterministisch reproduzierbare Fälle begründen und stärksten zulässigen Nachweis verwenden.

Gates, Restores und Reviews seriell. Commit, Exitcodes, Auswahl und Ergebniszahlen im vorhandenen Nachweis sichern; überschreibbare Logs/TRX allein genügen nicht. Rote/unvollständige Pflichtprüfungen bleiben offen. Dieser Konzeptauftrag führt ausschließlich Dokumentationsprüfung aus.

## Kompakte englische Toolreferenz und Setupdokumentation

`docs/tools/README.md`: alle 21 Tools auf einer englischen Seite mit Zweck, Targets, Wire-Parametern/Verwendung, Defaults/Caps und Besonderheiten. Gemeinsame Budget-/Recovery-/Handoff-/Pagingregeln einmal beschreiben oder verlinken. Beispiele für gemeinsame Muster und abweichende Verträge, kein Parameterkreuzprodukt. Gegen originale SDK-Definitionen/Handler abgleichen; kein JSON-Export, keine Exportpipeline oder 21 Einzeltoolseiten.

`docs/setup/README.md`: gemeinsame Voraussetzungen, Windows-Exe, lokales Stdio, absoluter Pfad, Argumente/Config, Loggingtrennung; kurze Abschnitte für Claude Desktop, Cursor und Antigravity. Clientconfigname/-Ort/-Syntax gegen aktuelle offizielle Dokumentation mit Quelle/Prüfdatum prüfen. Keine Einzelsetupseiten, Installation, Deployment, Benutzerconfigänderung oder Clientstarttests. Ungestartete Clients nicht als praktisch verifiziert bezeichnen.

Betroffene `docs/mcp-*`, `docs/navigation/*`, Build-/Testdokumentation, Indizes, Root-README und Rules mit den implementierten Änderungen aktualisieren. Pläne bleiben im Task. Dokumentationsreview prüft Links und Übereinstimmung mit implementierten Verträgen; die kompakte Abnahmematrix belegt den geprüften Stand.
