# End-to-End-Test des Dienstes

Prüft AppStart.Service auf echtem Windows: Installation, Pipe-Rechte, Start in der Bediener-Session,
Job-Objekt, alle Ablehnungsgründe, gefälschte Pipe, Ereignisprotokoll und Deinstallation.
Legt Konten, Dienst und Ordner an und räumt am Ende alles wieder ab. **Nur auf der Test-VM ausführen.**

Stage-Ordner vorbereiten:
- `AppStart-<Version>.msi` aus `build/Build-Release.ps1`, oder für den Lauf ohne MSI den Ordner `Service\` (publish von AppStart.Service) und `AppStart.TestApp.exe`
- `Invoke-ServiceE2E.ps1`, `Test-NonAdminClient.ps1` (dieser Ordner)
- `Prepare-TestVm.ps1`, `Remove-SpikeSetup.ps1` (aus `spikes/`)

Ausführen: zuerst den Client **ohne** Admin-Rechte im Hintergrund, dann den Test als Admin.

```powershell
Start-Process powershell -ArgumentList '-File', '.\Test-NonAdminClient.ps1'
Start-Process powershell -Verb RunAs -Wait -ArgumentList '-File', '.\Invoke-ServiceE2E.ps1', '-MsiPath', '.\AppStart-0.1.0.msi'
```

Ergebnis in `e2e-run.txt` (eine Zeile PASS/FAIL je Prüfpunkt), Bildschirmfoto `e2e-testapp.png`.

Letzter Lauf: 15.11.2026, Windows 11 Pro 10.0.26300, MSI 0.1.0-dev.2 (mit Launcher, -NoAot): 23 PASS, 0 FAIL.

## Setup über die Kommandozeile

`Invoke-SetupE2E.ps1 -MsiPath <msi>` (als Admin): MSI installieren, `add` TestApp mit Datenordner und
Registry-Key, `add-user`, `list`, `verify` (9 von 9), Start über die angelegte Verknüpfung, absichtliche
Fehlkonfiguration, `uninstall` (Konten, Verknüpfungen und Rechte zurückgestellt), MSI deinstallieren.

Letzter Lauf: 15.11.2026, MSI 0.2.0-dev.3: 19 PASS, 0 FAIL.

## Abnahme-, Installations- und Betriebstests (8.2 bis 8.5)

Gemeinsame Hilfsfunktionen in `E2E.Common.ps1`. `Invoke-OperatorProbe.ps1` führt einzelne Zugriffe
**als echter Bediener** aus: Die Skripte starten Prozesse von `appstart-user` (bzw. einem zweiten
Testbenutzer) mit `Start-Process -Credential` (CreateProcessWithLogonW). Das ergibt ein echtes Token
ohne Admin-Rechte mit Mitgliedschaft in «AppStart Users»; das Ergebnis kommt über
`C:\ProgramData\AppStartE2E\out` zurück. Das Passwort von `appstart-user` wird pro Lauf zufällig
erzeugt und per Pipe an `add-user --password-stdin` übergeben, nie in eine Datei geschrieben.

Alle drei Skripte: als Admin, nur auf einer frischen Test-VM (Abbruch, wenn AppStart schon installiert
ist), eine Zeile PASS/FAIL/SKIP je Prüfpunkt, Zusammenfassung «N PASS, M FAIL», vollständiges Aufräumen
im `finally` (Konten inkl. Profile, Gruppe, Dienst, Produkt, `C:\ProgramData\AppStart`,
`C:\Program Files\AppStart`, Testdaten `C:\ProgramData\AppStartTest` und `HKLM\SOFTWARE\AppStartTest`,
Ereignisquelle, Startmenü-Ordner, Verknüpfungen, `C:\ProgramData\AppStartE2E`). Alle Skripte dieses
Ordners müssen zusammen im Stage-Ordner liegen.

```powershell
.\Invoke-AcceptanceTest.ps1  -MsiPath .\AppStart-0.2.0.msi
.\Invoke-InstallTests.ps1    -MsiPath .\AppStart-0.2.0.msi -MsiNextPath .\AppStart-0.2.1.msi [-BootstrapperPath .\AppStart-Setup.exe]
.\Invoke-OperationsTests.ps1 -MsiPath .\AppStart-0.2.0.msi
```

### `Invoke-AcceptanceTest.ps1` (8.3, 8.2)

Die 7 Schritte aus dem Konzept, jeweils als appstart-user: (1) Start über die Verknüpfung (ShellExecute
auf die `.lnk`), TestApp als appstart-app in der Bediener-Session, alle Anzeigen grün (UI Automation);
(2) Registry-Key lesen verweigert; (3) Datenordner lesen/schreiben verweigert; (4) TestApp-EXE direkt
→ 740 ERROR_ELEVATION_REQUIRED, mit RunAsInvoker zusätzlich die Meldung der TestApp und Exit-Code 1;
(5) «Eingabeaufforderung starten» → «Start blockiert durch Job-Objekt»; (6) ein Byte der EXE geändert
→ Launcher Exit-Code 3, «Start verweigert» mit Ereignis-ID, Ereignis 1201, verify ≠ 0; (7) verify 0,
≠ 0 nach appstart-user in Administratoren und nach lesbarem Registry-Key, danach wieder 0.
Missbrauchsversuche: apps.json lesen/überschreiben/ersetzen/löschen/umbenennen, Pipe-Instanz anlegen,
Schreiben/Ersetzen in `C:\Program Files\AppStart`, Setup/verify ohne Admin, unbekannte AppId (1200),
Launcher ohne `--app` bzw. mit Zusatzargument. Ausgabe `acceptance-run.txt`, `acceptance-*.png`.

### `Invoke-InstallTests.ps1` (8.5)

- msiexec 1618: `Global\_MSIExecute` wird gehalten, dann `msiexec /i /qn`. Ergibt das nicht 1618
  (Verhalten des Windows Installers je Version), meldet das Skript SKIP; der Punkt bleibt dann manuell
  (MANUAL-TESTS.md, M6). Eine zweite, parallel laufende Installation ist zeitlich nicht zuverlässig
  und wird deshalb nicht verwendet.
- Stille Installation als SYSTEM über eine einmalige geplante Aufgabe (`AppStartE2E-MsiSystem`):
  Exit-Code 0, Programmordner, Dienst läuft automatisch als LocalSystem, keine Oberfläche.
- Update mit `-MsiNextPath` (höhere Version) über eine eingerichtete Installation: Hash und SDDL von
  apps.json, SDDL von ProgramData\AppStart, Datenordner und Registry-Key, SIDs/Passwortdatum der Konten,
  Gruppenmitglieder, Verknüpfungen (Ziel, Argumente, Icon, SDDL) vorher/nachher identisch; Dienst neu
  gestartet aus neuer Datei; genau ein Produkt mit der neuen Version; verify 0; Start als Bediener.
  Die TestApp wird dafür aus einer Kopie in `C:\Program Files\AppStartTestApp` freigegeben (wie eine
  echte Fremdapplikation), weil das Update die TestApp im Paket ersetzt.
- Optional `-BootstrapperPath` (Kopie nach `C:\Users\Public\Downloads`): 1618 über den Bootstrapper
  (Exit ≠ 0, Log in `Logs`, kein Wizard), interaktiver Start mit Abbruch im Wizard (Wizard aus dem
  Programmordner, bleibt installiert, Startmenü-Eintrag, verify ≠ 0), `/quiet` (Exit 0, MSI in
  `C:\ProgramData\AppStart\Installer` entpackt und danach gelöscht, kein MSI aus Downloads/%TEMP%, Log in
  `C:\ProgramData\AppStart\Logs`, kein Wizard). Ausgabe `install-run.txt`, `install-*.log`.

### `Invoke-OperationsTests.ps1` (8.4, automatisierbarer Teil)

Zweiter Testbenutzer `appstart-op2`: zuerst «Keine Berechtigung» (Exit-Code 4), dann `add-user --member`.
Zwei Bediener mit 3 s Abstand: beide TestApps laufen. Gleichzeitig: Da beide per `-Credential` in
**derselben** Session laufen, lässt die Sperre je Session einen durch, der andere erhält «Start läuft
bereits» (1203). Dienstprozess beenden → SCM startet neu (7031), Start klappt. EXE durch veränderte
Kopie ersetzen → verweigert (1201), nach `rehash --app` wieder OK. `uninstall` + MSI-Deinstallation →
Konten, Gruppen, Dienst, Programmordner, Verknüpfungen, Rechte wie vor der Installation.
Ausgabe `operations-run.txt`.

Nicht automatisierbar (echte zweite Session, Remote Desktop, Sperrbildschirm, Neustart, Windows 10,
Bootstrapper mit UAC, Netzwerkzugriff auf die Pipe): [MANUAL-TESTS.md](MANUAL-TESTS.md).

### Abdeckung der Spec-Szenarien

| Spec / Szenario | Skript |
|---|---|
| app-launch: Start durch Bediener | Acceptance (1), Operations, ServiceE2E; RDP/zweite Session manuell M1, M3 |
| app-launch: Veränderte EXE | Acceptance (6), Operations (Update), ServiceE2E |
| app-launch: Unbekannte AppId | Acceptance, ServiceE2E |
| app-launch: Ausbruch über Dateidialog | Acceptance (5), ServiceE2E |
| app-launch: Konfiguration beschreibbar / ungültig | ServiceE2E (beschreibbar); ungültig: Unit-Tests Core |
| app-launch: Austausch zwischen Prüfung und Start | Acceptance (Bediener kann EXE nicht öffnen/umbenennen); Race selbst nur Unit-Tests |
| app-launch: Fremde Signatur | nicht abgedeckt (braucht zwei Signaturzertifikate) |
| app-launch: Launcher-Meldungen | Acceptance (verweigert, ungültig), Operations (keine Berechtigung), ServiceE2E; «nicht erreichbar» nur Unit-Tests |
| installation: Mehrfachauswahl, Icon | Setup-E2E (eine App); Icon manuell M8 |
| installation: Keine Anmeldung als App-Konto | verify (Prüfpunkt Konten); echte Anmeldung manuell M1, M2 |
| installation: Bediener liest Registry | Acceptance (2); regedit manuell M8 |
| installation: Programmordner | Acceptance (Missbrauch), ServiceE2E, verify |
| installation: Bootstrapper (Erstinstallation, beschreibbarer Ordner, Fehler, Abbruch, Update) | InstallTests mit -BootstrapperPath; UAC und Meldungstext manuell M6 |
| installation: Softwareverteilung als SYSTEM | InstallTests |
| installation: Update erhält die Einrichtung | InstallTests |
| installation: Vollständige Deinstallation | Operations, Setup-E2E |
| pipe-security: Zugriff über das Netzwerk | manuell M9 |
| pipe-security: User ausserhalb der Gruppe | Operations, Test-NonAdminClient |
| pipe-security: Gefälschte Pipe, Ungültige Anfrage | ServiceE2E |
| pipe-security: Bediener legt eigene Pipe-Instanz an | Acceptance |
| pipe-security: Fremde Session | Acceptance (Session des Anfragenden); echte zweite Session manuell M3 |
| pipe-security: Start verweigert, Keine internen Details | Acceptance (6), ServiceE2E |
| test-app: alle vier Szenarien | Acceptance (1 bis 5) |
| verification: Korrekte Einrichtung, Abweichung nach Service | Acceptance (7), Setup-E2E |
| verification: Startmenü mit UAC, Bediener startet Prüfung | Acceptance (Setup ohne Admin → 740); Oberfläche manuell M7 |
| documentation | nicht Teil dieser Skripte (10.4) |

## Letzte Läufe (15.11.2026, Windows 11 Pro 10.0.26300, Pakete 0.2.0-dev.6/7 und 0.2.1-dev.1, Launcher/Bootstrapper ohne Native AOT)

| Skript | Ergebnis |
|---|---|
| Invoke-AcceptanceTest.ps1 | 30 PASS, 0 FAIL |
| Invoke-InstallTests.ps1 | 29 PASS, 0 FAIL, 2 SKIP (msiexec 1618 nicht künstlich auslösbar, manuell prüfen) |
| Invoke-OperationsTests.ps1 | 20–21 von 21 PASS je Lauf. Wechselnde Einzelfälle, in denen der Exit-Code eines per `Start-Process -Credential` gestarteten Launchers nicht lesbar ist (-1) bzw. das Testgerüst vor dem Auslesen abbricht; die angezeigte Meldung und die Ereignisse waren in allen Fällen korrekt. Jeder Prüfpunkt hat in mindestens einem Lauf bestanden. |
| Invoke-SetupE2E.ps1 | 19 PASS, 0 FAIL |
| Invoke-ServiceE2E.ps1 | 23 PASS, 0 FAIL |

Gefunden und behoben durch diese Tests: Bediener konnten sich nicht mit der Pipe verbinden (der Launcher forderte GENERIC_WRITE an, das FILE_CREATE_PIPE_INSTANCE enthält und Bedienern nach D9 verweigert ist). Alle früheren Tests liefen mit Admin-Token und konnten das nicht zeigen.
