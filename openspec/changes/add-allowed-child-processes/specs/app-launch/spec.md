## MODIFIED Requirements

### Requirement: Keine Kindprozesse
Der Dienst SHALL jede Applikation gemäss ihrem Feld childProcesses starten: blocked (Standard) in einem Job-Objekt, das weitere Prozesse verhindert; allowlist in einem überwachten Job-Objekt gemäss Anforderung «Positivliste für Kindprozesse»; all ohne Job-Objekt. apps.json Version 1 SHALL weiter gelten: allowChildProcesses false entspricht blocked, true entspricht all.

#### Scenario: Ausbruch über Dateidialog
- **WHEN** eine Applikation mit childProcesses blocked versucht, cmd.exe zu starten
- **THEN** schlägt der Start des Kindprozesses fehl

#### Scenario: Konfiguration Version 1
- **WHEN** apps.json Version 1 eine Applikation mit allowChildProcesses false enthält
- **THEN** startet der Dienst sie wie mit childProcesses blocked

## ADDED Requirements

### Requirement: Positivliste für Kindprozesse
Bei childProcesses allowlist SHALL der Dienst die Applikation in einem Job-Objekt ohne Prozesslimit, ohne Breakaway und mit KILL_ON_JOB_CLOSE starten, jeden neuen Prozess im Job gegen allowedChildren prüfen (Pfad und Signatur oder SHA-256) und jeden Prozess ohne gültigen Eintrag sofort beenden. Jeder Fehler bei der Prüfung SHALL zum Beenden führen. Jede Entscheidung SHALL mit Bediener, AppId, Pfad und Grund im Ereignisprotokoll stehen.

#### Scenario: Erlaubtes Hilfsprogramm
- **WHEN** die Applikation Export.exe startet und Export.exe mit gültiger Prüfung in allowedChildren steht
- **THEN** läuft Export.exe weiter und ein Info-Ereignis wird geschrieben

#### Scenario: Nicht erlaubter Prozess
- **WHEN** die Applikation cmd.exe startet und cmd.exe nicht in allowedChildren steht
- **THEN** beendet der Dienst cmd.exe sofort und schreibt ein Warn-Ereignis

#### Scenario: Veränderte Datei
- **WHEN** eine veränderte Export.exe gestartet wird, deren SHA-256 nicht zum Eintrag passt
- **THEN** wird sie beendet und das Ereignis nennt den Grund «Prüfsumme stimmt nicht»

#### Scenario: Enkelprozess
- **WHEN** das erlaubte Export.exe einen Prozess startet, der nicht in allowedChildren steht
- **THEN** wird dieser Prozess beendet

#### Scenario: Applikation endet vor dem Hilfsprogramm
- **WHEN** die Applikation endet, während ein erlaubter Updater.exe noch läuft
- **THEN** läuft Updater.exe weiter und bleibt bis zu seinem Ende überwacht

#### Scenario: Dienst stoppt
- **WHEN** der Dienst gestoppt wird oder abstürzt, während eine Applikation mit allowlist läuft
- **THEN** beendet Windows alle Prozesse ihres Job-Objekts

### Requirement: Lernmodus
Der Dienst SHALL für eine AppId einen zeitlich begrenzten Lernmodus unterstützen, den nur Administratoren über die geschützte Datei learn.json einschalten können. Im Lernmodus SHALL der Dienst Prozesse im Job nicht beenden, sondern Pfad, Signatur, SHA-256, Anzahl, Zeitpunkte und Elternprozess in C:\ProgramData\AppStart\Learn\<AppId>.json aufzeichnen. Nach Ablauf SHALL wieder die Positivliste gelten, auch für bereits laufende Instanzen.

#### Scenario: Aufzeichnung
- **WHEN** der Lernmodus für appa aktiv ist und Applikation A Export.exe startet
- **THEN** läuft Export.exe und steht mit Pfad, Prüfwerten und Anzahl in der Aufzeichnung

#### Scenario: Zeitablauf
- **WHEN** die Ablaufzeit vorbei ist und die noch laufende Applikation A cmd.exe startet
- **THEN** beendet der Dienst cmd.exe gemäss Positivliste

#### Scenario: Manipulierte Steuerdatei
- **WHEN** learn.json oder ihr Ordner für Bediener beschreibbar ist
- **THEN** ignoriert der Dienst den Lernmodus und schreibt ein Fehler-Ereignis
