# Konzeptprüfung und Freigabe

Du führst **Schritt 2 von 4** aus. Keine Roadmap, keine Umsetzung; keinen anderen Workflow-Schritt starten.

## Start

Aufruf: `Führe 02-konzept-pruefung-und-freigabe.md aus. Task: tasks/<name>`

Ohne Taskverzeichnis: danach fragen. Lies die [README.md](README.md) dieses Ordners, `AGENTS.md` und relevante Regeln des Repos, dann das Konzept des Tasks. Fehlt das Konzept: danach fragen, keines anlegen. Bearbeite ausschließlich das Konzept im Taskverzeichnis.

Der Aufruf autorisiert ausdrücklich `status: ready`, sobald die folgenden Freigabekriterien erfüllt sind; keine zusätzliche Bestätigung. Bis dahin `status: draft`, auch bei erneuter Prüfung eines bereits freigegebenen Konzepts.

## Prüfung

1. Starte einen Subagenten, der nur liest, mit frischem Kontext: Konzept, relevante Projektquellen, Repository-Regeln und der folgende Prüfauftrag. Keine Planungsdiskussion oder Rechtfertigungen des Autors mitgeben. Modell/Denkaufwand: ausdrückliche Nutzerwahl, dann Repository-Vorgaben, sonst `gpt-6-luna` / `high`. Warte auf seine Findings.
2. Bewerte jedes Finding anhand seiner Belege. Verwirf unbelegte Findings und ungefragte Features. Arbeite belegte Korrekturen ein, die vereinbarte Intention, Scope und fachliches Verhalten erhalten. Ändert eine Korrektur diese oder verlangt sie eine offene Abwägung, frage den Nutzer vor dem Einarbeiten. Halte jede Entscheidung im Konzept fest, bevor du fortfährst.
3. Verändern Korrekturen wesentliche Zusammenhänge oder bleiben ihre Folgen unklar, starte einen frischen Subagenten auf dem überarbeiteten Konzept mit demselben Auftrag. Insgesamt höchstens **zwei Audit-Aufrufe**. Findings wie oben bewerten und bearbeiten; keine weitere Prüfschleife.
4. Wende die Freigabekriterien an. Nenne Korrekturen, verworfene Findings mit kurzer Begründung und den resultierenden Status. Stoppen.

## Auftrag an den Subagenten

- Lies das Konzept als alleinige Spezifikation; prüfe Aussagen zum Ist-Stand gegen relevante Projektquellen.
- Prüfe Widersprüche, unklare Begriffe/Verträge, fehlende Umsetzungsentscheidungen, Konflikte zwischen Scope und Nicht-Zielen sowie die Prüfbarkeit des beabsichtigten Ergebnisses anhand der Akzeptanzkriterien.
- Melde nur handlungsrelevante Findings: **Fundstelle, konkretes Problem und Auswirkung, Korrekturvorschlag**. Unterscheide belegte Mängel von notwendigen Nutzerentscheidungen. Keine Anforderungen erfinden oder Scope erweitern. Keine Findings ist ein gültiges Ergebnis.
- Nur lesen; Findings an den Hauptagenten zurückgeben. Keine Dateiänderungen oder Umsetzung.

## Freigabekriterien

`status: ready` erst nach Prüfung und Korrekturen: kein offenes blockierendes Finding, keine offene Nutzerentscheidung oder Unklarheit, die die Umsetzung erraten müsste; Intention, Scope, Nicht-Ziele und Verifikation reichen für diesen Task aus. Ein Audit ohne Findings allein genügt nicht.

Vor Freigabe erledigte offene Punkte und Draft-Arbeitsgedächtnis entfernen. Andernfalls `draft` beibehalten und verbleibende Blocker nennen, auch nach dem zweiten Audit. Das Audit-Limit erzwingt keine Freigabe. Keine separate Audit-Berichtsdatei.
