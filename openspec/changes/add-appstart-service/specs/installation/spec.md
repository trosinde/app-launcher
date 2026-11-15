## ADDED Requirements

### Requirement: Auswahl der Applikationen
Das Setup SHALL die Auswahl einer oder mehrerer Applikations-EXE ermöglichen, per Dialog und per Kommandozeile.

#### Scenario: Mehrfachauswahl
- **WHEN** der Installateur zwei Applikationen auswählt
- **THEN** stehen beide in apps.json und jede hat eine eigene Verknüpfung

### Requirement: Verknüpfung je Applikation
Das Setup SHALL je Applikation eine Verknüpfung auf den Launcher mit --app <AppId> anlegen, mit dem Icon aus der EXE der Applikation.

#### Scenario: Icon übernommen
- **WHEN** die Verknüpfung angelegt ist
- **THEN** zeigt sie das Icon der gewählten Applikation

### Requirement: Konten und Gruppe
Das Setup SHALL das Konto appstart-app (Admin, keine interaktive Anmeldung), die Gruppe AppStart Users und auf Wunsch das Standard-Konto appstart-user anlegen.

#### Scenario: Keine Anmeldung als App-Konto
- **WHEN** jemand sich interaktiv oder per Remote Desktop als appstart-app anmelden will
- **THEN** wird die Anmeldung verweigert

### Requirement: Rechte auf Daten und Registry
Das Setup SHALL Datenordner und Registry-Key je Applikation auf appstart-app, SYSTEM und Administratoren beschränken.

#### Scenario: Bediener liest Registry
- **WHEN** ein Bediener den Registry-Key in regedit öffnet
- **THEN** wird der Zugriff verweigert

### Requirement: Installationsort
Das Paket SHALL ausschliesslich per-machine nach Program Files installieren.

#### Scenario: Programmordner
- **WHEN** die Installation abgeschlossen ist
- **THEN** können nur SYSTEM und Administratoren im Programmordner schreiben

### Requirement: Interaktive Installation per Bootstrapper
Das Paket SHALL einen signierten Bootstrapper AppStart-Setup.exe enthalten (Native AOT, keine entpackten Bibliotheken), der Admin-Rechte verlangt, das eingebettete MSI still installiert und danach das installierte AppStart.Setup aus dem Programmordner mit dem Wizard startet.

#### Scenario: Erstinstallation
- **WHEN** der Installateur AppStart-Setup.exe startet und die UAC-Abfrage bestätigt
- **THEN** wird das MSI installiert und anschliessend erscheint Schritt 1 des Wizards aus C:\Program Files\AppStart, ohne zweite UAC-Abfrage

#### Scenario: Start aus beschreibbarem Ordner
- **WHEN** AppStart-Setup.exe aus einem für Benutzer beschreibbaren Ordner wie Downloads gestartet wird
- **THEN** lädt oder startet der Bootstrapper mit Admin-Rechten keine Dateien aus diesem Ordner oder aus %TEMP% und startet AppStart.Setup nur aus dem Programmordner nach erfolgreicher Signaturprüfung

#### Scenario: Fehler bei der MSI-Installation
- **WHEN** msiexec mit einem Fehler endet, etwa 1618 weil eine andere Installation läuft
- **THEN** zeigt der Bootstrapper eine Meldung mit Ursache und Massnahme, legt das MSI-Log unter C:\ProgramData\AppStart\Logs ab und startet den Wizard nicht

#### Scenario: Abbruch im Wizard
- **WHEN** der Installateur den Wizard nach erfolgreicher MSI-Installation abbricht
- **THEN** bleibt AppStart installiert, der Wizard ist über das Startmenü erneut startbar und verify meldet die fehlende Einrichtung als Abweichung

#### Scenario: Update über bestehende Installation
- **WHEN** AppStart-Setup.exe einer neueren Version auf einem eingerichteten Rechner gestartet wird
- **THEN** wird das MSI aktualisiert, der Wizard zeigt die bestehende Konfiguration und am Ende läuft verify

### Requirement: Stille Installation per MSI
Das MSI SHALL ohne Bootstrapper und ohne Oberfläche installierbar sein, damit Softwareverteilung es nutzen kann. Die Einrichtung erfolgt dann über die Kommandozeile von AppStart.Setup.

#### Scenario: Softwareverteilung als SYSTEM
- **WHEN** msiexec /i AppStart.msi /qn als SYSTEM ausgeführt wird
- **THEN** ist AppStart im Programmordner installiert, der Dienst läuft und es erscheint keine Oberfläche

### Requirement: Installer-Hooks ohne Oberfläche
Der Install-Hook des Pakets SHALL nur den Dienst registrieren und starten, der Uninstall-Hook nur den Dienst stoppen und löschen. Beide zeigen keine Oberfläche. Konfiguration, Konten, Gruppe, Verknüpfungen und Rechte entfernt ausschliesslich AppStart.Setup.exe uninstall.

#### Scenario: Update erhält die Einrichtung
- **WHEN** ein neues MSI über eine eingerichtete Installation installiert wird
- **THEN** bleiben apps.json, Konten, Gruppe, Verknüpfungen und Rechte unverändert und der Dienst läuft in der neuen Version

#### Scenario: Vollständige Deinstallation
- **WHEN** der Servicetechniker AppStart.Setup.exe uninstall und danach die MSI-Deinstallation ausführt
- **THEN** sind die ursprünglichen Verknüpfungen zurückgestellt, Rechte auf Datenordnern und Registry-Keys zurückgesetzt, Konten und Gruppe entfernt sowie Dienst und Programmordner gelöscht
