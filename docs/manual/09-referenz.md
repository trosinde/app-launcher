# Referenz

## Kommandozeile AppStart.Setup.exe

Pfad: `C:\Program Files\AppStart\current\AppStart.Setup.exe`

```text
AppStart.Setup.exe [Befehl] [Optionen]
```

«AppStart Setup: Applikationen freigeben, Konten einrichten, Einrichtung prüfen. Ohne Befehl öffnet sich der Assistent.»

Allgemeines:

- Das Programm verlangt Admin-Rechte (UAC). Für die Kommandozeile eine Eingabeaufforderung oder PowerShell «Als Administrator ausführen» öffnen; nur dann erscheinen Ausgaben und Passwortabfrage im selben Fenster.
- `AppStart.Setup.exe` ist ein Windows-Programm. In Batch-Dateien wartet `cmd` auf das Ende; in einer interaktiven Eingabeaufforderung `start "" /wait` voranstellen, damit `%ERRORLEVEL%` den Exit-Code enthält. In PowerShell `Start-Process … -Wait -PassThru` verwenden (Beispiel im Kapitel [Installation](03-installation.md)).
- Pfade mit Leerzeichen in Anführungszeichen setzen.
- Ändernde Befehle (`add`, `add-user`, `remove`, `rehash`, `set-children`, `allow-child`, `deny-child`, `learn apply`) führen danach automatisch `verify` aus und legen einen Prüfbericht ab. `learn start` und `learn stop` ändern `apps.json` nicht und prüfen nicht automatisch.

Exit-Codes:

| Code | Bedeutung |
|---|---|
| 0 | Erfolgreich, Prüfung ohne Abweichung |
| 1 | Ein Schritt ist fehlgeschlagen, die Prüfung hat Abweichungen gefunden, oder Befehl bzw. Optionen sind ungültig (z.B. Pflichtoption fehlt) |
| 2 | Ungültige Angabe, nichts wurde geändert: `add-user` (falsche oder fehlende Angabe, Passwort leer oder verschieden), `add --id` ungültig oder vergeben, «nur erlaubte Hilfsprogramme» ohne Eintrag, Shell ohne `--allow-shell`, Prüfung `signature` für eine nicht signierte Datei, Dauer des Lernmodus ausserhalb 5–120, `learn apply` ohne oder mit beiden Angaben `--all`/`--exe`. Assistent: vor der Einrichtung abgebrochen. `startmenu`: weder oder beide Optionen. |

Optionen für alle Befehle:

| Option | Wirkung |
|---|---|
| `--help`, `-h`, `/h`, `-?`, `/?` | Zeigt die Hilfe zum Programm bzw. zum Befehl, z.B. `AppStart.Setup.exe add --help` |
| `--version` | Zeigt die Version von AppStart.Setup |

Übersicht:

| Befehl | Zweck |
|---|---|
| (kein Befehl) | Assistent öffnen |
| `add` | Applikation freigeben oder Freigabe aktualisieren |
| `add-user` | Bedienerkonto anlegen oder Benutzer in die Gruppe aufnehmen |
| `remove` | Freigabe entziehen |
| `rehash` | Nach Update der Applikation Signatur bzw. Prüfsumme neu erfassen |
| `set-children` | Weitere Programme: gesperrt, nur erlaubte Hilfsprogramme oder alle erlaubt |
| `allow-child` | Hilfsprogramm erlauben |
| `deny-child` | Hilfsprogramm entfernen |
| `learn start`, `learn stop`, `learn show`, `learn apply` | Lernmodus |
| `list` | Freigaben, Gruppe und Dienst anzeigen |
| `verify` | Einrichtung prüfen, Prüfbericht ablegen |
| `uninstall` | Alles zurückstellen (vor der MSI-Deinstallation) |
| `startmenu` | Intern: Startmenü-Einträge von AppStart |

### `add`

Gibt eine Applikation frei oder aktualisiert ihre Freigabe.

```text
AppStart.Setup.exe add --exe "<Pfad>" [--data-dir "<Ordner>"] [--reg-key "<Key>"] [--name "<Anzeigename>"] [--id <AppId>] [--child-processes blocked|allowlist|all]
```

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--exe <Pfad>` | ja | Pfad zur EXE der Applikation. |
| `--data-dir <Ordner>` | nein | Datenordner (Austauschdateien), wird für Bediener gesperrt. |
| `--reg-key <Key>` | nein | Registry-Key mit Passwörtern unter HKLM\\, wird für Bediener gesperrt. |
| `--name <Anzeigename>` | nein | Anzeigename (Name der Verknüpfung). |
| `--id <AppId>` | nein | AppId festlegen (a-z, 0-9, -; sonst aus dem Dateinamen). Nur bei einer neuen Freigabe. |
| `--child-processes <Modus>` | nein | Weitere Programme: blocked (gesperrt), allowlist (nur erlaubte Hilfsprogramme) oder all (alle erlaubt). |

Ohne `--name`: Dateibeschreibung, sonst Produktname der EXE, sonst Dateiname. Ist die EXE schon freigegeben, werden Signatur bzw. Prüfsumme neu erfasst und angegebene Werte übernommen; AppId und nicht angegebene Werte bleiben. Meldungen zu `--id` (Exit-Code 2): «AppId «My App» ist ungültig. Erlaubt sind a-z, 0-9 und -, 1 bis 32 Zeichen.», «AppId «app-a» ist bereits vergeben.», «Die Applikation ist bereits als «app-a» freigegeben; die AppId lässt sich nicht ändern (remove, dann add).» `--child-processes allowlist` geht nur, wenn die Applikation schon Hilfsprogramme hat; sonst zuerst `allow-child`.

Beispiele:

```text
AppStart.Setup.exe add --exe "C:\Program Files\Hersteller\AppA\AppA.exe" --data-dir "C:\ProgramData\Hersteller\AppA" --reg-key "HKLM\SOFTWARE\Hersteller\AppA"
AppStart.Setup.exe add --exe "C:\Program Files\Hersteller\AppB\AppB.exe" --name "Applikation B"
```

### `add-user`

Legt das Bedienerkonto appstart-user an oder nimmt einen User in die Gruppe AppStart Users auf.

```text
AppStart.Setup.exe add-user --name appstart-user [--password-stdin]
AppStart.Setup.exe add-user --member "<DOMAIN\Benutzer>"
```

| Option | Beschreibung |
|---|---|
| `--name appstart-user` | Nur appstart-user: legt das lokale Bedienerkonto an. |
| `--member <Konto>` | Lokaler oder Domänen-User (DOMAIN\User) für die Gruppe AppStart Users. |
| `--password-stdin` | Passwort für appstart-user aus einer Zeile der Standardeingabe lesen (stille Installation). |

Genau eine der Optionen `--name` oder `--member` angeben. Ohne `--password-stdin` fragt der Befehl das Passwort zweimal ab. Ein bestehendes `appstart-user` bleibt unverändert. Kontoschreibweisen für `--member`: `DOMAIN\Benutzer`, `.\Benutzer`, `Benutzer`; nur Benutzerkonten.

Beispiele:

```text
AppStart.Setup.exe add-user --name appstart-user
AppStart.Setup.exe add-user --name appstart-user --password-stdin < passwort.txt
AppStart.Setup.exe add-user --member "DOMAIN\bediener01"
AppStart.Setup.exe add-user --member ".\lokalerbenutzer"
```

### `remove`

Entzieht einer Applikation die Freigabe und stellt ihre Verknüpfungen und Rechte zurück.

```text
AppStart.Setup.exe remove --app <AppId>
```

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |

Beispiel: `AppStart.Setup.exe remove --app app-a`

### `rehash`

Erfasst nach einem Update der Applikation Signatur oder Prüfsumme neu.

```text
AppStart.Setup.exe rehash --app <AppId> [--child "<Pfad>"]
```

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |
| `--child <Pfad>` | nein | Nur die Prüfsumme dieses Hilfsprogramms neu erfassen. |

Beispiel: `AppStart.Setup.exe rehash --app appstart-testapp`. Wendet zugleich Schutz und Verknüpfungen aller Applikationen erneut an. Ohne `--child` werden auch die Prüfsummen aller Hilfsprogramme mit SHA-256 neu erfasst; Hilfsprogramme mit Signatur brauchen das nicht.

### `set-children`

Stellt ein, welche weiteren Programme eine Applikation starten darf.

```text
AppStart.Setup.exe set-children --app <AppId> --mode blocked|allowlist|all
```

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |
| `--mode <Modus>` | ja | blocked, allowlist oder all. allowlist braucht mindestens ein Hilfsprogramm. |

Erklärung der Modi: [Applikationen verwalten → Weitere Programme](04-applikationen-verwalten.md). Mit `blocked` oder `all` wird die Liste der Hilfsprogramme verworfen.

### `allow-child`

Erlaubt einer Applikation ein Hilfsprogramm (Modus wird «nur erlaubte Hilfsprogramme»).

```text
AppStart.Setup.exe allow-child --app <AppId> --exe "<Pfad>" [--check signature|sha256] [--allow-shell]
```

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |
| `--exe <Pfad>` | ja | Pfad zur EXE des Hilfsprogramms. |
| `--check <Prüfung>` | nein | Prüfung signature oder sha256 (ohne Angabe: Signatur, wenn gültig signiert, sonst SHA-256). |
| `--allow-shell` | nein | Bestätigt, dass eine Shell oder ein Skript-Host erlaubt werden soll (z.B. cmd.exe, powershell.exe). |

Exit-Code 1, wenn das Programm in einem Ordner liegt, den Bediener beschreiben können; Exit-Code 2 bei einer Shell ohne `--allow-shell`.

### `deny-child`

Entfernt ein Hilfsprogramm aus der Liste (beim letzten wird der Modus wieder «gesperrt»).

```text
AppStart.Setup.exe deny-child --app <AppId> --exe "<Pfad>"
```

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |
| `--exe <Pfad>` | ja | Pfad zur EXE des Hilfsprogramms. |

### `learn`

Lernmodus: zeichnet auf, welche Programme eine Applikation startet, und übernimmt sie als Hilfsprogramme. Ablauf und Hinweise: [Applikationen verwalten → Lernmodus](04-applikationen-verwalten.md).

```text
AppStart.Setup.exe learn start --app <AppId> [--minutes <5–120>]
AppStart.Setup.exe learn stop  --app <AppId>
AppStart.Setup.exe learn show  --app <AppId>
AppStart.Setup.exe learn apply --app <AppId> (--all | --exe "<Pfad>" [--exe "<Pfad>" …])
```

#### learn `start`

Startet den Lernmodus für eine Applikation.

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |
| `--minutes <Minuten>` | nein | Dauer in Minuten (5 bis 120, Standard 30). |

#### learn `stop`

Beendet den Lernmodus sofort.

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |

#### learn `show`

Zeigt, was der Lernmodus aufgezeichnet hat.

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |

Ausgabe:

```text
Lernmodus für «app-a»: aktiv bis 15.11.2026 10:55.
C:\Program Files\Hersteller\AppA\Export.exe
    3 Start(s), Prüfung Signatur, zuletzt 15.11.2026 10:41:07
C:\Windows\System32\cmd.exe – Shell oder Skript-Host
    1 Start(s), Prüfung Signatur, zuletzt 15.11.2026 10:44:10
```

Ohne laufenden Lernmodus erste Zeile «Lernmodus für «app-a»: nicht aktiv.», ohne Aufzeichnung «Nichts aufgezeichnet.»

#### learn `apply`

Übernimmt aufgezeichnete Programme als Hilfsprogramme und löscht die Aufzeichnung.

| Option | Pflicht | Beschreibung |
|---|---|---|
| `--app <AppId>` | ja | AppId der Applikation (siehe list). |
| `--all` | eine von beiden | Alle aufgezeichneten Programme ausser Shells und Skript-Hosts. |
| `--exe <Pfad>` | eine von beiden | Nur diese aufgezeichneten Programme (mehrfach möglich). |

Ohne oder mit beiden Angaben: «Bitte genau eine Angabe machen: --all oder --exe \<Pfad> (mehrfach möglich).», Exit-Code 2. Setzt den Modus auf «nur erlaubte Hilfsprogramme» und beendet einen noch laufenden Lernmodus.

### `list`

Zeigt freigegebene Applikationen, Bediener und Dienststatus.

```text
AppStart.Setup.exe list
```

Keine Optionen. Ausgabe siehe [Applikationen verwalten](04-applikationen-verwalten.md); je Applikation zusätzlich die Zeile «Weitere Programme: …» und darunter die erlaubten Hilfsprogramme mit Prüfung, Shells mit dem Zusatz «– Shell oder Skript-Host». Exit-Code immer 0.

### `verify`

Prüft die Einrichtung und legt einen Prüfbericht ab. Exit-Code 0 nur ohne Abweichung.

```text
AppStart.Setup.exe verify [--report "<Pfad>"] [--ui]
```

| Option | Beschreibung |
|---|---|
| `--report <Pfad>` | Zusätzlicher Speicherort für den Prüfbericht (HTML). |
| `--ui` | Prüfansicht öffnen (Startmenü-Eintrag «AppStart – Einrichtung prüfen»). |

Der Bericht liegt immer unter `C:\ProgramData\AppStart\Reports\verify-<JJJJ-MM-TT-hhmmss>.html` und `.json`; `--report` kopiert die HTML-Datei zusätzlich. Mit `--ui` wird `--report` nicht beachtet; die Prüfansicht bietet «Bericht speichern unter». Prüfpunkte, Ausgabe und Abweichungen: [Prüfung nach Serviceeinsatz](06-pruefung-nach-serviceeinsatz.md).

Beispiele:

```text
AppStart.Setup.exe verify
AppStart.Setup.exe verify --report "E:\Einsatz\appstart-pruefung.html"
AppStart.Setup.exe verify --ui
```

### `uninstall`

Stellt alles zurück, was das Setup geändert hat (vor der Deinstallation des MSI ausführen).

```text
AppStart.Setup.exe uninstall
```

Keine Optionen. Ablauf und Ergebnis: [Update und Deinstallation](08-update-und-deinstallation.md).

### `startmenu`

Intern, in der Hilfe nicht aufgeführt. Wird vom Installationspaket bei Installation, Update und Deinstallation aufgerufen und legt den Startmenü-Ordner «AppStart» unter `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\AppStart` an bzw. entfernt ihn. Von Hand nur nötig, wenn die Einträge fehlen.

```text
AppStart.Setup.exe startmenu --create
AppStart.Setup.exe startmenu --remove
```

| Option | Wirkung |
|---|---|
| `--create` | Legt «AppStart – Einrichtung», «AppStart – Einrichtung prüfen» und «AppStart – Handbuch» an (bestehende werden ersetzt) |
| `--remove` | Löscht den Ordner «AppStart» im Startmenü |

«AppStart – Handbuch» öffnet das mitgelieferte PDF `C:\Program Files\AppStart\current\AppStart-Handbuch.pdf`; der Eintrag entsteht nur, wenn die Datei vorhanden ist.

Genau eine Option angeben, sonst Exit-Code 2.

<a id="appstart-setup-exe"></a>
## Installationsprogramm AppStart-Setup.exe

```text
AppStart-Setup.exe [/quiet | /?]
```

| Parameter | Wirkung |
|---|---|
| (keiner) | Installiert AppStart und startet danach die Einrichtung |
| `/quiet` (auch `/q`, `-quiet`, `--quiet`) | Installiert nur, ohne Oberfläche und ohne Einrichtung. Exit-Code = Exit-Code von `msiexec`. |
| `/?` (auch `/h`, `/help`, `-?`, `-h`, `--help`) | Zeigt die Hilfe. Zusammen mit `/quiet` ungültig. |

Exit-Codes:

| Code | Bedeutung |
|---|---|
| 0 | Installiert (oder dieselbe bzw. neuere Version war schon installiert) und Einrichtung gestartet bzw. mit `/quiet` fertig |
| 3010 | Installiert, Neustart erforderlich |
| 1 | Installiert, aber die Einrichtung konnte nicht gestartet werden |
| 2 | Installiert, aber `AppStart.Setup.exe` fehlt |
| 5 | Ohne Administratorrechte gestartet |
| 87 | Unbekannter Parameter |
| 577 | `AppStart.Setup.exe` nicht vom selben Herausgeber signiert |
| 1602 | Installation abgebrochen |
| 1603 | Installation fehlgeschlagen, `C:\ProgramData\AppStart` nicht geschützt, Entpacken oder Start von `msiexec` gescheitert, unerwarteter Fehler |
| 1618 | Eine andere Installation läuft |
| 1620 | Kein Installationspaket enthalten (Entwicklungs-Build) |
| andere | Exit-Code von `msiexec` |

Protokolle: `C:\ProgramData\AppStart\Logs\install-<JJJJMMTT-hhmmss>.log` (Windows Installer) und `bootstrapper-<JJJJMMTT-hhmmss>.log`. Das MSI wird nur nach `C:\ProgramData\AppStart\Installer\<Guid>` entpackt und danach gelöscht. Meldungen: [Meldungen und Fehlerbehebung](07-meldungen-und-fehlerbehebung.md#installationsprogramm).

## Installationspaket (MSI)

| Aufgabe | Befehl |
|---|---|
| Installieren / aktualisieren | `msiexec /i AppStart-<Version>.msi /qn /l*v <Log-Datei>` |
| Deinstallieren | `msiexec /x AppStart-<Version>.msi /qn` (vorher `AppStart.Setup.exe uninstall`) |

Installiert immer für den ganzen Rechner nach `C:\Program Files\AppStart`. Ein neues MSI über ein altes wirkt als Update. Nach Installation und Update: Dienst registriert und gestartet, Startmenü-Einträge angelegt, automatische Prüfung mit Bericht.

## Launcher AppStart.Launcher.exe

```text
"C:\Program Files\AppStart\current\AppStart.Launcher.exe" --app <AppId>
```

Akzeptiert genau `--app` und eine gültige AppId, sonst «Ungültige Verknüpfung». Sendet `START <AppId>` an den Dienst, wartet höchstens 3 Sekunden auf die Verbindung und 30 Sekunden auf die Antwort. Exit-Codes: 0 gestartet, 1 ungültige Verknüpfung, 2 Dienst nicht erreichbar, 3 verweigert oder läuft bereits, 4 keine Berechtigung, 5 unerwartete Antwort. Meldungen: [Meldungen und Fehlerbehebung](07-meldungen-und-fehlerbehebung.md).

## Soll-Konfiguration apps.json

Pfad: `C:\ProgramData\AppStart\apps.json`. Lesen und schreiben nur SYSTEM und Administratoren (Besitzer Administratoren, Zugriffsliste geschützt, nicht geerbt). Der Dienst liest die Datei bei jeder Startanfrage neu. Das Setup schreibt sie; von Hand nur für `allowChildProcesses` bearbeiten.

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
      "signerThumbprint": "3F2A9C0D8E7B6A5948372615F4E3D2C1B0A99887",
      "sha256": null,
      "allowChildProcesses": false,
      "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
      "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA"
    },
    {
      "id": "appstart-testapp",
      "name": "AppStart TestApp",
      "exe": "C:\\Program Files\\AppStart\\current\\TestApp\\AppStart.TestApp.exe",
      "workingDir": "C:\\Program Files\\AppStart\\current\\TestApp",
      "check": "sha256",
      "signerThumbprint": null,
      "sha256": "9B74C9897BAC770FFC029102A200C5DE6A8FD2D3E5B4C6A1F0E9D8C7B6A59483",
      "allowChildProcesses": false,
      "dataDir": "C:\\ProgramData\\AppStartTest",
      "registryKey": "HKLM\\SOFTWARE\\AppStartTest"
    }
  ]
}
```

| Feld | Pflicht | Regel |
|---|---|---|
| `version` | ja | Immer `1` |
| `apps` | ja | Liste der freigegebenen Applikationen, darf leer sein |
| `id` | ja | AppId: `a-z`, `0-9` und `-`, 1 bis 32 Zeichen, eindeutig. Steht in der Verknüpfung (`--app`). |
| `name` | ja | Anzeigename, nicht leer. Name der Verknüpfung `<name>.lnk`. |
| `exe` | ja | Absoluter lokaler Pfad mit Laufwerksbuchstabe, endet auf `.exe`. Keine Netzwerkpfade, kein `..`. |
| `workingDir` | ja | Arbeitsordner, absoluter lokaler Pfad (Ordner der EXE) |
| `check` | ja | `signature`: gültige Authenticode-Signatur und Zertifikat mit `signerThumbprint` (keine Online-Sperrprüfung). `sha256`: Prüfsumme muss exakt stimmen. |
| `signerThumbprint` | bei `signature` | 40 Hex-Zeichen, sonst `null` |
| `sha256` | bei `sha256` | 64 Hex-Zeichen, sonst `null` |
| `allowChildProcesses` | nein | `false` (Standard): Applikation im Job-Objekt, keine weiteren Programme. `true`: kein Job-Objekt. |
| `dataDir` | nein | Datenordner, absoluter lokaler Pfad, oder `null` |
| `registryKey` | nein | Key unter `HKLM\`, oder `null` |

Unbekannte Felder, Kommentare und Kommas nach dem letzten Element sind nicht erlaubt; die Datei ist dann ungültig.

## Pfade

| Pfad | Inhalt | Rechte |
|---|---|---|
| `C:\Program Files\AppStart` | Programmordner (Installationspaket) | Schreiben nur SYSTEM, Administratoren, TrustedInstaller (von «Program Files» geerbt) |
| `C:\Program Files\AppStart\current\AppStart.Service.exe` | Dienst | wie oben |
| `C:\Program Files\AppStart\current\AppStart.Launcher.exe` | Launcher, Ziel aller Verknüpfungen | wie oben |
| `C:\Program Files\AppStart\current\AppStart.Setup.exe` | Setup (Assistent, Kommandozeile, Prüfung) | wie oben |
| `C:\Program Files\AppStart\current\TestApp\AppStart.TestApp.exe` | Testapplikation | wie oben |
| `C:\ProgramData\AppStart` | Daten von AppStart | Nur SYSTEM und Administratoren |
| `C:\ProgramData\AppStart\apps.json` | Freigaben | Nur SYSTEM und Administratoren |
| `C:\ProgramData\AppStart\setup-state.json` | Was das Setup geändert hat (gesicherte Rechte, angelegte Verknüpfungen), Grundlage für `remove` und `uninstall`. Nicht bearbeiten. | Nur SYSTEM und Administratoren |
| `C:\ProgramData\AppStart\ShortcutBackup` | Gesicherte Verknüpfungen, die direkt auf eine EXE zeigten | Nur SYSTEM und Administratoren |
| `C:\ProgramData\AppStart\Reports` | Prüfberichte `verify-<JJJJ-MM-TT-hhmmss>.html` und `.json` | Nur SYSTEM und Administratoren |
| `C:\ProgramData\AppStart\Logs` | Protokolle des Installationsprogramms | Nur SYSTEM und Administratoren |
| `C:\ProgramData\AppStart\Installer` | Entpacktes MSI während der Installation (danach leer) | Nur SYSTEM und Administratoren |
| `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\AppStart` | Startmenü-Ordner «AppStart»: «AppStart – Einrichtung», «AppStart – Einrichtung prüfen», «AppStart – Handbuch» | Windows-Standard |
| `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\<Name>.lnk` | Verknüpfung je Applikation (öffentliches Startmenü) | Windows-Standard |
| `C:\Users\Public\Desktop\<Name>.lnk` | Verknüpfung je Applikation (öffentlicher Desktop) | Windows-Standard |
| `C:\Users\appstart-app` | Profil des App-Kontos (HKCU der Applikationen) | Nur `appstart-app`, SYSTEM, Administratoren |

<!-- Handbuch: Ablageort des PDF im Paket legt Build-Release.ps1 fest (Startmenü-Eintrag «AppStart – Handbuch»). -->

## Konten und Gruppe

| Name | Art | Rechte |
|---|---|---|
| `appstart-app` | Lokales Konto | Mitglied der Administratoren. `SeDenyInteractiveLogonRight` und `SeDenyRemoteInteractiveLogonRight`. Anmeldung durch den Dienst per S4U (Logon-Typ Batch, Recht über die Administratoren). Zufallspasswort, nirgends gespeichert. |
| `AppStart Users` | Lokale Gruppe | Verbinden mit der Pipe. Mitglieder: `appstart-user` und/oder weitere Benutzer. |
| `appstart-user` | Lokales Standardkonto (optional) | Mitglied von `AppStart Users`, keine Admin-Rechte |

## Dienst

| Eigenschaft | Wert |
|---|---|
| Name / Anzeigename | `AppStart` / AppStart |
| Beschreibung | «Startet freigegebene Applikationen für Bediener mit Admin-Rechten (AppStart).» |
| Programm | `C:\Program Files\AppStart\current\AppStart.Service.exe` |
| Konto | LocalSystem |
| Starttyp | Automatisch |
| Wiederherstellung | Neustart nach 5 s, 5 s, 30 s; Zähler nach 1 Tag zurückgesetzt |
| Ereignisprotokoll | Anwendung, Quelle `AppStart`, IDs 1000–1302 ([Meldungen und Fehlerbehebung](07-meldungen-und-fehlerbehebung.md)) |

Befehle: `sc query AppStart`, `sc start AppStart`, `sc stop AppStart`.

## Named Pipe

| Eigenschaft | Wert |
|---|---|
| Name | `\\.\pipe\AppStart` |
| Zugriffsliste (SDDL) | `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x12018b;;;<SID AppStart Users>)(D;;GA;;;NU)` |
| Bedeutung | Geschützt (`P`, keine geerbten Rechte). SYSTEM (`SY`) und Administratoren (`BA`) Vollzugriff. `AppStart Users` nur `0x0012018B` = Lesen, Daten schreiben, Attribute schreiben; ohne `FILE_CREATE_PIPE_INSTANCE`, damit kein Bediener eine eigene Instanz der Pipe anlegen kann. Netzwerk-Anmeldungen (`NU`) verweigert. |
| Flags | `FILE_FLAG_FIRST_PIPE_INSTANCE` (existiert die Pipe schon, startet der Dienst nicht: Ereignis 1300), `PIPE_REJECT_REMOTE_CLIENTS`, Nachrichtenmodus |
| Instanzen | höchstens 4 gleichzeitig |
| Anfrage | `START <AppId>`, ASCII, höchstens 64 Byte, muss innerhalb von 2 s eintreffen |
| Antwort | `OK` oder `DENIED <Grund> <Vorgangsnummer>`, höchstens 64 Byte |
| Gründe | `unknown-app` (1200), `integrity` (1201), `config` (1202), `busy` (1203), `error` (1205) |
| Begrenzung | Eine Anfrage je Sitzung gleichzeitig, danach 2 s Sperrzeit. Start nur in der Sitzung des Anfragenden. |

Ungültige Anfragen beantwortet der Dienst nicht (Ereignis 1204).
