## ADDED Requirements

### Requirement: Testapplikation wie ein betroffener Fat Client
AppStart.TestApp SHALL Admin-Rechte verlangen, Werte inkl. Test-Passwort in HKLM\SOFTWARE\AppStartTest speichern, Dateien in C:\ProgramData\AppStartTest lesen und schreiben und ihren Zustand anzeigen.

#### Scenario: Start ohne Admin-Rechte
- **WHEN** ein Standard-User die TestApp-EXE direkt startet
- **THEN** bricht sie mit einer Meldung ab

#### Scenario: Start über den Launcher
- **WHEN** ein Mitglied von AppStart Users die TestApp über ihre Verknüpfung startet
- **THEN** zeigt sie Konto appstart-app, Admin ja und Zugriff auf Registry und Dateien ok

#### Scenario: Daten für Bediener gesperrt
- **WHEN** der Bediener Registry-Key oder Datenordner der TestApp selbst öffnet
- **THEN** wird der Zugriff verweigert

#### Scenario: Ausbruchsversuch
- **WHEN** in der TestApp «Eingabeaufforderung starten» gewählt wird
- **THEN** scheitert der Start von cmd.exe
