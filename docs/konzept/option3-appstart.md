# Option 3 – Eigener Start-Dienst «AppStart» (Umsetzung)

Diese Seite ist die Übergabe an Claude Code. AppStart wird als eigenständige Komponente in einem eigenen Repository spec-driven mit OpenSpec umgesetzt. Applikationen und ihre Installer bleiben unverändert.

## Ziel und Abgrenzung

- Standard-User starten freigegebene Applikationen mit Admin-Rechten, ohne Passwort und ohne UAC-Abfrage.
- Austauschdateien und Passwörter in der Registry sind für Standard-User gesperrt.
- Bei der Installation werden eine oder mehrere Applikationen ausgewählt. Jede bekommt eine eigene Verknüpfung mit ihrem eigenen Icon.
- Eigenes Repository, eigener Release-Zyklus, eigenes Installationspaket.
- Nicht Teil dieser Komponente: der Umbau der Applikationen (Option 4).

## UI-Entwürfe

Verbindliche Vorlage für die Oberflächen: [AppStart – UI-Entwürfe](https://claude.ai/artifact/VSjT6YSVy9BbDpmqEMvwrW)

| Entwurf | Komponente |
|---|---|
| Setup 1/3 – Applikationen auswählen (Mehrfachauswahl, Datenordner, Registry-Key) | AppStart.Setup |
| Setup 2/3 – Konten und Gruppe | AppStart.Setup |
| Setup 3/3 – Zusammenfassung und Prüfung | AppStart.Setup |
| Prüfbericht (`verify`) | AppStart.Setup |
| Testapplikation | AppStart.TestApp |
| Meldungen: Dienst nicht erreichbar, Start verweigert | AppStart.Launcher |

## Komponenten

| Komponente | Art | Aufgabe | Läuft mit |
|---|---|---|---|
| AppStart.Service | Windows-Dienst (.NET Worker Service) | Nimmt Startanfragen an, prüft sie und startet die Applikation in der Session des Users | LocalSystem |
| AppStart.Launcher | EXE ohne Fenster (Native AOT) | Sendet nur `START <AppId>` an den Dienst | Benutzerrechten |
| AppStart.Setup | Konfigurations-Tool mit WPF-Oberfläche und Kommandozeile | Applikationen auswählen, Konfiguration schreiben, Verknüpfungen und Rechte setzen, Einrichtung prüfen (`verify`) | Admin-Rechten |
| AppStart.Core | Bibliothek | Konfigurationsmodell, Prüfsumme und Signatur, Pipe-Protokoll, gemeinsame Konstanten | – |
| Installationspaket | Velopack, MSI per-machine | Installiert alles nach `C:\Program Files\AppStart` | Admin-Rechten |

## Ablauf beim Start einer Applikation

1. Der User klickt die Verknüpfung der Applikation. Sie zeigt auf den Launcher mit `--app <AppId>` und trägt das Icon der Applikation.
1. Der Launcher prüft das Format der AppId und sendet `START <AppId>` über die Named Pipe an den Dienst.
1. Der Dienst ermittelt die Session des Anfragenden, sucht die AppId in der Liste freigegebener Applikationen und prüft Signatur oder Prüfsumme der EXE.
1. Der Dienst startet die Applikation als Konto appstart-app in genau dieser Session, eingeschlossen in ein Job-Objekt (siehe Sicherheit).
1. Jede Anfrage wird mit Zeit, User, Session, AppId und Ergebnis ins Windows-Ereignisprotokoll geschrieben.

## Technologie

| Baustein | Entscheid | Begründung |
|---|---|---|
| Sprache und Laufzeit | C# auf .NET 10 LTS, self-contained | Bestehender Stack. .NET 8 ist seit 10.11.2026 ohne Support, .NET 10 wird bis November 2028 unterstützt. Auf dem Kunden-PC muss kein .NET installiert sein. |
| Dienst | .NET Worker Service mit `Microsoft.Extensions.Hosting.WindowsServices` | Standardweg für Windows-Dienste in .NET, Ereignisprotokoll eingebaut |
| Windows-Schnittstellen | CsWin32 (Microsoft-Codegenerator für Win32-Aufrufe) | Typsichere Aufrufe für Token, Session, Pipe und Job-Objekt |
| Launcher | Native AOT, WinExe ohne Fenster | Startet sofort, keine Abhängigkeiten |
| Oberflächen | WPF (.NET 10), MVVM, Fluent-Theme – festgelegt | Für Setup und Testapplikation. Setzt die UI-Entwürfe genau um, Logik in testbaren ViewModels |
| Kommandozeile | System.CommandLine | Stille Installation und Prüfbefehl für den Service |
| Installationspaket | Velopack, als MSI mit `--msi --instLocation PerMachine` | Gewünschtes Werkzeug. Nur die per-machine-MSI installiert nach Program Files. Lizenz: Open Source (MIT). |
| Signatur | Authenticode mit dem Code-Signing-Zertifikat des Herausgebers für alle EXE und das MSI | Integrität und Vertrauen beim Kunden |
| Tests | xUnit, Integrationstests auf einer Windows-VM | Dienst, Pipe und Rechte lassen sich nur unter echtem Windows prüfen |
| Build | Azure DevOps Pipeline auf Windows-Agent | Bestehende Umgebung |

## Konten, die die Installation anlegt

| Konto | Art | Zweck | Rechte |
|---|---|---|---|
| `appstart-app` | Lokales Konto, nicht interaktiv anmeldbar | Unter diesem Konto laufen die freigegebenen Applikationen | Mitglied der lokalen Administratoren |
| `appstart-user` | Lokales Standard-Benutzerkonto | Konto für die Bediener, die die Applikationen über den Launcher starten | Keine Admin-Rechte, Mitglied der Gruppe `AppStart Users` |
| `AppStart Users` | Lokale Gruppe | Nur Mitglieder dürfen den Launcher bzw. die Pipe nutzen. Weitere lokale oder Domänen-User können aufgenommen werden. | – |

Regeln:

- `appstart-app` bekommt ein Zufallspasswort pro PC, das niemand kennt. Interaktive Anmeldung und Anmeldung über Remote Desktop werden per Benutzerrecht verweigert.
- Der Dienst holt sich das Token für `appstart-app` bevorzugt per S4U-Anmeldung (kein gespeichertes Passwort). Geht das nicht, liegt das Passwort mit DPAPI (LocalMachine) verschlüsselt in einer Datei, die nur SYSTEM lesen kann. Klärung im Spike, Task 1.
- Die Applikation läuft damit nicht als SYSTEM, sondern als `appstart-app`. Ihr Benutzerbereich der Registry liegt im eigenen Profil dieses Kontos und ist für Bediener nicht lesbar.
- Damit die Applikation auf dem Bildschirm des Bedieners erscheint, gibt der Dienst dem Konto Zugriff auf `WinSta0\Default` dieser Session, wie es auch `runas` tut.
- `appstart-user` wird nur angelegt, wenn bei der Installation gewählt. Das Passwort vergibt der Installateur. Bestehende User können stattdessen in die Gruppe aufgenommen werden.

## Installation und Auswahl der Applikationen

1. Installation des Velopack-MSI per-machine nach `C:\Program Files\AppStart`. Nie das per-user-Setup von Velopack verwenden: Es installiert in einen Ordner, den der User beschreiben kann. Damit könnte er die Dienst-EXE austauschen und SYSTEM-Rechte erlangen.
1. Danach startet `AppStart.Setup` mit Admin-Rechten und zeigt den Auswahldialog.
1. Der Installateur wählt eine oder mehrere Applikations-EXE aus (Mehrfachauswahl). Je Applikation kann er Datenordner und Registry-Key mit den Passwörtern angeben.
1. Das Setup legt die Konten und die Gruppe an (siehe oben), sofern noch nicht vorhanden.
1. Je Applikation: AppId vergeben, Signatur oder SHA-256 erfassen, Eintrag in `apps.json` schreiben.
1. Je Applikation: eine eigene Verknüpfung im öffentlichen Startmenü und auf dem öffentlichen Desktop. Ziel ist der Launcher mit `--app <AppId>`, das Icon kommt aus der EXE der Applikation, Arbeitsordner ist der Ordner der Applikation.
1. Vorhandene Verknüpfungen, die direkt auf die EXE der Applikation zeigen, werden gesichert und ersetzt. Bei der Deinstallation werden sie zurückgestellt.
1. Datenordner und Registry-Key je Applikation sperren: Zugriff nur für `appstart-app`, SYSTEM und Administratoren.
1. Dienst registrieren (Start automatisch, Neustart nach Absturz) und starten.
1. Zum Schluss läuft automatisch `verify`. Der Prüfbericht wird abgelegt.

Kommandozeile für stille Installation und Service:

```text
AppStart.Setup.exe add --exe "<Pfad>" [--data-dir "<Ordner>"] [--reg-key "<Key>"] [--name "<Anzeigename>"]
AppStart.Setup.exe add-user --name appstart-user | --member "<DOMAIN\User>"
AppStart.Setup.exe remove --app <AppId>
AppStart.Setup.exe list
AppStart.Setup.exe rehash --app <AppId>     # nach Update einer Applikation
AppStart.Setup.exe verify [--report "<Pfad>"]
AppStart.Setup.exe uninstall
```

Updates: Automatische Updates von Velopack bleiben aus. Updates installiert der Servicetechniker mit einem neuen MSI. Vorher wird der Dienst gestoppt, danach läuft `verify`.

## Konfiguration

Datei `C:\ProgramData\AppStart\apps.json`, nur für SYSTEM und Administratoren les- und schreibbar:

```json
{
  "version": 1,
  "apps": [
    {
      "id": "app-a",
      "name": "Applikation A",
      "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
      "workingDir": "C:\\Program Files\\Hersteller\\AppA",
      "check": "signature",
      "signerThumbprint": "<Thumbprint>",
      "sha256": null,
      "allowChildProcesses": false,
      "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
      "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA"
    }
  ]
}
```

- `check`: `signature` wenn die EXE signiert ist (übersteht Updates der Applikation), sonst `sha256`.
- AppId: nur Kleinbuchstaben, Ziffern und Bindestrich, maximal 32 Zeichen.
- Der Dienst liest die Datei bei jeder Anfrage. Ist sie für Nicht-Admins beschreibbar, verweigert er jeden Start und meldet einen Fehler.

## Sicherheit

**Named Pipe** `\\.\pipe\AppStart`:

1. Rechte: Verbinden nur für die Gruppe `AppStart Users`, Vollzugriff für SYSTEM und Administratoren, Netzwerk ausdrücklich verweigert. Beispiel-SDDL: `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;<SID ``AppStart Users>)(D;;GA;;;NU)`.
1. `PIPE_REJECT_REMOTE_CLIENTS` gesetzt.
1. Der Dienst legt die Pipe beim Start als erste Instanz an (`FILE_FLAG_FIRST_PIPE_INSTANCE`). Existiert sie schon, startet er nicht und meldet einen Fehler.
1. Session des Anfragenden per `GetNamedPipeClientSessionId`. Start nur in genau dieser Session.
1. Anfrage hat das feste Format `START <AppId>`, maximal 64 Byte. Alles andere wird verworfen. Eine Anfrage pro Session gleichzeitig, kurze Sperrzeit nach jedem Start, Timeout beim Lesen.

**Start der Applikation:**

- Nur Applikationen aus `apps.json`, nach erfolgreicher Prüfung von Signatur oder Prüfsumme.
- Start im Job-Objekt. Bei `allowChildProcesses: false` darf die Applikation keine weiteren Programme starten. Das verhindert, dass ein Bediener über einen Datei-öffnen-Dialog eine Eingabeaufforderung mit Admin-Rechten öffnet. Je Applikation testen, ob sie ohne Kindprozesse funktioniert.

**Registry-Key mit Passwörtern sperren** (Teil von `AppStart.Setup`, hier als PowerShell-Referenz):

```powershell
$RegKey = "HKLM:\SOFTWARE\Hersteller\AppA"
$acl = New-Object System.Security.AccessControl.RegistrySecurity
$acl.SetAccessRuleProtection($true, $false)
$inh = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit
$prop = [System.Security.AccessControl.PropagationFlags]::None
foreach ($id in @("$env:COMPUTERNAME\appstart-app", "NT AUTHORITY\SYSTEM", "BUILTIN\Administrators")) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule($id, "FullControl", $inh, $prop, "Allow")))
}
Set-Acl -Path $RegKey -AclObject $acl
```

**Programmordner** `C:\Program Files\AppStart`: nur für SYSTEM und Administratoren beschreibbar.

**Restrisiko:** Administratoren können die Passwörter weiterhin lesen. Erst Option 4 behebt das.

## Prüfbefehl für das Service and Security Manual

`AppStart.Setup.exe verify` prüft und erzeugt einen Prüfbericht (lesbar und JSON). Exit-Code ungleich 0 bei jeder Abweichung.

| Prüfpunkt | Soll |
|---|---|
| Dienst | Installiert, läuft, Start automatisch, Konto LocalSystem |
| Programmordner | Nur SYSTEM und Administratoren schreiben |
| Konten | `appstart-app` vorhanden, keine interaktive Anmeldung; Bediener ohne Admin-Rechte |
| Pipe | Rechte gemäss SDDL, keine Fernzugriffe |
| `apps.json` | Nur SYSTEM und Administratoren, jede EXE besteht die Prüfung |
| Datenordner je App | Nur `appstart-app`, SYSTEM, Administratoren |
| Registry-Key je App | Für Bediener nicht lesbar |
| Verknüpfungen | Zeigen auf den Launcher, keine direkte Verknüpfung auf die App-EXE |

Das Service and Security Manual verlangt `verify` nach der Installation und am Ende jedes Serviceeinsatzes. Der Bericht wird dem Einsatz beigelegt.

**So wird die Prüfung gestartet:**

1. **Startmenü (Standardweg für den Servicetechniker):** Eintrag «AppStart – Einrichtung prüfen» unter AppStart. Nach der UAC-Abfrage mit Admin-Zugangsdaten öffnet sich die Prüfansicht. Sie zeigt das Ergebnis je Prüfpunkt, speichert den Bericht automatisch und bietet «Bericht drucken» und «Bericht speichern unter».
1. **Setup-Oberfläche:** Knopf «Einrichtung prüfen», gleiche Prüfansicht.
1. **Automatisch:** am Ende jeder Installation, jeder Änderung im Setup und jedes Updates.
1. **Kommandozeile für Skripte:** `AppStart.Setup.exe verify --report "<Pfad>"`, Exit-Code 0 nur ohne Abweichung.

Bediener können die Prüfung nicht starten, da sie Admin-Rechte verlangt. Berichte liegen unter `C:\ProgramData\AppStart\Reports`, je Prüfung eine Datei mit Datum und Uhrzeit.

## Benutzerhandbuch (Pflicht-Lieferumfang)

Teil der Entwicklung ist ein vollständiges Benutzerhandbuch für Installateur und Servicetechniker. Ohne Handbuch gilt die Umsetzung nicht als abgeschlossen. Es ist zugleich die Grundlage für das Service and Security Manual.

| Kapitel | Inhalt |
|---|---|
| Überblick | Zweck, Funktionsweise in einem Bild, Begriffe (App-Konto, Bedienerkonto, Gruppe, Launcher) |
| Voraussetzungen | Windows-Versionen, Admin-Rechte, benötigte Angaben je Applikation (EXE, Datenordner, Registry-Key) |
| Installation | Schritt für Schritt mit Bildschirmfotos aller drei Setup-Schritte, stille Installation |
| Applikationen verwalten | Hinzufügen, entfernen, nach Update der Applikation neu prüfen (`rehash`) |
| Konten und Gruppe | Bedienerkonto anlegen, weitere User in die Gruppe aufnehmen |
| Prüfung nach Serviceeinsatz | Start über das Startmenü, Prüfbericht lesen, jede mögliche Abweichung mit Ursache und Behebung, Ablage des Berichts |
| Meldungen und Fehlerbehebung | Alle Meldungen des Launchers und Einträge im Ereignisprotokoll mit Bedeutung und Massnahme |
| Update und Deinstallation | Ablauf, was zurückgestellt wird |
| Referenz | Alle Kommandozeilenbefehle, Soll-Konfiguration, Pfade |

- Quelle als Markdown in `docs/manual/` im Repository, daraus erzeugt die Pipeline ein PDF.
- Das PDF wird im Paket mitgeliefert und ist im Startmenü unter «AppStart – Handbuch» erreichbar.
- Bildschirmfotos stammen aus der fertigen Software auf der Test-VM, nicht aus den UI-Entwürfen.
- Jede Meldung, jeder Prüfpunkt und jeder Befehl der Software kommt im Handbuch vor. Ein Test prüft das gegen die Ressourcen der Software.
- Sprache: Deutsch. Englische Fassung offen.

## Testapplikation `AppStart.TestApp`

Eine kleine Testapplikation, mit der sich AppStart vollständig prüfen lässt, ohne eine echte Applikation zu brauchen. Sie verhält sich wie die betroffenen Fat Clients.

| Eigenschaft | Umsetzung |
|---|---|
| Braucht Admin-Rechte | Manifest `requireAdministrator`. Ohne Admin-Rechte bricht sie mit einer Meldung ab. |
| Speichert in der Registry | Schreibt Testwerte inkl. eines Test-Passworts im Klartext nach `HKLM\SOFTWARE\AppStartTest` |
| Nutzt Dateien | Schreibt und liest Austauschdateien in `C:\ProgramData\AppStartTest` |
| Zeigt ihren Zustand | Fenster zeigt: laufendes Konto, Admin ja/nein, Registry lesen/schreiben ok, Datei lesen/schreiben ok |
| Ausbruchstest | Knopf «Eingabeaufforderung starten» versucht `cmd.exe` zu starten. Mit Job-Objekt muss das scheitern. |
| Signatur | Wie alle Binaries signiert, damit die Signaturprüfung getestet werden kann |

Die Testapplikation wird mit AppStart ausgeliefert, aber nicht automatisch freigegeben. Der Tester wählt sie im Setup wie jede andere Applikation aus, mit Datenordner und Registry-Key oben.

**Abnahmetest mit der Testapplikation:**

1. Als `appstart-user` über die Verknüpfung starten: Applikation läuft als `appstart-app`, alle Anzeigen grün.
1. Als `appstart-user` den Registry-Key in regedit öffnen: Zugriff verweigert.
1. Als `appstart-user` den Datenordner im Explorer öffnen: Zugriff verweigert.
1. Als `appstart-user` die TestApp-EXE direkt starten: Abbruch, weil keine Admin-Rechte.
1. Knopf «Eingabeaufforderung starten»: Start scheitert.
1. Ein Byte der TestApp-EXE ändern: Start über den Launcher wird verweigert und protokolliert.
1. `verify` liefert Exit-Code 0, nach absichtlicher Fehlkonfiguration ungleich 0.

## Entwicklungs- und Testumgebung

Die Entwicklung läuft ausschliesslich unter Windows. Oberflächen (WPF), Launcher (Native AOT), Dienst, Pipe, Konten, Rechte, MSI und Signatur lassen sich nur unter Windows bauen, ausführen und testen.

| Umgebung | Zweck | Vorgabe |
|---|---|---|
| Windows-Entwicklungsrechner oder Windows-VM | Claude Code, Entwicklung, Build, Unit-Tests | Windows 11 x64, .NET 10 SDK, Git, OpenSpec CLI, Velopack CLI (`vpk`), Claude Code nativ unter Windows |
| Separate Windows-Test-VM mit Snapshot | Installation, Integrationstests, Abnahmetest mit der TestApp | Windows 10 und 11 x64, nie der eigene Arbeitsrechner, vor jedem Test auf Snapshot zurücksetzen |
| Azure DevOps Pipeline | Build, Tests, Paket, Signatur | Windows-Agent |

Das Setup legt Konten an, registriert einen Dienst und ändert Rechte. Deshalb wird es nur auf der Test-VM ausgeführt, nie auf dem Entwicklungsrechner.

## Repository

```text
appstart/
  CLAUDE.md
  openspec/
    project.md
    changes/add-appstart-service/
      proposal.md
      tasks.md
      specs/app-launch/spec.md
      specs/pipe-security/spec.md
      specs/installation/spec.md
      specs/verification/spec.md
      specs/test-app/spec.md
      specs/documentation/spec.md
  docs/manual/
  src/AppStart.Core/
  src/AppStart.Service/
  src/AppStart.Launcher/
  src/AppStart.Setup/
  src/AppStart.TestApp/
  tests/AppStart.Core.Tests/
  tests/AppStart.IntegrationTests/
  build/velopack/
  azure-pipelines.yml
```

## OpenSpec-Change `add-appstart-service`

### openspec/project.md

```markdown
# AppStart

## Purpose
Startet freigegebene Windows-Applikationen für Standard-User mit Admin-Rechten, ohne Passwort und ohne UAC. Eigenständige Komponente, ändert weder Applikationen noch deren Installer.

## Tech Stack
- C# / .NET 10 LTS, self-contained, x64, Windows 10 und 11
- Dienst: .NET Worker Service (Microsoft.Extensions.Hosting.WindowsServices)
- Win32: CsWin32
- Launcher: Native AOT, WinExe ohne Fenster
- UI: WPF (MVVM, Fluent-Theme) für Setup und TestApp
- Kommandozeile: System.CommandLine
- Paket: Velopack MSI, per-machine
- Tests: xUnit, Integrationstests auf Windows-Test-VM mit Snapshot
- CI: Azure DevOps, Windows-Agent

## Environment
- Entwicklung, Build und Tests ausschliesslich unter Windows 11 x64
- Setup, Dienstinstallation und Integrationstests nur auf der separaten Test-VM, nie auf dem Entwicklungsrechner

## Conventions
- Sicherheit vor Komfort: jede Abweichung von der Soll-Konfiguration führt zu Ablehnung und Eintrag im Ereignisprotokoll
- Keine Pfade oder freien Parameter vom Launcher, nur AppId aus apps.json
- Kein Passwort im Klartext, keine Geheimnisse im Repository
- Alle Binaries Authenticode-signiert
- Code und Kommentare Englisch, Doku Deutsch
- Jede Anforderung hat mindestens einen automatisierten Test
```

### proposal.md

```markdown
## Why
Mehrere Applikationen brauchen Admin-Rechte. Bediener arbeiten deshalb heute als Administrator und können Austauschdateien ändern und Passwörter in der Registry lesen. Befund bei Audits und Mangel nach EU CRA.

## What Changes
- Neuer Windows-Dienst AppStart.Service (LocalSystem) startet freigegebene Applikationen als Konto appstart-app in der Session des Bedieners
- Launcher ohne Rechte, eine Verknüpfung je Applikation mit deren Icon
- Setup mit Mehrfachauswahl der Applikationen, legt Konten, Gruppe, Konfiguration, Verknüpfungen und Rechte an
- Prüfbefehl verify für das Service and Security Manual
- Installationspaket Velopack MSI per-machine

## Impact
- Keine Änderung an Applikationen und deren Installern
- Neue Komponenten: Service, Launcher, Setup, Core, MSI
- Restrisiko: Administratoren können Passwörter weiterhin lesen (Option 4)
```

### specs/app-launch/spec.md

```markdown
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
```

### specs/pipe-security/spec.md

```markdown
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
```

### specs/installation/spec.md

```markdown
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
```

### specs/verification/spec.md

```markdown
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
```

### specs/test-app/spec.md

```markdown
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
```

### specs/documentation/spec.md

```markdown
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
```

### tasks.md

```markdown
## 1. Spike und Klärung
- [ ] 1.1 S4U-Anmeldung für appstart-app aus dem Dienst prüfen; sonst Passwort mit DPAPI LocalMachine
- [ ] 1.2 Anzeige der Applikation in der User-Session (WinSta0\Default) als appstart-app prüfen
- [ ] 1.3 Velopack MSI per-machine: Lauf von Setup nach Installation, Dienst stoppen bei Update, Deinstallation; Ergebnis dokumentieren, sonst Fallback WiX MSI vorschlagen
- [ ] 1.4 Je Ziel-Applikation prüfen: läuft sie ohne Kindprozesse, wo liegen Daten und Registry-Key

## 2. Testapplikation (zuerst, dient allen weiteren Tests)
- [ ] 2.1 AppStart.TestApp: WPF (MVVM), Manifest requireAdministrator
- [ ] 2.2 Registry-Werte inkl. Test-Passwort in HKLM\SOFTWARE\AppStartTest
- [ ] 2.3 Austauschdateien in C:\ProgramData\AppStartTest
- [ ] 2.4 Zustandsanzeige (Konto, Admin, Registry, Dateien) und Knopf «Eingabeaufforderung starten»

## 3. Core
- [ ] 3.1 Konfigurationsmodell apps.json, Validierung AppId, Prüfung der Datei-Rechte
- [ ] 3.2 Prüfung Signatur (WinVerifyTrust, Thumbprint) und SHA-256
- [ ] 3.3 Pipe-Protokoll START <AppId>, Parser mit Längenlimit

## 4. Dienst
- [ ] 4.1 Worker Service, LocalSystem, Ereignisprotokoll-Quelle AppStart
- [ ] 4.2 Named Pipe mit SDDL, PIPE_REJECT_REMOTE_CLIENTS, FILE_FLAG_FIRST_PIPE_INSTANCE
- [ ] 4.3 Session des Anfragenden, Sperrzeit, Timeout, eine Anfrage pro Session
- [ ] 4.4 Token für appstart-app, Zugriff auf WinSta0\Default, CreateProcessAsUser in der User-Session
- [ ] 4.5 Job-Objekt mit Prozesslimit bei allowChildProcesses false

## 5. Launcher
- [ ] 5.1 Native AOT WinExe, Argument --app, Formatprüfung, Meldung bei nicht erreichbarem Dienst

## 6. Setup
- [ ] 6.1 WPF-Oberfläche nach UI-Entwürfen (MVVM): Mehrfachauswahl, Datenordner und Registry-Key je App
- [ ] 6.2 Konten appstart-app, appstart-user (optional), Gruppe AppStart Users; Anmelderechte verweigern
- [ ] 6.3 apps.json schreiben, Rechte setzen auf ProgramData, Datenordner, Registry-Keys
- [ ] 6.4 Verknüpfungen je App mit Icon der EXE, Originale sichern und ersetzen
- [ ] 6.5 Dienst registrieren und starten
- [ ] 6.6 Befehle add, add-user, remove, list, rehash, verify, uninstall

## 7. Paket und Build
- [ ] 7.1 Velopack MSI per-machine, Auto-Update aus, TestApp im Paket
- [ ] 7.2 Authenticode-Signatur aller Binaries und des MSI
- [ ] 7.3 Azure DevOps Pipeline: Build, Tests, Paket, Signatur

## 8. Tests
- [ ] 8.1 Unit-Tests Core
- [ ] 8.2 Integrationstests auf Windows-VM für alle Scenarios, inkl. Missbrauchsversuche
- [ ] 8.3 Abnahmetest mit der TestApp (7 Schritte im Konzept)
- [ ] 8.4 Mehrbenutzer, Remote Desktop, Sperrbildschirm, Neustart, Update einer Applikation, Deinstallation

## 9. Prüfung nach Serviceeinsatz
- [ ] 9.1 WPF-Prüfansicht nach UI-Entwurf «Prüfbericht», Bericht drucken und speichern
- [ ] 9.2 Startmenü-Eintrag «AppStart – Einrichtung prüfen» mit UAC-Abfrage, Knopf im Setup
- [ ] 9.3 Automatische Prüfung nach Installation, Änderung und Update; Berichte mit Datum und Uhrzeit ablegen

## 10. Benutzerhandbuch
- [ ] 10.1 Handbuch als Markdown in docs/manual/ mit allen Kapiteln laut Konzept
- [ ] 10.2 Bildschirmfotos aus der fertigen Software auf der Test-VM
- [ ] 10.3 PDF-Erzeugung in der Pipeline, PDF im Paket, Startmenü-Eintrag «AppStart – Handbuch»
- [ ] 10.4 Test auf Vollständigkeit: jede Meldung, jeder Prüfpunkt, jeder Befehl im Handbuch
```

## Übergabe an Claude Code

1. Repository `appstart` anlegen und `openspec init` ausführen.
1. Inhalte oben in `openspec/project.md` und `openspec/changes/add-appstart-service/` ablegen.
1. `openspec validate add-appstart-service --strict` muss fehlerfrei durchlaufen.
1. `CLAUDE.md` im Repository:

```markdown
# CLAUDE.md
- Arbeite spec-driven nach openspec/project.md und dem aktiven Change.
- Entwicklung läuft unter Windows. Setup und Dienst nie auf diesem Rechner installieren, nur auf der Test-VM.
- Setze tasks.md der Reihe nach um, hake erledigte Tasks ab.
- Kein Code ohne Anforderung in den Specs. Fehlt etwas, Spec zuerst ergänzen und validieren.
- Spike-Ergebnisse (Abschnitt 1) im Change dokumentieren, bevor Abschnitt 3 beginnt.
- Sicherheitsregeln aus project.md sind nicht verhandelbar.
- Nach Abschluss: alle Tests grün, dann openspec archive add-appstart-service.
```

1. Start-Prompt für Claude Code: «Lies openspec/project.md und den Change add-appstart-service. Validiere ihn. Beginne mit den Spikes in Abschnitt 1 von tasks.md und berichte die Ergebnisse, bevor du weitermachst.»

## Offene Punkte

- Velopack: ob das Setup nach der MSI-Installation zuverlässig mit Admin-Rechten läuft und Updates den Dienst sauber stoppen, ist in der Doku nicht beschrieben. Spike 1.3, Fallback WiX.
- S4U-Anmeldung für das App-Konto: Spike 1.1.
- Liste der betroffenen Applikationen mit Datenordner und Registry-Key: Spike 1.4.
- Code-Signing-Zertifikat für AppStart bereitstellen.
