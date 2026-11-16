# CLAUDE.md
- Arbeite spec-driven nach openspec/project.md und dem aktiven Change.
- Entwicklung läuft unter Windows. Setup und Dienst nur auf der Test-VM installieren. Dieser Rechner ist die Test-VM (Proxmox, Windows 11 Pro): Installation über die E2E-/UAT-Skripte (tests/e2e/Invoke-Uat.ps1, geplante Aufgabe «AppStartUAT»), die alles wieder entfernen.
- Setze tasks.md der Reihe nach um, hake erledigte Tasks ab.
- Kein Code ohne Anforderung in den Specs. Fehlt etwas, Spec zuerst ergänzen und validieren.
- Spike-Ergebnisse (Abschnitt 1) im Change dokumentieren, bevor Abschnitt 3 beginnt.
- Sicherheitsregeln aus project.md sind nicht verhandelbar.
- Nach Abschluss: alle Tests grün, dann openspec archive add-appstart-service.
