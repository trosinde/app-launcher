## ADDED Requirements

### Requirement: Geschützte Named Pipe
Die Pipe SHALL nur für AppStart Users erreichbar sein, keine Fernzugriffe annehmen, beim Dienststart als erste Instanz angelegt werden und nur Anfragen im Format START <AppId> mit maximal 64 Byte annehmen.

#### Scenario: Zugriff über das Netzwerk
- **WHEN** ein anderer Rechner die Pipe öffnet
- **THEN** wird die Verbindung abgewiesen

#### Scenario: User ausserhalb der Gruppe
- **WHEN** ein User ohne Mitgliedschaft in AppStart Users die Pipe öffnet
- **THEN** wird der Zugriff verweigert

#### Scenario: Gefälschte Pipe
- **WHEN** beim Dienststart bereits eine Pipe mit dem Namen existiert
- **THEN** startet der Dienst nicht und meldet einen Fehler

#### Scenario: Ungültige Anfrage
- **WHEN** eine Anfrage ein anderes Format hat oder länger als 64 Byte ist
- **THEN** wird sie verworfen und protokolliert

#### Scenario: Fremde Session
- **WHEN** eine Anfrage eingeht
- **THEN** startet die Applikation nur in der Session des Anfragenden

### Requirement: Antwort an den Launcher
Der Dienst SHALL jede gültige Anfrage mit genau einer Zeile beantworten: OK, oder DENIED <Grund> <Ereignis-ID> mit Grund aus unknown-app, integrity, config, busy, error. Die Antwort enthält keine Pfade, Prüfsummen oder anderen internen Details.

#### Scenario: Start verweigert
- **WHEN** der Dienst einen Start verweigert
- **THEN** erhält der Launcher DENIED mit Grund und Ereignis-ID und zeigt die Meldung «Start verweigert» mit dieser Ereignis-ID

#### Scenario: Keine internen Details
- **WHEN** ein Start wegen einer veränderten EXE verweigert wird
- **THEN** enthält die Antwort weder den Pfad noch die erwartete oder gefundene Prüfsumme
