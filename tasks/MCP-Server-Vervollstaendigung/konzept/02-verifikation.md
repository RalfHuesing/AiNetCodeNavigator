# 02 — Verifikation, Dokumentation und Abnahme ohne E2E

Dieses Kapitel gehört zu [Konzept.md](../Konzept.md). Es definiert die verbindliche Abnahme aller fachlichen Verträge mit zulässigen Nicht-E2E-Nachweisen.

## Ausgeschlossene und zulässige Testwege

E2E ist in diesem Vorhaben ein automatisierter Test, der einen vollständigen Produkt-/Lab-Einstieg aufruft und über dessen äußere Schnittstelle den zusammenhängenden Gesamtablauf bis zur Navigation beziehungsweise Artefaktpublikation prüft. Ausgeschlossen sind insbesondere Child-Process-MCP-Stdio-Tests, MCP-Client/Server-Handshake oder JSON-RPC über Streams, auch in-memory, und vollständige Lab-CLI→Worker→Navigation→Artefakt-Prozessketten. Ein anderer Testname, ein internes Fixturetarget oder eine Platzierung in FastTests ändert diese Grenze nicht.

Zulässig sind Unit-/Komponententests der echten Registrierungsdefinitionen, gemeinsamen Validierung/SDK-Funktionsbindung, Handler, Formatter, Stores und Runtime ohne MCP-Server/-Client/-Transport. Zulässig sind begrenzte Integrationen konkreter Komponenten mit Roslyn/MSBuild, Dateien, Decompiler, dem Git-Prozessowner oder einer isolierten Named-Pipe-Komponente. Sie starten keinen vollständigen Produkt-/Labprozess und prüfen nicht die Gesamtkette. Tests von Registrierungs-/SDK-Adapterobjekten dürfen deren öffentliche API verwenden, ohne einen Server oder eine Transportverbindung zu erzeugen.

Bewertete Agentenuntersuchungen über die transportlose Lab-Exe sind verpflichtende manuelle/agentische Experimente mit separat dokumentiertem Outcome. Sie sind keine automatisierten E2E-Integrationstests und kein Stdio-Transportnachweis. Die Exe automatisiert Transportkontrolle und Archivierung, nicht Modellstart, fachliche Aufgabenlösung oder Fixes. Diese ausdrückliche Abgrenzung gilt auch für echte Workerprozesse in den Agentenruns.

Vor neuen Testgates werden die existierenden Testfälle nach ihrem tatsächlichen Ablauf inventarisiert. Alle ausgeschlossenen Fälle erhalten `Category=E2EIntegration`; das schließt die vorhandenen MCP-Stdio-/SDK-Streamfälle und vollständige externe Reportpublikationsläufe ein. Assertions und ausführbare bestehende E2E-Tests bleiben erhalten. Gemischte Testklassen erhalten die Kategorie am einzelnen Fall oder werden bei unveränderten Assertions nach Testzweck getrennt. Es wird kein E2E durch eine fehlende Kennzeichnung in einen Routinegate geschmuggelt.

Die tatsächliche Ausschlussauswahl wird vor dem Lauf anhand Testinventar und Kategorien nachgewiesen. Im Vorhaben werden die offiziellen Testskripte ausschließlich mit dem expliziten Filter `Category!=E2EIntegration` ausgeführt; engere Filter werden mit diesem Ausdruck per AND kombiniert. Das Buildskript besitzt keinen Testfilter und läuft normal. `ExtendedIntegration` bleibt zusätzlich standardmäßig ausgeschlossen. `-IncludeExtended` darf nur für konkret betroffene, zuvor als Nicht-E2E eingestufte Fälle verwendet werden, immer zusammen mit dem E2E-Ausschlussfilter. Ein unselektierter vollständiger Suite-Lauf oder Release-E2E-Gate gehört nicht zum Auftrag.

## Nachweismatrix für alle 22 Tools

Die Implementierung legt unter `docs/tools/` eine aus der produktiven Registrierung gewonnene vollständige Definition und Parameterreferenz an. Sie erstellt `Abnahmematrix.md` in diesem Taskverzeichnis als Nachweis des geprüften Implementierungsstands. `docs/` enthält implementierte Fakten, keine offenen Pläne.

Die Abnahmematrix enthält für jede tatsächlich relevante Zelle Tool, Targetart, Fall, öffentliche Requestparameter, erwartete fachliche Fakten/Status, exakten Testnamen, ausgeführten Gate, Commit und Ergebnis. Zellen verwenden genau `passed`, `failed`, `not_run` oder `not_applicable`. `not_applicable` benötigt den konkreten Vertragsgrund. Ein `not_run` oder fehlendes Testresultat ist kein Erfolg. E2E-Zellen erhalten `not_run` mit Grund `excluded_by_user`; sie blockieren die hier festgelegte Nicht-E2E-Abnahme nicht.

Für jedes Tool und jede unterstützte Targetart gelten:

1. Mindestens ein fachlich geprüftes normales Resultat und ein tatsächlicher toolbezogener Invalid-/Fehlerfall. Ein gemeinsamer Schemafiltertest ersetzt keine fachliche Handlerprüfung.
2. Jedes veröffentlichte Argument erhält einen Normal-/Wirkungsnachweis und alle auf es zutreffenden Boundaryklassen: omitted, gültiger Wert, null sofern advertised, zero falls definiert, Minimum/Maximum und außerhalb Range, gültige/ungültige Enums, leere/zu große Arrays oder Strings sofern beschränkt. Genau-einer-Selektoren prüfen beide/keinen und jedes gültige Einzelargument. Kein exhaustives Kreuzprodukt; zusätzliche Kombinationen sind dort Pflicht, wo Selektoren, Caps, Querybinding oder Scopes miteinander wirken.
3. Filter-before-count/cap, jede publizierte Sortierung, Defaults/Zero-Normalisierung und zielabhängige Semantik werden im tatsächlichen Handler geprüft. Fälle der Sourceparameter im Assemblymodus weisen deren dokumentierte Inapplicability nach.
4. Jede ausgegebene Handoffart wird mindestens einmal beim passenden Consumer derselben Runtime zu tatsächlicher Source/Body/Struktur aufgelöst; unknown/foreign/stale und Ambiguität werden am gemeinsamen Resolver sowie am jeweils abweichenden Handlerweg geprüft. Eine sichtbare `h:`-Zeichenfolge ohne Folgeaufruf ist kein Roundtripbeweis.
5. Alle publizierten Tools besitzen einen ausführbaren Byte-/Tokenbudgetvertrag einschließlich Success, toolbezogenem Error und Recovery. Shared Formatter-/Storetests belegen gemeinsame Semantik; echte Toolprojektionen schließen die jeweiligen Zellen.
6. Vorhandene äußere Pagingwege, Domaincaps und Domaincursor werden entsprechend ihrer unterschiedlichen Semantik nachgewiesen. Keine `not_applicable`-Markierung nur wegen einer zu kleinen Fixture.
7. Loading, Operationspoll und Cancellation werden deterministisch an echten Ownershipgrenzen mit steuerbarer Lade-/Operationkomponente geprüft. Flaky Wartezeiten oder ein plausibler Text ohne folgende erfolgreiche Operation genügen nicht.

Die vollständige Parameterliste wird aus dem produktiven SDK-ProtocolTool-Katalog des geprüften Stands erzeugt und nach jeder Vertragsänderung aktualisiert. Das in Kapitel 01 festgelegte `get_index_scope` einschließlich Budget-, Operations- und Fortsetzungsparametern gehört in diesen Katalog.

## Konkrete fachliche Fehlerfälle

Die folgenden Fälle sind Pflichtprüfungen der echten Handler über den gemeinsamen validierenden Dispatcher. Die nicht genannten Pflichtparameter erhalten gültige Fixturewerte und beide Budgets sind groß genug für die fachliche Fehlerantwort. Ein unbekanntes `h:` ist syntaktisch gültig, aber in dieser Runtime nicht registriert. Ein früher Fehler in einem nicht relevanten Pflichtfeld ersetzt den erwarteten Fall nicht. Zusätzlich gelten die Parameter-/Target-/Boundaryprüfungen oben für sämtliche unterstützten Wege.

| Tool | Ungültiger beziehungsweise fehlerhafter fachlicher Fall | Erwartung |
|---|---|---|
| `find_symbol` | Weder `pattern` noch `namePatterns` angegeben | `INVALID_ARGUMENT` |
| `get_symbol_body` | Unbekanntes Assembly-`h:` als Symbolauswahl | `HANDOFF_UNKNOWN` |
| `get_file_skeleton` | Source-Dateiauswahl `Missing.cs`, die nicht im Target existiert | `INVALID_ARGUMENT` |
| `get_class_structure` | Unbekanntes Assembly-`h:` als Typauswahl | `HANDOFF_UNKNOWN` |
| `get_file_tree` | Nicht unterstütztes `view` | `INVALID_ARGUMENT` |
| `get_namespace_tree` | Nicht unterstütztes `kind` | `INVALID_ARGUMENT` |
| `get_index_scope` | Existierende verwaltete Assembly als Target | `INVALID_ARGUMENT` |
| `get_call_tree` | Nicht unterstützte `direction` und unbekanntes `h:` | `INVALID_ARGUMENT`; Richtungsprüfung vor Symbolauflösung |
| `find_references` | Unbekanntes Source-`h:` als Symbolauswahl | `HANDOFF_UNKNOWN` |
| `get_type_hierarchy` | Unbekanntes Assembly-`h:` als Typauswahl | `HANDOFF_UNKNOWN` |
| `find_implementations` | Unbekanntes Assembly-`h:` als Symbolauswahl | `HANDOFF_UNKNOWN` |
| `get_impact` | `symbolIdentifier` und `gitRef` gleichzeitig gesetzt | `INVALID_ARGUMENT` |
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

Jede fachliche Fehlerantwort setzt `IsError` und enthält sichere Ursache sowie ausführbare nächste Aktion. Nach dem Fehler bleibt ein gültiger Dispatcheraufruf möglich. Schemaverworfene Enumwerte werden zusätzlich am direkten Handler geprüft, damit der frühe Validator die erforderliche fachliche Reihenfolge nicht verdeckt. Ein Sanitized-`InvalidParams`-Fallback wegen eines nicht darstellbaren Envelopes gehört ausschließlich zu den gesonderten Tiny-Budget-Prüfungen.

## Verbindliche Budget-, Loading- und Pagingfälle

Alle zwanzig Navigationstools werden transportlos am gemeinsamen Produktionspfad geprüft. Byte und Token gelten separat: eine konkrete fachliche Ergebnisprojektion so begrenzen, dass sie entweder einen korrekten ausführbaren Budgetfehler oder eine passende Seite erzeugt; bei Budgetfehler beide angegebenen Werte unverändert für den Retry verwenden; das tatsächliche Folgeresultat prüfen.

Ist für eine konkrete Toolprojektion innerhalb der publizierten Bytegrenzen keine Byte-Budgetfehlersituation konstruierbar, wird stattdessen die kleinste gültige Bytegrenze mit passenden vollständigen Ergebnissen beziehungsweise erfolgreichen Seiten nachgewiesen. Die Errorzelle wird begründet `not_applicable`, nicht künstlich durch ungültige Bytes unter 512 erzwungen. Dasselbe gilt für atomare Normalantworten unter 512 Bytes. Ein Tokencap, der den Pflichtfehler nicht darstellen kann, prüft `InvalidParams`, nicht einen angeblich fehlenden `RESPONSE_BUDGET_TOO_SMALL`-Envelope.

Bei tatsächlicher Budgeterror-Recovery muss das Paar zur finalen ausführbaren Success-, Page-, Error- oder Controlprojektion passen. Für immutable Projektionen werden gemessene Bytes/Tokens und angebotene Minima exakt verglichen. Für dynamisches Health wird die ausdrücklich konservative Paar-/Snapshotgarantie getestet. Eine Recovery kann legal eine erste Seite statt den vollständigen Ergebniskorpus liefern; diese Seite muss ausführbar fortsetzbar sein.

| Toolgruppe | Verbindliche konkrete Projektionen |
|---|---|
| `find_symbol` | eigenes Budgetpaar für Trefferausgabe/Fehler; kaltes Loading→Retry; vollständige Rekonstruktion von 200 Treffern über äußere Pages |
| `get_symbol_body` | Body- und Fehlerprojektion; Loading; line-safe 800-Zeilen-Rekonstruktion und Batch-/Fenstergrenzen |
| `get_file_skeleton` | eigenes Skeletonbudget/-Fehler; Loading; tatsächliche äußere Fortsetzung und vollständige Rekonstruktion |
| `get_class_structure` | direktes Structurebudget/-Fehler unabhängig vom Context; Loading; äußere Pages; verbindliche Sortierung vor Cap |
| `get_file_tree`, `get_namespace_tree`, `get_index_scope` | je eigener Byte-/Tokenresultatweg, kaltes Loading und äußere Textrekonstruktion; fachliche Tiefe/Caps bleiben truncation; Indexscopeparameter zuerst ergänzen |
| `get_call_tree` | bounded Graph-/Fehlerprojektion und exaktes Tokenpaar statt nur one-token fallback; Loading und äußere ASCII-/Mermaidpages |
| `find_references`, `get_type_hierarchy`, `find_implementations` | je eigener Resultat-/Fehler-/Loadingweg und äußere Rekonstruktion, mit unveränderten fachlichen Grenzen |
| `get_impact` | Source-/Assembly-Symbolresultat, Gitresultat, Pending-/Pollkontrollen, typisierte Fehler, Endresultat und äußere Pages; cold target loading separat vom Gitoperationtoken |
| `dependency_graph`, `resolve_type_origin` | je eigene erfolgreiche/Fehler-/Loadingprojektion; graph/origin Textpages; keine neuen Domaincursor |
| `get_assembly_context` | voller gewählter Sectionmix und dessen Error; exakte Recovery; kaltes Assemblyloading; vollständige Rekonstruktion eines 800-Zeilen-Bodys sowie der übrigen angeforderten Sections |
| `inspect_assembly` | 512-Byte-Anfrage mit ausführbarer Seite beziehungsweise Recovery anhand der tatsächlich angegebenen Minima; eigenes exaktes Tokenpaar; Loading; äußere Pages und `v1.`-Domainpages separat und ineinander geschachtelt |
| `search_assembly` | eigene Byte-/Token-Recovery für Searchprojection; Loading; Domainreplay und zusätzliche äußere Pages, finale matched-file-Truncation ohne falschen Cursor |
| `find_assembly_extensions` | eigenes Extension-/Errorbudget; Loading; äußere Pages; Receiver optional und Resultcaps |
| `get_feature_context`, `get_test_context` | je eigene Context-/Fehler-/Loadingprojektion und äußere Rekonstruktion; leere Identifier bleiben INVALID_ARGUMENT vor Scan |
| `get_server_health` | vollständiger atomarer Snapshot mit konservativ ausführbarer Paar-Recovery und winzigem Envelopefallback; Targetstatus ohne Load; Loading/Paging nicht anwendbar |
| `reload_config` | Acknowledgement und Configerrors unter byte-/tokenseitiger Grenze; exakte Paar-Recovery wo darstellbar; kein Settingspublish bei abgelehntem Budget; Loading/Paging nicht anwendbar |

Für alle äußeren Pagingfähigkeiten wird die vollständige bereits erzeugte immutable Ergebnisprojektion über sämtliche Seiten rekonstruiert, ohne Auslassungen, Doppelungen oder beschädigte Unicodezeichen. Replay, Budgetänderung, fremder Target/Query, abgelaufener Token und Nachfolgetokenexpiry sind separate Fälle. Ein Formatterunittest ohne echten Toolresultatweg schließt die Toolzelle nicht. Fachliche Domaintruncation darf nach vollständiger äußerer Rekonstruktion weiterhin bestehen.

Für jedes Navigationstool wird kaltes Loading mit der vorhandenen Resident-/Sessiongrenze deterministisch ausgelöst und die anschließende fachliche Antwort geprüft. Storeeigene Operationskontrolle wird je tatsächlich benutztem Routingweg geprüft; die gemeinsame Ownership wird mindestens mit zwei unabhängigen wartenden Aufrufen und cancellation eines Waiters nachgewiesen. Wartung ist kein Targetloading und wird entsprechend als `not_applicable` geführt.

## Host-, Lifecycle-, Read-only- und Assemblynachweise

Registrierung: exakt 22 eindeutige Tools, korrekte ReadOnly/Destructive/Idempotent/OpenWorld-Metadaten, vollständige Purpose-/Parameter-/Schemas und keine Lintertools. Die SDK-Adapterobjekte verwenden genau den gemeinsamen validierenden Produktionskatalog. Der Audit liest zusätzlich den tatsächlichen Stdio-Hostaufbau und bestätigt dessen Verdrahtung zum selben Katalog. Der reale Handshake wird nicht neu ausgeführt.

Validierung: Required, unbekannte Top-Level-Felder auch bei `$ref`-/composed Roots, Null, Arrays, primitive Typen, Numbers einschließlich fractional/out-of-range int, Enums und Bindbarkeitsprüfung. Es bleiben die publizierten Wire-Namen mit `AIParameterNameAttribute`/CLR-Fallback maßgeblich; Serializer-PropertyNamingPolicy benennt keine Methodenargumente um. Externe Schemareferenzen werden ohne Netzwerkzugriff abgewiesen. Fehlerpfade nennen sichere `fieldPath`-Orte mit unsafe-key-Fallback `$`. Ein schemaverworfener Aufruf führt keinen Handler aus; danach bleibt ein gültiger Dispatcheraufruf möglich.

Lifecycle: reale Runtime-/Store-/Registrydisposal, parallele Dispatcheraufträge, initiale Cancellation gegenüber Pollwaiter, Ablauf/Capacity, Stopmitteilung, eigener Gitkindprozess-Kill-and-Drain und frische Runtime ohne Handles der vorherigen Runtime. Testbar sind die Besitzer dieser Effekte; ein kompletter JSON-RPC-/EOF-Prozessablauf bleibt ausgeschlossen. Stdoutsauberkeit wird am Logging-/CLI-Writer und in einem read-only Codeaudit des Hoststarts geprüft.

Read-only: tatsächlicher Loader gegen kalte, kontrollierte Sourcefixtures einschließlich zwei Solutions mit gleichen Projektnamen; Start-/Endsnapshot von Dateien und Verzeichnissen einschließlich obj/bin und Custom-Targetfälle gemäß Kapitel 01; Dispose/Scratchcleanup vor Endvergleich. Keine Tests durch einen kompletten MCP-Host. Source-/Assemblynavigation und normale Wartung schreiben keine analysierten Inputs oder Benutzerconfig. Der Testtarget darf für einen ausdrücklich bezeichneten Stalenessfall durch die Fixture verändert werden; dies ist kein Navigator-Schreibzugriff und darf nicht als unveränderter Evaluationsrun gewertet werden.

Assembly: tatsächliche Root→B→C-PEidentitäten mit gleichen/mehrdeutigen Namen, nur direkt referenzierendem Root, same-identity Referenzersatz, Missing/Native/Locked, Rawinputs und strict opake Owner, Memberimplementierungen/Overrides und handoff→Body. Hierarchie/Implementierungen verbleiben auf ihrer publizierten Rootsemantik; Closuretests werden bei den tatsächlich referenzfähigen Relationshiptools ausgeführt. Metadata-only Symbole erhalten keine falschen Source-Handoffs. Sectionfehler in Context wird nicht neben scheinbarem Gesamterfolg verborgen.

## Offizielle Gates und Nachweise

Während der späteren Umsetzung sind nach abgeschlossener Testkategorisierung folgende offiziellen Nicht-E2E-Gates vorgeschrieben:

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test-fast.ps1 -Filter 'Category!=E2EIntegration'
pwsh -File ./scripts/test-integration.ps1 -Filter 'Category!=E2EIntegration'
pwsh -File ./scripts/test.ps1 -Filter 'Category!=E2EIntegration'
```

Bei einem fokussierten Test lautet der Filter beispielsweise `(FullyQualifiedName~AffectedComponent)&(Category!=E2EIntegration)`. Offizielle Skripte mit Filter werden verwendet; keine alternative Direkt-Dotnet-Testabkürzung. Build muss 0 Warnungen/Fehler liefern; alle gewählten erforderlichen Tests müssen bestehen. Unbeabsichtigte Skips oder eine leere Auswahl sind kein Nachweis. Die Auswahl wird vor Ergebnisbewertung gegen das Testinventar abgeglichen.

Vor einer reproduzierbaren Codefehlerkorrektur wird der Defekt durch einen fehlgeschlagenen geeigneten Nicht-E2E-Test nachgewiesen; nach der Korrektur muss derselbe Test bestehen. Bereits korrekter Code wird nicht künstlich zurückgenommen, um einen Fehlernachweis zu erzeugen; seine Vertragsprüfung am abgenommenen Stand bleibt erforderlich. Wenn deterministische Reproduktion nicht machbar ist, wird dies konkret begründet und der stärkste zulässige Nachweis verwendet.

Gates, Builds, Restores und Reviews laufen seriell. Exitcode, ausgewählte Tests und Passed/Failed/Skipped sowie geprüfter Commit werden gesichert; überschreibbare `temp/*.log`-/TRX-Dateien allein reichen nicht als dauerhafter Abschlussnachweis. Dokumentationsänderungen benötigen Diffreview und `git diff --check`, keine Produktgates. Dieser Konzeptauftrag führt nur diese Dokumentationsprüfung aus.

Eine rote Required-Prüfung bleibt offen. Ein unvollständiger Gate oder Tests einer nicht betroffenen Komponente dürfen sie nicht ersetzen. Das Lab darf erst beginnen, wenn der Serverteil unter diesen Testgrenzen tatsächlich akzeptiert wurde.

## Englischer Toolkatalog und Setupdokumentation

`docs/tools/README.md` indexiert genau 22 Tools. Die Implementierung exportiert vollständige ProtocolTool-Definitionen einschließlich Input-/Outputschema, Annotationen, Beschreibungen und publizierten SDK-Feldern aus dem echten Katalog in `docs/tools/catalog.json`; Schema-/Defaultangaben werden nicht unabhängig handgeschrieben. Englische Referenzseiten erklären je Tool Zweck, Targets, Parameter und Defaults/Caps, erfolgreiche Requests, Ergebnis-/Fehler-/Recoveryfelder, Handoff-/Ownergebrauch, Grenzen und tatsächliche Pagination. Beispiele werden über den Nicht-E2E-Dispatcher geprüft. Bei späteren Vertragsänderungen wird der Export neu erzeugt und mit den Seiten abgeglichen.

`docs/setup/README.md` sowie `claude-desktop.md`, `cursor.md` und `antigravity.md` erklären Voraussetzungen, gebaute Windows-Exe, lokales Stdio, absoluten Pfad, Argumente/Config und Protokoll-/Loggingtrennung. Clientconfigdateiname/-Ort/-Syntax wird bei Umsetzung gegen aktuelle offizielle Clientdokumentation verifiziert und mit Quelle/Prüfdatum belegt. Dies ist ein Dokumentationsauftrag; keine Installation, kein MCPdeployment und keine Änderung der echten Benutzerclientconfig. Ein Clientstarttest ist ausgeschlossen; nicht selbst gestartete Clients werden nicht als praktisch verifiziert bezeichnet.

Mit den implementierten Änderungen werden betroffene `docs/mcp-*`, `docs/navigation/*`, `docs/development/build-and-tests.md`, `docs/README.md`, Root-README und die relevanten Rules aktualisiert. Geplante Soll-Verträge bleiben bis dahin in diesem Task.

Ein Dokumentationsreview prüft gültige Links und die Übereinstimmung von Katalog, Referenzseiten, Setupseiten und tatsächlich implementierten Verträgen. Die Abnahmematrix dieses Auftrags ist der lokale Nachweis für den geprüften Stand.
