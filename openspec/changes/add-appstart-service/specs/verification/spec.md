## ADDED Requirements

### Requirement: Prüfbefehl
verify SHALL alle Prüfpunkte der Soll-Einrichtung prüfen, einen Prüfbericht erzeugen und bei Abweichung einen Exit-Code ungleich 0 liefern.

#### Scenario: Korrekte Einrichtung
- **WHEN** verify auf einer korrekt eingerichteten Maschine läuft
- **THEN** ist der Exit-Code 0 und der Bericht zeigt alle Punkte als erfüllt

#### Scenario: Abweichung nach Service
- **WHEN** ein Bediener wieder Admin ist oder der Registry-Key wieder lesbar ist
- **THEN** meldet verify die Abweichung mit Exit-Code ungleich 0

### Requirement: Start der Prüfung
Die Prüfung SHALL über einen Startmenü-Eintrag, einen Knopf im Setup und die Kommandozeile startbar sein, Admin-Rechte verlangen und jeden Bericht mit Datum und Uhrzeit ablegen.

#### Scenario: Servicetechniker nach dem Einsatz
- **WHEN** der Servicetechniker «AppStart – Einrichtung prüfen» im Startmenü wählt und die UAC-Abfrage bestätigt
- **THEN** zeigt die Prüfansicht das Ergebnis je Prüfpunkt und der Bericht liegt in C:\ProgramData\AppStart\Reports

#### Scenario: Bediener startet die Prüfung
- **WHEN** ein Bediener ohne Admin-Rechte die Prüfung starten will
- **THEN** läuft sie nur nach Eingabe von Admin-Zugangsdaten
