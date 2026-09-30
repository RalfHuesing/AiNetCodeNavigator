# Roadmap erstellen

Du führst **Schritt 3 von 4** aus. Der Nutzer startet Schritt 4 selbst. Nicht implementieren, nicht orchestrieren.

## Start

Sinngemäß: `Führe 03-roadmap-erstellung.md aus. Task: tasks/<name>`

Ohne Taskverzeichnis: nur danach fragen. Lies [README.md](README.md) in diesem Ordner, Projektregeln, dann das Konzept im Taskverzeichnis.

Ohne `status: ready` nicht zerlegen, außer der Nutzer fordert es ausdrücklich. Taucht beim Zerlegen ein unentschiedener Fork auf, war das Konzept nicht ready: stoppen, nicht nachentscheiden.

## Zerlegen

Mach aus dem Konzept eine ausführbare Reihenfolge. Jeder umsetzbare Punkt hat `- [ ]`. Kein Optional, kein Nice-to-have, kein Empfohlen — entweder im Punkt oder nicht.

Größe nach Aufwand, nicht nach Schema:

- Klein: eine `roadmap.md`, wenige Punkte, keine Extra-Dateien.
- Groß: Milestones und Leaf-Dateien nur wo ein Punkt eine eigene Agent-Session braucht (grober Richtwert ~512k Kontext).

Keine künstlichen Unterpunkte. Ein Punkt, den ein Agent in einer Session nicht halten kann, vorher teilen.

Ein ausführbarer Punkt ist ohne Raten umsetzbar: Intention, Scope, Nicht-Ziele, Abnahme. Details einmal; woanders nur Links.

Parent-Checkboxen sind Aggregate. Ein Audit-Punkt gehört ans Ende jedes Milestones; ohne Milestone-Schnitt einmal ans Ende der Roadmap.

Nicht umsetzen. Am Ende die Struktur knapp zeigen — und **stoppen**.
