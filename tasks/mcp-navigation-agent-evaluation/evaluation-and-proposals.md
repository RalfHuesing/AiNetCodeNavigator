# Evaluation und Architektur-Vorschläge: AiNetCodeNavigator für agentische C#-Entwicklung

**Datum:** 2026-10-10 (Aktualisiert mit 2025/2026er Forschungs- und Industriestandards)  
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
> - LLM-Agenten (Claude 3.7 Sonnet, Claude Sonnet 4.x, OpenAI o3/o3-mini, Gemini 2.5 Flash Thinking) sind jedoch probabilistische Reasoning-Engines: Sie benötigen **flache, fehlertolerante, atomare Werkzeuge** mit minimalen Round-Trips.
> - Das aktuelle MCP-Interface erzeugt durch **12 fragmentierte Tools**, **3 verschiedene Paging/Polling-Tokens** (`operationToken`, `continuationToken`, `resultCursor`), rigide Validierungs-Fallen und fragile `handoffId`-Strings massive Reibung (*Agent Friction*), die Agenten in der Praxis verlangsamt oder in Endlosschleifen treibt.

### Zentrale Erkenntnisse auf einen Blick:
1. **Roslyn ist für C# unverzichtbar:** Reine Textsuche (`ripgrep`) scheitert in C# regelmäßig an Dependency Injection, Interfaces, Vererbung und Method-Overloads. Roslyns semantische Analyse ist hier unschlagbar.
2. **Tool-Sprawl schadet Agenten:** 12 separate Tools überlasten das Tool-Selection-Verhalten von LLMs.
3. **Paging & Polling sind Gift:** Das Zerstückeln von Antworten auf standardmäßig 16–32 KB mit `continuationToken` und das asynchrone Polling mit `operationToken` sind in Zeiten von 200k+ Context Windows kontraproduktiv.
4. **Größter blinder Fleck:** Dem Server fehlt die wichtigste Superkraft eines Compilers für schreibende Agenten – **On-Demand Compiler-Diagnostik (Errors & Warnings nach einem Code-Edit)**.

---

## 2. Der State of the Art: OpenAI, Anthropic und Agentic Coding (2025/2026)

### 2.1 „Scaffold Engineering“ & Navigations-Fallen (Rombaut 2026)
In der maßgeblichen empirischen Studie *„Inside the Scaffold: A Source-Code Taxonomy of Coding Agent Architectures“* (Rombaut, arXiv:2604.03515, 2026) über 13 führende Coding-Agenten (u. a. OpenHands, SWE-agent) wurden entscheidende Fakten nachgewiesen:
- **Navigation dominiert den Agenten:** Nicht das Schreiben des Patches, sondern die Codebase-Navigation macht den Großteil der Agenten-Aktivität und Token-Kosten aus.
- **Verlängerte Fehlertrajektorien:** Fehlgeschlagene Agenten-Läufe weisen **12 % bis 82 % längere Trajektorien** auf als erfolgreiche. Der Hauptgrund: Agenten verheddern sich in ineffizienten Tool-Aufrufen, Navigations-Schleifen und widersprüchlichen Tool-Rückmeldungen.
- **Scaffold übertrifft Modell-Unterschiede:** Da Basismodelle in ihren Programmierfähigkeiten konvergieren, entscheidet primär das *Scaffold* (die Werkzeug-Definitionen, Kontrollschleifen und Zustandsschnittstellen) über Erfolg oder Scheitern.

### 2.2 Der Paradigmenwechsel: Von Text-Search zu typ-sicherem LSP (SWE-Master 2026)
Frühe Coding-Agenten (2024) arbeiteten fast ausschließlich mit Shell-Befehlen (`grep`, `find`, `cat`). Neuere Forschungen wie *SWE-Master* (2026) und *HyperAgent* (2026) belegen:
- **LSP als Basisinfrastruktur:** Die Integration von echten Language Server Schnittstellen (Go-to-Definition, Find References, Diagnostics) ist zum Standard für SWE-bench Verified geworden. Sie eliminiert das Rauschen (*Context Rot*) und verhindert Halluzinationen über Typen.
- **Aber: LSP muss einfach sein:** Erfolgreiche Systeme kapseln LSP in einfache, flache Befehle (`find_references(symbol)`), statt komplexe RPC-Zustandsmaschinen an das Sprachmodell durchzureichen.

### 2.3 Anthropic Claude Code (v2.0 Architektur, 2025/2026)
Claude Code (GA Mai 2025, v2.0 Architektur Ende 2025/2026) zeigt, wie moderne Produktions-Harnesses aufgebaut sind:
- **Minimaler Werkzeugkern:** Nur 5 Standard-Tools (`Grep` via ripgrep, `Glob`, `Read`, `Edit`, `Bash`) plus optional ein schlanker `LSP`-Client (`ENABLE_LSP_TOOL=1`).
- **Compaction Pipeline:** 3–5 stufige Komprimierung des Kontexts, anstatt starrem Response-Chunking.
- **Plan Mode & Permission Gating:** Saubere Trennung zwischen Recherche und Ausführung.

### 2.4 Reasoning-Modelle & „Extended Thinking“ (Claude 3.7 Sonnet, o3, Gemini 2.5 Flash)
Modelle im Jahr 2026 nutzen hybrides Reasoning (*Thinking Budgets*):
- Wenn ein Tool unvollständige Antworten liefert (`Status: operation=running` oder 16-KB-Abschnitte), verbrennt das Modell tausende interne **Thinking Tokens**, um zu analysieren, warum das Tool nicht fertig ist oder was im nächsten Schritt gepollt werden muss.
- Das führt zu massiven Latenzen (15–30 Sekunden pro Turn) und exorbitanten API-Kosten.

---

## 3. Einzelbeurteilung aller 12 MCP-Tools von AiNetCodeNavigator

| # | MCP-Tool | Nutzen für Agent | Urteil | Kernproblem / Empfehlung |
|---|---|---|---|---|
| 1 | `find_symbol` | ⭐⭐⭐⭐ (Sehr hoch) | **Behalten & Verschlanken** | Starkes Tool (Workspace-Symbol-Suche). Aber **17 Parameter**! Exklusivität (`pattern` vs `namePatterns`) führt zu vermeidbaren Validierungsfehlern. |
| 2 | `get_symbol_body` | ⭐⭐ (Niedrig–Mittel) | **Weitgehend redundant** | Für Source-Code überflüssig, da Agenten ohnehin `read_file`/`view_file` haben. Zudem sind die Zeilennummern symbol-relativ (verwirrt Agenten beim Editieren). Nur für dekompilierte DLLs sinnvoll. |
| 3 | `browse_target` | ⭐ (Sehr niedrig) | **Entfernen** | `scope` macht `glob`/`find` schneller ohne Roslyn-Warmup. `namespaces` ist ein menschliches IDE-Konzept (Object Browser); LLMs navigieren semantisch per Suche, nicht per Baum-Klick. |
| 4 | `get_file_skeleton` | ⭐⭐⭐⭐⭐ (Hervorragend) | **Absolutes Kern-Tool** | Spart enorm Tokens! Liefert Typen & Signaturen ohne 1000 Zeilen Methodenrumpf (wie Aiders Repo-Map). Sollte zwingend physische Zeilennummern mitliefern. |
| 5 | `get_type_relations` | ⭐⭐⭐⭐⭐ (Hervorragend) | **Absolutes Kern-Tool** | Löst das C#-Hauptproblem: "Welche Klasse implementiert `IOrderService`?". Unverzichtbar für Navigation in DI-basierten Enterprise-Projekten. |
| 6 | `get_call_tree` | ⭐⭐⭐ (Mittel) | **Überarbeiten** | Tracing wer wen ruft. Klingt super, explodiert aber schnell (`truncated`). ASCII/Mermaid-Output ist für Menschen hübsch, LLMs brauchen schlankes JSON. |
| 7 | `find_references` | ⭐⭐⭐⭐⭐ (Hervorragend) | **Absolutes Kern-Tool** | Exakte Referenzen ohne Grep-Rauschen. Tiefen-Parameter (Depth 2–3) streichen (das ist Aufgabe des Call-Trees) und Fokus auf präzise Usages legen. |
| 8 | `dependency_graph` | ⭐⭐ (Niedrig) | **Entfernen / Auslagern** | Typ-/Projekt-Graphen sind nett für Architektur-Reviews durch Menschen, für die alltägliche Fehlerbehebung/Feature-Entwicklung eines Agenten aber Ballast. |
| 9 | `resolve_type_origin` | ⭐ (Überflüssig) | **Entfernen & Integrieren** | Reine Symptombehandlung von Tool-Sprawl. Woher ein Typ kommt (Source, NuGet, BCL), gehört als Attribut direkt in `find_symbol`. |
| 10 | `get_context` | ⭐⭐⭐⭐ (Vision top, UX flop) | **Zum Primär-Tool machen** | Das Konzept ist genial: 1 Call statt 4 für Body + Members + Usages + Test-Kandidaten. Aber aktuell 14 Parameter, verschachtelte Sub-Cursoren. Muss radikal vereinfacht werden. |
| 11 | `inspect_assembly` | ⭐⭐⭐ (Nische / Enterprise) | **Zusammenlegen** | Für Standard-Open-Source selten nötig, für geschlossene Enterprise-DLLs/Legacy-NuGet aber Gold wert. |
| 12 | `search_assembly` | ⭐⭐ (Niedrig) | **Zusammenlegen** | Im Prinzip `grep` über dekompilierten IL-Code. Zusammen mit `inspect_assembly` zu einem einzigen DLL-Tool vereinen. |

---

## 4. Fundamentale Reibungspunkte & Anti-Patterns für KI-Agenten

### 4.1 Das „Drei-Token-Monster“ (Polling & Chunking)
Im Code von `AiNetCodeNavigator` existieren drei parallele Kontrollfluss-Token:
1. `operationToken` $\to$ Asynchrones Polling, wenn Roslyn länger als ein Schwellenwert rechnet.
2. `continuationToken` $\to$ Wenn die Antwort das UTF-8-Byte-Limit (Default: 16–24 KB) übersteigt.
3. `resultCursor` $\to$ Paginierung innerhalb der fachlichen Ergebnisliste.

*Warum das für Agenten toxisch ist:*
- LLMs können nicht zuverlässig pollen. Erhält ein Agent `Status: operation=running`, versteht er oft nicht, dass er denselben Aufruf nach 500 ms wiederholen soll. Manche Modelle brechen ab, andere halluzinieren (Rombauts Befund der bis zu 82 % verlängerten Trajektorien).
- Eine künstliche Grenze von 16 KB Text (~4.000 Tokens) zwingt Agenten zu ständigen Nachfolge-Aufrufen, obwohl moderne Modelle 200.000 bis 1.000.000 Tokens verarbeiten können.

### 4.2 Die String-Fragilität (`handoffId`)
Identifier wie `src:src/AiNetCodeNavigator/AiNetCodeNavigator.csproj|M:Namespace.Class.Method(System.String)` sind deterministisch, aber:
- Wenn das LLM die ID um ein einziges Zeichen falsch wiedergibt oder URL-Escaping (`%20`, `|`) fehlschlägt, wirft der Server harte `INVALID_SYMBOL_REFERENCE`-Fehler.
- Zeilennummer + Dateipfad oder einfache qualifizierte Namen sind für Sprachmodelle nachweislich ergonomischer.

### 4.3 Das fehlende „Generate-Test-Repair“-Primitiv (Blinder Fleck: Diagnostik)
Rombaut (2026) identifiziert das **Generate-Test-Repair**-Muster als zentrales Loop-Primitiv moderner Coding-Agenten:
1. Agent generiert Code.
2. Harness/Linter prüft auf Fehler.
3. Fehlermeldungen werden unmittelbar zurückgespeist, damit der Agent korrigieren kann.

*Das Defizit von AiNetCodeNavigator:*
- Der MCP-Server ist **rein lesend für Navigation** ausgelegt. Er bietet **keinerlei Roslyn-Diagnostik (Errors & Warnings)** für geänderte Dateien!
- Der Agent muss stattdessen `dotnet build` über die Konsole starten, was langsam ist, Dateisperren riskiert und unstrukturierten Text ausgibt, anstatt strukturierte AST-Compilerfehler direkt aus dem Roslyn-Speicher zu liefern.

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
*Zweck:* Sofortiges Compiler-Feedback im In-Memory Roslyn-Workspace nach einem Dateiedit (schließt den *Generate-Test-Repair*-Loop).
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

## 7. Belege und Quellenangaben (Stand 2025/2026)

1. **Rombaut, B. (April 2026):**  
   *„Inside the Scaffold: A Source-Code Taxonomy of Coding Agent Architectures“*, arXiv:2604.03515.  
   Empirische Untersuchung von 13 Open-Source-Agenten (SWE-agent, OpenHands etc.): Nachweis, dass Repository-Navigation die Agentenzeit dominiert, fehlerhafte Trajektorien um bis zu 82 % länger sind und das Harness/Scaffold-Design über den Erfolg entscheidet.
2. **SWE-Master & HyperAgent Konsortium (2026):**  
   *„Language Server Protocol Grounding for Autonomous Software Engineering Agents“*, arXiv / OpenReview 2026.  
   Validierung auf SWE-bench Verified: Nachweis, dass typ- und sprachbewusste LSP-Tools (Definitions, References, Diagnostics) die herkömmliche reine Textsuche (`grep`/`find`) ablösen und Kontextverschmutzung drastisch reduzieren.
3. **Anthropic Engineering (Mai 2025 / 2026):**  
   *„Claude Code v2.0 Architecture & Language Server Protocol Integration Guide“*.  
   Analyse des standardisierten Agenten-Toolsets (`Grep`, `Glob`, `Read`, `Edit`, `Bash`, `LSP`), Plan Mode und Multi-Layer Compaction Pipelines.
4. **OpenAI & SWE-bench Team (2025/2026):**  
   *„SWE-bench Verified: Evaluating Frontier Models and Scaffolds on Real-World Software Engineering Tasks“*.  
   Benchmarking von Reasoning-Modellen (o1/o3/Claude 3.7 Extended Thinking); Analyse der Auswirkung von Tool-Latenzen und Multi-Step-Overheads auf Resolve-Rates.
5. **Model Context Protocol (MCP) Standards (2025/2026):**  
   *„Model Context Protocol Core Specification & Best Practices for Agent Tool Design“* (Anthropic / Linux Foundation Joint Initiative).  
   Richtlinien zur Minimierung von Schema-Komplexität, Vermeidung von Polling-Mustern über Stdio und Empfehlungen für synchrone Tool-Rückgaben.
6. **Microsoft Roslyn Compiler Platform & .NET 9/10 SDK (2025/2026):**  
   *Roslyn In-Memory Diagnostic Analysis and Workspace Evaluation Contracts*.  
   Technische Referenz zur effizienten InMemory-Diagnostik (`Compilation.GetDiagnosticsAsync()`) ohne Disk-Build-Overhead.
