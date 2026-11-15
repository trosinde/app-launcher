## ADDED Requirements

### Requirement: Vollständiges Benutzerhandbuch
Das Release SHALL ein Benutzerhandbuch für Installateur und Servicetechniker enthalten, mit Installation, Verwaltung der Applikationen und Konten, Prüfung nach Serviceeinsatz, Meldungen und Fehlerbehebung, Update, Deinstallation und Kommandozeilen-Referenz.

#### Scenario: Handbuch im Paket
- **WHEN** AppStart installiert ist
- **THEN** ist das Handbuch als PDF im Startmenü unter AppStart – Handbuch erreichbar

#### Scenario: Vollständigkeit
- **WHEN** die Pipeline baut
- **THEN** prüft ein Test, dass jede Meldung, jeder Prüfpunkt und jeder Befehl der Software im Handbuch beschrieben ist

#### Scenario: Abweichung im Prüfbericht
- **WHEN** der Prüfbericht eine Abweichung zeigt
- **THEN** findet der Servicetechniker im Handbuch Ursache und Behebung zu genau diesem Prüfpunkt
