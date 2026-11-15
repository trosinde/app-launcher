## Context
Heute (add-appstart-service, D3) startet der Dienst die Applikation bei `allowChildProcesses: false` in einem Job-Objekt mit `ActiveProcessLimit = 1` (SessionLauncher.CreateSingleProcessJob). Bei `true` gibt es kein Job-Objekt. Der Wert lässt sich nur von Hand in apps.json setzen. Viele Ziel-Applikationen brauchen einzelne Hilfsprogramme, siehe offene Task 1.4 in add-appstart-service.

## Goals / Non-Goals
Goals:
- Je Applikation genau festlegen, welche Hilfsprogramme starten dürfen
- Hilfsprogramme ohne Handarbeit ermitteln (Lernmodus)
- Alles per Assistent und per Kommandozeile einstellbar, stille Einrichtung in einem Schritt

Non-Goals:
- Prüfung der Startparameter von Hilfsprogrammen (Kommandozeile fremder Prozesse auslesen ist unzuverlässig)
- Blockieren vor dem Start (bräuchte einen Kernel-Treiber, AppLocker oder WDAC; separater Change)
- Passwörter in der Antwortdatei

## Decisions

### D1 Konfiguration Version 2
Je Applikation ersetzt `childProcesses` das Feld `allowChildProcesses`:

| Wert | Verhalten |
|---|---|
| `blocked` (Standard) | wie heute: Job-Objekt mit ActiveProcessLimit 1 |
| `allowlist` | Job-Objekt mit Überwachung (D2), nur Einträge aus `allowedChildren` |
| `all` | wie heute `true`: kein Job-Objekt; verify nennt es als Hinweis (Ergebnis bleibt Erfüllt) |

```json
{
  "version": 2,
  "apps": [{
    "id": "appa", "name": "Applikation A",
    "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
    "workingDir": "C:\\Program Files\\Hersteller\\AppA",
    "check": "signature", "signerThumbprint": "<40 Hex>",
    "childProcesses": "allowlist",
    "allowedChildren": [
      { "exe": "C:\\Program Files\\Hersteller\\AppA\\Export.exe", "check": "signature", "signerThumbprint": "<40 Hex>" },
      { "exe": "C:\\Program Files\\Hersteller\\AppA\\Updater.exe", "check": "sha256", "sha256": "<64 Hex>" }
    ],
    "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
    "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA"
  }]
}
```

Regeln: `allowedChildren` nur bei `allowlist`, dort mindestens ein Eintrag (sonst ist `blocked` gemeint); jeder Pfad absolut, ohne Platzhalter, eindeutig je Applikation; `check` und Prüfwerte nach denselben Regeln wie bei der Haupt-EXE (ExecutableVerifier wiederverwenden). Der Dienst liest Version 1 weiter (`false` = `blocked`, `true` = `all`). Das Setup schreibt nur noch Version 2 und migriert beim ersten Schreiben.

### D2 Überwachung bei `allowlist`
- Job-Objekt ohne Prozesslimit, mit `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, ohne `BREAKAWAY_OK`. Zuweisung wie heute im Zustand suspended.
- Completion Port per `JobObjectAssociateCompletionPortInformation`. Je `JOB_OBJECT_MSG_NEW_PROCESS`: Prozess öffnen (`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE`), Pfad per `QueryFullProcessImageName`, Datei mit FileShare.Read öffnen (wie LockedExecutable) und gegen den Eintrag prüfen.
- Kein Treffer, Prüfung fehlgeschlagen oder Prozess nicht lesbar: `TerminateProcess` sofort, Warn-Ereignis (Bediener, AppId, Pfad, Grund). Treffer: Info-Ereignis. Fail-closed: jeder Fehler in der Prüfung beendet den Prozess.
- Die Prüfung gilt für jede Tiefe (Enkelprozesse sind im selben Job).
- **conhost.exe ist immer erlaubt** (Spike 1.1): nur genau `%SystemRoot%\System32\conhost.exe`, und nur wenn Datei und Ordner nach WriteAccessPolicy geschützt sind (nur SYSTEM, Administratoren, TrustedInstaller dürfen schreiben). Eine Signaturprüfung entfällt: conhost.exe ist katalogsigniert, die Prüfung der eingebetteten Signatur (ExecutableVerifier) schlägt dafür fehl, und wer System32 beschreiben kann, ist ohnehin Administrator. Kein Eintrag in apps.json, keine Aufzeichnung im Lernmodus, keine Anzeige in verify. Jede andere Datei namens conhost.exe wird wie ein nicht erlaubter Prozess behandelt. Info-Ereignis wie bei einem erlaubten Hilfsprogramm.
- **WerFault.exe bekommt keine Ausnahme** (Spike 1.1): Es erscheint unter Windows 11 nicht im Job, der Fehlerbericht entsteht über den Dienst WerSvc. Erscheint es doch (Windows 10, Gegenprobe 7.1), wird es beendet wie jeder andere Prozess.
- Endet die Applikation, überwacht der Dienst weiter, bis `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO` kommt, und gibt erst dann den Job frei. Erlaubte Hilfsprogramme dürfen also weiterlaufen (z.B. ein Updater).
- Stoppt oder stürzt der Dienst, schliesst Windows den Job-Handle und beendet wegen KILL_ON_JOB_CLOSE alle Prozesse im Job. Kein Zustand ohne Überwachung.
- `blocked` und `all` bleiben unverändert.

### D3 Lernmodus
- Ein Administrator startet ihn im Setup oder per Kommandozeile für eine AppId und eine Dauer (Standard 30, erlaubt 5 bis 120 Minuten).
- Das Setup schreibt dazu `C:\ProgramData\AppStart\learn.json` (AppId, Ablaufzeit UTC) mit denselben Rechten wie apps.json. Kein neuer Pipe-Befehl: die Pipe bleibt für Bediener auf `START <AppId>` beschränkt. Der Dienst liest learn.json bei jeder Startanfrage mit derselben Rechteprüfung wie apps.json; ist die Datei für Bediener beschreibbar, gilt sie als nicht vorhanden und es wird ein Fehler-Ereignis geschrieben.
- Startet die Applikation im Lernmodus, läuft sie im Job aus D2, aber der Dienst beendet nichts. Er zeichnet in `C:\ProgramData\AppStart\Learn\<AppId>.json` je Programm auf: Pfad, Signatur gültig und Thumbprint, SHA-256, Anzahl Starts, erster und letzter Start, Pfad des Elternprozesses.
- Ablauf: Nach der Ablaufzeit gilt bei jedem neuen Prozess wieder der eingestellte Modus, auch in bereits laufenden Instanzen: `allowlist` prüft nach D2, `blocked` beendet jeden weiteren Prozess, `all` lässt jeden zu. Der Lernmodus wirkt in jedem Modus, damit eine gesperrte Applikation ihre Hilfsprogramme lernen kann; solange er aktiv ist, startet der Dienst sie im Job aus D2. Start und Ende des Lernmodus stehen im Ereignisprotokoll (Start und vorzeitiges Ende schreibt das Setup, den Ablauf in einer laufenden Instanz der Dienst).
- Nie vorgeschlagen und im Lernergebnis nicht auswählbar («Shells und Skript-Hosts», eine Liste in Core): cmd.exe, powershell.exe, pwsh.exe, wscript.exe, cscript.exe, mshta.exe, rundll32.exe, regsvr32.exe, explorer.exe. Von Hand eintragbar nur mit Bestätigung (Assistent) bzw. `--allow-shell` (Kommandozeile).
- Vorgeschlagene Prüfung: `signature`, wenn die Datei gültig signiert ist, sonst `sha256`.

### D4 Kommandozeile (AppStart.Setup.exe)
Neu bzw. erweitert, gleiche Exit-Codes wie heute (0 erfolgreich, 1 Fehler oder Abweichung, 2 ungültige Optionen); jede Änderung endet wie heute mit automatischer Prüfung.

| Befehl | Zweck |
|---|---|
| `add --exe <Pfad> … [--id <AppId>] [--child-processes blocked\|allowlist\|all]` | wie heute; `--id` legt die AppId fest (sonst wie heute aus dem Namen), `--child-processes` den Modus |
| `set-children --app <AppId> --mode blocked\|allowlist\|all` | Modus ändern; `allowlist` ohne Einträge ergibt Exit-Code 2 |
| `allow-child --app <AppId> --exe <Pfad> [--check signature\|sha256] [--allow-shell]` | Eintrag hinzufügen; ohne `--check` wie D3; Shells und Skript-Hosts nur mit `--allow-shell`; Ordner für Bediener beschreibbar ergibt Exit-Code 1 |
| `deny-child --app <AppId> --exe <Pfad>` | Eintrag entfernen |
| `rehash --app <AppId> [--child <Pfad>]` | wie heute, aktualisiert zusätzlich die SHA-256-Einträge der Hilfsprogramme bzw. nur den genannten |
| `learn start --app <AppId> [--minutes <5–120>]` | Lernmodus starten |
| `learn stop --app <AppId>` | Lernmodus sofort beenden |
| `learn show --app <AppId>` | Aufzeichnung ausgeben, Shells markiert |
| `learn apply --app <AppId> (--all \| --exe <Pfad>…)` | Vorschläge übernehmen (`--all` ohne Shells), setzt Modus `allowlist`, löscht die Aufzeichnung |
| `list` | zeigt zusätzlich Modus und Hilfsprogramme je Applikation |

`allow-child` stellt den Modus der Applikation auf `allowlist` (sonst gäbe es keinen gültigen Weg von `blocked` zu einer Positivliste, weil `set-children --mode allowlist` ohne Einträge ungültig ist); `deny-child` des letzten Eintrags stellt ihn auf `blocked`. Beides steht in der Ausgabe. Ein schon vorhandener Eintrag mit demselben Pfad wird ersetzt.
| `apply --config <Datei> [--replace]` | Antwortdatei anwenden (D5) |

### D5 Antwortdatei für die stille Einrichtung
`apply --config` setzt den beschriebenen Zustand idempotent: zweimal angewendet ändert nichts. Format JSON, strikt (unbekannte Felder, Kommentare und Kommas am Ende sind Fehler, Exit-Code 2, nichts wird geändert):

```json
{
  "version": 1,
  "apps": [{
    "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
    "id": "appa", "name": "Applikation A",
    "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
    "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA",
    "childProcesses": "allowlist",
    "allowedChildren": [ { "exe": "C:\\Program Files\\Hersteller\\AppA\\Export.exe" } ]
  }],
  "operators": [ "DOMAIN\\Bediener-Gruppe" ]
}
```

- Prüfwerte (Thumbprint, SHA-256) stehen nicht in der Datei, sondern werden auf dem Zielrechner aus den Dateien ermittelt; optional `check` je EXE.
- Applikationen, die nicht in der Datei stehen, bleiben, ausser mit `--replace`.
- Shells in `allowedChildren` nur mit `"allowShell": true` am Eintrag.
- Das lokale Konto appstart-user legt `apply` nicht an (kein Passwort in Dateien); dafür weiter `add-user --name appstart-user --password-stdin`.
- Vorher wird alles validiert (Datei, Pfade, Prüfungen); erst wenn alles gültig ist, wird geschrieben.

### D6 Bootstrapper `/config`
`AppStart-Setup.exe /quiet /config <Pfad>`: installiert wie heute das MSI, startet danach das installierte AppStart.Setup.exe aus dem Programmordner (gleiche Signaturprüfung wie SetupStarter) mit `apply --config <Pfad>` ohne Oberfläche und wartet. `/config` ohne `/quiet` ist ungültig (87). Der Pfad muss absolut sein und darf keine Anführungszeichen enthalten. Exit-Code: msiexec-Fehler → msiexec-Code wie heute. Danach gilt: `apply` 0 → 0 (bzw. 3010, wenn msiexec 3010 lieferte); `apply` 1 → 10 (Einrichtung mit Fehler oder Abweichung); `apply` 2 → 11 (Antwortdatei ungültig). Neue Codes, weil 1 und 2 im Bootstrapper schon belegt sind (Einrichtung nicht startbar, Einrichtung fehlt). Die Ausgabe von `apply` landet in `C:\ProgramData\AppStart\Logs\apply-<Zeitstempel>.log`.

## Risks / Trade-offs
- **Lücke von Millisekunden** (D2): Ein nicht erlaubtes Programm läuft kurz. Gemessen in Spike 1.1: 0,7 bis 4,7 ms von der Erstellung bis zum Ende, einmal 16 ms unter Last; der startende Prozess erhielt jeweils «Zugriff verweigert», bevor `CreateProcess` zurückkehrte. Das ist kein garantiertes Verhalten. Gegen Bediener reicht das praktisch nicht für interaktive Arbeit, ein vorbereitetes Skript könnte aber etwas ausführen. Gegenmassnahmen: Hilfsprogramme nur aus Ordnern, die Bediener nicht beschreiben können (verify); Datenordner der Applikation für Bediener gesperrt; Shells nie automatisch erlaubt. Ist das Restrisiko nicht tragbar: Modus `blocked` oder ein späterer Change mit AppLocker/WDAC.
- **Hilfsprogramme laufen mit Admin-Rechten.** Ein erlaubter PDF-Viewer kann Dateien öffnen und speichern. Der Assistent zeigt dafür einen Hinweis; Handbuch und Sicherheitsmodell erklären die Abwägung.
- **conhost.exe und WerFault.exe**: Geklärt in Spike 1.1. conhost.exe erscheint bei jedem Konsolen-Programm im Job und ist nach D2 immer erlaubt. WerFault.exe erscheint nicht im Job und bekommt keine Ausnahme.
- **Übergabe an Prozesse ausserhalb des Jobs** (neu aus Spike 1.1): Ein Programm im Job kann eine Aufgabe an eine bereits laufende Instanz (im Spike: Edge öffnet eine PDF in der laufenden Instanz) oder an einen COM-Server übergeben, den Windows über DcomLaunch ausserhalb des Jobs startet. Diese Prozesse überwacht weder `blocked` noch `allowlist`. Betroffen sind nur Prozesse des Kontos appstart-app in derselben Session; das Risiko besteht bereits mit add-appstart-service. Prüfung im Dienstkontext in 7.1; eine Gegenmassnahme (z.B. COM-Aktivierung für appstart-app einschränken) wäre ein eigener Change.
- **SHA-256 bei Updates**: Unsignierte Hilfsprogramme werden nach einem Update blockiert. Assistent und verify zeigen das, `rehash` behebt es.

## Migration Plan
Version 1 bleibt lesbar. Beim ersten Schreiben durch das neue Setup wird apps.json auf Version 2 gebracht. Ein Downgrade des Dienstes auf eine Version ohne Version 2 verweigert jeden Start (fail-closed, Ereignis «Konfiguration ungültig»).
