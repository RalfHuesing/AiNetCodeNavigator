# Quellenabdeckung und ausdrückliche Ablösung der Altaufgaben

Diese Datei gehört zur Planungsgrundlage. Sie ist keine Roadmap und enthält keine neu abgehakten Implementierungs-/Auditaufgaben. Stand: 2026-10-01, ursprünglicher Commit `a27bdda444d3648c0a927605dc55734f4c253ff6`.

## Verlustfreie Übernahme

Alle **26 Dateien** aus den beiden Altordnern sind vollständig, bytegetreu und unabhängig von deren späterer Existenz in [source-snapshot.zip](evidence/source-snapshot.zip) enthalten. [source-manifest.json](evidence/source-manifest.json) nennt je Datei ursprünglichen Pfad, ZIP-Entry, Bytezahl und SHA-256. Das ZIP ist ein nicht ausführbares Herkunftsarchiv. Darin enthaltene alte Pläne, Links, Status-/Stopanweisungen und Modellvorgaben haben keine normative Wirkung.

Das neue Konzept trägt die geltenden Anforderungen selbst: [Server](konzept/01-server.md), [Verifikation](konzept/02-verifikation.md), [Lab](konzept/03-agent-lab.md). Der Implementierer muss die alten Ordner nicht öffnen. Das Archiv wird nur zur Prüfung historischer Befunde, Zähler und Fix-/Gateprovenienz konsultiert. Die neue [historische Matrix](evidence/public-contract-matrix.md) erhält die konkreten alten Testnamen und offenen Zellen. [public-method-snapshot.json](evidence/public-method-snapshot.json) ergänzt die tatsächlich gelesenen 22 Produktionsmethodensignaturen; es ist kein erfundenes SDK-Schema.

## Jede ursprüngliche Datei

Dateinamen in der ersten Spalte sind Herkunftsbezeichnungen, keine Links auf die alten Verzeichnisse. Jeder Eintrag ist im Manifest und Archiv enthalten.

| Ursprüngliche Datei unter tasks/ | Sachinhalt / neue primäre Ablage |
|---|---|
| Navigator-Migration/Konzept.md | Produktziel, 20+2, Read-only, englische Texte, lokale Referenzen, Priorität, serielle Writer, Blocker und historische Auditgrenzen → Konzept.md/01/02; alter Ausführungsauftrag und E2E-Ziel ausdrücklich ersetzt |
| Navigator-Migration/CodeMap-Navigator.md | Ownership-/Code-/Test-/Docsanker → 01-server; vollständige alte Landkarte archiviert, keine neue Code-Map-Datei |
| Navigator-Migration/Findings.md | kompletter Stop-/Reststand und Provenienz → Konzept.md Auditstatustabelle und 01/02; stop allein bleibt historisch, unterbrochener Gesamtgate wird nicht als grün ausgegeben |
| Navigator-Migration/Review-2026-09-30.md | ursprüngliche Handoff-, Host-, Snapshot-, Fixture-/MSTest-, AssemblyDTO- und 20+2-Befunde → Erhaltungsinvarianten/Restverifikation; überholte Placeholder-/offen-Behauptungen nicht als aktuell übernommen |
| Navigator-Migration/Clusters/Cluster-01.md | TestKit, Logging, Cache → 01 Basisinvarianten, 02 Testgrenzen; abgeschlossene Basis erhalten |
| Navigator-Migration/Clusters/Cluster-02.md | Target, Registry, Load/Snapshot, MSBuildimports/globs → 01 Struktur-/Read-only-Restarbeit, 02 Komponentenfälle; 2.3 offen und 3/3 beibehalten |
| Navigator-Migration/Clusters/Cluster-03.md | kanonische Identität, opake Handles, Producer/Consumer → 01 Handoff/Owner, 02 Roundtrips; accepted/exhausted nicht neu auditiert |
| Navigator-Migration/Clusters/Cluster-04.md | Suche, Body, Skeleton, Class, Test/Feature, Resolver → 01 Toolmenge/Basis, 02 echte Handlerzellen |
| Navigator-Migration/Clusters/Cluster-05.md | Calls, transitive References/Impact, Hierarchie/Implementierungen, Dependencies → 01 Funktionen/Invarianten, 02 Crossproject-/Assemblyfälle |
| Navigator-Migration/Clusters/Cluster-06.md | Datei-/Namespace-/Indexscope, Reparse, Generated/Mixedlanguages → 01 Funktionen/Basis/Indexscopeergänzung, 02 Boundaries |
| Navigator-Migration/Clusters/Cluster-07.md | Decompiler, virtuelle Source, Assemblytools, Session/Cache/Referenzen → 01 Assembly-/Owner-/Retentioninvarianten, 02 tatsächliche Assemblyfälle |
| Navigator-Migration/Clusters/Cluster-08.md | Formatter/Results, Budgets, Status, Store, Validator und offene 8.5 → 01 gemeinsame Verträge, 02 vollständige Recovery/Loading/Pagingmatrix |
| Navigator-Migration/Clusters/Cluster-09.md | Registrierung/Wartung und offene 9.2/9.3/9.4 → 01 Handler/Lifecycle/Sortierungsfix, 02 Nicht-E2E-Audit/Definitionsparität; historischer Stand erhalten |
| Navigator-Migration/Clusters/Cluster-11.md | 11.1 E2E ausgeschlossen; 11.2 Toolkatalog/3 Clientsetups übernommen; 11.3 Gate auf Nicht-E2E begrenzt; 11.4 relevante 22er-Matrix übernommen → 02 |
| Navigator-Migration/Reviews/Cluster-01.md | alle Basisfindings/Fixes/Zähler/Gates archiviert; kein offener Clusterbefund laut finalem Review; Regressionserhalt → 01 Basis/02 |
| Navigator-Migration/Reviews/Cluster-02.md | alle Target-/Linked-/Disposal-/Importbefunde/Fixes/Zähler/Gates archiviert; Wildcard/unresolved-Restgrenze → 01; kein viertes 2.3-Punktaudit |
| Navigator-Migration/Reviews/Cluster-03.md | alle Identitäts-/Handoff-/Prefixbefunde/Fixes/Zähler; reconciled Assemblyconsumerabschluss archiviert → 01; kein viertes 3.3-Punktaudit |
| Navigator-Migration/Reviews/Cluster-04.md | Kind/Generated, Skeletoninitializers/-Variable, Markdown, Scope/Testframework/EvidenceMode und finaler Clusterreview archiviert → 01/02 Erhaltungsanforderungen |
| Navigator-Migration/Reviews/Cluster-05.md | Fanout/BCL, Relationshiptiefe/-Provenienz, Generic-/Dokumentfenster-/Sharedoffsetbefunde und finaler Clusterreview archiviert → 01/02 |
| Navigator-Migration/Reviews/Cluster-06.md | Generated/Tests, Totalzählung, Scope-/Mixedlanguage-Integration und beide Fixrunden archiviert → 01/02; keine bereits geschlossenen Findings wieder öffnen |
| Navigator-Migration/Reviews/Cluster-07.md | Decompiler/Cache/Session/Expiry/Capacity/Missingreference und referenzcontentgebundene Cachepublikation archiviert → 01/02 |
| Navigator-Migration/Reviews/Cluster-08.md | alle exakten Envelope-/Minimum-/Unicode-/Replay-/Expiry-/Bindingfixes archiviert; 8.5 und öffentlicher Restreview bleiben offen → 01/02 |
| Navigator-Migration/Reviews/Cluster-09.md | vollständige Registered-/Owner-/Closure-/Raw-/Git-/Budget-/Lifecycle-Slices und Gates archiviert; P2 unbestätigt, P3 offen, 9.3/9.4 unbestätigt → Konzept.md/01/02 |
| Navigator-Migration/Reviews/public-contract-matrix.md | jede Success-/Failure-/Budget-/Paging-/Loading-/Lifecyclezelle vollständig konserviert → evidence/public-contract-matrix.md; neue Zellen und zulässiger Prüfweg → 02 |
| Agent-Interaction-Lab/README.md | separate Exe, Produktabgrenzung, gemeinsame Contracts, 22 Tools, Infrastruktur/Untersuchung → 03; überholte separate Review-/Freigabesperre ersetzt |
| Agent-Interaction-Lab/Konzept.md | gesamter technischer Vertrag einschließlich SDKmetadaten, Worker/IPC/CLI, Zustände/Limits, Targets/Fingerprints, Rawartefakte/Renderer, Rollen/Isolation, Tasks/Coverage/Messung und zweier Gates → 03; konkrete ersetzte Vorgaben siehe unten |

## Alle offenen und unnummerierten Migrationsthemen

| Altes Thema | Verbindliche neue Disposition |
|---|---|
| 2.3 deklarierte Wildcardimports | Muss: passende Importpfadmenge samt Inhaltsänderungen und Empty→Match/Missingfälle; 01/02 |
| 2.3 unaufgelöste Expressions | Muss: explizit unsichere Struktur mit erneuter Evaluation/Load statt unbegründet aktuellem alten Snapshot; 01/02 |
| 8.5 per-tool Byte-/Tokenminima | Muss: alle realen Projektionen, ausführbares gemeinsames Paar, Envelopefallback; konkrete echte Inapplicability begründen; 01/02 |
| 8.5 kaltes Loading/Operationen | Muss: deterministisches Control→Retry→Ergebnis und Ownership; keine pauschale Sharedfixtureabnahme; 02 |
| 8.5 fehlende Outerrekonstruktion | Muss: je tatsächlicher Pagingtoolprojektion immutable vollständige Rekonstruktion; Domaincaps/Cursor separat; 02 |
| 9.2 P2 default-false Rawimpactfix | implementiert laut Vorgängernachweis, unabhängig noch zu bestätigen; 01/02 |
| 9.2 P3 Assemblymembersortierung | Muss schließen: filepath/start/name vor Cap bei omitted/lines, explicit sorting/Memberhandoffs erhalten; 01/02 |
| 9.2 Parameter/Default/Cap/Filterparität | Muss: vollständige publizierte Parameter und tatsächliche Handlerwirkung, Targetgrenzen und gekoppelte Selektoren; 01/02 |
| breitere Assemblybeziehungen | Muss: relevante Owner-/Closure-/Raw-/Replacement-/Missing-/Depthfälle für referenzfähige Tools; Rootsemantik von Hierarchie/Implementierungen bleibt; kein unspezifizierter universeller Closureausbau; 01/02 |
| 9.3 Registration/Schema/Fehlerüberleben | Muss unabhängiger Audit von Katalog, produktivem Adapter, Validator/Handler und validem Folgeaufruf; kein neuer Transporttest; 02 |
| 9.4 Parallelität, Pollcancellation, Gitchild/Shutdown, Neustart, Logs | Muss komponentennah unabhängig prüfen; Hostverdrahtung read-only auditieren; alte Transportnachweise nur historisch; 01/02 |
| Cluster 8/9 abschließende Zusammenschaltung | Muss gemeinsame Schnittstellenreview mit erhaltenen Zählern; keine laufende Schreibphase; Konzept.md/02 |
| Custom-MSBuild-Targetredirection | Muss kontrollierte Fälle prüfen und produktive Outputgrenzen einhalten; keine unbelegte allgemeine Sandboxzusage; 01/02 |
| ungewöhnliche Gitnamen/Rename | Muss zulässige tatsächliche Gitdaten und Rename-/Deletionsemantik komponentennah prüfen; 01/02 |
| lange Integrationstests | Muss historische Phasenursachen untersuchen und vermeidbare Fixturearbeit reduzieren; keine E2E-Neumessung/Speedbehauptung; 01 |
| 11.1 neuer E2E-Ausbau | Ausdrücklich ausgeschlossen durch Nutzer; erhaltene Tests nicht löschen/abschwächen, hier nicht ausführen; 02 |
| 11.2 Katalog/Schema/Parameter | Muss vollständiger aus dem Produktionskatalog erzeugter Export plus verständliche Englischreferenzen; 02 |
| 11.2 Claude Desktop/Cursor/Antigravity | Muss drei aktuelle belegte Setupanleitungen; kein Deployment/Clientstarttest; 02 |
| 11.3 Build/Gesamtgate | Muss offizielle zulässige Gateauswahl 0 Warnungen/Fehler, alle gewählten erforderlichen Tests grün; ausgeschlossene E2E separat; 02 |
| 11.4 vollständige 22er-Matrix/keine Lintertools | Muss relevante öffentliche Vertragsabdeckung am gemeinsamen Produktionspfad und exakte erlaubte Toolmenge; 01/02 |
| Root-/Docs-/AGENTS-Links auf Altordner | Muss vor Löschung auf neue Quelle umstellen; diese Planung ändert ausschließlich neuen Task; 02 |

Zusätzlicher Codebefund dieser Planung: `get_index_scope` kann die bisher geforderten öffentlichen Budget-/Operations-/Pagingzellen derzeit mangels Parameter nicht bedienen. Die konkrete APIergänzung ist in 01 festgelegt und wird in 02 nachgewiesen; dieser Befund ist nicht als schon behoben markiert.

## Bewusste Konfliktauflösung für das Lab

| Alte Vorgabe | Neue eindeutige Festlegung |
|---|---|
| separate allgemeine Umsetzungssperre bis nach weiterer Nutzerfreigabe | ein Gesamtkonzept bleibt draft; Workflowfreigabe in Schritt 2. Technischer Preflight nach Serverabschluss und frischer Verständnisprüfer bleiben Muss, keine zusätzliche allgemeine Taskfreigabeschleife |
| historische Annahme nur Wartung registriert / Navigationsplaceholder | überholt; aktuell 22 Registrierungen gelesen, restliche Abnahme offen |
| teils „beide Solutions/Assemblies“, sonst nur zwei Target-IDs | genau navigator-source und navigator-assembly desselben eigenen Repositorys; kein zweites Repository |
| nur eine Untersuchung, keine automatische Verbesserung / spätere Fixaufträge außerhalb | Muss R0 und R1, R2 nach verbliebenen bestätigten Findings; getrennte Korrekturphasen innerhalb dieses Vorhabens; keine Fixes während bewerteten Runs |
| Medium-Audit-/Repositorydefault-Modell | Umsetzung/Taskautor/Test/Verständnis: gpt-6-luna/high; Audit/Analyse: gpt-6.1-sol/high |
| neue MCP-/SDK-Transportparität und komplette Prozessintegration als Testgate | SDK-Adapter-/Funktions-/Worker-/IPCkomponenten transportlos; echte Agentenruns getrennt; E2E nicht eingeführt/ausgeführt |
| selber Targetcheckout ändert sich mit Produktfixes | unveränderter eigener Source-/Assemblybaseline-Snapshot, wechselnde getestete Produktversion; Hash-/Referenzgleichheit je Run |
| historisches Gate 1 Infrastruktur / Gate 2 erste Untersuchung | L1 unverändert in seiner Infrastrukturintention, unter Nicht-E2E-Grenze; L2 erweitert um begrenzte Verbesserung und Vergleich |
| fehlende Beobachtung teils Blocker, teils not_observed | unbearbeitete/blocked Pflichtaufgabe blockiert; dokumentierter gültiger Versuch eines seltenen Spezialfalls darf qualitative not_observed-Zelle hinterlassen; deterministische Produktabnahme bleibt Pflicht |
| Raw-/Event-/Crash-/Busy-/Recoverylücken aus alten Verständnisreviews | präzisierte technische Verträge vollständig aus Altentwurf übernommen; keine Umdeutung syntaktischer Laberrors zu Produktfehlern oder erfundener Crashresultate |

## Löschbarkeit

Der Konzept- und Evidenzbestand dieses neuen Ordners bleibt vollständig, wenn beide alten Taskordner entfernt werden. Historische Details stehen im ZIP, aktive Soll-Verträge in den drei Kapiteln; kein Kapitel verlinkt die alten Ordner. Die repositoryweite Quellenumschaltung aus Kapitel 02 ist vor dem tatsächlichen Löschen zusätzlich erforderlich, damit bereits vorhandene Root-/Docs-/AGENTS-Links nicht ins Leere zeigen. Sie gehört zur späteren Umsetzung; die beiden Altordner werden durch diese Konzeptarbeit nicht gelöscht.

## Ausgeführte Quellenprüfung dieser Konzeptarbeit

Am 2026-10-01 wurden alle 26 ZIP-Einträge erneut gelesen und gegen Manifest sowie unveränderte Originaldateien verglichen: Bytezahlen und SHA-256 stimmen vollständig überein. Für jeden Originalpfad existiert eine Zuordnungszeile oben. Alle 29 lokalen Markdownlinks dieses neuen Ordners wurden auf vorhandene Ziele und Unabhängigkeit von den alten Taskverzeichnissen geprüft. Der Quellmethodensnapshot enthält exakt 22 eindeutige annotierte Toolnamen. Diese Ergebnisse prüfen Übernahme und Dokumentation; sie behaupten keine neuen erfolgreichen Produktgates oder ein unabhängiges Konzeptaudit.
