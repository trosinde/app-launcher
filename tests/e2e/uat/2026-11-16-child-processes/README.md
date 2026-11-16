# UAT-Protokoll: Weitere Programme (add-allowed-child-processes, Task 7.1)

| | |
|---|---|
| Datum | 16.11.2026, 05:45–05:50 (Weitere Programme), 05:34–05:42 (Abnahme und Setup mit uat.3) |
| Prüfling | MSI `AppStart-0.3.0-uat.4.msi` (unsigniert, Launcher ohne Native AOT), SHA-256 `AB8EC01189CA1588EE2038B1CBE069F2295BA287548DF647F90A4BAB95D9EE00`, mit Bootstrapper `AppStart-Setup.exe` |
| Gegenprobe | MSI `AppStart-0.3.0-uat.3.msi`, SHA-256 `C3DF5C328623E28DA6857107155B274D04E5ECAFAF60717F83730E121E756BB1`; unterscheidet sich von uat.4 nur in TestApp und TestHelper (Fehlerzuordnung, Ausgabe-Kodierung) |
| Produktcode | Branch `spec/allowed-child-processes`, Commit mit diesem Protokoll |
| Umgebung | Test-VM Windows 11 Pro 10.0.26300 (Proxmox), Session 1 |
| Ablauf | `tests/e2e/Invoke-Uat.ps1` über die geplante Aufgabe «AppStartUAT»; Suite `ChildProcesses` = `tests/e2e/Invoke-ChildProcessE2E.ps1` (Installation als SYSTEM, TestApp als echter Bediener `appstart-user`) |
| Ergebnis | **Weitere Programme 29 PASS, 0 FAIL** (uat.4 und uat.3); Abnahmetest 30 PASS, 0 FAIL; Setup 19 PASS, 0 FAIL (uat.3) |
| Bestätigt durch | Claude (automatisierte Auswertung der Protokolle) |

## Ergebnis je Prüfpunkt

| Spec / Szenario | Prüfpunkt | Ergebnis |
|---|---|---|
| installation «Softwareverteilung in einem Schritt» | `AppStart-Setup.exe /quiet /config` als SYSTEM (geplante Aufgabe) → Exit-Code 0, TestApp eingerichtet, `Logs\apply-*.log` vorhanden | PASS |
| installation «Einrichtung scheitert» | Antwortdatei mit unbekanntem Feld `password` → Bootstrapper Exit-Code 11 | PASS |
| installation «Zweite Anwendung» | dieselbe Antwortdatei erneut → apps.json unverändert, Exit-Code 0 | PASS |
| installation «Ungültige Datei» | EXE-Pfad existiert nicht → Exit-Code 2, Ausgabe nennt `apps[0].exe`, nichts geändert | PASS |
| app-launch «Modus gesperrt» | TestHelper und cmd.exe: «Start blockiert durch Job-Objekt» | PASS (2 Prüfpunkte) |
| installation «Shell ohne Bestätigung» | `allow-child` cmd.exe ohne `--allow-shell` → Exit-Code 2, apps.json unverändert | PASS |
| installation «Beschreibbarer Ordner» | Hilfsprogramm in `C:\Users\Public` → Exit-Code 1, apps.json unverändert | PASS |
| installation «Hilfsprogramm per Kommandozeile» | `allow-child` TestHelper → Exit-Code 0, Modus allowlist, automatische Prüfung lief | PASS |
| app-launch «Erlaubtes Hilfsprogramm» | TestHelper läuft als `appstart-app` mit Admin-Rechten, Ereignis 1101 | PASS |
| app-launch «Enkelprozess» | cmd.exe aus dem TestHelper wird beendet, Ereignis 1206 | PASS |
| app-launch «Nicht erlaubter Prozess» | cmd.exe aus der TestApp sofort beendet; Ereignis 1206 nennt Benutzer, AppId, Pfad, Grund | PASS |
| app-launch «Veränderte Datei» | ein Byte im TestHelper geändert → beendet, Ereignis nennt «Prüfsumme stimmt nicht» | PASS |
| verification «Hilfsprogramm verändert» | `verify` ≠ 0 mit Pfad und Befehl `rehash --app … --child …`; Original zurück → 0 | PASS (2 Prüfpunkte) |
| app-launch «Applikation endet vor dem Hilfsprogramm» | erlaubtes cmd.exe läuft nach dem Ende der TestApp weiter | PASS |
| app-launch «Dienst stoppt» | `Stop-Service AppStart` → Windows beendet das cmd.exe des Job-Objekts (KILL_ON_JOB_CLOSE) | PASS |
| verification «Alle weiteren Programme erlaubt» | Modus all → `verify` 0 mit Hinweis und AppId | PASS |
| app-launch «Lernmodus» | `learn start` → Exit-Code 0, Ereignis 1400 | PASS |
| verification «Lernmodus vergessen» | `verify` ≠ 0 mit Ablaufzeit und Befehl `learn stop` | PASS |
| app-launch «Aufzeichnung» | cmd.exe läuft im Lernmodus, steht mit Prüfwerten in der Aufzeichnung, Ereignis 1102 | PASS |
| installation «Lernergebnis übernehmen» | `learn apply --all` übernimmt die Shell nicht und beendet den Lernmodus | PASS |
| app-launch «Zeitablauf» | nach Ablauf beendet der Dienst cmd.exe in der laufenden Instanz, Ereignis 1401 | PASS |
| app-launch «Manipulierte Steuerdatei» | für Bediener beschreibbare `learn.json` → ignoriert, Ereignis 1207, cmd.exe beendet | PASS |
| Aufräumen | `learn stop` schützt `learn.json` wieder, `verify` 0; Deinstallation im `finally` | PASS |

Die Vorbereitungsschritte (Bedienerkonto, cmd.exe erlauben und wieder entfernen, zurück zu allowlist) sind als eigene PASS-Zeilen im Lauf enthalten und hier nicht einzeln aufgeführt.

## Befunde auf dem Weg zum Ergebnis

Der erste Lauf (uat.2) ergab 25 PASS, 4 FAIL, der zweite (uat.3) 28 PASS, 1 FAIL. AppStart selbst war in keinem Fall die Ursache:

1. `Invoke-ChildProcessE2E.ps1` und `Invoke-Uat.ps1` waren ohne BOM gespeichert. Windows PowerShell 5.1 liest sie dann als ANSI, und Muster mit «ü» oder «–» trafen nicht (3 FAIL). Beide Dateien sind jetzt UTF-8 mit BOM wie die übrigen Skripte.
2. `$cmd = @(Get-OperatorCmd)` überschrieb `$Cmd` (den Pfad zu cmd.exe), weil PowerShell bei Variablennamen nicht zwischen Gross- und Kleinschreibung unterscheidet. `deny-child` bekam deshalb einen falschen Pfad und endete richtig mit Exit-Code 2. Die Variable heisst jetzt `$cmdProcs`. Ein Unit-Test (`Shell_allowed_and_denied_again_with_the_real_cmd`) bestätigt `allow-child`/`deny-child` mit dem echten cmd.exe.
3. Beim Zeitablauf beendete der Dienst cmd.exe, während `CreateProcess` den Prozess noch einrichtete. Windows meldete dabei `ERROR_PARTIAL_COPY` (299) statt «Zugriff verweigert» (5). Die TestApp und der TestHelper ordnen beide Codes jetzt als «von AppStart beendet» ein. Der Dienst hat sich richtig verhalten, Ereignis 1401 war vorhanden.
4. Die Ausgabe des TestHelpers kam in der Codepage OEM an («l„uft»). Der TestHelper schreibt jetzt UTF-8, die TestApp liest UTF-8.

## Nicht Teil dieser Abnahme

- Task 7.2: Update von einer Installation mit apps.json Version 1.
- Task 1.2: Lernmodus mit den realen Ziel-Applikationen.
- Signierte Pakete; der TestHelper ist unsigniert und deshalb mit SHA-256 auf der Liste.
- Bildschirmfotos: Die Aufnahme des TestApp-Fensters (`child-allowlist.png`) war leer, das Fenster des Bedieners erschien nicht in der Bildschirmaufnahme der Testsitzung. Ausgewertet wurden die Texte der Oberfläche per UI Automation und die Ereignisse.

## Bestätigung

Die Szenarien aus specs/app-launch, specs/installation und specs/verification von add-allowed-child-processes sind für den genannten Prüfling auf der Test-VM bestanden, einschliesslich Dienst-Stopp während laufender Applikation und `/quiet /config` als SYSTEM. Die oben ausgenommenen Punkte sind damit nicht abgenommen.
