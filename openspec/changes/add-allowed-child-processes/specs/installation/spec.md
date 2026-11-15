## ADDED Requirements

### Requirement: Weitere Programme im Assistenten
Der Assistent SHALL je Applikation in Schritt 1 die Auswahl «Weitere Programme» (Gesperrt (empfohlen), Nur erlaubte Hilfsprogramme, Alle erlaubt (unsicher)) und den Dialog «Erlaubte Hilfsprogramme» mit Hinzufügen, Entfernen, Prüfsumme aktualisieren und Lernmodus anbieten, gemäss docs/ui-designs/Main, Helpers, Learn und LearnResult.

#### Scenario: Hilfsprogramm per Dialog erlauben
- **WHEN** der Installateur bei Applikation A «Nur erlaubte Hilfsprogramme» wählt, Export.exe hinzufügt und den Assistenten abschliesst
- **THEN** steht Applikation A mit childProcesses allowlist und Export.exe in allowedChildren in apps.json

#### Scenario: Shell im Lernergebnis
- **WHEN** die Aufzeichnung des Lernmodus cmd.exe enthält
- **THEN** zeigt der Assistent cmd.exe markiert und nicht auswählbar

#### Scenario: Alle erlaubt
- **WHEN** der Installateur «Alle erlaubt (unsicher)» wählt
- **THEN** verlangt der Assistent eine Bestätigung, bevor er weitergeht

### Requirement: Hilfsprogramme per Kommandozeile
AppStart.Setup.exe SHALL Modus, Positivliste und Lernmodus ohne Oberfläche verwalten: add --child-processes, set-children, allow-child, deny-child, rehash --child, learn start|stop|show|apply und list, mit den Exit-Codes 0, 1 und 2. Shells und Skript-Hosts SHALL nur mit --allow-shell eintragbar sein.

#### Scenario: Hilfsprogramm per Kommandozeile erlauben
- **WHEN** allow-child --app appa --exe "C:\Program Files\Hersteller\AppA\Export.exe" ausgeführt wird
- **THEN** steht Export.exe mit Signatur oder SHA-256 in allowedChildren, verify läuft automatisch und der Exit-Code ist 0

#### Scenario: Shell ohne Bestätigung
- **WHEN** allow-child mit C:\Windows\System32\cmd.exe ohne --allow-shell ausgeführt wird
- **THEN** bleibt apps.json unverändert und der Exit-Code ist 2

#### Scenario: Beschreibbarer Ordner
- **WHEN** das Hilfsprogramm in einem Ordner liegt, den Bediener beschreiben können
- **THEN** bleibt apps.json unverändert und der Exit-Code ist 1

### Requirement: Stille Einrichtung per Antwortdatei
AppStart.Setup.exe apply --config <Datei> SHALL Applikationen, Schutz, Modus, Hilfsprogramme und Bediener aus einer JSON-Antwortdatei idempotent einrichten. Die Datei SHALL vollständig geprüft werden, bevor etwas geändert wird, und darf keine Passwörter enthalten. Applikationen, die nicht in der Datei stehen, SHALL bleiben, ausser mit --replace.

#### Scenario: Erste Einrichtung
- **WHEN** apply --config mit einer gültigen Datei für zwei Applikationen ausgeführt wird
- **THEN** sind beide wie im Assistenten eingerichtet, verify läuft und der Exit-Code ist 0

#### Scenario: Zweite Anwendung
- **WHEN** dieselbe Datei erneut angewendet wird
- **THEN** ändert sich nichts und der Exit-Code ist 0

#### Scenario: Ungültige Datei
- **WHEN** die Datei ein unbekanntes Feld enthält oder ein Pfad nicht existiert
- **THEN** wird nichts geändert, die Ausgabe nennt Feld oder Pfad und der Exit-Code ist 2

### Requirement: Bootstrapper mit Antwortdatei
AppStart-Setup.exe /quiet /config <Pfad> SHALL das MSI still installieren und danach das installierte, signaturgeprüfte AppStart.Setup.exe mit apply --config <Pfad> ohne Oberfläche ausführen. /config ohne /quiet SHALL ungültig sein (Exit-Code 87). Endet apply mit 1, SHALL der Bootstrapper mit 10 enden, endet apply mit 2, mit 11.

#### Scenario: Softwareverteilung in einem Schritt
- **WHEN** AppStart-Setup.exe /quiet /config C:\Deploy\appstart.json als SYSTEM ausgeführt wird
- **THEN** ist AppStart installiert und eingerichtet, das Protokoll liegt unter C:\ProgramData\AppStart\Logs und der Exit-Code ist 0 (oder 3010 bei nötigem Neustart)

#### Scenario: Einrichtung scheitert
- **WHEN** die Installation gelingt, apply aber mit 2 endet, weil die Antwortdatei ungültig ist
- **THEN** endet der Bootstrapper mit Exit-Code 11

### Requirement: Migration der Konfiguration
Das Setup SHALL apps.json Version 1 beim ersten Schreiben in Version 2 überführen: allowChildProcesses false wird childProcesses blocked, true wird all.

#### Scenario: Update einer bestehenden Installation
- **WHEN** nach einem Update das Setup eine Änderung an apps.json Version 1 speichert
- **THEN** ist apps.json Version 2 und jede Applikation hat denselben Schutz wie vorher
