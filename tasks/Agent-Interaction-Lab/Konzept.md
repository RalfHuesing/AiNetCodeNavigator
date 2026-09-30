---
status: draft
---

# Agent Interaction Lab

## Intention

Ein Agent soll anhand der echten öffentlichen Tooldefinitionen konkrete Navigationsaufgaben auf definierten Repositories lösen. Wir untersuchen, ob Beschreibungen und Parameter verständlich sind und ob Antworten korrekt, nutzbringend, möglichst rauscharm und tokeneffizient sind.

Der Zugang arbeitet lokal gegen den Entwicklungsstand ohne MCP-Transport, Deployment oder Änderungen der Codex-MCP-Konfiguration. Zunächst erfolgen breite Einzelaufrufe und kurze A→B-Ketten. Die Aufgabenerstellung, Durchführung, Analyse und spätere Behebung erfolgen durch getrennte Agenten. Eine automatische Verbesserungsschleife ist nicht Gegenstand dieses Vorhabens.

## Festgelegte Grenzen

- Genau ein neues .NET-Konsolenprojekt: tests/AiNetCodeNavigator.AgentLab/, Assemblyname AiNetCodeNavigator.AgentLab. Die Ausgabe enthält eine eigene Windows-Exe.
- Lab → produktiver Toolcode → Core. Das Lab referenziert das bestehende Anwendungsprojekt; Produktionsprojekte referenzieren das Lab nicht.
- Alle Lab-Befehle, Sitzungssteuerung, Konfigurationen, Artefakte, Renderer und Messungen liegen im Lab.
- Die reguläre Anwendung erhält keinen Lab-Modus, keine Lab-Parameter und keine Dump-/Aufgabenlogik. Die reguläre Produktverteilung benötigt das Lab nicht.
- Definitionen, Validierung, Bindung, Handler, Zustandsverwaltung und Ergebnisformatierung stammen aus gemeinsamem produktivem Code. Keine Kopien der Tool-API und keine Lab-eigene Navigation.
- SDK-Typen, SDK-Schemaerzeugung und SDK-Funktionsbindung sind erlaubt; MCP-Client, MCP-Server, JSON-RPC, Handshake und MCP-Transport sind in Lab-Läufen ausgeschlossen.
- Erste Abnahme: AiNetCodeNavigator und AiNetLinter, Source und verwaltete Assemblies, alle 22 vorgesehenen Produkttools.
- Die Lab-Exe führt keine Modelle aus und startet keine Codex-Agenten. Der aufrufende Codex-Agent koordiniert die getrennten Rollen außerhalb der Exe.

## Geprüfte Grundlage und Abhängigkeit

Gelesener und durch den Verständnisreview erneut geprüfter Stand am 2026-09-30: HEAD 90adbfc9907a72f4f283041d319d74c2be520645. Die aufgeführten Produktionsdateien wurden gelesen, aber durch diese Konzeptarbeit weder geändert noch mit Builds oder Tests verifiziert.

- [Host](../../src/AiNetCodeNavigator/Mcp/McpServerHost.cs) und [Runtime](../../src/AiNetCodeNavigator/Mcp/NavigatorHostRuntime.cs) enthalten im gelesenen Arbeitsbaum Stdio-Start, Dependency Injection und Prozesszustand. Der Host registriert zwei Wartungstools; die Navigationsklassen sind noch Platzhalter. [Cluster 9](../AiNetLinter-Uebernehmen/Clusters/Cluster-09.md) besitzt deren Umsetzung.
- Die bestehenden FastTests und IntegrationTests referenzieren bereits das Anwendungsprojekt. InternalsVisibleTo wird für Testzugriff verwendet.
- [Argumentvalidierung](../../src/AiNetCodeNavigator/Mcp/Validation/McpArgumentValidationFilter.cs) erhält derzeit einen SDK-RequestContext. SDK 2.2.0 verlangt hierfür einen McpServer; dieser Einstieg kann deshalb nicht unverändert der transportlose Lab-Einstieg sein.
- Die lokal installierte SDK-Dokumentation belegt den öffentlichen Einstieg McpServerTool.Create(AIFunction, options). Die Funktionsbindung kann über Microsoft.Extensions.AI und dieselben Serializeroptionen geteilt werden. Die konkrete SDK-Klasse AIFunctionMcpServerTool ist intern; das Lab darf sie weder per Reflection verwenden noch deren Implementierung kopieren.
- [Formatierung](../../src/AiNetCodeNavigator/Mcp/Formatting/McpResponseFormatter.cs), [Ergebnisbau](../../src/AiNetCodeNavigator/Mcp/Formatting/McpToolResults.cs) und [Operations-/Fortsetzungsspeicher](../../src/AiNetCodeNavigator/Mcp/LongRunningToolCallStore.cs) sind vorhandene Bausteine; gezählt wird mit cl100k_base.
- [SDK-Streamtests](../../tests/AiNetCodeNavigator.FastTests/Mcp/McpArgumentValidationFilterTests.cs) bieten Vorarbeiten für separate Paritätsprüfungen. Sie sind keine transportlosen Agentenläufe.

Die produktiven Registrierungen müssen für alle 22 Tools vor der vollständigen Lab-Abnahme existieren. Dieses Vorhaben implementiert deren Navigationssemantik nicht. Bis dahin darf Lab-Infrastruktur mit ausdrücklich als solchen bezeichneten Fixture-Tools geprüft werden; ein realer Lab-Lauf mit unvollständigem Katalog startet nicht.

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

Die folgenden vier Ziel-IDs sind für die erste Abnahme verbindlich:

| ID | Inhalt |
|---|---|
| navigator-source | AiNetCodeNavigator.slnx im eigenen Repository |
| linter-source | AiNetLinter.slnx im externen AiNetLinter-Repository |
| navigator-assembly | Bereits gebaute AiNetCodeNavigator.Core.dll |
| linter-assembly | Bereits gebaute AiNetLinter.dll |

Externe Repositories und deren Binaries werden nur gelesen; das Lab startet dort weder Restore noch Build. Fehlende Binaries müssen vor dem Lauf bereitgestellt werden. Es baut auch die eigenen Produktbinaries nicht implizit. Kein Aufruf des untersuchten Binaries.

Die Lab-eigene hostsettings.json wird beim Sitzungsstart mit minimumLogLevel=Information unter dem Runverzeichnis erzeugt und ausdrücklich als Konfigurationspfad der Runtime gesetzt. Wartungsszenarien bearbeiten nur diese Datei. Ein geplanter Fehlerfall darf sie vorübergehend ungültig machen oder entfernen; vor der nächsten Task wird ihr definierter Startzustand wiederhergestellt und erfolgreich geladen. Der normale Benutzer-Konfigurationspfad wird niemals verwendet.

Neue Targets sind über die gleiche lokale Konfiguration zulässig. Die vier verbindlichen Abnahmeziele bleiben erforderlich; keine implizite Suche nach beliebigen Solutions.

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

Die Umsetzung liefert vier englische Rollenaufträge unter dem Lab-Projekt in roles/: task-author.md, test-agent.md, analyst.md und fixer.md. Sie beschreiben Eingaben, erlaubte Zugriffe, Ergebnisformat und Abschlussbedingungen. Die Exe enthält keine Modell-API und keinen eingebauten Orchestrator. Codex startet die Rollen in getrennten Agentenkontexten; der Testagent erhält keine geerbte Gesprächshistorie. Bei Subagenten ohne explizite Modellwahl gelten die Repository-Defaults.

| Rolle | Auftrag und Zugriff |
|---|---|
| Aufgabenagent | Liest die Targets und den Katalog, darf zur Erstellung lösbarer Aufgaben Referenzcode lesen. Erstellt task.json und verdeckte Referenzkriterien. Gibt keine richtige Toolsequenz an den Testagenten weiter. |
| Testagent | Erhält ausschließlich öffentliche task.json, Targetpfade, tools.json und Lab-Bedienung. Erstellt Argumentdateien, verwendet call und liest dessen Ergebnisse. Keine direkte Source-/Assemblyinspektion, keine andere MCP-Navigation, keine früheren Findings oder Referenzen. |
| Analyseagent | Prüft nach dem Lauf Dumps und finale Agentenantwort gegen unabhängigen Code, Tests und verdeckte Referenzkriterien. Ändert weder Produktcode noch ursprüngliche Dumps. |
| Umsetzungsagent | Erhält bestätigte Findings in einem getrennten späteren Umsetzungsauftrag. Reproduziert Defekte mit passenden Tests und beachtet Repository-Gates. Führt im bewerteten Run keine Fixes aus. |

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

Für jedes Tool gibt es mindestens eine Aufgabe für den normalen Erfolg und eine für einen relevanten Parameter- oder Fehlerfall. Source-Navigation wird auf beiden Solutions untersucht; Assembly-Navigation und resolve_type_origin auf beiden Assemblyzielen; get_symbol_body zusätzlich als Assemblyfolge. Wartung wird einmal auf der Lab-Runtime untersucht, nicht künstlich pro Repository dupliziert. Die Eignung von Tasks wird anhand des realen Targets geprüft; fehlende Referenzdaten dürfen keinen Scheinerfolg erzeugen.

Drei kurze Ketten sind Pflicht:

- Source: find_symbol → get_symbol_body mit unverändertem handoffId, auf beiden Solutions.
- Source: find_symbol → find_references mit unverändertem handoffId, auf beiden Solutions.
- Assembly: inspect_assembly → get_symbol_body mit dem tatsächlich angebotenen handoffId, auf beiden Assemblies.

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

Ein Finding enthält ID, Priorität P0–P3, Kategorie (product, description, output, lab, task, agent oder undetermined), taskId, Call-IDs, Produkt-/Targetstand, Reproduktion/Antwortausschnitt, konkrete Auswirkung und überprüfbare Abnahmebedingung. Bestätigte Findings werden in Findings.md unter diesem Taskverzeichnis mit ausreichenden Belegen versioniert; ein bloßer Link auf später löschbare temp-Dateien genügt nicht.

Kürzer gilt nur bei erhaltener Korrektheit und Aufgabenlösung als besser. Ein einzelner Lauf liefert Beobachtungen, keinen statistischen Stabilitätsnachweis. Ein späterer Verbesserungsvergleich verwendet gleiche Aufgaben/Targets/bekannte Agenteneinstellungen und neue Agentenkontexte. Zustandsabhängige IDs werden aus aktuellen Vorgängerantworten gewonnen; Rohverläufe sind kein allgemeines Replayprogramm.

## Verifikation und Abschlussbedingungen

### Gate 1: Infrastrukturabnahme

Die bestehende Testinfrastruktur wird erweitert; kein zusätzliches Testprojekt. FastTests erhalten eine Referenz auf das Lab für Renderer, Metadatenvalidierung und Protokollzustände; IntegrationTests für Prozess-/Named-Pipe-Lifecycle und gemeinsame Aufrufparität. Das Lab erhält Zugriff für diese Testassemblies. Es wird in die Solution und damit die offiziellen Build-/Testgates aufgenommen.

Dieses Gate darf vor Abschluss von Cluster 9 mit internen Fixture-Katalogen erfüllt werden. Es belegt die Infrastruktur und repräsentative gemeinsame Vertragsfälle, nicht die Vollständigkeit oder Qualität aller Produkttools. Der reguläre Lab-Start bleibt trotzdem an den vollständigen 22er-Katalog gebunden. Für Tests wird die Katalogquelle intern injiziert; kein öffentlicher Fixture-Schalter und kein alternativer Produktionskatalog.

Verbindliche Nachweise:

- Referenzgraph ohne Abhängigkeit von Produktionsprojekten auf das Lab; reguläre CLI ohne Lab-Befehle/-Parameter und keine Registrierung von Lab-Diensten im Produktstart.
- Transportloser gemeinsamer Aufruf in Produktion und Lab; kein MCP-Client/-Server/Loopback im Lab.
- Fixture-Katalogfelder stimmen mit einem separaten realen SDK-/MCP-Testpfad überein. Eingabe-/Ergebnisschemas und Annotationen werden vollständig verglichen; Protokollversion des Vergleichs entspricht der aktuellen SDK-Definition.
- Repräsentative Fixture-Paritätsfälle für Erfolg/Fehler, Defaults, Null, unbekannte Felder, Enum-/Zahlenbindung, Budget, ProtocolException, Handoffs, Running und Fortsetzungen. Flüchtige Werte werden nur in ausdrücklich benannten Feldern normalisiert; deren Folgefunktion wird separat geprüft.
- A→B funktioniert in derselben Workerinstanz; fremde/abgelaufene/alte Handoffs erzeugen den vorgesehenen produktiven Fehler.
- Busy, Clientabbruch, Timeout, Stop, doppelter Call-ID, Workercrash und unvollständige Artefakte sind nachvollziehbar und führen nicht zu Doppelcalls oder falschen Erfolgsmeldungen.
- Gleiche Artefakte erzeugen byteidentisches Markdown; Codefences, Unicode, mehrteiliger Content und StructuredContent bleiben vollständig.
- Inputs/Outputs werden atomar und vor/nach Dispatch entsprechend dem Vertrag gespeichert; Lab-Fehler bleiben von produktiven Fehlern getrennt.
- Normale Navigation und Wartungsprüfung verändern keine analysierten Source-/Assemblydateien und keine Benutzerkonfiguration. Snapshotdifferenzen werden nicht als unveränderter Lauf gemeldet.
- Offizielle Gates: scripts/build.ps1, scripts/test-fast.ps1, scripts/test-integration.ps1 und scripts/test.ps1. Ausführung und Ergebnisse werden gemäß Repository-Regeln dokumentiert; docs/ wird erst mit implementiertem Stand aktualisiert.

### Gate 2: Produktintegration und erste Agentenuntersuchung

Dieses Gate setzt Gate 1, alle produktiven Registrierungen aus Cluster 9 und die bereitgestellten vier Targets voraus. Vor den bewerteten Agentenläufen werden die Fixture-Paritätsprüfungen auf den echten Katalog erweitert: sämtliche Definitionsfelder sowie mindestens ein gültiger und ein ungültiger Aufruf pro Produkttool. Fehler der Lab-/SDK-Parität sind Infrastrukturfehler; ein fachlich falsches, auf beiden Wegen identisches produktives Resultat ist ein Produktfinding.

Die Abnahme liefert Tasks, unveränderte JSON-/Markdown-Dumps, finale Agentenantworten und einen separat erstellten Bericht für beide Repositories und die vollständige Toolmenge. Ein erster Run pro Repository genügt; es werden keine statistischen Zusagen gemacht. Die Anzahl der Sitzungen darf an die festen Grenzen angepasst werden; keine Grenze wird still angehoben.

Das Lab kann korrekt implementiert sein, obwohl die Untersuchung Produktdefekte oder Verständlichkeitsprobleme aufzeigt. Diese sind Findings, keine fehlgeschlagene Infrastrukturabnahme. Fehlende Tools/Targets, ungültige Tasks, beschädigte Spuren und Zieländerungen blockieren den Gesamtabschluss. Ein nach dokumentiertem gültigem Versuch nicht reproduzierter Spezialfall bleibt dagegen als not_observed eine Coverage-Lücke und blockiert Gate 2 nicht. Vollständige Produktqualität oder vollständige qualitative Abdeckung darf daraus nicht behauptet werden.

Gesamtabschluss dieses Vorhabens verlangt beide Gates und einen Bericht zu allen geplanten Tool-/Target-Aufgaben. Jede gültige Aufgabe wurde tatsächlich versucht und ausgewertet; failed aufgrund eines Produktdefekts ist ein legitimes Ergebnis. Ein vom Agenten trotz gültiger Aufgabe nicht ausgewähltes Tool oder ein trotz dokumentiertem Versuch nicht beobachteter Loadingfall darf als not_observed abschließen, belegt aber keine erfolgreiche Abdeckung dieser Zelle. Unbearbeitete/blocked Aufgaben, ein fehlender Katalog, fehlende Targets oder invalide Spuren schließen den Gesamtabschluss aus. Dann lautet die Meldung ausdrücklich „Infrastruktur fertig, Produktintegration/Untersuchung blockiert oder unvollständig“, nicht „Lab-Vorhaben abgeschlossen“.

Die Umsetzung umfasst die erste Agentenuntersuchung und das belegte Findingsregister. Behebung der Produktfindings erfolgt im getrennten späteren Auftrag an den Umsetzungsagenten; dieser Auftrag ist kein Bestandteil der Lab-Infrastrukturabnahme.

## Nicht-Ziele

- MCP-Deployment, Codex-MCP-Konfigurationswechsel und MCP-Transport in Lab-Läufen.
- Ersatz der regulären MCP-Integrationstests, der bestehenden Produktabnahme oder der Navigationstool-Implementierung in Cluster 9.
- Zusätzliche Navigations-/Lint-/Refactoringfunktionen, Schreiben in analysierte Repositories oder Ausführen der untersuchten Binaries.
- Neue Modell-API, eingebauter Agenten-Orchestrator, Weboberfläche oder technische Codex-Sandbox.
- Kopierte Tooldefinitionen, manueller Parameterbinder oder Umgehung des gemeinsamen produktiven Validators.
- Python-Runtime, PowerShell-Startwrapper, zusätzliches Bibliotheks-/Testprojekt oder allgemeine Replay-Skriptsprache.
- Automatische Fixschleifen, Fixes während der Bewertung, exhaustive Parameterkombinationen oder statistische Modellbenchmarks.

## Arbeitsgedächtnis (nur Draft)

Die Sachentscheidungen sind getroffen: separate .NET-Lab-Exe, AiNetCodeNavigator/AiNetLinter und alle 22 Tools. Technische Verträge, Grenzen und Nachweise sind in diesem Entwurf festgelegt; kein Implementierer soll aus offenen Varianten auswählen.

Der Verständnisreview mit gpt-6-luna/high gegen 90adbfc hat sieben Vertragslücken belegt. Sie sind konkretisiert: BindingMetadata/Fixtureadapter, Abschluss-/Crashdaten, Referenzbaseline, Busy-/Zählerregeln, Raw-/Envelope-Versionierung, Protokollfehlerausgabe und zwei getrennte Abschlussgates. Im Nachcheck bestätigte Luna diese sieben Klärungen und benannte zwei verbleibende Textwidersprüche: not_observed als Lücke versus Blocker und Vorübertragungsfehler versus Call-Artefakte. Beide Stellen sind entsprechend präzisiert.

Offen ist ausschließlich die ausdrückliche Freigabe dieses konkretisierten Gesamtkonzepts. Danach wird status auf ready gesetzt und dieser Abschnitt entfernt. Dies startet weder Roadmap noch Umsetzung; der Nutzer ruft den nächsten Workflow-Schritt selbst auf.
