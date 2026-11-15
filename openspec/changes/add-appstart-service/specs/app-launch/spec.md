## ADDED Requirements

### Requirement: Start freigegebener Applikationen
Der Dienst SHALL nur Applikationen aus apps.json starten, nach erfolgreicher Prüfung von Signatur oder SHA-256, als Konto appstart-app in der Session des anfragenden Users.

#### Scenario: Start durch Bediener
- **WHEN** ein Mitglied von AppStart Users die Verknüpfung einer freigegebenen Applikation startet
- **THEN** läuft die Applikation als appstart-app auf seinem Bildschirm, ohne Passwort- oder UAC-Abfrage

#### Scenario: Veränderte EXE
- **WHEN** Signatur oder Prüfsumme der EXE nicht stimmt
- **THEN** wird der Start verweigert und im Ereignisprotokoll gemeldet

#### Scenario: Unbekannte AppId
- **WHEN** eine AppId angefragt wird, die nicht in apps.json steht
- **THEN** wird der Start verweigert und protokolliert

### Requirement: Keine Kindprozesse
Der Dienst SHALL Applikationen mit allowChildProcesses false in einem Job-Objekt starten, das weitere Prozesse verhindert.

#### Scenario: Ausbruch über Dateidialog
- **WHEN** die Applikation versucht, cmd.exe zu starten
- **THEN** schlägt der Start des Kindprozesses fehl

### Requirement: Geschützte und gültige Konfiguration
Der Dienst SHALL apps.json bei jeder Anfrage neu lesen und jeden Start verweigern, wenn die Datei oder ihr Ordner für andere Konten als SYSTEM, Administratoren und TrustedInstaller beschreibbar ist oder die Datei ungültig ist. Gültig heisst: Version 1, jede AppId besteht nur aus Kleinbuchstaben, Ziffern und Bindestrich mit 1 bis 32 Zeichen und kommt nur einmal vor, jeder Pfad ist absolut, check ist signature mit Thumbprint (40 Hex-Zeichen) oder sha256 mit Prüfsumme (64 Hex-Zeichen).

#### Scenario: Konfiguration für Bediener beschreibbar
- **WHEN** apps.json oder C:\ProgramData\AppStart Schreibrechte für Benutzer oder einen Bediener enthält
- **THEN** verweigert der Dienst jeden Start und protokolliert die Ursache

#### Scenario: Ungültige Konfiguration
- **WHEN** apps.json nicht dem Format entspricht, etwa wegen einer AppId mit Grossbuchstaben oder einer doppelten AppId
- **THEN** verweigert der Dienst jeden Start und protokolliert den ersten Fehler mit Zeile oder Feld

### Requirement: Prüfung und Start derselben Datei
Der Dienst SHALL die EXE vor der Prüfung so öffnen, dass sie bis zum Start des Prozesses weder geändert, ersetzt noch gelöscht werden kann, und Signatur oder Prüfsumme an genau diesem geöffneten Inhalt prüfen. Die Signaturprüfung verlangt eine gültige Authenticode-Kette und den Thumbprint aus apps.json; eine Online-Sperrprüfung erfolgt nicht, da Laborrechner oft offline sind.

#### Scenario: Austausch zwischen Prüfung und Start
- **WHEN** während der Prüfung versucht wird, die EXE zu überschreiben oder umzubenennen
- **THEN** schlägt dieser Versuch fehl und gestartet wird nur der geprüfte Inhalt

#### Scenario: Fremde Signatur
- **WHEN** die EXE gültig signiert ist, aber von einem anderen Zertifikat als dem in apps.json hinterlegten
- **THEN** wird der Start verweigert und protokolliert

### Requirement: Launcher ohne Rechte und mit verständlichen Meldungen
Der Launcher SHALL nur das Argument --app <AppId> annehmen, das Format der AppId vor dem Senden prüfen, START <AppId> über die Pipe senden und bei Erfolg ohne Fenster enden. Bei jedem Fehler zeigt er eine Meldung nach UI-Entwurf mit dem Namen der Verknüpfung als Titel, einer Überschrift, einem Text mit Massnahme und, falls vorhanden, der Ereignis-ID mit Datum und Uhrzeit.

#### Scenario: Dienst nicht erreichbar
- **WHEN** der Dienst AppStart nicht läuft
- **THEN** zeigt der Launcher «Start-Dienst nicht erreichbar» mit dem Hinweis, sich an Administrator oder Service zu wenden

#### Scenario: Start verweigert
- **WHEN** der Dienst mit DENIED integrity antwortet
- **THEN** zeigt der Launcher «Start verweigert», den Text zur veränderten Applikation und die Ereignis-ID

#### Scenario: Keine Berechtigung
- **WHEN** der Benutzer nicht Mitglied von AppStart Users ist
- **THEN** zeigt der Launcher «Keine Berechtigung» und sendet nichts

#### Scenario: Ungültige Verknüpfung
- **WHEN** der Launcher ohne --app, mit weiteren Argumenten oder mit einer ungültigen AppId gestartet wird
- **THEN** zeigt er «Ungültige Verknüpfung» und verbindet sich nicht mit dem Dienst
