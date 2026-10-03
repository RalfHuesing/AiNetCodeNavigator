# M2-A — Audit der Agentenoberfläche

[Roadmap index and status](../roadmap.md) · [Execution and evidence rules](../roadmap.md#ausführung-und-nachweise) · [Approved concept](../Konzept.md)

**Intention/Scope:** Alle betroffenen Source-/Assembly-Routen, Projektionen, Cursor-/Budgetverträge, Testkandidaten, Kontextabschnitte, Ersatzwege, den exakten 17-Tool-Katalog und aktuelle Agentenanleitung unabhängig gegen Muss 2–7 und 9–11 prüfen. Nicht gewählte Kontextarbeit und entfernte exklusive Pfade besonders beachten.

**Nicht:** Kein Benchmark der Zwischenstände, keine Scope-Erweiterung und keine Implementierung durch den Auditor.

**Abnahme:** Keine offene blockierende Abweichung. Findings und belegte Behandlung stehen direkt am Punkt. Die fachlichen Voraussetzungen für den finalen Vorher-/Nachher-Abgleich sind hergestellt; erst dann wird der M2-Aggregate-Haken gesetzt.

## Auditbefund (2026-10-03)

**Offen — verbliebener `detailLevel`-Hilfscode:** `src/AiNetCodeNavigator/Mcp/Tools/Assemblies/AssemblyTools.cs:198-221` enthält weiterhin die privaten Methoden `TryDetail` und `TryGetDetailBudget`. Eine Suche über Produktionscode findet keinen Aufrufer. Der Vergleich mit dem M2-Baseline-Stand `55c1993^` zeigt, dass diese Methoden ausschließlich den entfernten `detailLevel`-Pfad unterstützten. Die öffentliche Tooldefinition akzeptiert `detailLevel` nicht mehr; der tote Hilfscode ist jedoch ein übrig gebliebener Legacy-Pfad entgegen der Greenfield-Vorgabe und muss im begrenzten Korrekturslice entfernt werden. Das Finding betrifft keine aktuelle Schemaausgabe.

**Geprüfter Umfang:** Der unabhängige Vergleich erfolgte gegen den kumulativen Diff `55c1993^..5b6727b3c2bba4709cf44fd6028a5181a7644fca`. Die aktuellen Handler, Core-Scanner und Vertragsprüfungen belegen die 17-Tool-Registrierung, Assembly-Projektionen und Owner-Handoffs, Diagnoseprojektion, Literal-/Regexvertrag, getrennte Ergebnis-/Antwortfortsetzung, Body-/Namespace-Recovery, Testkandidatenbounds, ausgewählte Kontextabschnitte, gemeinsame Identität/Leases/Snapshotbindung, Scopevalidierung sowie Fehler- und Teilergebnisstatus. Die drei früheren Kontext-Handler/Scanner/DTO-Routen sind aus den Produktions- und aktiven Dokumentationspfaden entfernt; wiederverwendbare Testkandidatenermittlung bleibt über `TestRecommendationBuilder` erhalten. Quellbelege und die in M2-T1 bis M2-T6 protokollierten gezielten Gates wurden gegen den kumulativen Endstand abgeglichen.

Der generierte Hostassembly-Informationsversionswert in `src/AiNetCodeNavigator/obj/Debug/net10.0/AiNetCodeNavigator.AssemblyInfo.cs` lautet `1.0.0+5b6727b3c2bba4709cf44fd6028a5181a7644fca` und stimmt mit dem geprüften HEAD überein. Hostcode setzt diesen Wert für `ServerInfo` und das bestehende Startup-Ereignis; MCP-SDK-XML und transportfreie Vertragsprüfungen stützen die Zuordnung. Ein MCP-Prozessstart mit Initialize/stdio-Mitschnitt wurde nicht ausgeführt; daher wird kein Laufzeitmitschnitt behauptet. Die aktuellen Toolreferenzen und `.agents/rules/08-mcp-navigation.mdc` beschreiben den implementierten 17-Tool-Katalog, Owner-Recovery, äußere Seiten, Polling, Ergebnis-Cursor und die Kontextabschnitte. Historische Roadmap-Evidence darf die entfernten Namen zur Beschreibung des Verlaufs enthalten.

Das Finding hält M2-A und das M2-Aggregat offen. Der Audit hat keine Produktionskorrektur vorgenommen und keine zusätzlichen Builds, Tests oder Laufzeit-/E2E-Prüfungen ausgeführt. `git diff --check 55c1993^..HEAD` war erfolgreich.
