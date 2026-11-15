# Dokumentation

| Dokument | Inhalt | Für wen |
|---|---|---|
| [Funktionsweise](funktionsweise.md) | Ablauf beim Start, Konten, `apps.json`, Einrichtung, Meldungen, Ereignisprotokoll, Prüfung | alle |
| [Sicherheitsmodell](sicherheitsmodell.md) | Schutzziele, Angriffe und Gegenmassnahmen, Restrisiken | Sicherheit und Review |
| [Entwicklung](entwicklung.md) | Voraussetzungen, Build, Tests, Release, Projektstruktur | Entwicklung und Mitwirkende |
| Benutzerhandbuch (`manual/`) | Installation und Service Schritt für Schritt, mit Bildschirmfotos der fertigen Software | Installation und Service – **in Arbeit** |

Weitere Quellen:

- [UI-Entwürfe](ui-designs/) – verbindliche Vorlage für die Oberflächen. Gerenderte Ansichten liegen in [`images/`](images/).
- [Ursprüngliches Konzept](konzept/konzept.md) – Stand zu Projektbeginn. Wo es von den Specs abweicht, gelten die Specs.
- [OpenSpec-Change](../openspec/changes/add-appstart-service/) – Anforderungen, Entscheide, Aufgaben.

## Bilder aktualisieren

Die Bilder in `images/` sind aus den HTML-Entwürfen in `ui-designs/` gerendert. Sobald die Oberflächen fertig sind, werden sie durch Bildschirmfotos der echten Software von der Test-VM ersetzt. Dateinamen bleiben gleich, damit die Verweise stimmen.
