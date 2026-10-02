# 01 — Server und verbindliche Navigationsverträge

Dieses Kapitel gehört zu [Konzept.md](../Konzept.md). Seine Aussagen sind Soll-Verträge; ein Muss ohne Implementierungsnachweis ist kein Ist-Zustand.

## Produktgrenze und Architektur

Das Produkt ist genau ein MCP-Stdio-Server für read-only C#-Navigation. `stdout` ist ausschließlich JSON-RPC vorbehalten. CLI-Hilfe/-Fehler und Logging gehen nach `stderr` beziehungsweise in rotierende Dateien. Core und Navigationsmodelle erhalten keine MCP-Transport/-Protokolltypen. Navigation verändert weder die analysierten Dateien noch den Benutzer-Workspace oder Benutzerkonfiguration. Produkttexte, Tool-/Parameterbeschreibungen, Fehler und nächste Aktionen sind Englisch.

Source-Targets sind vorhandene absolute `.sln`-/`.slnx`-Pfade; Assembly-Targets vorhandene absolute verwaltete `.dll`-/`.exe`-Pfade. Keine Suche nach einer beliebigen Solution und keine Ausführung untersuchter Binaries. Pfadvarianten werden kanonisiert. Verzeichnis-, Wildcard-, fehlende, gesperrte, native und während des Fingerprintings verschwundene Targets ergeben definierte Fehler. Erwartete Dateizugriffsfehler bleiben wiederherstellbar; unerwartete Fehler dürfen nicht als Erfolg verschwinden.

Die vorhandenen Verantwortlichen bleiben zuständig: `Core/Workspace` für Target/Resident/Snapshot, `Core/Symbols` für gemeinsame Symbolauflösung, `Core/Assemblies` für Dekompilation/Owner/Referenzen, `Mcp/Formatting` für Budget/Ergebnis, `LongRunningToolCallStore` für Operations-/Antwortzustand und die vorhandenen Toolklassen für fachliches Routing.

Der bestehende Stdio-Host und seine SDK-Registrierung bleiben der produktive Aufrufpfad. Tests prüfen originale Tooldefinitionen, SDK-Bindung, Validator und Handler ohne MCP-Transport. Wo der Validator bislang einen RequestContext benötigt, darf ausschließlich seine vorhandene Validierungslogik in eine interne, direkt prüfbare Funktion mit registrierter Tooldefinition und Argumenten extrahiert werden. Der Hostfilter delegiert dann an diese Funktion. Es werden weder Schemas noch Binder oder Validierungsregeln für Tests kopiert. Produktive Registrierung und Aufrufarchitektur werden für die Tests nicht ersetzt.

## Exakte Toolmenge und gültige Targets

S = Source-Solution, A = verwaltete Assembly, R = Runtime. Jedes Tool muss mit seinem unterstützten Target arbeiten und ein fachlich unzulässiges Target verständlich zurückweisen. Ein gesetzter Source-Scope wird bei Assembly-Navigation nicht als echte Produktions-/Testklassifikation ausgegeben. Source-only und Assembly-only sind absichtliche Grenzen.

| Tool | Targets | Pflichtfunktion und wichtige Fälle |
|---|---|---|
| `find_symbol` | S, A | genau ein `pattern` oder 1–10 nicht leere `namePatterns`; Namen/Kinds/Scopes/Generated; Treffer mit Owner und nutzbarem Handoff; Assemblyreferenzen bei `includeReferences=true` |
| `get_symbol_body` | S, A | Batch in Eingabereihenfolge; Bodyfenster, Partials, Owner; gemischter gültiger/ungültiger Batch meldet Teilvollständigkeit, nur ungültige Einträge Fehler |
| `get_file_skeleton` | S, A | deklarative Typ-/Memberübersicht ohne Bodies und ausführbare Feld-/Eventinitializer; Source indexed absolute/relative/linked Pfade; Assembly nur eigene Dekompilate; separater Handoff je Variablendeklarator |
| `get_class_structure` | S, A | deklarierte Member, Visibility, Signaturen, Partialdateien, Recordparameter, Filter und stabile Sortierung vor Cap; Member→Body |
| `get_file_tree` | S, A | `tree`, `files`, `summary`; relative Unterwurzel, Extensions/Patterns/Tiefe/Sortierung/Metadaten; Reparse-Grenzen; Assembly keine benachbarten DLL-Verzeichnisdateien |
| `get_namespace_tree` | S, A | Projektübersicht/Namespaceprefix/Typen/Kinds/Depth; exakter Projektpfad bei gleichen Namen; selectable Typ-Handoffs; vollständig gezählte Totals vor Darstellungscaps |
| `get_index_scope` | S | Roslyn-Dokumentinventar einschließlich physisch mehrfach eingebundener Dateien, Sprach-/C#-/Generated-/Testzählungen; keine angebliche vollständige physische Dateiinventur |
| `get_call_tree` | S, A | incoming/outgoing/both, ASCII/Mermaid, Depth/TopN, BCL/Sourcescope/Generated; begrenzte Assemblyowner-Closure; globale Fanout-/Node-/Edgegrenzen mit ehrlicher Vollständigkeit |
| `find_references` | S, A | direkte/transitive Verwendungen mit Position, Spalte, Dispatch/Provenienz und Owner; Depth/Resultlimit; referenzierte Assemblysource bei `includeReferences=true` |
| `get_type_hierarchy` | S, A | Basisklassen, Interfaces, abgeleitete Typen, Partialdeklarationen und auflösbare Source-/Assembly-Handoffs; Assembly betrachtet eigene dekompilierte Source, keine neue Referenzclosure-Option |
| `find_implementations` | S, A | Interface/abstrakt/virtuell, Methoden und Properties einschließlich Overrides; konkrete Owner-Handoffs; unsupported Targetsymbol wiederherstellbar |
| `get_impact` | S, A | Erforderliches `symbolIdentifier`; transitiv betroffene Caller/Projekte und begrenzte Assemblyclosure. Kein Git- oder Änderungsdateimodus. |
| `dependency_graph` | S, A | genau eine Datei- oder Symbolauswahl; incoming/outgoing/both; projektgenaue Typ-/Datei-/Namespacekanten; generische Typen, Dokumentfenster und begrenzte Traversierung |
| `resolve_type_origin` | S, A | genau ein nicht leerer Symbolidentifier oder Typname; eigene Source, Metadata-/Framework-/NuGetherkunft und exakte DLL-Identität; Nested/Generic, Ambiguität und fehlende Herkunft |
| `get_assembly_context` | A | Assemblyübersicht beziehungsweise ausgewähltes Symbol mit angeforderten Body-/Structure-/Caller-/Impactabschnitten; Sectionfehler ist kein Gesamterfolg; optionale Referenzen |
| `inspect_assembly` | A | öffentliche API/Typen/Member, Namespace-/Type-/Memberfilter, exact/publicOnly, referenzierte Assemblies, Details und querygebundene Domainpages |
| `search_assembly` | A | text/external_calls/data_access, literal/regex/auto, Case, Deklarations-/Kind-/Filefilter, Kontext, matched-file-Cap und querygebundene Domainpages; Type-/Methodhandoffs mit Owner |
| `find_assembly_extensions` | A | Receiver-/Method-/Namespacefilter und optionale Referenzen; leerer/fehlender Receiver ist gültige breite Suche |
| `get_feature_context` | S | Deklaration/Signatur/Caller und statische Testkandidaten; Scope/Generated vor Counts/Caps; keine Linter-Violations |
| `get_test_context` | S | projektgenaue xUnit-/NUnit-/MSTestkandidaten und name-only Unknown; ausdrücklich heuristisch, keine gemessene Testabdeckung |
| `get_server_health` | R; Targetcheck S/A | atomare vollständige Runtime-/Cache-/Memory-/Versionübersicht; optionaler Targetcheck lädt/refreshes kein Target |
| `reload_config` | R | serialisierter atomarer Reload ausschließlich `minimumLogLevel`; Budgetpreflight vor Zustandsänderung; identische Settings behalten Version |

Hierarchie und Implementierungen betrachten die eigene dekompilierte Source der Assembly-Root. Die begrenzte Ownerclosure wird bei den dafür vorgesehenen Tools geprüft; ein universeller referenzierter Assemblyindex ist kein Bestandteil. Toolnamen für Verify, Metrics, Hotspots, Pattern-/Duplicatechecks oder Refactoring dürfen weder registriert noch in Folgehinweisen angeboten werden.

## Verbindliche Basisinvarianten

Die folgenden Invarianten müssen in der Produktion gelten und durch passende Tests nachgewiesen sein:

- TestKit liefert konsistente `Solution`/`Workspace.CurrentSolution`, Projekt-/Dokument-/Referenzidentitäten und validierte Builderinputs. Handoffassertions verwenden das Produktalphabet.
- Cache-Hits benötigen denselben vollständigen Fingerprint. Ein Lookup ohne Hash darf keinen gespeicherten Hash umgehen. Compilationfingerprints umfassen Sourcepfade/-Inhalte, Parse-/Compilationoptions, Projekt- und Metadatareferenzen einschließlich Inhalt/Identität/Properties.
- ProjectRegistry dedupliziert paralleles Laden, schützt aktive Leases vor Eviction und verhindert Publikation nach Disposal. Derselbe physische Linked-Pfad wird in allen zugehörigen Dokumenten refreshed. Lösungs-/Projektlisten, geänderte Referenzen, neue/entfernte Sources und externe Compileglobs verändern den Snapshot.
- Symbolsuche unterscheidet class/interface/record/record class/record struct/struct/enum/delegate/method/property/field. Plain struct schließt Record Struct aus. Scope und Generatedfilter greifen vor Counts, Sortierung und Limits. Nicht-C#-Symbolsuche wird nicht durch eine gemischte Roslynsolution eingeschleust.
- Sourcebody hält Batchreihenfolge und Zeilenfenster; Skeletons halten getrennte Multi-Variable-Handoffs; Class Structure hält deklarierte Member einschließlich Konstanten/Events/Recordparameter, Partialdateien und gültige Markdownzellen.
- Testkandidaten werden solutionweit ohne Zusammenfallen gleichnamiger Fixtures verschiedener Projekte gesammelt. Attribute erkennen xUnit/NUnit/MSTest; `TestMethodAttribute` ist nicht NUnit. Bloße Namensähnlichkeit bleibt Unknown. Feature- und Test-DTOs sowie Text weisen `static-test-candidates-only` aus.
- Beziehungsergebnisse behalten konkrete Callsite-Spalten und Reached-from-Provenienz auch bei mehrfachen Aufrufen auf derselben Zeile und reconverging Pfaden. Source in frameworkähnlichen Namespaces wird nicht als BCL weggefiltert; non-BCL Metadata wird bei den vorgesehenen Defaults erhalten. Both-TopN wird gemeinsam angewendet.
- Dependencytraversierung erreicht spätere Dokumentfenster auch hinter Dokument 1000, erhält projektgenaue gleichnamige/generische Typen und zählt unterschiedlich lange Relationshipcollections bei gemeinsamen Pageoffsets korrekt.
- File Tree begrenzt die aktive Ansicht, sperrt absolute Unterwurzeln und Pfadausbrüche durch jeden Reparseancestor. Namespace und Indexscope unterscheiden physische Dateien, Roslyndokumente, C#-Navigation, Generated und Testscope; Totals bleiben von Darstellungslimits unabhängig.
- Assemblyregistry hält geleaste Generationen resident, invalidiert bei Root-/Referenzinhaltänderung und erholt sich nach fehlgeschlagenem Refresh, ohne die alte Generation als neue auszugeben. Die bestehende 32-Targetgrenze bleibt auch bei aktiven Zugriffen wirksam. Dekompilationcache liest und publiziert nur gegen denselben Referenzcontentfingerprint; same-identity Ersatzbytes dürfen nicht alte Source weiterverwenden.

Die jeweiligen englischen `docs/navigation/`-Seiten sowie lokale Scanner und Tests dienen zur Prüfung der tatsächlichen Komponentenverantwortung und Implementierung. Korrekt implementierte Funktionen werden wiederverwendet und nachgewiesen; fehlende oder fehlerhafte Verträge werden bei den zuständigen Komponenten ergänzt beziehungsweise korrigiert.

## MSBuild-Strukturinvalidierung

Zuständige Komponenten: `MSBuildStructureInputCollector`, `SolutionStructureFingerprint`, `ResidentSolution`, `MSBuildSolutionLoader`. Effektive Imports, externe Compileglobs und konditionale Imports einschließlich `ImportGroup` müssen den nächsten Navigationssnapshot aktualisieren; das gilt auch für bei der vorherigen Evaluation noch nicht vorhandene konditionale Imports.

Für deklarierte Wildcardimports muss der Strukturvergleich die mit der MSBuild-Auswertung übereinstimmende kanonische Menge passender Importpfade und deren Inhalte einschließlich der leeren Ausgangsmenge erfassen. Addition, Änderung und Entfernung eines Matches triggern vor der nächsten fachlichen Navigation die Neuladung. Es wird nicht wahllos jedes File im übergeordneten Baum in einen Import umgedeutet. MSBuild-Matching/-Expansion wird aus den vorhandenen MSBuild-APIs gewonnen; keine eigene allgemeine MSBuild-Sprache.

Eine bei der geladenen Evaluation weiterhin unaufgelöste Property-/Item-/Metadataexpression wird explizit als nicht vollständig fingerprintbare Struktur markiert. In diesem Zustand darf `ResidentSolution` vor einem neuen fachlichen Source-Aufruf nicht ausschließlich wegen gleicher alter Fingerprints den alten Strukturstand als aktuell ausgeben. Sie führt eine erneute Evaluation/Neuladung mit den aktuellen bekannten Eingaben durch. Wird die Expression dabei auflösbar, geht sie in die normale Beobachtung über. Ein Evaluation-/Loadfehler liefert Ursache und Retry, keinen stillen Alt-Snapshot. Nicht auflösbare Struktur wird dokumentiert; es wird keine universelle Erkennung beliebiger externer Environment-/Taskeffekte behauptet.

Komponentenfälle müssen zumindest abdecken: anfangs leeres Wildcardimportverzeichnis → neuer `.props`-Match mit ProjectReference → geänderter Match → gelöschter Match; importierendes File außerhalb der Projektwurzel; verschachtelte konditionale Importcontainer; kontrolliert zunächst unaufgelöste Expression → Neuladung/aufgelöster Stand; Fehler → verständliche Recovery.

## Öffentliche Parameter und konsistente Symbolauflösung

Bytebudgets sind verbindlich; ihre Defaults sind toolspezifisch. Tokenbudgets sind optional und werden bei Angabe zusätzlich als harte Grenze angewendet. Öffentliche Parameter werden nur für konkrete Funktionen ergänzt. Bei `get_index_scope` sind Budgetparameter für die harte Antwortgrenze und Tokens für ausführbare Folgeaufrufe erforderlich: Der Bericht enthält Projekt-/Dateityplisten, und der vorhandene gemeinsame Aufrufpfad kann Antwortseiten oder einen laufenden Auftrag liefern. Diese Zustände dürfen keinen Token anbieten, den das Tool nicht wieder annimmt.

`get_index_scope` erhält zusätzlich zu `targetPath` genau `maxResponseBytes` (int, Default 16.384, Range 512–65.536), `maxResponseTokens` (nullable int, Default null, positive Werte), `operationToken` (nullable string, Default null) und `continuationToken` (nullable string, Default null). CancellationToken bleibt ein nicht veröffentlichter Parameter. Die Felder werden an das vorhandene gemeinsame Routing angebunden und in Schema, Validierung, Tests und Toolreferenz berücksichtigt. Budgets und Tokens gehören nicht zur fachlichen Queryidentität; ihr vorhandener Storebindungsvertrag bleibt bestehen. Kein eigener Store, neuer Routingmechanismus oder vereinheitlichungsbedingter Ausbau anderer Tools. Das Tool bleibt source-only. Neue öffentliche Projekt-/Inventoryfilter werden hier nicht eingeführt.

Assembly-`get_impact` mit omitted/false `includeReferences` muss raw Method-Doc-ID, qualifizierten Namen und gültige emittierte Position konsistent auflösen: Sie liefern dieselben Caller und Owner-Handoffs wie `h:`; unknown/foreign/source/stale opake Handles bleiben strikt; `true` aktiviert die Referenzclosure. Fehlende oder mehrdeutige Rawinputs liefern geeignete typisierte Recovery, keinen zufälligen Treffer. Der unabhängige Audit prüft diese Varianten am akzeptierten Produktionspfad.

`get_class_structure` und der Class-Structure-Abschnitt in `get_assembly_context` verwenden für omitted/`lines` vor `maxMembers`: `FilePath` mit `OrdinalIgnoreCase`, dann `StartLine` aufsteigend, dann `Name` mit `OrdinalIgnoreCase`. `lines` bedeutet Deklarationsreihenfolge und nicht Bodylänge. `kind` und `name` behalten ihre unterstützte explizite Semantik; der gemeinsame Vergleich gegen Source wird getestet. Die Regression verwendet nichtalphabetische Deklarationsreihenfolge und Cap 1 sowie vollständige Ergebnisreihenfolge und nutzbare Memberhandoffs.

Alle veröffentlichten Felder müssen genau den publizierten SDK-Schema-/Bindingvertrag erfüllen. Die produktiven SDK-Definitionen sind die Parameterquelle; eine zweite vollständige Parameter-/Nachweistabelle wird nicht erstellt. Die Toolreferenz erläutert deren Verwendung und die konkreten Abweichungen wie Defaults, Zero-Normalisierung oder Targetgrenzen. Kein schema-akzeptierter Parameter darf still unberücksichtigt bleiben, außer einer ausdrücklich beschriebenen Target-Inapplicability wie Sourcescope im Assemblymodus. Scope/Filter greifen vor Counts/Caps; fachlich ungültige Enum-/Selektorkombinationen werden zurückgewiesen.

Die verbindlichen Zero-Defaults lauten: `inspect_assembly.maxResults/maxMembers=0` → 100; `search_assembly.maxResults=0` → 50; `find_assembly_extensions.maxResults=0` → 100; `get_assembly_context.maxResults=0` → 100. `search_assembly.maxFiles=0` bedeutet kein zusätzliches matched-file-Limit, nicht keine Ergebnisse. Zero-Bytebudgets der Assemblytools wählen deren publizierten Default. Andere Tools dürfen daraus keine unpublizierte Zero-Semantik ableiten. Core-Defaults ersetzen keine abweichend festgelegten öffentlichen Defaults.

## Handoffs, Owner und Fehler

Alle veröffentlichten `h:...`-IDs sind opak und unverändert für Folgeaufrufe zu verwenden. Producer und Consumer binden Sourceidentität an kanonischen Target-/Solutionsnapshot und stabilen Projektmarker; Assemblyidentität an tatsächliche Owner-DLL, Root-/Referenzfingerprint und Generation. DTO und sichtbarer Text geben dieselbe externe ID aus. Ohne kanonische Identität gibt es weder Handoff noch falsche Werbung für einen Folgeaufruf.

Gleiche Doc-IDs, Typ-/Projekt-/Assemblynamen oder physisch geteilte Pfade dürfen nicht den ersten beliebigen Owner auswählen. Mehrdeutigkeit liefert Auswahlkandidaten mit Kontext und tatsächlich auflösbaren Handoffs. Rawqualified-/Doc-ID-/Positionsinputs benutzen die vorhandene gemeinsame Auflösung; Literal-/Punctuationpositionen und Metadatatypen werden nicht als irgendeine umgebende Sourcesymboldeklaration ausgegeben. Handoffähnliche malformed Eingaben gehen nicht in eine freie Namenssuche; Windowsdrivepfade bleiben Pfade.

Unknown, foreign, stale, abgelaufene oder frühere Runtimehandles liefern die dafür vorgesehenen typisierten Fehler ohne alte Source. Referenzowner werden nur über bewiesene aktuelle Assemblyidentitäten erreicht. Root→B→C-Followups benutzen den angebotenen kanonischen Ownerpfad. Unaufgelöste/gelöschte Referenzen müssen Vollständigkeit reduzieren; fehlende Closure darf keine scheinbar eindeutige Rawauflösung erzeugen.

## Runtime, Langläufer, Continuations und Wartung

Der Host und die gemeinsame Runtime müssen parallele unabhängige Requests bedienen. Operationsstore besitzt die Cancellation der Hintergrundarbeit. Abbruch des ersten Requests beendet seine Arbeit; Abbruch eines Poll-Waiters beendet ausschließlich dessen Warten. Hoststop/EOF beendet und drained eigene Operationen, bevor Resident-/Assemblyzustand disposed wird. Ein neuer Prozess reaktiviert keine alten opaken Handles/Tokens. Logging darf die Protokollausgabe nicht verschmutzen.

Verbindliche Grenzen: Responsewindow 15 s; bis zu vier aktive und 32 abgeschlossene Operations; standardmäßig 30 min Inaktivitätsretention; Continuations bis 32 Snapshots, 8 MiB Text, 512 Tokens und vier Budgetvarianten pro Pagetoken. Laufende Idleexpiry wird auch ohne spätere Calls angewendet. Cancellation-/Dispose-/Completionwege dürfen keine disposed-CTS-Races erzeugen. Replay einer completed Operation alloziert nicht für jedes Poll einen neuen Snapshot.

Operationstokens binden Tool, kanonischen Target, fachlichen Argumentkey und gegebenenfalls exakten Domaincursor. Budgets dürfen beim Retry steigen; andere fachliche Parameter bleiben gleich. Domaincursor und operationToken können für dieselbe Seite zusammen verwendet werden; operationToken und äußeres Pagetoken dürfen nicht vermischt werden. Storeeigene Runningkontrollen und delegiertes Loading bleiben Steuerzustände, keine angeblich vollständigen fachlichen Erfolge.

`inspect_assembly` und `search_assembly` haben `v1.<offset>.<binding>`-Domaincursor mit Query-/Target-/Root-/Referenzbinding. Äußere Antwortseiten besitzen eigene opake decimal Tokens und immutable Textsnapshots. Zuerst die äußeren Seiten vollständig lesen, dann den dort enthaltenen Domaincursor. Replay, geänderte Budgets, veränderte Query, Root-/Referenzersatz, Ablauf und Capacity sind getrennte Fälle. Ein abgelaufenes Nachfolgetoken wird nicht mit anderer Bedeutung wiederbelebt.

Graphen, Trees, Referenzen, Hierarchien, Impact und Context erhalten ihre fachlichen Caps und truthful Domaintruncation; ihnen wird kein neuer Domaincursor erfunden. Eine äußere Textfortsetzung rekonstruiert nur den bereits begrenzten fachlichen Snapshot. Sie kann deshalb nicht versteckte Domainergebnisse nachliefern.

Health ist atomar, ohne Targetload. Reload ist atomar, idempotent bei unveränderten Settings und prüft vor dem Publish das vollständige Acknowledgement im Budget. Budgetfehler, fehlende/ungültige/nicht lesbare Config lassen den alten Zustand unverändert. Nur die exakten Levels Verbose/Debug/Information/Warning/Error/Fatal sind zulässig. Die Benutzerconfig wird nicht geschrieben oder automatisch beobachtet.

## Harte Responsebudgets und Latenz

Harte Grenzen und automatisch passende Antwortseiten sind verbindlich. Explizite `maxResponseBytes`/`maxResponseTokens` werden niemals still angehoben, deaktiviert oder überschritten. Allgemeine Bytegrenzen sind inklusive 512–65.536; Tokenlimit ist optional und bei Angabe positiv. Es gibt keine globale Default-Tokenbegrenzung. Toolspezifische Byte-Defaults: find 16 KiB, body/call-tree 32 KiB, skeleton/feature/dependency/inspect/search 24 KiB, tree 8 KiB, übrige reguläre Tools 16 KiB; Assemblycontext compact/standard 32 KiB und full 65.536, mit Vorrang eines gültigen expliziten Bytecaps.

Die Budgeteinheit ist der komplette sichtbare Produkttext inklusive Status, Code, Recovery, Token und Fortsetzungshinweis: exakte UTF-8-Bytes und SharpToken `cl100k_base`-Tokens. Das ist keine Obergrenze für den gesamten JSON-RPC-Envelope oder verborgene Modelltokens. Tests messen die tatsächlich budgetierten Produktstrings.

Ein Ergebnis oberhalb des Budgets ist nicht automatisch ein Budgetfehler. Bei ausreichendem Platz für mindestens die nächste ganze Einheit inklusive verpflichtender Metadaten liefert der Server sofort eine passende, wahrheitsgetreu als truncated markierte Seite mit ausführbarem Continuationtoken. Zeilen-/Unicodescalargrenzen bleiben intakt. Bei Textpagination kein scheinbar vollständiges StructuredContent neben Teiltext. Ein atomar erforderliches strukturiertes Ergebnis darf weiterhin `STRUCTURED_RESULT_TOO_LARGE` liefern; es wird nicht still in ein anderes Ergebnisformat umgedeutet.

`RESPONSE_BUDGET_TOO_SMALL` ist erforderlich, wenn die nächste notwendige unteilbare Texteinheit, eine atomare Control-/Fehlerantwort oder ein atomarer Wartungssnapshot nicht in den gewählten Grenzen darstellbar ist. `minimumResponseBytes` und `minimumResponseTokens` werden gemeinsam aus einer tatsächlich ausführbaren Ergebnisprojektion einschließlich finalem Status und Recovery-/Continuationmetadaten berechnet. Für unveränderte immutable Projektionen gilt: Byte-Minimum = max(512, gemessene UTF-8-Bytes); Token-Minimum = gemessene cl100k_base-Tokens. Das sind die kleinsten gültigen Budgets dieser gewählten Projektion, kein pauschaler Zuschlag. Bei dynamischem Health bleiben die bestehenden konservativen ausführbaren Minima ausdrücklich als konservativ gekennzeichnet; hier wird kein mathematisch kleinster Wert über den nächsten wechselnden Snapshot versprochen.

Wenn die notwendige Einheit auch oberhalb der öffentlichen 65.536-Bytegrenze nicht darstellbar ist, empfiehlt die Antwort das Eingrenzen der fachlichen Query; sie empfiehlt kein unzulässiges Bytebudget und keinen endlosen Retry. Wenn nicht einmal der verpflichtende Error-/Recovery-Envelope in das angegebene Budget passt, folgt sanitized `InvalidParams` (-32602), ohne Stacktrace/interne Details und ohne überbudgetierten Tooltext. Pflichtfehlerfelder werden niemals einzeln abgeschnitten.

Recovery wiederholt denselben fachlichen Aufruf beziehungsweise dieselbe gebundene Seite mit beiden angebotenen Mindestwerten. Nur Bytes zu erhöhen genügt bei zugleich zu kleinem Tokencap nicht. Der Server startet beim Replay einer gespeicherten Operations-/Continuationprojektion keine neue Analyse. Normale Loading-Retries ohne gecachten fachlichen Erfolg behalten ihre definierte Ladebedeutung.

Ein zusätzlich notwendiger Toolcall kann zusätzliche Latenz erzeugen; er erzwingt technisch nicht in jedem Client einen zweiten LLM-Turn. Ein weiterer Modellturn, ein API-Request, ein Toolcall und eine Continuationpage sind unterschiedliche Messgrößen. Keine künstliche Latenzzusage und keine erfundenen API-Kosten.

Vermeidbar sind falsche Minima, fehlende Recoveryfelder, erneute Arbeit auf Immutable-Replay, doppelte Status-/Textdaten und unbrauchbare Metadatenprojektionen. Diese Ursachen sind zu beheben. Pflichtinformation wird dabei nicht entfernt. Tests prüfen bei identischen gespeicherten Inputs/Resultaten, dass normale und ausführbare Recoveryseiten keinen zusätzlichen Budgetfehler produzieren. Große fachliche Ergebnismengen dürfen legitime Folgeseiten benötigen.

## Read-only-Grenze und Laufzeit

Design-Time-MSBuild erhält Scratch außerhalb des analysierten Workspace. Die Read-only-Grenze für Custom-Targets wird mit kontrollierten importierten Targets geprüft, die Intermediate-/Outputpfade umleiten. Bekannte produktive Outputpfade dürfen weder Sources noch `obj`/`bin` oder fremde Benutzerpfade beschreiben. Ein vom Loader nicht sicher unterstützter Fall muss vor dem betreffenden Schreibeffekt als nicht unterstütztes Laden scheitern; nachträgliches Zurückkopieren ist keine Read-only-Lösung. Die Nachweise erfassen Dateien und Verzeichnisse vor/nach Loader-/Runtime-Dispose sowie Scratchcleanup.

Das Produkt ist keine Sicherheits-Sandbox für willkürlich bösartigen, ausführbaren MSBuildcode. Der Abschlussbericht benennt die konkret getesteten Custom-Targetfälle und kann daraus keinen Beweis für jeden beliebigen Fremdtask ableiten. Die Navigation selbst implementiert keine Workspace-Schreibfunktion.

`get_impact` accepts a required source or assembly `symbolIdentifier` and returns caller impact. Git revisions, worktree status, and changed-file mapping are outside the product contract.
