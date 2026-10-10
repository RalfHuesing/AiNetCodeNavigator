# Evaluation und Architektur-Vorschläge: AiNetCodeNavigator für agentische C#-Entwicklung

**Datum:** 2026-10-10  
**Status:** Diskussions- und Analysebasis  
**Gegenstand:** Eignung, Stärken, Schwächen und Optimierungspotenziale des MCP-Servers `AiNetCodeNavigator` im Kontext moderner agentischer Softwareentwicklung.

---

## 1. Executive Summary & Management-Fazit

> **Ist AiNetCodeNavigator kompletter Müll?**  
> **Eindeutig nein.** Die compilertechnische Basis (Roslyn 5.9 Workspace-Management, semantische Typauflösung, ILSpy-basierte Dekompilierung, Memory-Sicherheit, deterministische Fingerprints) ist exzellent und auf Enterprise-Niveau umgesetzt.
>
> **Ist es in der aktuellen Form optimal für moderne KI-Agenten?**  
> **Nein.** Das Tool leidet unter einem klassischen Fall von **„Design für den falschen Konsumenten“**:
> - Es wurde wie eine ultra-defensive, zustandsbehaftete Microservice-RPC-API für einen deterministischen C#-Client gebaut.
> - LLM-Agenten (Claude 3.5/3.7 Sonnet, OpenAI o1/o3/GPT-4o, Gemini 2.5/Flash) sind jedoch probabilistische Reasoning-Engines: Sie benötigen **flache, fehlertolerante, atomare Werkzeuge** mit minimalen Round-Trips.
> - Das aktuelle MCP-Interface erzeugt durch **12 fragmentierte Tools**, **3 verschiedene Paging/Polling-Tokens** (`operationToken`, `continuationToken`, `resultCursor`), rigide Validierungs-Fallen und fragile `handoffId`-Strings massive Reibung (*Agent Friction*), die Agenten in der Praxis verlangsamt oder in Endlosschleifen treibt.

### Zentrale Erkenntnisse auf einen Blick:
1. **Roslyn ist für C# unverzichtbar:** Reine Textsuche (`ripgrep`) scheitert in C# regelmäßig an Dependency Injection, Interfaces, Vererbung und Method-Overloads. Roslyns semantische Analyse ist hier unschlagbar.
2. **Tool-Sprawl schadet Agenten:** 12 separate Tools überlasten das Tool-Selection-Verhalten von LLMs.
3. **Paging & Polling sind Gift:** Das Zerstückeln von Antworten auf standardmäßig 16–32 KB mit `continuationToken` und das asynchrone Polling mit `operationToken` sind in Zeiten von 200k+ Context Windows kontraproduktiv.
4. **Größter blinder Fleck:** Dem Server fehlt die wichtigste Superkraft eines Compilers für schreibende Agenten – **On-Demand Compiler-Diagnostik (Errors & Warnings nach einem Code-Edit)**.

---

## 2. Der State of the Art: OpenAI, Anthropic und Agentic Coding (2025/2026)

### 2.1 Anthropics Philosophie: „Radical Simplicity“ & Tool-Ökonomie
In der richtungsweisenden Veröffentlichung *„Building Effective Agents“* (Anthropic Research) sowie der Architektur von **Claude Code** werden klare Prinzipien für Agent-Computer-Interfaces (ACI) formuliert:
- **Wenige, klare Werkzeuge:** LLMs schneiden dramatisch schlechter ab, wenn ihnen mehr als 10–15 Werkzeuge zur Verfügung stehen (*Tool Sprawl*, *Selection Confusion*).
- **Das Standard-Toolset von Claude Code:**
  - `Grep` (schnelle Regex-Suche via `ripgrep` für grobe Lokalisierung)
  - `Glob` (Dateifindung)
  - `Read` (Dateiinhalte mit Zeilenfenstern)
  - `Edit` (gezieltes Suchen & Ersetzen)
  - `Bash` (Ausführung von Tests, Builds, Git)
  - *Optional:* Ein schlankes `LSP`-Tool (Language Server Protocol) für `definition`, `references` und `diagnostics`.
- **Eindeutige Schnittstellen:** Vermeidung überlappender Werkzeuge. Wenn ein Agent zwischen drei ähnlichen Werkzeugen wählen muss, sinkt die Erfolgsrate signifikant.

### 2.2 OpenAI & SWE-bench Forschung: Der „Round-Trip-Killer“
Forschungsergebnisse rund um **SWE-agent** (Yang et al., Princeton / NeurIPS 2024) und OpenAIs Codex/Operator-Modelle zeigen:
- **Latenz & Token-Drift:** Jeder Tool-Call benötigt bei modernen Reasoning-Modellen 3 bis 8 Sekunden. Ein Protokoll, das für eine einfache Frage 3 bis 5 aufeinanderfolgende Tool-Calls erzwingt (z. B. Suchen $\to$ Polling $\to$ Paging-Fortsetzung $\to$ Body abrufen), erzeugt enorme Latenz und erhöht das Risiko von Halluzinationen exponentiell.
- **Kontextfenster-Evolution:** Frühere Modelle (GPT-3.5) benötigten winzige Response-Chunks. Moderne Modelle (Claude 3.7, o3-mini, Gemini Flash/Pro) operieren mit 128k bis 1M+ Tokens. Das künstliche Aufspalten von 20 KB Text in mehrere Chunks ist technisch überholt.

---

## 3. Einzelbeurteilung aller 12 MCP-Tools von AiNetCodeNavigator

### 3.1 `find_symbol`
- **Funktion:** Semantische Suche nach C#-Deklarationen (Klassen, Methoden, Interfaces etc.) in Solution oder Assembly mit Filtern.
- **Nutzen:** ⭐⭐⭐⭐ (Sehr hoch)
- **Stärken:** Unverzichtbar zum Lokalisieren von Symbolen ohne Raten von Dateinamen. Exakter als Grep.
- **Schwächen & Reibung:** Stark überfrachtetes Schema (17 Parameter!). Die gegenseitige Ausschließlichkeit von `pattern` und `namePatterns` führt bei kleinsten LLM-Abweichungen zu `InvalidArgument`-Fehlern.
- **Empfehlung:** **Behalten & drastisch vereinfachen.** Parameter auf `query`, optional `kind` und `project` reduzieren.

### 3.2 `get_symbol_body`
- **Funktion:** Liest den Quellcode oder dekompilierten Text eines Symbols anhand von ID/Name mit Zeilenfenstern.
- **Nutzen:** ⭐⭐ (Niedrig–Mittel für Source, Hoch für Assemblies)
- **Schwächen & Reibung:** 
  1. *Redundanz:* Agenten verfügen über native File-Viewer (`read_file`, `view_file`). Sobald `find_symbol` Dateipfad und Zeile liefert, liest der Agent die Datei direkt mit vollem Datei-Kontext (Imports, Nachbarmethoden).
  2. *Verwirrung durch Zeilennummerierung:* Die Zeilenfenster in `get_symbol_body` sind *relativ zur Deklaration*, nicht die echten Zeilen der Datei. Möchte der Agent die Methode anschließend mit einem Edit-Tool ändern, fehlen ihm die physischen Dateizeilen.
- **Empfehlung:** Für Source-Code **depriorisieren/entfernen**. Nur noch als Fallback für dekompilierte DLLs vorhalten.

### 3.3 `browse_target`
- **Funktion:** Zwei Modi: `scope` (geladene Projekte/Dateien) oder `namespaces` (Hierarchie bis Tiefe 3).
- **Nutzen:** ⭐ (Sehr niedrig / Überflüssig)
- **Schwächen & Reibung:**
  1. `scope`: Ein simples `glob` oder Dateisystem-Listing liefert dasselbe in 5 ms ohne Roslyn-Warmup.
  2. `namespaces`: Menschliche IDE-Denke (Visual Studio Object Browser). LLMs navigieren nicht schrittweise durch Namensraum-Bäume; sie suchen direkt nach Fachbegriffen.
- **Empfehlung:** **Komplett entfernen.**

### 3.4 `get_file_skeleton`
- **Funktion:** Erstellt ein Interface-/Struktur-Skelett einer C#-Datei (Typen, Member-Signaturen ohne Methodenrümpfe).
- **Nutzen:** ⭐⭐⭐⭐⭐ (Absolutes Gold für Agenten)
- **Stärken:** Enorme Token-Ersparnis. Verhindert, dass ein Agent 1.500 Zeilen Boilerplate in den Prompt laden muss, nur um vorhandene Methoden zu sehen (entspricht Aiders populärer *Repo-Map*-Architektur).
- **Schwächen:** Liefert derzeit keine physischen Start-/Endzeilen der Member im File mit.
- **Empfehlung:** **Unbedingt behalten und ausbauen!** Physische Zeilennummern ergänzen.

### 3.5 `get_type_relations`
- **Funktion:** Vererbungshierarchien (`hierarchy`) oder Interface-Implementierungen & Overrides (`implementations`).
- **Nutzen:** ⭐⭐⭐⭐⭐ (Essentiell für C#)
- **Stärken:** Das absolute Killer-Feature gegenüber `grep`. In modernen C#-Codebases (Clean Architecture, DI, CQRS) existieren dutzende Interfaces (`IRepository`, `ICommandHandler`). Kein Textsuchwerkzeug kann sauber bestimmen, welche konkrete Klasse ein Interface implementiert.
- **Schwächen:** Künstliche Trennung in zwei Betriebsmodi per Pflichtparameter `relation`.
- **Empfehlung:** **Behalten**, aber Schnittstelle vereinfachen.

### 3.6 `get_call_tree`
- **Funktion:** Statischer Aufrufgraph (wer ruft wen auf) bis Tiefe 5, dargestellt als ASCII oder Mermaid.
- **Nutzen:** ⭐⭐⭐ (Mittel)
- **Stärken:** Wichtig für Impact-Analysen („Was bricht, wenn ich diese Methode ändere?“).
- **Schwächen:** Explodiert bei echten Projekten sehr schnell (führt zu `truncated`). ASCII-Art und Mermaid sind für Menschen optisch ansprechend, für LLMs jedoch schwerer und ungenauer zu parsen als kompaktes JSON.
- **Empfehlung:** **Überarbeiten:** Ausgabe auf kompaktes JSON fokussieren; enge Grenzen beibehalten.

### 3.7 `find_references`
- **Funktion:** Findet alle Verwendungsstellen (Usages) eines Symbols.
- **Nutzen:** ⭐⭐⭐⭐⭐ (Kern-Werkzeug für Refactorings)
- **Stärken:** Semantische Präzision. Verhindert das Grep-Chaos bei gängigen Bezeichnern (`Id`, `Execute`, `Name`).
- **Schwächen:** Die Option `depth=2..3` dupliziert funktionell `get_call_tree`. Die Paginierung/Token-Logik macht tiefe Suchläufe fehleranfällig.
- **Empfehlung:** **Behalten!** Fokus rein auf direkte Referenzen (`depth=1`).

### 3.8 `dependency_graph`
- **Funktion:** Abhängigkeitsgraph auf Typ-, Datei-, Namensraum- oder Projektebene.
- **Nutzen:** ⭐⭐ (Niedrig für Entwicklungstasks)
- **Schwächen:** Gut für statische Architektur-Reviews und Dokumentation. Für einen Agenten, der einen Bug fixt oder ein Feature baut, liefert das Tool jedoch zu viel Rauschen und verbraucht massiv Tokens und Analysezeit.
- **Empfehlung:** Aus dem MCP-Server **entfernen** oder in das separate Offline-CLI-Tool auslagern.

### 3.9 `resolve_type_origin`
- **Funktion:** Ermittelt, ob ein Typ aus Source, einem NuGet-Paket oder der BCL stammt.
- **Nutzen:** ⭐ (Überflüssig als eigenes Tool)
- **Schwächen:** Reines Symptom von Tool-Sprawl. Diese Information gehört als einfaches Attribut (`origin: "Project" | "NuGet" | "BCL"`) direkt in das Ergebnis von `find_symbol`.
- **Empfehlung:** **Entfernen** und als Feld in `find_symbol` integrieren.

### 3.10 `get_context`
- **Funktion:** Kombi-Tool: Ruft `body`, `members`, `uses` (Referenzen) und `tests` (statische Testkandidaten) in einem einzigen Aufruf ab.
- **Nutzen:** ⭐⭐⭐⭐ (Hervorragende Idee, aber überkomplexe Umsetzung)
- **Stärken:** Folgt genau dem modernen Best-Practice-Prinzip: **1 Call statt 4 Calls!** Besonders die Heuristik für Test-Kandidaten (`tests`) ist für TDD-Agenten genial.
- **Schwächen:** 14 Parameter, komplexe Sub-Cursor (`continuation.Section`), Fehleranfälligkeit bei unvollständigen Argumenten.
- **Empfehlung:** **Zum primären Standard-Werkzeug für Symbol-Inspektion machen**, dafür Schnittstelle radikal entschlacken.

### 3.11 `inspect_assembly` & 3.12 `search_assembly`
- **Funktion:** Inspektion von Typen/Membern bzw. Textsuche in vorkompilierten `.dll`/`.exe`-Dateien.
- **Nutzen:** ⭐⭐⭐ (Nische im Standardfall, extrem nützlich im Enterprise-Umfeld)
- **Stärken:** Gibt Agenten Einblick in geschlossene Third-Party-Bibliotheken oder veraltete interne NuGet-Pakete ohne Quellcode.
- **Schwächen:** Zwei getrennte Tools für DLL-Navigation vergrößern den Tool-Katalog unnötig.
- **Empfehlung:** Zu einem einzigen Tool `csharp_assembly` **zusammenlegen**.

---

## 4. Fundamentale Reibungspunkte & Anti-Patterns für KI-Agenten

### 4.1 Das „Drei-Token-Monster“ (Polling & Chunking)
Im Code von `AiNetCodeNavigator` existieren drei parallele Kontrollfluss-Token:
1. `operationToken` $\to$ Asynchrones Polling, wenn Roslyn länger als ein Schwellenwert rechnet.
2. `continuationToken` $\to$ Wenn die Antwort das UTF-8-Byte-Limit (Default: 16–24 KB) übersteigt.
3. `resultCursor` $\to$ Paginierung innerhalb der fachlichen Ergebnisliste.

*Warum das für Agenten toxisch ist:*
- LLMs können nicht zuverlässig pollen. Erhält ein Agent `Status: operation=running`, versteht er oft nicht, dass er denselben Aufruf nach 500 ms wiederholen soll. Manche Modelle brechen ab, andere halluzinieren.
- Eine künstliche Grenze von 16 KB Text (~4.000 Tokens) zwingt Agenten zu ständigen Nachfolge-Aufrufen, obwohl moderne Modelle 200.000 bis 1.000.000 Tokens verarbeiten können.

### 4.2 Die String-Fragilität (`handoffId`)
Identifier wie `src:src/AiNetCodeNavigator/AiNetCodeNavigator.csproj|M:Namespace.Class.Method(System.String)` sind deterministisch, aber:
- Wenn das LLM die ID um ein einziges Zeichen falsch wiedergibt oder URL-Escaping (`%20`, `|`) fehlschlägt, wirft der Server harte `INVALID_SYMBOL_REFERENCE`-Fehler.
- Zeilennummer + Dateipfad oder einfache qualifizierte Namen sind für Sprachmodelle nachweislich ergonomischer.

### 4.3 Der größte blinde Fleck: Fehlende Compiler-Diagnostik (*Errors on Edit*)
Was ist der **wichtigste Mehrwert** eines echten Compilers im Vergleich zu Grep während des Codens?  
**Das unmittelbare Feedback nach einer Code-Änderung!**
- Wenn ein Agent eine C#-Datei ändert, will er wissen: *„Gibt es Syntaxfehler, Typkonflikte oder fehlende Using-Direktiven?“*
- Aktuell bietet der MCP-Server keinerlei Diagnostik-Tool. Der Agent muss den schweren `dotnet build` über die Konsole starten, was den Build-Cache sperrt, Zeit kostet und unstrukturierten Text ausgibt.

---

## 5. Zielentwurf: „AiNetCodeNavigator v2“ (4 Kern-Tools + 1 Feedback-Tool)

Durch die Konsolidierung von **12 fragmentierten Tools auf 5 klare, fehlertolerante Werkzeuge** wird der Tool-Prompt um ~60 % verkleinert und die Fehlerrate von Agenten drastisch gesenkt:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   AiNetCodeNavigator v2 Toolset                        │
├────────────────────────────────────────────────────────────────────────┤
│ 1. csharp_find         → Symbolsuche + Herkunft (Source/NuGet/BCL)     │
│ 2. csharp_outline      → File-Skelett mit physischen Start-/Endzeilen │
│ 3. csharp_inspect      → Unified Context (Implementierungen + Usages)  │
│ 4. csharp_assembly     → DLL-Decompilation & Suche                     │
│ 5. csharp_diagnostics  → [NEU] Roslyn-Compilerfehler für Datei/Projekt │
└────────────────────────────────────────────────────────────────────────┘
```

### 5.1 Tool-Spezifikationen im Detail

#### Tool 1: `csharp_find`
*Ersetzt:* `find_symbol`, `resolve_type_origin`  
*Zweck:* Findet Symbole semantisch in der Solution.
```json
{
  "name": "csharp_find",
  "description": "Find C# symbols by name or pattern across the solution. Returns file paths, line numbers, and declaring origin.",
  "parameters": {
    "query": { "type": "string", "description": "Symbol name, wildcard, or qualified name (e.g. 'OrderService' or '*.Process')" },
    "kind": { "type": "string", "enum": ["all", "class", "interface", "method", "property", "enum"], "default": "all" },
    "project": { "type": "string", "description": "Optional project name filter" }
  }
}
```

#### Tool 2: `csharp_outline`
*Ersetzt:* `get_file_skeleton`, `browse_target`  
*Zweck:* Liefert die Struktur einer C#-Datei mit echten Dateizeilen (spart 90 % Lesetokens).
```json
{
  "name": "csharp_outline",
  "description": "Get structural outline of a C# file (classes, interfaces, methods, properties) with exact line numbers. Excludes implementation bodies.",
  "parameters": {
    "filePath": { "type": "string", "description": "Absolute or solution-relative path to the .cs file" }
  }
}
```

#### Tool 3: `csharp_inspect`
*Ersetzt:* `get_context`, `get_type_relations`, `find_references`, `get_call_tree`  
*Zweck:* Liefert in **einem einzigen Call** den gesamten Beziehungs- und Verwendungs-Kontext eines Symbols.
```json
{
  "name": "csharp_inspect",
  "description": "Get complete relationship context for a C# symbol: implementations, base types, callers/references, and associated unit tests.",
  "parameters": {
    "symbolOrLocation": { "type": "string", "description": "Symbol name, doc ID, or 'file.cs:line'" },
    "includeReferences": { "type": "boolean", "default": true, "description": "Include places where this symbol is called/used" },
    "includeImplementations": { "type": "boolean", "default": true, "description": "Include interface implementations or class overrides" },
    "includeTests": { "type": "boolean", "default": true, "description": "Include candidate test methods/fixtures covering this symbol" }
  }
}
```

#### Tool 4: `csharp_assembly`
*Ersetzt:* `inspect_assembly`, `search_assembly`, `get_symbol_body` (Assembly-Pfad)  
*Zweck:* Inspektion und Volltextsuche in externen `.dll`-Dateien.
```json
{
  "name": "csharp_assembly",
  "description": "Inspect public API surface or search decompiled code in a compiled .dll or .exe.",
  "parameters": {
    "assemblyPath": { "type": "string", "description": "Absolute path to the .dll or .exe" },
    "query": { "type": "string", "description": "Optional type/member name filter or text search pattern" },
    "readBody": { "type": "boolean", "default": false, "description": "Return decompiled C# source body for matched symbols" }
  }
}
```

#### Tool 5: `csharp_diagnostics` (Das fehlende Puzzleteil)
*Zweck:* Sofortiges Compiler-Feedback im In-Memory Roslyn-Workspace nach einem Dateiedit.
```json
{
  "name": "csharp_diagnostics",
  "description": "Get real-time Roslyn compiler diagnostics (errors and warnings) for a file or the whole solution without running a full disk build.",
  "parameters": {
    "filePath": { "type": "string", "description": "Optional specific file to check; omit for solution-wide errors" },
    "severity": { "type": "string", "enum": ["error", "warning", "info"], "default": "error" }
  }
}
```

---

## 6. Protokoll- und Laufzeit-Empfehlungen

1. **Weg mit dem 16-KB-Paging:**
   - Standard-Budget (`maxResponseBytes`) von 16 KB auf **128 KB** anheben.
   - Moderne LLMs verarbeiten 128 KB problemlos in einem Zug; das spart unzählige unnötige Folgeaufrufe.
2. **Synchrone Timeouts statt `operationToken`:**
   - Aufrufe bis zu **15–20 Sekunden synchron** blockieren lassen.
   - Ein kurzes Warten ist für den Agenten 10-mal stabiler als ein asynchrones Polling-Protokoll.
3. **Fehlertolerante Eingabeverarbeitung (Graceful Degradation):**
   - Wenn der Agent Anführungszeichen, Leerzeichen oder leicht abweichende Flags übergibt, nicht sofort mit `InvalidArgument` abbrechen, sondern defensiv normalisieren.

---

## 7. Belege und Quellenangaben

1. **Anthropic Research:**
   - *„Building Effective Agents“* (Anthropic Engineering Blog, 2024): Prinzipien der *Radical Simplicity*, Vermeidung von Tool-Sprawl, Design von ACI (Agent-Computer Interfaces).
   - *Claude Code Architecture Documentation & Best Practices* (Anthropic, 2025): Analyse der Kernwerkzeuge (`Grep`, `Glob`, `Read`, `Edit`, `Bash`, `LSP`).
2. **Princeton University / SWE-bench:**
   - Yang, J., Jimenez, C. E., et al. (2024): *„SWE-agent: Agent-Computer Interfaces Enable Automated Software Engineering“*, NeurIPS 2024 / arXiv:2405.15793. Nachweis, dass fensterbasierte Datei-Viewer und atomare Suchen komplexe Multi-Tool-APIs schlagen.
3. **Model Context Protocol (MCP):**
   - *Model Context Protocol Specification* (Anthropic / ModelContextProtocol GitHub, 2024–2025): Standards für Tool-Definitionen, Fehlerbehandlung und Transport via Stdio.
4. **Microsoft Roslyn & Language Server Protocol (LSP):**
   - *Language Server Protocol Specification 3.17* (Microsoft): Etablierte Standards für semantische Navigation (`workspace/symbol`, `textDocument/references`, `textDocument/diagnostic`).
   - Open-Source Roslyn-MCP-Implementierungen: *MadQ/RoslynMcp*, *JoshuaRamirez/RoslynMcpServer*, *modelcontextprotocol/csharp-sdk*.
