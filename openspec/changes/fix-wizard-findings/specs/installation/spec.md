## ADDED Requirements

### Requirement: Warnung bei Administratoren ohne Passwort
Der Assistent SHALL im Schritt «Konten und Gruppe» warnen und die betroffenen Konten nennen, wenn ein aktiviertes Konto in der Gruppe Administratoren kein Passwort hat. Die Einrichtung bleibt möglich.

#### Scenario: Warnung im Assistenten
- **WHEN** der Techniker den Schritt «Konten und Gruppe» öffnet und ein aktiviertes Admin-Konto kein Passwort hat
- **THEN** zeigt der Assistent eine Warnung mit dem Kontonamen und dem Hinweis, ein Passwort zu setzen

### Requirement: Erklärung von Datenordner und Registry-Key
Der Assistent SHALL im Schritt «Applikation auswählen» an den Feldern Datenordner und Registry-Key erklären, dass das Ziel auf appstart-app, SYSTEM und Administratoren gesperrt wird, Bediener danach keinen Zugriff haben, die ursprünglichen Rechte bei der Deinstallation zurückkommen und ein leeres Feld nichts sperrt.

#### Scenario: Erklärung sichtbar
- **WHEN** der Techniker eine Applikation auswählt
- **THEN** stehen neben Datenordner und Registry-Key die Erklärung und ein Beispiel (C:\ProgramData\<Hersteller>\<App>, HKLM\SOFTWARE\<Hersteller>\<App>)

### Requirement: Warnung bei Datenordner im Programmordner
Der Assistent SHALL warnen, wenn der Datenordner der Ordner der EXE ist, darin liegt oder ihn enthält. Die Warnung SHALL nennen, dass appstart-app dann EXE und DLLs ändern könnte, ausgetauschte DLLs von der Hash- und Signaturprüfung nicht erkannt werden und ein Ordner unter C:\ProgramData empfohlen ist. Weiter geht es erst nach ausdrücklicher Bestätigung. apply und die Antwortdatei geben dieselbe Warnung aus.

#### Scenario: Datenordner gleich Programmordner
- **WHEN** der Techniker als Datenordner den Ordner der gewählten EXE einträgt
- **THEN** zeigt der Assistent die Warnung und «Weiter» ist erst nach Bestätigung möglich

#### Scenario: Datenordner ausserhalb
- **WHEN** der Datenordner weder den Ordner der EXE enthält noch darin liegt
- **THEN** erscheint keine Warnung

### Requirement: Bediener entfernen
Der Assistent SHALL jedes Mitglied der Gruppe AppStart Users ausser appstart-user entfernen können, auch Mitglieder aus früheren Läufen und von Hand eingetragene. Beim Übernehmen SHALL das Setup alle Mitglieder aus der Gruppe nehmen, die nicht mehr in der Liste stehen.

#### Scenario: Früher hinzugefügten Bediener entfernen
- **WHEN** der Techniker den Assistenten erneut öffnet, einen bereits eingetragenen Bediener entfernt und übernimmt
- **THEN** ist das Konto nicht mehr Mitglied von AppStart Users und kann keine Applikation mehr über AppStart starten

#### Scenario: appstart-user bleibt
- **WHEN** der Techniker die Liste der Bediener bearbeitet
- **THEN** steht appstart-user nicht in der entfernbaren Liste; es wird nur über die eigene Option gesteuert
