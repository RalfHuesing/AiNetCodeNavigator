# 03 — Agent Interaction Lab als letzter Arbeitsblock

Dieses Kapitel gehört zu [Konzept.md](../Konzept.md). Alle folgenden Festlegungen sind verbindlich. Das Lab beginnt nach der Nicht-E2E-Serverabnahme; es ist keine Voraussetzung für die vorherige Fertigstellung der Navigationsfunktionen.

## Intention und eindeutige Voraussetzung

Das Lab untersucht die Verständlichkeit der echten produktiven Tooldefinitionen, die Korrektheit und den Nutzwert ihrer Resultate und den Call-/Token-/Latenzaufwand. Es arbeitet lokal gegen den Entwicklungsstand ohne MCP-Transport, Deployment oder Codex-MCP-Konfigurationswechsel. Es liefert eine separate .NET-Windows-Exe unter tests/AiNetCodeNavigator.AgentLab/ mit Assemblyname AiNetCodeNavigator.AgentLab. Genau ein zusätzliches Konsolenprojekt, kein zusätzliches Bibliotheks- oder Testprojekt. Der unten spezifizierte gemeinsame produktive Katalog/Dispatcher gehört bereits zur Servervorbereitung und -abnahme gemäß Kapitel 01; sämtliche Lab-spezifischen Teile werden erst danach umgesetzt.

Vor Beginn gleicht der Implementierer den abgeschlossenen Serverstand mit diesem Kapitel ab und protokolliert Commit, Arbeitsbaum, Datum, die 22 Definitions-/Schema-/Bindingverträge, Runtime/DI, Budgets, Status-/Handoff-/Operations-/Continuationverträge, Assemblyowner und Testzugänge. Ein frischer gpt-6-luna/high-Verständnisprüfer ohne Planungsdialog prüft den ausführbaren Lab-Auftrag und meldet erforderliches Raten. Der unabhängige technische Audit verwendet gpt-6.1-sol/high. Korrekturen, die die festgelegte Intention/Verträge lediglich präzisieren, gehören zur später autorisierten Umsetzung; echte neue Produktentscheidungen gehen an den Nutzer. Die alte separate allgemeine Lab-Freigabesperre entfällt zugunsten der Freigabe dieses Gesamtkonzepts in Workflowschritt 2. Dieser Planungsauftrag startet weder diese Prüfer noch die Lab-Implementierung.

Lab → produktiver Toolcode → Core. Die regulären Produktprojekte referenzieren das Lab nicht und enthalten weder Lab-Modus/-Parameter noch Task-, Dump- oder Modelllogik. Das Lab wird für die reguläre Produktverteilung nicht benötigt. SDK-Typen, öffentliche SDK-Schemaerzeugung und SDK-Funktionsbindung sind erlaubt; McpServer/McpClient, RequestContext-Erzeugung, JSON-RPC, Handshake und MCP-Transport/Loopback sind ausgeschlossen. Es gibt keine kopierte Tool-API oder Lab-eigene Navigation. Die Lab-Exe startet keine Modelle/Codex-Agenten; der koordinierende Codex-Agent führt die getrennten Rollen und die begrenzte Verbesserungsschleife aus.

Die geprüften aktuellen Integrationsowner sind McpServerHost, NavigatorHostRuntime, McpArgumentValidationFilter, NavigationToolSupport, McpResponseFormatter, McpToolResults und LongRunningToolCallStore im vorhandenen Anwendungsprojekt sowie AssemblyReferenceResolver im Core. Der Host registriert bereits alle 22 Tools; die frühere Placeholder-Annahme ist überholt. Der Validator nimmt aktuell einen SDK-RequestContext entgegen und benötigt deshalb den nachfolgend beschriebenen gemeinsamen transportunabhängigen Einstieg. InternalsVisibleTo wird bereits über die bestehenden csproj-Dateien verwendet. Die konkreten öffentlichen SDK-/AIFunction-APIs werden gegen die tatsächlich gepinnten lokalen Pakete geprüft, ohne neue Reflectionabhängigkeit auf interne SDK-Typen.

## Eingefrorene eigene Repositorybasis für den Rundenvergleich

AiNetCodeNavigator ist die einzige verbindliche Repositorybasis. Zwei Targets bedeuten Source und Assembly desselben Repositorys, nicht zwei Solutions oder zwei fremde Repositories. Der Koordinator erstellt nach der Serverabnahme und vor dem ersten bewerteten Run einen sauberen lokalen, detached Sourcecheckout desselben akzeptierten Gitcommits unter dem ignorierten temp/agent-interaction-lab/target-baseline/. Die angegebene Source-Solution liegt in diesem Checkout. Die zu diesem akzeptierten Stand bereits gebaute Core-Assembly und benötigte eigene Referenzbinaries werden vor dem ersten Run in diesen Baselinebereich übernommen. Es wird kein zweites Produktrepository verwendet.

Die Einrichtung ist eine ausdrücklich protokollierte Vorbereitung außerhalb bewerteter Runs. Sie baut nicht innerhalb eines Lab-Kommandos, verändert keine analysierten Dateien während der Bewertung und führt keine untersuchten Binaries aus. Sie hält den Referenzbestand gemäß dem vorhandenen Resolver fest. Root-, Source- und Referenzhashes bleiben für alle Vergleichsrunden gleich; package-/frameworkreferenzen außerhalb der Baseline werden vor jedem Assemblydispatch unverändert geprüft. Fehlende erforderliche Binaries werden vor dem Run bereitgestellt. Ist ein erforderlicher Snapshot/Referenzbestand nicht vollständig nachweisbar, ist der Vergleich blockiert; unknown wird nicht zu unverändert erklärt.

Produkt und Target sind getrennte Messgegenstände: Die jeweils aktuelle Lab-Exe/Serverhandler werden zwischen Runs gebaut und korrigiert; die analysierte Source-/Assemblybasis wird nicht mitgebaut. Diese Trennung ist notwendig, weil ein Produktfix im eigenen Repo sonst zugleich den untersuchten Targetcode verändern würde. Die Startmetadaten in run.json enthalten productCommit sowie je Target targetCommit und die nachfolgend festgelegten Hashes. Die Taskreferenzen bleiben auf dem eingefrorenen Targetstand. Der normale Arbeitscheckout wird nicht mit während eines Runs erzeugtem oder geändertem Target verwechselt. Das Lab sucht keine Targets implizit; target-baseline-Pfade werden in der nachfolgend definierten lokalen Konfiguration eingetragen.

## Gemeinsamer produktiver Aufrufpfad

Der Hostbereich erhält einen internen, transportunabhängigen Toolkatalog und einen gemeinsamen Dispatcher. Zugriff für das Lab erfolgt über InternalsVisibleTo für AiNetCodeNavigator.AgentLab; keine neue öffentliche Produkt-API und kein weiteres Bibliotheksprojekt.

Der gemeinsame Katalog wird aus einer expliziten Liste der produktiven Toolklassen und deren annotierten Methoden aufgebaut. Keine Suche über sämtliche Repository-Assemblies. Namen müssen eindeutig sein. Jeder Registrierungseintrag besitzt genau eine originale AIFunction-Bindung, die validierende Hülle, die SDK-ProtocolTool-Definition und explizite BindingMetadata:

- Method: die MethodInfo der originalen annotierten Handlermethode, nicht die Methode der Hülle.
- SerializerOptions: exakt die Optionen, mit denen die originale Funktionsbindung erzeugt wurde.
- JsonParameters: Zuordnung von veröffentlichtem Wire-Namen zur originalen ParameterInfo. AIParameterNameAttribute hat Vorrang, sonst gilt der CLR-Parametername. PropertyNamingPolicy benennt Objektmember um, nicht Methodensignaturparameter. CancellationToken und aus DI gebundene Dienste fehlen in dieser Zuordnung.

Der produktive Registrierungsaufbau erstellt diese Metadaten selbst, bevor der Katalog veröffentlicht wird. Die gemeinsame Bindbarkeitsprüfung verwendet genau diese Zuordnung/Optionen. Sie gewinnt keine Informationen aus internen SDK-Funktionstypen per Reflection. Die SDK-Definition wird beim Registrierungsaufbau erzeugt und ihr InputSchema mit der Hülle verbunden; erst dann wird der vollständige Eintrag unveränderlich veröffentlicht. Ein halb initialisierter Eintrag darf nicht dispatcht werden.

Verbindlicher Weg:

1. Die gemeinsame Registrierung bindet produktive Toolinstanzen aus der bestehenden Runtime. Microsoft.Extensions.AI erzeugt die Funktionsbindung aus den originalen Methoden. Die gemeinsamen Optionen verwenden McpJsonUtilities.DefaultOptions; die Resultatbindung erhält CallToolResult als solchen und serialisiert ihn nicht vorzeitig in einen anderen Ergebnisvertrag.
2. Eine gemeinsame validierende Funktionshülle erhält Name, Beschreibung, Parameter-/Ergebnisschema und ursprüngliche Bindungsmetadaten. Sie validiert gegen die tatsächlich veröffentlichte SDK-InputSchema, führt anschließend genau die originale Funktionsbindung aus und liefert den produktiven CallToolResult.
3. McpServerTool.Create(AIFunction, options) adaptiert diese Hülle für den regulären SDK-Host. Toolname, Beschreibung, Annotationen, Schemas und Metadaten stammen aus den originalen Registrierungsinformationen; das Lab schreibt sie nicht neu.
4. Das Lab ruft dieselbe validierende Hülle direkt mit JSON-Argumenten und CancellationToken auf. Es erzeugt keinen RequestContext und keinen McpServer.
5. Der bisherige Argumentfilter delegiert seine Validierungslogik an einen gemeinsamen Einstieg mit registrierter Tooldefinition, expliziten BindingMetadata und Argumenten. Die produktive Registrierung validiert nur einmal an der gemeinsamen Hülle. Der Filter wird nicht zusätzlich um diese Hüllen gelegt.

Der vorhandene TryGetBindingMetadata-Reflectionadapter bleibt ausschließlich für die bestehenden direkten SDK-Fixturetools aus McpServerTool.Create(Delegate) nutzbar. Deren Filtertests zu Zahlenbindung, umbenannten Parametern und Serializeroptionen bleiben erhalten. Weder der neue produktive Katalog noch das Lab verwenden diesen Adapter; die entsprechenden Tests weisen beide Wege getrennt nach. Das Verbot neuer Reflection-Zugriffe auf interne SDK-Typen ist kein Auftrag, die vorhandene Fixtureabdeckung abzuschaffen.

Die gemeinsame Registrierung enthält ausschließlich Handler mit JSON-Parametern, CancellationToken und vorhandenen Anwendungsdiensten. Navigationstools dürfen für diese Verarbeitung keinen MCP-Server, RequestContext, Clientcallback oder Progress-Transport verlangen. Interne Loading-, Running- und Continuation-Ergebnisse bleiben produktive Resultate.

Schema-/Binderabweichungen, fehlende CallToolResult-Erhaltung und Abweichungen zum SDK-Pfad sind Abnahmefehler. Der Implementierer darf sie nicht durch einen manuellen Binder, kopierte Schemas oder einen versteckten MCP-Loopback umgehen. Die bestehenden Validierungsfälle, insbesondere unbekannte Felder, Null, Enumwerte, Zahlenbereiche und nicht bindbare Zahlen, bleiben erhalten. Ein unbekannter Toolname wird im gemeinsamen Dispatcher als produktive ProtocolException mit InvalidParams zurückgegeben; das Lab erfindet dafür keinen Navigationserfolg.

## Sitzung und Kommandozeilenvertrag

Die Windows-Abnahme verwendet NamedPipeServerStream/NamedPipeClientStream aus .NET mit CurrentUserOnly. Es gibt keinen HTTP-Dienst und keine Netzwerkadresse. Named Pipes übertragen ausschließlich Lab-Steueraufträge; sie implementieren kein MCP.

Eine Sitzung gehört genau einem Runverzeichnis und einem Workerprozess. Der Worker startet denselben Lab-Einstieg mit dem internen Befehl worker; Start erfolgt ohne sichtbares Fenster. Er hält Runtime, Toolinstanzen, Registry, Handoffs, Operationen und Fortsetzungen über alle Aufrufe dieses Runs am Leben. Andere Runs teilen keinen Workerprozess. Die gemeinsame Anwendungs-DI wird ohne AddMcpServer/Transport-/Serverdienste aufgebaut; IHostApplicationLifetime und andere Anwendungsdienste werden durch den normalen .NET Generic Host bereitgestellt. Workerlogs liegen im Runverzeichnis. Logging-/Konfigurationseinrichtung ist vom regulären Produktstart getrennt, verwendet aber dessen vorhandene Anwendungsbausteine.

Befehle der Lab-Exe:

| Befehl | Vertrag |
|---|---|
| start --config <absolute-path> | Prüft Zielkonfiguration/Pfade, erzeugt einen neuen Run unter temp/agent-interaction-lab/<runId>/, startet Worker und wartet höchstens 30 Sekunden auf Bereitschaft. Nur der Worker erzeugt Runtime/Katalog und prüft die vollständige Toolmenge vor ready. Gibt den absoluten Runpfad und session.json zurück; Fehler ergeben keine nutzbare Sitzung. |
| catalog --run <absolute-run-path> | Gibt den beim Start exportierten produktiven Katalog aus; kein erneuter Targetload und keine neue Registrierung. |
| task --run <path> --file <absolute-task-path> | Validiert und registriert eine neue task.json. Existierende Task-IDs werden nicht überschrieben. |
| call --run <path> --task <task-id> --tool <name> --args-file <absolute-path> [--parent <call-id>] | Sendet genau die vom Agenten gelieferten Argumente an den Worker. Die Task-ID ist Pflicht. Parent referenziert einen früheren Call derselben Task/Sitzung, verändert die Argumente aber nicht. |
| status --run <path> | Zeigt Sitzungszustand, aktuellen Call und verbrauchte Grenzen; ruft kein Produkttool auf. |
| stop --run <path> | Stoppt die Annahme neuer Calls, cancelt und beendet laufende Arbeit und disposed die produktive Runtime. Zweiter Stop ist erfolgreich und startet keinen Worker. |
| render --run <path> | Erzeugt nach Sitzungsende die Markdown-Dumps erneut aus vorhandenen JSON-Artefakten. Bei lebender Sitzung LAB_BUSY; ruft kein Produkttool auf. |

runId und callId sind vom Lab erzeugte lowercase GUIDs im N-Format. session.json enthält schemaVersion, runId, workerPid, pipeName, state und zuletzt abgeschlossene Call-ID. Der Pipename lautet ainetnav-lab-<runId>. Sitzungszustände: starting, ready, stopping, stopped, failed. Ein Workerlock verhindert zwei Besitzer desselben Runs.

Pipe-Nachrichten verwenden einen 4-Byte-Little-Endian-Längenpräfix und UTF-8-JSON, maximal 1 MiB je Steuerauftrag. Der Client liest die Argumentdatei einmal und sendet deren Bytes als argumentsBase64; der Worker verwendet genau diese Bytes statt die Datei erneut zu lesen. Steuerdaten enthalten schemaVersion, command, runId und die zum Befehl gehörenden Task-/Callfelder. Ein Produktresultat wird nicht über die Pipe dupliziert; die terminale Antwort enthält state, callId, exitCode und relative Artefaktpfade. task überträgt analog die gelesene task.json, die anderen Befehle nur ihre Steuerdaten. Ungültige Nachrichten werden vor Dispatch als LAB_INPUT_INVALID zurückgewiesen.

Der Client erzeugt die Call-ID vor dem Senden und gibt ID/Runpfad auf stderr aus. Wiederholung derselben ID startet nie einen zweiten Produktaufruf. Andere Task-/Tool-/Parentdaten oder andere Argumentbytes bei gleicher ID ergeben LAB_CALL_ID_CONFLICT. Der öffentliche call-Befehl erzeugt immer eine neue ID; Idempotenz schützt interne Übertragungswiederholungen, nicht eine erneute fachliche Ausführung durch den Agenten.

Verbindungsaufbau wartet höchstens fünf Sekunden. call wartet nach Annahme bis zum terminalen Resultat, höchstens 160 Sekunden; der Worker beendet Produktarbeit gemäß den eigenen Call-/Gracegrenzen. Bei Clienttimeout ist der Erfolg unbekannt: keine neue Call-ID automatisch senden, sondern die gespeicherten Ereignisse/Artefakte der gemeldeten ID lesen. Fehlt der Worker nach seinem Tod, status markiert die Sitzung failed und nennt unvollständige Calls. stop und render führen keine neuen Produktaufrufe aus.

Pro Sitzung wird höchstens ein Call gleichzeitig angenommen/verarbeitet, einschließlich Archivierung und Produktdispatch. LAB_BUSY ist eine Ablehnung vor Annahme: keine ID-Reservierung, kein Callverzeichnis, keine call_received-/call_failed-Ereignisse, keine Änderung von Task-/Sitzungszählern, Taskstartzeit oder Idletimer. Der Client hält ausschließlich die typisierte Ablehnung unter client-errors/ fest. Eine noch nie angenommene Busy-ID kann bei einer internen Wiederholung später angenommen werden.

Annahme reserviert unter einem gemeinsamen Gate die ID und ihre Task-/Tool-/Parentdaten sowie Argumentbytes und markiert die Sitzung als belegt. Schon reservierte IDs werden vor dem Busy-/Limitcheck erkannt: identische Wiederholung erhält denselben vorhandenen Zustand/dieselben Artefakte und startet nichts erneut; abweichende Daten ergeben LAB_CALL_ID_CONFLICT. Eine ID bleibt nach Annahme auch bei Lab-Eingabefehlern oder Absturz reserviert. Status und Stop bleiben während eines Calls bedienbar. Nach Clientverbindungsabbruch läuft ein bereits angenommener Call begrenzt weiter und speichert sein Resultat. Es gibt keinen automatischen Retry.

Task-/Sitzungsbudgets zählen erst den Eintritt in den gemeinsamen produktiven Dispatcher, einschließlich Toollookup, Argumentfehler, Polls und Fortsetzungen, nicht nur erfolgreiche Handlerausführung. Genau an diesem Eintritt startet beim ersten Produktcall die Taskzeit und wird der Idletimer zurückgesetzt. Syntaktisch ungültige Argumentdateien und andere Lab-Fehler verbrauchen keinen Produktcall und starten keine Taskzeit. Vor Annahme werden die bestehenden Limits geprüft; Ablehnungen erzeugen limit_reached, aber keine Call-ID-Reservierung oder Produktcall-Artefakte. request.sequence zählt angenommene Calls; event.sequence zählt Ereignisse. Beide sind monoton, aber nicht derselbe Zähler.

Feste Grenzen für Version 1:

| Grenze | Wert |
|---|---|
| Produktcalls je Task | 8, inklusive Fehler, Polls und Fortsetzungen |
| Zeit je Task | 5 Minuten ab Eintritt ihres ersten Calls in den produktiven Dispatcher |
| Zeit je Produktcall | 120 Sekunden; anschließend Cancellation |
| Calls je Sitzung | 200 |
| Gesamtdauer einer Sitzung | 60 Minuten |
| Idle-Ende | 15 Minuten ab ready beziehungsweise dem letzten Eintritt in den produktiven Dispatcher; Ablehnungen und reine Statusabfragen verlängern nicht |
| Cancellation-/Shutdown-Grace | 30 Sekunden; danach Worker beenden und Sitzung failed markieren |

Erreichte Grenzen verhindern neue Calls und werden als Lab-Ereignis ausgewiesen. Sie sind keine Produktfehler. Alle Zähler und Zeiten stehen im Protokoll. Taskzeit- oder Callzeitüberschreitung cancelt den aktiven Call; nach Rückkehr darf die Sitzung andere Tasks weiter bearbeiten. Wenn Arbeit binnen der Grace nicht endet, wird die gesamte Sitzung beendet. Nach einem Prozesscrash werden alte IDs/Tokens nicht in einem neuen Prozess wiederbelebt.

Reguläres Sitzungsende durch stop, Idle oder Gesamtdauer erfolgt in dieser Reihenfolge: Annahme schließen und stopping veröffentlichen → aktiven Call cancellen/drainen → produktive Runtime entsorgen und damit auch laufende Hintergrundoperationen beenden → Endfingerprints und summary.json schreiben → session_stopped und state=stopped veröffentlichen → Logger flushen und Worker beenden. Die 30-Sekunden-Grace gilt für Cancellation und Runtime-Dispose; für die anschließende Endfingerprint-Erfassung gelten weitere 30 Sekunden. Nicht rechtzeitig lesbare Snapshotdaten ergeben unknown; sie verlängern das Ende nicht unbegrenzt.

Bei unkontrolliertem Workercrash oder erzwungener Beendigung darf status unter exklusivem, freiem Workerlock session.json als failed und eine eigene recovery.json mit beobachteter Ausfallzeit/Grund schreiben. Es verändert keine Ereignisspur und erfindet kein session_failed oder normales Sitzungsende. Fehlt summary.json, sind Endfingerprints und snapshotStatus ausdrücklich unknown. Ein bereits vollständig veröffentlichtes summary.json bleibt als Beleg erhalten; ein Crash danach ist weiterhin ein Lifecyclefehler und macht die Sitzung nicht stopped.

CLI-Exitcodes: 0 für einen empfangenen CallToolResult, auch bei IsError=true; 1 für eine produktive ProtocolException; 2 für Lab-/Bedienfehler; 3 für Timeout, Cancellation oder unerwartetes Sitzungsende. stderr enthält nur Lab-Diagnostik. Beim call schreibt stdout exakt display.json inklusive abschließendem LF. Bei einem Resultat enthält diese Datei nur die originale produktive Resultatdarstellung; bei einer ProtocolException das unten definierte product_protocol_error-Envelope, bei Lab-Fehlern lab_error und bei Cancellation call_cancelled. Keine Toolerklärungen, Messdaten oder Erfolgskommentare in dieser Ausgabe. Die übrigen Befehle dürfen Lab-Metadaten ausgeben. Die stdout-Regeln des regulären MCP-Einstiegs werden nicht geändert.

## Konfiguration und Targets

Die lokale Konfiguration wird nicht versioniert; sie liegt unter dem bereits ignorierten temp/agent-interaction-lab/targets.local.json. Ein englisches Beispiel ohne maschinenspezifische reale Pfade wird als Dokumentation im Lab-Projekt versioniert. Die Konfiguration hat ausschließlich schemaVersion 1, repositoryRoot als absoluten Pfad zur AiNetCodeNavigator-Checkoutwurzel und targets als Liste. Jeder Targeteintrag hat id, kind (source oder assembly), repositoryRoot und targetPath, jeweils absolute Pfade. IDs bestehen aus ASCII-Buchstaben, Ziffern und Bindestrichen und sind eindeutig. Der Ausgabeordner ist fest unter repositoryRoot/temp/agent-interaction-lab/; alle vom Lab erzeugten Pfade werden auf Zugehörigkeit geprüft. Das Lab löscht keine alten Runs automatisch.

Die folgenden zwei Ziel-IDs sind für die erste Abnahme verbindlich:

| ID | Inhalt |
|---|---|
| navigator-source | AiNetCodeNavigator.slnx im eingefrorenen lokalen Snapshot dieses Repositorys |
| navigator-assembly | Bereits gebaute, separat eingefrorene AiNetCodeNavigator.Core.dll samt auflösbarem Referenzbestand dieses Repositorystands |

Die Navigator-Ziele und deren Binaries werden nur gelesen; das Lab startet dafür weder Restore noch Build. Fehlende Binaries müssen vor dem Lauf bereitgestellt werden. Es baut auch die eigenen Produktbinaries nicht implizit. Kein Aufruf des untersuchten Binaries.

Die Lab-eigene hostsettings.json wird beim Sitzungsstart mit minimumLogLevel=Information unter dem Runverzeichnis erzeugt und ausdrücklich als Konfigurationspfad der Runtime gesetzt. Wartungsszenarien bearbeiten nur diese Datei. Ein geplanter Fehlerfall darf sie vorübergehend ungültig machen oder entfernen; vor der nächsten Task wird ihr definierter Startzustand wiederhergestellt und erfolgreich geladen. Der normale Benutzer-Konfigurationspfad wird niemals verwendet.

Neue Targets sind über die gleiche lokale Konfiguration zulässig. Die zwei verbindlichen Abnahmeziele bleiben erforderlich; keine implizite Suche nach beliebigen Solutions.

Produktargumente werden nicht umgeschrieben: Der Testagent erhält die erlaubten absoluten Zielpfade und setzt targetPath selbst. Die Targetliste ist eine Experimentregel, keine Dateisystem-Sandbox. Ein Zugriff auf ein anderes echtes Repository wird als isolationStatus=violated bewertet; gezielte Fehlertests mit fehlendem oder ungültigem targetPath müssen in der zugehörigen Referenz ausdrücklich vorgesehen sein.

Beim Start und Sitzungsende werden je Repository HEAD, git status --porcelain und SHA-256 für die vorhandenen versionierten und nicht ignorierten unversionierten Dateien erfasst; gelöschte Dateien werden als fehlend protokolliert. Startdaten liegen unveränderlich in run.json; Enddaten und Statuswerte in summary.json. Hash-/Snapshotaufwand wird getrennt von Toolzeit gemessen.

Für jedes konfigurierte Assemblyziel erfasst der Worker vor ready zusätzlich dessen Hash und einen Referenzbestand. Er verwendet dafür den vorhandenen AssemblyReferenceResolver mit denselben Auflösungsregeln wie das Produkt, ohne Decompilation, Targetausführung, Build oder Befüllen der residenten Navigationsregistry. Er speichert kanonische Pfade, Identitäten/Auflösungszustände und SHA-256 aller erfolgreich aufgelösten Referenzen einschließlich der tatsächlich erzeugten MetadataReference-Dateipfade. Der Lab-Assembly wird dafür begrenzter InternalsVisibleTo-Zugriff auf diesen vorhandenen Core-Baustein gewährt; Core erhält keine Lab-Logik oder umgekehrte Projektabhängigkeit.

Vor jedem Assemblydispatch wird der Bestand mit demselben Resolver erneut geprüft; bei regulärem Sitzungsende werden ursprüngliche Pfadmenge, Hashes und Auflösungszustände erneut verglichen. Nicht aufgelöste/nicht lesbare Referenzen, neu hinzukommende Pfade ohne Ausgangshash oder ein nicht verfügbarer Abschlussbestand ergeben unknown für die Referenzvergleichbarkeit. Ein nachweislich veränderter Ausgangshash oder eine gelöschte Ausgangsdatei ist changed. Lab und produktive Assemblynavigation müssen denselben Resolver und dieselbe Referenzfingerprintlogik verwenden; eine abweichende produktive Auflösung darf nicht als unverändert behauptet werden. Der Vorabvergleich wärmt keinen Decompilation-/Navigationcache; reine Metadaten-/OS-Cacheeffekte werden als Preflightaufwand dokumentiert.

snapshotStatus ist unchanged_observed, changed oder unknown. changed hat Vorrang, sobald eine konkrete Differenz bewiesen ist; ansonsten ergibt unvollständige Erfassung unknown. unchanged_observed erfordert vollständige, gleiche Ausgangs-/Enddaten. Fehlende Enddaten sind nie unchanged_observed. Diese Vorher-/Nachher-Prüfung garantiert keine Erkennung vorübergehender Änderungen. Während eines bewerteten Runs finden keine Repositoryänderungen oder Rebuilds statt. changed/unknown schließt belastbare Vorher-/Nachher-Aussagen aus.

## Artefaktverträge

Alle Lab-eigenen Envelopes und jede Eventzeile verwenden schemaVersion 1, camelCase, UTC-Zeitangaben im ISO-8601-Format, UTF-8 ohne BOM und LF. Unbekannte Metadatenfelder, doppelte Keys und falsche Typen werden als Lab-Eingabefehler zurückgewiesen. Davon ausdrücklich ausgenommen sind rohe arguments.json-Bytes, hostsettings.json im originalen Produkt-Konfigurationsformat, der originale CallToolResult in response.json, seine identische Darstellung in display.json und die originalen ProtocolTool-Objekte innerhalb von tools.json. Diese Produktdaten bekommen keine Lab-Felder. Tatsächliche Toolargumente bleiben unverändert; Syntax-/Formfehler des Lab-Eingabewegs bleiben davon getrennt.

~~~text
temp/agent-interaction-lab/<runId>/
  run.json
  summary.json
  session.json
  recovery.json
  tools.json
  hostsettings.json
  events.jsonl
  tasks/<taskId>/task.json
  calls/<callId>/arguments.json
  calls/<callId>/request.json
  calls/<callId>/response.json
  calls/<callId>/error.json
  calls/<callId>/display.json
  calls/<callId>/metrics.json
  calls/<callId>/call.md
  report.md
  client-errors/<errorId>.json
~~~

- run.json hält unveränderliche Startmetadaten: runId, Produkt-HEAD/Arbeitsbaum, Paket-/SDK-/Renderer-Versionen, Targets und Startfingerprints, Grenzen und bekannte Agentenangaben. Unbekanntes ist null, keine erfundene Modellangabe.
- summary.json enthält schemaVersion, runId, endedUtc, endSnapshots, snapshotStatus, die Endzähler und den Abschlussgrund. Es wird nur nach kontrolliertem Ende geschrieben. run.json wird dafür nicht nachträglich verändert. recovery.json dokumentiert ausschließlich einen nachträglich beobachteten Prozessausfall, keine wiederhergestellten Produktresultate oder Endfingerprints.
- tools.json hat exakt die Hülle {schemaVersion: 1, tools: [...]} mit allen originalen ProtocolTool-Definitionen, ordinal nach Toolname sortiert. Beschreibungen, Parameterbeschreibungen, Schemas, Annotationen und weitere SDK-Felder werden nicht verkürzt. Es ist der Katalog der aktuellen SDK-Definition, keine Simulation einer ausgehandelten alten MCP-Protokollversion.
- arguments.json ist eine bytegetreue Kopie der Eingabedatei, einschließlich ungültigen JSONs.
- request.json enthält schemaVersion, runId, callId, sequence, taskId, parentCallId (oder null), toolName, receivedUtc, inputStatus und arguments. inputStatus ist valid_json für ein darstellbares Argumentobjekt, invalid_json für Syntaxfehler oder invalid_shape für eine andere JSON-Wurzel beziehungsweise doppelte Top-Level-Argumentnamen. arguments hält den geparsten unveränderten JSON-Wert oder bei Syntaxfehlern null; die originale Datei bleibt immer unter arguments.json erhalten.
- response.json enthält ausschließlich den originalen CallToolResult mit den gemeinsamen SDK-Serializeroptionen. Keine Lab-Felder im Produktresultat. Es existiert nur bei einem erhaltenen CallToolResult. Eine produktive ProtocolException steht in error.json und wird nicht zu einem normalen IsError-Resultat umgedeutet.
- error.json enthält schemaVersion, kind, callId, code und message. kind=product_protocol_error verwendet den ursprünglichen numerischen Protokollcode und die vom gemeinsamen produktiven Pfad freigegebene Message, ohne Stacktrace/InnerException; kind=lab_error verwendet einen LAB_...-Stringcode. kind=call_cancelled verwendet LAB_CALL_TIMEOUT, LAB_TASK_TIMEOUT oder LAB_SESSION_STOPPED und Exitcode 3. Wenn noch kein angenommener Call existiert, darf callId null sein; Ablehnungen dürfen ihre vom Client erzeugte, nicht reservierte ID nennen. Die gleiche Hülle wird bei einem Callfehler byteidentisch als display.json/auf stdout ausgegeben. Bei einem empfangenen Resultat gibt es keine error.json; IsError=true bleibt ein CallToolResult.
- Bei einem bereits angenommenen Call mit syntaktisch ungültigem JSON, Nicht-Objekt als Argumentwurzel oder doppelten Top-Level-Argumentnamen erfolgt kein Produktdispatch; request.json, das call_failed-Ereignis und error.json kennzeichnen ausdrücklich lab_error. Doppelte Top-Level-Argumentnamen sind im produktiven Dictionary-Vertrag nicht darstellbar und werden nicht still auf den letzten Wert reduziert. Fehlt die Eingabedatei vor Übertragung oder ist sie nicht lesbar, schreibt ausschließlich der Client eine typisierte Datei unter client-errors/. Es gibt dafür weder Annahme/ID-Reservierung noch Callverzeichnis oder Workerereignis. Ein falscher Wert innerhalb eines gültigen Argumentobjekts gelangt unverändert zur produktiven Schemavalidierung.
- display.json ist die genaue kompakte UTF-8-Darstellung des Resultats, die call auf stdout ausgibt, einschließlich aller Textblöcke, StructuredContent und IsError. Die Ausgabe endet mit LF; diese Ausgabeform wird getrennt vom Produkttextbudget gemessen. Eine ProtocolException oder ein Lab-Fehler wird als eindeutig typisierter Fehler dargestellt.
- metrics.json enthält Zeiten, Outputbytes und die unten definierten Tokenmessungen. Keine Messdaten werden in response.json eingefügt.
- events.jsonl ist eine vom Worker seriell geschriebene Ereignisspur. Jede Zeile enthält schemaVersion, sequence, utc, runId, taskId/callId (oder null), eventType und data. event.sequence ist monoton; data enthält Ereignisdetails. Typen: session_started, task_registered, call_received, dispatch_started, result_saved, call_failed, limit_reached, session_stopping, session_stopped, session_failed. session_failed wird nur vom lebenden Worker für einen von ihm selbst beobachteten Fehler geschrieben; nach einem Crash bleibt ein fehlendes terminales Event fehlend.
- Neue Artefakte werden unter temporärem Dateinamen geschrieben und atomar veröffentlicht. Existierende Call-/Taskinhalte werden nicht überschrieben. session.json wird atomar ersetzt, events.jsonl append-only geschrieben.
- request.json und Eingabekopie müssen veröffentlicht sein, bevor ein Produktdispatch startet. Schlägt dies fehl, findet kein Call statt. Fehlt nach einem Crash ein terminales Ereignis, bleibt der Call incomplete; keine erfundene Antwort und kein stiller Replay.
- start besitzt vorbereitende Run-/Konfigurationsdateien bis zur Workerübergabe. Danach ist der Worker alleiniger Autor der Laufartefakte. Der CLI-Client darf bei Fehlern vor Annahme ausschließlich einen eigenen, per GUID benannten Clientfehler unter client-errors/ speichern; er verändert keine Calls oder Ereignisspur. Nach Sitzungsende darf render ausschließlich erzeugte call.md-Dateien aktualisieren. status darf unter exklusivem Workerlock eine verwaiste session.json auf failed setzen und recovery.json schreiben; es erfindet keine summary.json oder Workerereignisse. Der Analyseagent besitzt report.md; die Exe schreibt keine freie Analyse.

call.md hat englische Überschriften in fester Reihenfolge: Identity, Task, Tool definition, Request, Outcome, Metrics. Tooldefinition und Request werden vollständig ausgegeben. Outcome erhält die originalen Textblöcke in ihrer Reihenfolge, StructuredContent und weitere Resultatfelder; bei Fehlern die typisierte Fehlerdarstellung. Es gibt keine freie Zusammenfassung oder nachträgliche Kürzung. Codefences sind mindestens drei Backticks und länger als jede Backtickfolge im eingeschlossenen Text. Das verhindert beschädigte Dumps bei Code oder Markdown im Ergebnis.

Gleiche gespeicherte Artefakte und gleiche Rendererversion erzeugen byteidentische call.md-Dateien. Neue Produkt-/Agentenläufe müssen wegen Zeitangaben, asynchroner Abläufe und opaker IDs nicht byteidentisch sein.

## Agentenrollen und Aufgaben

Die Umsetzung liefert vier englische Rollenaufträge unter dem Lab-Projekt in roles/: task-author.md, test-agent.md, analyst.md und fixer.md. Sie beschreiben Eingaben, erlaubte Zugriffe, Ergebnisformat und Abschlussbedingungen. Die Exe enthält keine Modell-API und keinen eingebauten Orchestrator. Codex startet die Rollen in getrennten Agentenkontexten; der Testagent erhält keine geerbte Gesprächshistorie. Aufgabenautor und Testagent verwenden gpt-6-luna/high; Analyse-/Auditagent verwendet gpt-6.1-sol/high; Umsetzungsagent verwendet gpt-6-luna/high. Dies ist eine ausdrückliche Modellfestlegung.

| Rolle | Auftrag und Zugriff |
|---|---|
| Aufgabenagent | Liest die Targets und den Katalog, darf zur Erstellung lösbarer Aufgaben Referenzcode lesen. Erstellt task.json und verdeckte Referenzkriterien. Gibt keine richtige Toolsequenz an den Testagenten weiter. |
| Testagent | Erhält ausschließlich öffentliche task.json, Targetpfade, tools.json und Lab-Bedienung. Erstellt Argumentdateien, verwendet call und liest dessen Ergebnisse. Keine direkte Source-/Assemblyinspektion, keine andere MCP-Navigation, keine früheren Findings oder Referenzen. |
| Analyseagent | Prüft nach dem Lauf Dumps und finale Agentenantwort gegen unabhängigen Code, Tests und verdeckte Referenzkriterien. Ändert weder Produktcode noch ursprüngliche Dumps. |
| Umsetzungsagent | Erhält bestätigte Findings als getrennten Korrekturauftrag zwischen zwei bewerteten Runs innerhalb dieses Vorhabens. Reproduziert Defekte mit passenden Tests und beachtet Repository-Gates. Führt im bewerteten Run keine Fixes aus. |

Die öffentlichen Tasks enthalten exakt schemaVersion, id, targetId, goal und mode. id verwendet dieselbe ASCII-ID-Regel wie Target-IDs. mode ist single oder chain; beide erlauben begrenzte Korrekturaufrufe. targetId ist eine konfigurierte Ziel-ID oder runtime für Wartung. goal ist ein nicht leerer String und beschreibt das fachliche Ergebnis, nicht die zu verwendenden Tools. Schemafehler werden vor der Taskregistrierung zurückgewiesen. Lab-Grenzen gelten auch für single, statt den Agenten nach einem ersten missverständlichen Aufruf abzuschneiden.

Der Aufgabenagent hält getrennt Referenzkriterien mit taskId, erwarteten Fakten, unabhängigen Code-/Testankern, beabsichtigten Fehlerfällen und gewünschten Coverage-Zellen fest. Der Analyseagent prüft vor Weitergabe der Aufgabe, ob das Ziel tatsächlich existiert oder ein ausdrücklich beabsichtigter Negativfall ist. Referenzen liegen außerhalb der dem Testagenten übergebenen Dateien. Sie sind auf derselben Maschine keine Sicherheitsgrenze.

Für die breite Untersuchung dürfen Aufgaben gezielt einen Parameterfall beschreiben, beispielsweise eine gewünschte Sichtbarkeit, geringe Antwortmenge oder eine Mehrdeutigkeit. Sie nennen dem Testagenten trotzdem weder einen Toolnamen noch eine fertige Argumentliste. Er darf bis zur Taskgrenze selbst korrigieren und Folgeaufrufe wählen. Er liefert am Ende eine knappe Antwort mit seinen Call-IDs und den belegten Fakten.

Direkte Navigation außerhalb des Zugangs ergibt isolationStatus=violated und eine ungültige Tool-Verständlichkeitsbewertung. Der koordinierende Agent liefert eine Zugriffserklärung und verfügbare Toolspuren für die Auswertung. Ohne vollständige beobachtbare Spur ist isolationStatus=declared, nicht verified. Das Lab behauptet keine technische Sandbox und keinen Beweis, was Codex tatsächlich vollständig in den Modellkontext übernommen hat.

## Abdeckung aller 22 Tools

Der Start erwartet exakt die folgende produktive Toolmenge. Fehlende, doppelte oder zusätzliche Tools verhindern einen realen Run mit LAB_CATALOG_MISMATCH. Fixture-Kataloge dürfen ausschließlich interne automatisierte Tests verwenden; keine öffentliche Fixture-Startoption.

| Gruppe | Tools |
|---|---|
| Symbol | find_symbol, get_symbol_body |
| Struktur | get_file_skeleton, get_class_structure, get_file_tree, get_namespace_tree, get_index_scope |
| Beziehungen | get_call_tree, find_references, get_type_hierarchy, find_implementations, get_impact, dependency_graph, resolve_type_origin |
| Assembly | get_assembly_context, inspect_assembly, search_assembly, find_assembly_extensions |
| Kontext | get_feature_context, get_test_context |
| Wartung | get_server_health, reload_config |

Für jedes Tool gibt es mindestens eine Aufgabe für den normalen Erfolg und eine für einen relevanten Parameter- oder Fehlerfall. Source-Navigation wird auf navigator-source untersucht; Assembly-Navigation auf navigator-assembly; resolve_type_origin wird auf beiden unterstützten Targetarten untersucht; get_symbol_body zusätzlich als Assemblyfolge. Wartung wird einmal auf der Lab-Runtime untersucht. Die Eignung von Tasks wird anhand des realen Targets geprüft; fehlende Referenzdaten dürfen keinen Scheinerfolg erzeugen.

Drei kurze Ketten sind Pflicht:

- Source: find_symbol → get_symbol_body mit unverändertem handoffId, auf navigator-source.
- Source: find_symbol → find_references mit unverändertem handoffId, auf der Navigator-Solution.
- Assembly: inspect_assembly → get_symbol_body mit dem tatsächlich angebotenen handoffId, auf navigator-assembly.

Die Namen dieser Ketten gehören in die verdeckten Abnahmekriterien und die Auswertung, nicht in die Zielbeschreibung des Testagenten. Die Aufgabe nennt das fachliche Ziel. Parent-IDs machen die Verwendung des vorherigen Resultats nachvollziehbar; es gibt keinen automatischen Parameterersatz.

Über den Gesamtbestand sind zusätzlich diese Fälle abzudecken: kein Treffer, Mehrdeutigkeit, unbekannter Parameter, falscher Parametertyp, RESPONSE_BUDGET_TOO_SMALL mit Recovery, Truncation mit einer echten Fortsetzung sowie Loading/Running mit Polling. Letztere werden gezielt bei kalter Runtime oder kontrolliertem geringem Budget angestoßen. Scheitert eine reale Reproduktion, wird sie als not_observed dokumentiert; deterministische Fixturetests prüfen den Lab-Mechanismus, ersetzen aber den fehlenden Produktnachweis nicht.

Abdeckung wird als Matrix Tool × Target × Fall dokumentiert. Ein Tool, das der Agent trotz gezielter Aufgaben nicht auswählt, bleibt not_observed und liefert einen möglichen Verständlichkeitsbefund. Ein Aufgabenfehler wird getrennt benannt. Die Auswertung darf Lücken nicht durch vorgegebene Toolsequenzen als angeblich autonome Erfolge schließen.

## Messungen und nachgelagerte Analyse

Erfasst werden getrennt: Katalogtext, Argumentdatei, produktive Textblöcke, StructuredContent und gesamte CLI-Resultatdarstellung. UTF-8-Bytes werden exakt gezählt; Tokenzahlen verwenden den vorhandenen cl100k_base-Zähler mit benannter Version. Textbudgetmessungen verwenden exakt die produktiven Textstrings; JSONdarstellung und Escapezeichen werden separat gezählt. Die Bestandteile werden nicht als unabhängige Gesamtkosten zusammenaddiert und damit doppelt gezählt.

Callzeiten trennen Archivierung/Snapshotaufwand, gemeinsame Validierung/Bindung/Handler und Darstellung. Fehler, Korrekturaufrufe, Polls und Fortsetzungen zählen mit. Verfügbarkeit von Modell-/Reasoningeinstellungen und realen Usagezahlen wird protokolliert; unbekannte Werte bleiben null. Das Lab misst weder verborgene Reasoningtokens noch exakte Kosten des tatsächlichen Agentenmodells oder die Truncation einer externen Codex-Ausgabefläche.

report.md enthält für jede Aufgabe:

- Outcome: solved, failed, blocked, invalid oder inconclusive.
- Code-/Testbelege für Korrektheit; eine plausibel klingende Antwort allein genügt nicht.
- Tool-/Parameterverständnis mit konkreten Fehlversuchen.
- Nutzwert und Rauschen mit genauen Antwortstellen, nicht mit pauschalem Score.
- Aufruf-/Tokenaufwand und snapshotStatus/isolationStatus.
- Coverage-Zellen und Findings beziehungsweise eine begründete Aussage, dass kein Finding belegt ist.

Ein Finding enthält ID, Priorität P0–P3, Kategorie (product, description, output, lab, task, agent oder undetermined), taskId, Call-IDs, Produkt-/Targetstand, Reproduktion/Antwortausschnitt, konkrete Auswirkung und überprüfbare Abnahmebedingung. Bestätigte Findings werden in lab-findings.md im neuen Taskverzeichnis mit ausreichenden Belegen versioniert; ein bloßer Link auf später löschbare temp-Dateien genügt nicht.

Kürzer gilt nur bei erhaltener Korrektheit und Aufgabenlösung als besser. Ein einzelner Lauf liefert Beobachtungen, keinen statistischen Stabilitätsnachweis. Der verpflichtende Verbesserungsvergleich verwendet unveränderte öffentliche Aufgaben, eingefrorene Target-Snapshots, gleiche bekannte Agenteneinstellungen und neue Agentenkontexte. Produktbinaries dürfen zwischen den Runs wechseln; Targetbinaries dürfen dabei nicht neu gebaut werden. Zustandsabhängige IDs werden aus aktuellen Vorgängerantworten gewonnen; Rohverläufe sind kein allgemeines Replayprogramm.

## Gate L1: Nicht-E2E-Abnahme der Lab-Infrastruktur

Die bestehende Testinfrastruktur wird erweitert. FastTests referenzieren das Lab für Renderer, Metadatenvalidierung, Argumentparser und Zustands-/Artefaktkoordination. Bestehende IntegrationTests können eng begrenzte Nicht-E2E-Fälle für eine isolierte Named-Pipe-/Datei-/Prozessownerkomponente und gemeinsame Funktionsbindung erhalten. Das Lab gewährt diesen Testassemblies Zugriff und wird in die Solution/Buildgates aufgenommen. Kein neuer Testprojektbaum, vollständiger start→Worker→call-Prozesstest oder MCP-Paritätsloopback.

L1 verlangt alle folgenden Nachweise:

- Referenzgraph ohne Produkt→Lab-Abhängigkeit; reguläre CLI ohne Laboptionen; Runtime-/DI-Erzeugung wiederverwendet ohne AddMcpServer oder Transport-/Serverdienste im Lab.
- Vollständiger Vergleich aller originalen Definitionsfelder, Input-/Outputschemas und Annotationen aus gemeinsamem Katalog und öffentlich erzeugten SDK-Adapterobjekten. Keine MCP-Verbindung. Alte SDK-Streamfixtures bleiben erhalten, werden aber hier nicht ausgeführt.
- Intern injizierte Fixture-Kataloge ausschließlich für Tests. Keine öffentliche Fixturestartoption und kein alternativer Produktionskatalog. Ready beim echten Start nur mit genau 22 Tools.
- Repräsentative gemeinsame Vertragsfälle für Defaults, Null, unbekannte Felder, Enum-/Zahlenbindung, Wire-Renames/Serializeroptions, Erfolg/Error, Budgets, ProtocolException, Handoffs, Loading/Running und Continuations. Der produktive Katalog validiert genau einmal. Flüchtige Felder werden nur mit ausdrücklich benannter Normalisierungsliste verglichen; ihre Folgefunktion wird separat geprüft.
- Originaler CallToolResult mit Content/StructuredContent/IsError bleibt erhalten. Unbekannter Toolname bleibt produktive ProtocolException; Laberrors werden nicht als Navigationserfolge dargestellt.
- Komponentenfälle mit steuerbaren Clocks, Runtime-/Dispatchergrenzen und Dateisystem: A→B in derselben Workerinstanz, Busy vor Annahme, Idempotenz/Call-ID-Konflikt, Clientabbruch, Timeout/Grace, Stop, Crash/unvollständige Artefakte, Zähler und Grenzen. Keine vollständige Lab-Exe-Testkette.
- Isolierte IPC-Prüfung für Prefix, UTF-8, Base64, 1-MiB-Grenze, einmal gelesene Argumentbytes, CurrentUserOnly und Zugehörigkeit aller Ausgabepfade.
- Atomare Requests/Rawargumente vor Dispatch, unveränderte Produktresponses danach, exklusive Writer-/Artefaktzuständigkeiten. Kein erfundener Erfolg oder Replay bei Crash.
- Vollständiger deterministischer Renderer mit Unicode, mehrteiligen Textblöcken, StructuredContent, Fehlern, Backtickfences, Definition und Request; gleiche Artefakte/Version ergeben byteidentisches call.md.
- Kontrolliertes Ende mit Endfingerprints; Crash/Graceüberschreitung mit failed und gegebenenfalls unknown. Erzwungener Stop betrifft nur den eigenen eindeutig identifizierten Worker; kein nachträglich erfundenes stopped.
- Normale Navigation/Wartung verändert keine analysierten Inputs oder Benutzerconfig. Changed/unknown wird nicht als unveränderter Lauf ausgegeben.

Die offiziellen Gates und E2E-Ausschlussfilter sind exakt die aus [Kapitel 02](02-verifikation.md). L1 belegt Infrastruktur, nicht die fachliche Qualität/autonome Verständlichkeit aller Tools. Fixturetests ersetzen weder Produktabnahme noch reale Agentenuntersuchung.

## Gate L2: Bewertete Audit- und Verbesserungsrunden

L2 setzt Serverabnahme, L1 und beide eingefrorenen Targets voraus. Vor dem ersten Run werden Definitionen und je ein gültiger/ungültiger echter Aufruf für alle 22 Tools am gemeinsamen transportlosen Produktionspfad gemäß Kapitel 02 geprüft. Der Aufgabenautor und Analyseagent prüfen Tasks und verdeckte Referenzen unabhängig auf tatsächliche Existenz/Lösbarkeit. Testagenten bekommen keine Referenzen, richtigen Antworten oder vorgeschriebenen Toolsequenzen.

R0 ist die Basisuntersuchung mit unabhängigem Audit. R1 ist der verpflichtende zweite bewertete Run nach den R0-Korrekturen. R2 findet genau dann statt, wenn R1 noch bestätigte, im Scope bearbeitbare Findings oder eine nachzuprüfende Regression enthält. Maximal drei bewertete Runden insgesamt und zwei Korrekturphasen dazwischen. Schon R0 und R1 werden unabhängig ausgewertet. Bei fehlenden R0-Findings wird kein künstlicher Codefix erzeugt; R1 findet trotzdem in frischem Kontext statt.

Jede Runde hat diese feste Reihenfolge:

1. Produkt-/Targetstand, Konfiguration, bekannte Modell-/Reasoningeinstellungen und Fingerprints erfassen. Neue Worker/Sitzungen; keine alten Handoffs/Operations-/Pagetokens. Unbekannte Angaben bleiben null.
2. Frischen gpt-6-luna/high-Testagenten ohne geerbte Historie, frühere Findings, Referenzcode oder versteckte Kriterien alle validierten öffentlichen Tasks über das Lab bearbeiten lassen. 8 Calls/Task und die übrigen Grenzen bleiben unverändert; Aufgaben werden über genügend Sessions verteilt.
3. Alle gültigen geplanten Aufgaben tatsächlich versuchen; finale Antwort und Call-IDs sowie unveränderte Dumps sichern. Outcome ist solved, failed, blocked, invalid oder inconclusive. IsError allein ist kein Aufgabenausgang.
4. Unabhängigen gpt-6.1-sol/high-Analyse-/Auditagenten Requests, Antworten und finale Fakten gegen den eingefrorenen Targetcode und verdeckte Kriterien prüfen lassen. Priorisierte belegte Findings und separater report.md. Keine Änderungen an Produkt oder Originaldumps während der Bewertung.
5. Ausschließlich zwischen beendeten Runs: bestätigte product/description/output/lab-Findings an getrennten gpt-6-luna/high-Umsetzer. Reproduktion durch erlaubte Nicht-E2E-Tests, vollständiger Ownerfix, Dokumentation, offizielle Gates und atomarer verifizierter Commit. Task-/Agentenfehler bleiben ihrer Kategorie zugeordnet und erzwingen keinen unbegründeten Produktfix.
6. Im nächsten Run dieselben Ziele/Target-Snapshots durch frische Testagenten lösen lassen; IDs aus aktuellen Antworten gewinnen. Nächster Audit bestätigt konkrete Fixes und prüft Regressionen sowie Call-/Outputaufwand.

Öffentliche Tasks und fachliche Referenzkriterien bleiben für den Vergleich unverändert. Fehlerhafte Aufgaben werden invalid erklärt; ihre bisherigen Ergebnisse sind keine gültige Vergleichsbasis. Nach nötiger Korrektur müssen für den betroffenen Bestand R0/R1 neu und innerhalb der noch verfügbaren drei Gesamtrunden erhoben werden. Reicht das Limit nicht, bleibt L2 unvollständig. Kein vierter Run durch Taskumbenennung und keine behauptete Verbesserung durch leichtere Ziele.

Die Pflichtaufgaben umfassen je Tool einen normalen und einen relevanten Parameter-/Fehlerfall, die jeweils unterstützten Source-/Assemblytargets, die drei kurzen Handoffketten und genannten Spezialfälle. Wird ein Tool trotz gültiger autonomer Aufgabe nicht gewählt, bleibt die Coveragezelle not_observed mit konkretem Verständlichkeits-/Aufgabenbefund; sie wird nach einer belegten Korrektur erneut versucht. Eine nachträglich verratene Toolsequenz ersetzt keinen autonomen Erfolg.

Ein selten reproduzierbarer Spezialfall wie Loading darf nach dokumentiertem gültigem Versuch not_observed bleiben; seine deterministische Komponentenabnahme aus Kapitel 02 muss trotzdem bestehen. not_observed bezeichnet eine qualitative Beobachtungslücke, keinen Produktfunktionsnachweis oder erfundenen gelösten Task. Unbearbeitete/blocked Pflichtaufgaben, fehlender Katalog/Target und invalide/changed/unknown-Spuren blockieren L2. Failed aufgrund eines Produktdefekts ist gültige Untersuchungsinformation; der Defekt unterliegt dennoch den Gesamtabschlussregeln.

Verbessert wird das Produkt: Beschreibungen/Schemas, Handlerkorrektheit, Owner/Handoffs, Antwortprojektionen und vermeidbare Calls/Noise. Testagenten erhalten keine Lösungsdatei. Rollenaufträge dürfen allgemeine bestätigte Bedienklarstellungen bekommen, keine Taskschlüssel/Referenzen/Findings; dieser Einfluss wird separat im Vergleich benannt. Das Produkt erhält keine Audit-/Refactoringtools.

## Pflichtvergleich und dauerhafte Evidenz

Pro Task/Runde werden gezählt: tatsächliche Produktdispatcheraufrufe einschließlich Fehler/Polls/Pages, Budgetfehler und Recoverycalls, Loading-Retries, lösungsrelevante Calls, Bytes/Tokens und getrennte Phasenzeiten. Folgecallgründe sind mandatory_result_pages, pending_operation, budget_recovery, argument_correction und unrelated_or_redundant. Jeder Call wird in der Gesamtsumme einmal gezählt; Kategorien und Text-/JSONbestandteile dürfen Gesamtkosten nicht doppelt erhöhen.

Die Budgetlatenzfrage wird ausdrücklich ausgewertet: Konkrete Request-/Responsepaare zeigen bei unverändert korrekter Aufgabenlösung, ob R1/R2 weniger vermeidbare Budget-/Korrekturcalls und redundanten Output brauchen. Legitimes Paging/Polling bleibt sichtbar. Toolcall, Modellturn und APIrequest sind getrennte Größen; echte Modellusage/Turnzahlen nur aus realen Spuren, sonst null. Kürzere falsche Antworten, verlorene Ergebnisse oder höhere explizite Budgets gelten nicht als Verbesserung. Ein fehlender Verbesserungseffekt wird ebenso ehrlich berichtet wie ein belegter Effekt. Keine statistische Stabilitäts- oder erfundene Kostenzusage.

Je Run besitzt der Analyseagent report.md. Bei Umsetzung entstehen lab-findings.md und lab-vergleich.md im neuen Taskverzeichnis: dauerhaft nötige unveränderte Requests/Resultatausschnitte, Task-/Call-IDs, Produkt-/Targetcommits und Hashes, Ursache/Auswirkung/Abnahme, Korrekturcommit, unabhängiger Nachcheck sowie offene/not_observed-Zellen. Kein Finding stützt sich nur auf löschbare/überschreibbare temp-Belege. Originalrunartefakte bleiben im ignorierten Runbereich; versionierte Auszüge werden unverändert und mit Herkunft gesichert.

Findingdispositionen sind open, fixed_pending_audit, accepted oder rejected_with_evidence. Accepted verlangt Bestätigung der konkreten Abnahme durch den unabhängigen nächsten Audit anhand neuer Antworten beziehungsweise geeigneter erlaubter Regressionnachweise. Das Rundenlimit erklärt keinen Defekt für behoben.

## Lab-Abschluss und Nicht-Ziele

Lababschluss verlangt bestandenes L1, Untersuchung/Auswertung aller gültigen geplanten Aufgaben, mindestens R0/R1 und anwendbares R2, erforderliche belegte Korrekturen/Gates, Vergleich und Findingsregister. Alle 22 Definitions-/Funktionsverträge sind in der Produktmatrix nachgewiesen. Not_observed-Spezialzellen bleiben qualitative Lücken und begründen keine vollständige autonome Abdeckung. Fehlende Tools/Targets, blockierte/unbearbeitete Aufgaben, invalide Spuren oder relevante offene Produktblocker verbieten vollständigen Gesamtabschluss. Neue P3-Restbefunde unterliegen der eindeutigen Abschlussregel in Konzept.md.

Nicht-Ziele sind MCPdeployment/Configwechsel, MCP-Transport im Lab oder neuen Tests, neue Navigation/Linter/Refactoringfunktionen, Schreiben in bewertete Targets, Ausführen untersuchter Assemblies, Modell-API/eingebauter Agentenorchestrator/Weboberfläche/Sandbox, Python-/PowerShellstartwrapper, zusätzliche Bibliotheks-/Testprojekte, manuelle Binder/kopierte Schemas/Validatorumgehung, allgemeine Replayprogrammiersprache, Fixes im bewerteten Run oder eine vierte Runde durch Umbenennung. Die begrenzte Korrekturschleife zwischen abgeschlossenen Runs ist ausdrücklicher Muss-Scope.
