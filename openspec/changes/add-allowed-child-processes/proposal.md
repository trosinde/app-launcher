## Why
Manche Ziel-Applikationen starten eigene Hilfsprogramme (Export, Updater, Treiber-Tool, PDF-Anzeige). Heute gibt es nur zwei Zustände: `allowChildProcesses: false` blockiert jedes Hilfsprogramm, die Applikation läuft nur teilweise. `allowChildProcesses: true` lässt jedes Programm zu, auch eine Eingabeaufforderung mit Admin-Rechten. Zudem lässt sich die Einstellung nur von Hand in apps.json ändern, nicht im Assistenten und nicht in der Kommandozeile, also auch nicht in einer stillen Installation.

## What Changes
- Neuer dritter Zustand je Applikation: Nur erlaubte Hilfsprogramme (Positivliste mit Pfad und Signatur oder SHA-256)
- Der Dienst überwacht alle Prozesse der Applikation im Job-Objekt und beendet sofort jeden Prozess, der nicht auf der Liste steht
- Lernmodus: zeichnet für eine begrenzte Zeit auf, welche Programme die Applikation startet, und schlägt sie vor
- Assistent: Auswahl «Weitere Programme» je Applikation, Dialog «Erlaubte Hilfsprogramme», Lernmodus (Vorlage: docs/ui-designs/Main, Helpers, Learn, LearnResult)
- Kommandozeile: Optionen und Befehle für Modus, Positivliste und Lernmodus
- Stille Einrichtung in einem Schritt: `AppStart.Setup.exe apply --config <Datei>` und `AppStart-Setup.exe /quiet /config <Datei>`
- verify prüft die Hilfsprogramme mit (Vorlage: docs/ui-designs/Verify)
- **BREAKING** (nur Konfiguration): apps.json Version 2. `allowChildProcesses` wird durch `childProcesses` (`blocked` | `allowlist` | `all`) und `allowedChildren` ersetzt. Das Setup migriert Version 1 automatisch: `false` → `blocked`, `true` → `all`.

## Impact
- Betroffen: AppStart.Core (Konfiguration, Prüfung), AppStart.Service (Start, Überwachung, Lernmodus), AppStart.Setup (Assistent, Kommandozeile, verify), AppStart.Bootstrapper (`/config`), AppStart.TestApp, Handbuch, Sicherheitsmodell
- Neues Restrisiko: Ein nicht erlaubtes Programm läuft wenige Millisekunden, bevor der Dienst es beendet (siehe Design)
- Reihenfolge: Umsetzung parallel zu add-appstart-service möglich. Archivieren erst nach add-appstart-service, damit die geänderte Anforderung «Keine Kindprozesse» in openspec/specs steht
