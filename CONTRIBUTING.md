# Beitragen

Danke für dein Interesse! Dieses Projekt hat einen einzigen Maintainer (@trosinde).
Beiträge sind per Pull Request willkommen. Pushen auf `main` und Mergen kann nur der Maintainer.

## So geht's

1. Repository forken und einen Branch von `main` anlegen.
2. Änderung umsetzen, Tests dafür ergänzen oder anpassen.
3. Tests lokal ausführen (Windows, .NET SDK laut `global.json`):
   `dotnet test --solution AppStart.slnx -c Release`
4. Pull Request gegen `main` öffnen.

## Wie es weitergeht

- Die CI läuft automatisch für deinen Pull Request. Bei ganz neuen GitHub-Konten muss der
  Maintainer den ersten Lauf freigeben.
- Der Pull Request wird **erst geprüft, wenn alle Pflicht-Checks grün sind**. Schlägt ein Check
  fehl, behebe ihn mit einem weiteren Push auf denselben Branch.
- Ist `main` weitergelaufen, muss dein Branch vor dem Merge aktualisiert werden („Update branch“
  oder Rebase). Lass „Allow edits by maintainers“ aktiviert, dann kann der Maintainer das übernehmen.
- Ob und wann gemergt wird, entscheidet der Maintainer. Pull Requests werden per Squash gemergt.

## Sicherheitslücken melden

Bitte keine öffentlichen Issues für Schwachstellen. Siehe [SECURITY.md](SECURITY.md).
