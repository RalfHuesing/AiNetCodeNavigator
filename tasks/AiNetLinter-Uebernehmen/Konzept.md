AiNetCodeNavigator ist die Ablösug von AiNetLinter.

Ich will alle Features von AiNetLinter die für Code-Navigation zuständig sind in diesem Projekt haben.
Inklusive voller test abdeckung.

AiNetlinter ist erprobt und getestet - wir sollen sehr viel davon übernehmen.

wir übernehmen NICHT den kompletten Linter Part.

AiNetCodeNavigator ist ausschließlich ein MCP Server für agentische navigation in c# code.

bei unklarheiten immer in AiNetLinter nachschauen und nach AiNetCodeNavigator adaptieren

bei entscheidungsfragen -> blocken und nutzer fragen!

roadmap:

grundlagen schaffen:
1. [x] .agents\rules anpassen (habe ich aus anderem repo kopiert)
2. [x] solution/projekt skelette anlegen (projekte, core, tests, usw. was wir so brauchen)
3. [ ] namespaces mit gitkeep und klassen hüllen
4. [ ] pwsh scripte für build und die tests - diese müssen dann immer aufgerufen werden (in rules oder docs erankern), output nach temp\ (sieht in docs dann das der agent dort nachlesen soll, evtl. gibt das script das auch so aus). temp\ in gitignore
5. [ ] komplette basis infrastrutur für tests und core module

