#!/usr/bin/env python3
"""
Find German texts across the AiNetCodeNavigator repository.
Outputs results in the format: file:line: text
"""

import os
import sys
import re
from pathlib import Path

# Ensure UTF-8 output on all platforms
if sys.stdout.encoding != "utf-8":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass

# Directories excluded from translation scope
EXCLUDED_DIRS = {
    ".git",
    ".agents",
    "tasks",
    "temp",
    "audit-reporting",
    "TestResults",
    "bin",
    "obj",
    ".vs",
    ".idea",
    "artifacts",
    "node_modules",
}

# File extensions to exclude (binary, generated, logs)
EXCLUDED_EXTS = {
    ".dll",
    ".exe",
    ".pdb",
    ".png",
    ".jpg",
    ".jpeg",
    ".gif",
    ".ico",
    ".svg",
    ".zip",
    ".tar",
    ".gz",
    ".nupkg",
    ".snupkg",
    ".binlog",
    ".log",
    ".gitkeep",
}

# Pattern for German umlauts and eszett
UMLAUT_PATTERN = re.compile(r"[äöüÄÖÜß]")

# Distinct German words that do not overlap with standard English words or C# code keywords
# Grouped by category for clarity and maintainability
DISTINCT_GERMAN_WORDS = [
    # --- Verbs & Participles ---
    r"nicht", r"wird", r"werden", r"wurde", r"wurden", r"worden",
    r"kann", r"kannst", r"können", r"koennen", r"könnt", r"koennt", r"konnte", r"konnten",
    r"muss", r"musst", r"müssen", r"muessen", r"müsst", r"muesst", r"musste", r"mussten",
    r"soll", r"sollst", r"sollen", r"sollt", r"sollte", r"sollten",
    r"darf", r"darfst", r"dürfen", r"duerfen", r"dürft", r"durfte", r"durften",
    r"haben", r"hatte", r"hatten", r"gehabt",
    r"bleibt", r"bleiben", r"gehört", r"gehoert", r"gehören", r"gehoeren",
    r"liegt", r"liegen", r"steht", r"stehen",
    r"ermittelt?", r"ermitteln", r"ermittlung",
    r"erstellt?", r"erstellen", r"erstellung",
    r"prüft?", r"prueft?", r"prüfen", r"pruefen", r"prüfung(?:en)?", r"pruefung(?:en)?", r"geprüft", r"geprueft",
    r"geladen", r"lädt", r"laedt",
    r"ausführen", r"ausfuehren", r"ausgeführt", r"ausgefuehrt", r"ausführung(?:en)?", r"ausfuehrung(?:en)?",
    r"erzeugt", r"erzeugen", r"erzeugung",
    r"enthält", r"enthaelt", r"enthalten",
    r"besteht", r"bestehen", r"bestehend[en]?",
    r"übertragen", r"uebertragen", r"übertrag(?:en)?", r"uebertrag(?:en)?",
    r"übernehmen", r"uebernehmen", r"übernahme", r"uebernahme",
    r"übersetzen", r"uebersetzen", r"übersetzung(?:en)?", r"uebersetzung(?:en)?",
    r"umstellen", r"umstellung",
    r"anpassen", r"anpassung(?:en)?",
    r"schreibt", r"schreiben", r"geschrieben",
    r"einsehen", r"gelesen",
    r"gezeigt", r"zeigen",
    r"abgeschnitten",
    r"erfordert?", r"erforderlich(?:e[rn]?)?",
    r"fehlt", r"fehlen", r"fehlend(?:e[rn]?)?",
    r"erreicht", r"erreichen",
    r"extrahiert?", r"extrahieren", r"extraktion",
    r"rendert", r"rendern",
    r"bereinigt?", r"bereinigen", r"bereinigung",
    r"konsolidierung",
    r"meintest", r"eventuell",
    r"wiederverwenden", r"wiederverwendet",
    r"abrufen", r"abgerufen",
    r"speichert",
    r"implementiert(?:e[rnms]?)?", r"implementierend(?:e[rnms]?)?",
    
    # --- Pronouns & Articles (distinctly German) ---
    r"dieser?", r"dieses", r"diesem", r"diesen", r"diese",
    r"welche[rsmn]?", r"jede[rsmn]?",
    r"alle[smnr]?", r"einem", r"einen", r"einer", r"eines",
    r"keine[rsmn]?", r"ihrem?", r"ihren", r"ihrer", r"unser[e]?", r"unsere[rmn]?",
    r"dessen", r"deren", r"des", r"dem",
    
    # --- Conjunctions & Prepositions (distinctly German) ---
    r"oder", r"aber", r"sondern", r"sowie", r"sowohl", r"weder",
    r"fuer", r"für", r"ueber", r"über", r"unter", r"zwischen",
    r"beim?", r"ohne", r"durch", r"gegenüber", r"gegenueber",
    r"damit", r"obwohl", r"während", r"waehrend",
    r"jedoch", r"deshalb", r"daher", r"somit", r"anstatt", r"sofern", r"insofern",
    
    # --- Adverbs / Particles ---
    r"auch", r"noch", r"schon", r"bereits", r"immer", r"wieder",
    r"hierbei", r"hierfür", r"hierfuer", r"dadurch", r"dazu",
    r"davon", r"darin", r"daraus", r"danach", r"dabei",
    r"bisher", r"zuerst", r"später", r"spaeter", r"sofort",
    r"beispielsweise", r"insbesondere", r"vollständig", r"vollstaendig",
    r"tatsächlich", r"tatsaechlich", r"möglicherweise", r"moeglicherweise",
    r"mindestens", r"höchstens", r"hoechstens", r"allenfalls",
    r"aktuell(?:e[rnms]?)?",
    
    # --- Nouns & Domain Terms ---
    r"fehler", r"fehlermeldung(?:en)?", r"fehlercodes?",
    r"hinweis(?:e)?", r"bezeichner", r"ergebnis(?:se)?",
    r"zeile(?:n)?", r"startzeile", r"endzeile", r"zeilenzahl",
    r"datei(?:en)?", r"dateipfad(?:e)?", r"ordner",
    r"verzeichnis(?:se)?", r"unterverzeichnis(?:se)?", r"testverzeichnis(?:se)?",
    r"ausgabe(?:n)?", r"eingabe(?:n)?", r"rueckgabe", r"rückgabe",
    r"nachricht(?:en)?", r"antwort(?:en)?",
    r"aufruf(?:e)?", r"aufrufer", r"aufrufgraph(?:en)?",
    r"methode(?:n)?", r"methodenrümpfe", r"methodenruempfe",
    r"eigenschaft(?:en)?", r"schnittstelle(?:n)?", r"klasse(?:n)?",
    r"basisklasse(?:n)?",
    r"implementierung(?:en)?",
    r"einheitlich(?:e[rnms]?)?",
    r"vertrags?", r"verträge", r"vertraege",
    r"lesbar(?:e[rnms]?)?", r"leseberechtigung",
    r"endung(?:en)?",
    r"vollständigkeit", r"vollstaendigkeit",
    r"beschreibung(?:en)?", r"baustein(?:e)?",
    r"konzept(?:e)?", r"aufgabe(?:n)?", r"schritt(?:e)?",
    r"änderung(?:en)?", r"aenderung(?:en)?",
    r"überblick", r"ueberblick", r"übersicht", r"uebersicht",
    r"abgeschlossen", r"abgebrochen", r"fehlgeschlagen", r"erfolgreich",
    r"bestanden", r"gefunden(?:e[rn]?)?", r"ungültig(?:e[rn]?)?", r"ungueltig(?:e[rn]?)?",
    r"gültig(?:e[rn]?)?", r"gueltig(?:e[rn]?)?",
    r"starte", r"beendet",
    r"vorhanden(?:e[rn]?)?", r"gesperrt", r"beschädigt", r"beschaedigt",
    r"unbekannt(?:e[rn]?)?", r"unverändert", r"unveraendert",
    r"zähler", r"zaehler", r"projektmappe(?:n)?", r"projekte",
    r"lösung(?:en)?", r"loesung(?:en)?",
    r"spalte(?:n)?", r"knoten", r"kante(?:n)?",
    r"pflicht", r"grenze(?:n)?", r"ursprung", r"sitzung",
    r"weiter(?:e|en|er|es)?",
    # Short labels and comments missed by the original vocabulary.
    r"typ(?:en)?", r"filtert", r"signatur(?:en)?", r"sichtbarkeit",
    r"quelle", r"pfade", r"diagnosen", r"referenzen", r"dekompilat",
    r"gesamt", r"gezeigt", r"gekürzt", r"lokal", r"generiert",
    r"sessiongebunden", r"erhöhen", r"verfeinern", r"angefordert",
    r"quell-syntax",
]

GERMAN_WORD_PATTERN = re.compile(
    r"\b(" + "|".join(DISTINCT_GERMAN_WORDS) + r")\b",
    re.IGNORECASE
)

# Compound or phrase patterns characteristic of German
PHRASE_PATTERNS = [
    # e.g., "1 von 2", "ShownCount von TotalTypes"
    re.compile(r"\b\w+\s+von\s+\w+\b", re.IGNORECASE),
    # e.g., "Top 10 Namespaces und"
    re.compile(r"\b(?:und|oder)\s+\d+\s+weitere\b", re.IGNORECASE),
    # e.g., "Du führst", "Du denkst"
    re.compile(r"\bDu\s+(?:führst|denkst|bist|startest|implementierst)\b", re.IGNORECASE),
    # e.g., "zur Bereinigung", "zur Ermittlung", "zur Erstellung"
    re.compile(r"\bzur\s+[A-ZÄÖÜ]\w+ung\b"),
    # Interpolated counts, e.g. "{p.ShownMemberCount} von {p.TotalMemberCount}".
    re.compile(r"\}\s+von\s+\{"),
]

# Preserve deliberate Unicode fixtures. Mask only the exact fixture text in its
# owning file, so German prose elsewhere on the same line is still detected.
LEGITIMATE_TEXT_EXCEPTIONS = {
    "tests/AiNetCodeNavigator.FastTests/Mcp/LongRunningToolCallStoreTests.cs": ("Grüße 🌍",),
    "tests/AiNetCodeNavigator.FastTests/Mcp/McpFormattingTests.cs": ("Grüße 🌍",),
    "tests/AiNetCodeNavigator.FastTests/Mcp/McpToolResultsTests.cs": ("Grüße 🌍",),
    "tests/AiNetCodeNavigator.FastTests/Symbols/HandoffCounterAlphabetTests.cs": ('[InlineData("äöü")]',),
    # These negative assertions explicitly guard against German output labels.
    "tests/AiNetCodeNavigator.FastTests/FileStructure/IndexScopeScannerTests.cs": ('Assert.DoesNotContain("Projekte:",',),
    "tests/AiNetCodeNavigator.FastTests/FileStructure/NamespaceTreeScannerTests.cs": (
        'Assert.DoesNotContain("Typen",', 'Assert.DoesNotContain("Gekürzt",',
    ),
    "tests/AiNetCodeNavigator.IntegrationTests/Mcp/McpServerIntegrationTests.cs": ("Untracked Git ü Impact.cs",),
    # English documentation describing the preserved UTF-8 filename fixture.
    "docs/navigation/mcp-registration-status.md": ("`ü`",),
    # Copyright holder's proper name is not translatable.
    "LICENSE": ("Hüsing",),
}

# Markdown link destinations are file/URL identifiers, not prose. Keep labels
# visible to the detector, including labels that contain German words.
MARKDOWN_LINK_DESTINATION = re.compile(r"(\[[^\]\n]*\])\([^\n)]*\)")

def is_german_line(line: str, file_path: str = "") -> tuple[bool, str]:
    """
    Checks if a line contains German text.
    Returns (is_match, reason).
    """
    stripped = line.strip()
    if not stripped:
        return False, ""

    for literal in LEGITIMATE_TEXT_EXCEPTIONS.get(file_path, ()):
        line = line.replace(literal, "")
    if Path(file_path).suffix.lower() == ".md":
        line = MARKDOWN_LINK_DESTINATION.sub(r"\1", line)
    
    # Check for German umlauts / eszett
    umlaut_matches = UMLAUT_PATTERN.findall(line)
    if umlaut_matches:
        return True, f"umlauts: {set(umlaut_matches)}"
    
    # Check for distinct German words
    word_matches = GERMAN_WORD_PATTERN.findall(line)
    if word_matches:
        return True, f"words: {set(word_matches)}"
    
    # Check for characteristic German phrases
    for pattern in PHRASE_PATTERNS:
        m = pattern.search(line)
        if m:
            return True, f"phrase: {m.group(0)}"
    
    return False, ""

def scan_repository(repo_root: Path):
    matches_by_file = {}
    total_files = 0
    total_lines = 0

    for root, dirs, files in os.walk(repo_root):
        # Exclude directories in-place so os.walk does not descend
        dirs[:] = [d for d in dirs if d not in EXCLUDED_DIRS and not d.startswith(".git")]
        
        for file_name in sorted(files):
            file_path = Path(root) / file_name
            ext = file_path.suffix.lower()
            if ext in EXCLUDED_EXTS:
                continue

            rel_path = file_path.relative_to(repo_root)
            rel_str = str(rel_path).replace("\\", "/")
            total_files += 1

            try:
                with open(file_path, "r", encoding="utf-8", errors="replace") as f:
                    file_matches = []
                    for line_no, line in enumerate(f, start=1):
                        total_lines += 1
                        is_match, reason = is_german_line(line, rel_str)
                        if is_match:
                            file_matches.append((line_no, line.rstrip("\r\n"), reason))
                    
                    if file_matches:
                        matches_by_file[rel_str] = file_matches
            except Exception as e:
                print(f"[WARN] Error reading {rel_path}: {e}", file=sys.stderr)

    return matches_by_file, total_files, total_lines

def main():
    repo_root = Path(__file__).resolve().parent.parent.parent
    output_file = Path(__file__).resolve().parent / "german_texts.txt"
    summary_file = Path(__file__).resolve().parent / "german_texts_summary.md"

    print(f"Scanning repository: {repo_root}")
    print(f"Excluding directories: {sorted(EXCLUDED_DIRS)}")
    print(f"Excluding extensions: {sorted(EXCLUDED_EXTS)}")

    matches_by_file, total_files, total_lines = scan_repository(repo_root)

    total_matching_lines = sum(len(matches) for matches in matches_by_file.values())
    print(f"\nScan completed:")
    print(f"  Files scanned: {total_files}")
    print(f"  Files with German text: {len(matches_by_file)}")
    print(f"  Total German lines found: {total_matching_lines}")

    # Write formatted results: file:line: text
    with open(output_file, "w", encoding="utf-8") as out:
        for file_path, matches in sorted(matches_by_file.items()):
            for line_no, line, reason in matches:
                out.write(f"{file_path}:{line_no}: {line}\n")

    print(f"Output dumped to: {output_file}")

    # Write markdown summary breakdown
    categories = {
        "src": [],
        "tests": [],
        "scripts": [],
        "docs": [],
        "other": [],
    }

    for file_path, matches in sorted(matches_by_file.items()):
        placed = False
        for cat in ["src", "tests", "scripts", "docs"]:
            if file_path.startswith(cat + "/") or file_path.startswith(cat + "\\"):
                categories[cat].append((file_path, matches))
                placed = True
                break
        if not placed:
            categories["other"].append((file_path, matches))

    with open(summary_file, "w", encoding="utf-8") as sm:
        sm.write("# German Text Findings Summary\n\n")
        sm.write(f"- **Scanned:** {total_files} files ({total_lines} lines)\n")
        sm.write(f"- **Files with German text:** {len(matches_by_file)}\n")
        sm.write(f"- **Total matching lines:** {total_matching_lines}\n\n")

        sm.write("## Overview by Area\n\n")
        sm.write("| Area | Files with findings | Line count |\n")
        sm.write("| :--- | :--- | :--- |\n")
        for cat, file_list in categories.items():
            if file_list:
                cat_lines = sum(len(m) for _, m in file_list)
                sm.write(f"| `{cat}/` | {len(file_list)} | {cat_lines} |\n")
        sm.write("\n---\n\n")

        for cat, file_list in categories.items():
            if not file_list:
                continue
            cat_lines = sum(len(m) for _, m in file_list)
            sm.write(f"## {cat}/ ({len(file_list)} files, {cat_lines} lines)\n\n")
            for file_path, matches in file_list:
                sm.write(f"- **`{file_path}`** ({len(matches)} findings)\n")
            sm.write("\n")

    print(f"Summary dumped to: {summary_file}")

if __name__ == "__main__":
    main()
