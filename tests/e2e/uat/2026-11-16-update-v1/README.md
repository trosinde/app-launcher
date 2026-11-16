# UAT-Protokoll: Update von einer Installation mit apps.json Version 1 (add-allowed-child-processes, Task 7.2)

| | |
|---|---|
| Datum | 16.11.2026, 06:02–06:07 |
| Ausgangsversion | MSI `AppStart-0.2.9.msi`, gebaut aus `main` 9b4021c (vor diesem Change, schreibt apps.json Version 1), SHA-256 `0DA8657B9FFDC699F563BAC095D6BB43F96AF023C6E67BF2AE79A5688251B9A4` |
| Prüfling | MSI `AppStart-0.3.0-uat.4.msi`, SHA-256 `AB8EC01189CA1588EE2038B1CBE069F2295BA287548DF647F90A4BAB95D9EE00`, mit Bootstrapper (wie im [Protokoll zu 7.1](../2026-11-16-child-processes/README.md)) |
| Umgebung | Test-VM Windows 11 Pro 10.0.26300 (Proxmox), Session 1 |
| Ablauf | `tests/e2e/Invoke-Uat.ps1`, Suite `Install` = `tests/e2e/Invoke-InstallTests.ps1 -MsiPath <0.2.9> -MsiNextPath <uat.4> -BootstrapperPath <uat.4>` |
| Ergebnis | **38 PASS, 0 FAIL, 2 SKIP** |
| Bestätigt durch | Claude (automatisierte Auswertung des Protokolls) |

## Ergebnis je Prüfpunkt

| Spec / Szenario | Prüfpunkt | Ergebnis |
|---|---|---|
| Vorbereitung | 0.2.9 still als SYSTEM installiert, TestApp aus `C:\Program Files\AppStartTestApp` freigegeben, `appstart-user`, verify 0, Start als Bediener | PASS (9 Prüfpunkte) |
| installation «Update erhält die Einrichtung» | Update auf 0.3.0: ein Produkt, Dienst neu gestartet aus neuer Datei; apps.json (Hash und SDDL), Konten, Gruppe, Verknüpfungen, Rechte von Datenordner und Registry-Key unverändert; verify 0; Start als Bediener | PASS (7 Prüfpunkte) |
| app-launch «Konfiguration Version 1» | apps.json Version 1 mit `allowChildProcesses: false` → TestApp startet, cmd.exe «Start blockiert durch Job-Objekt», kein cmd.exe als `appstart-app` | PASS |
| installation «Update einer bestehenden Installation» (false) | erste Änderung `rehash --app` → Version 2, `childProcesses: blocked`, kein `allowChildProcesses`; SDDL von apps.json gleich; cmd.exe weiterhin blockiert | PASS (3 Prüfpunkte) |
| app-launch «Konfiguration Version 1» (true) | von Hand Version 1 mit `allowChildProcesses: true` → cmd.exe läuft (wie `all`) | PASS |
| installation «Update einer bestehenden Installation» (true) | erste Änderung → Version 2, `childProcesses: all`; SDDL gleich; cmd.exe läuft weiterhin | PASS (3 Prüfpunkte) |
| Aufräumen | `set-children --mode blocked` → Exit-Code 0 (mit automatischer Prüfung) | PASS |
| installation «Bootstrapper» | Wizard aus dem Programmordner, Abbruch, `/quiet` mit MSI in `ProgramData\AppStart\Installer`, Logs geschützt | PASS (12 Prüfpunkte) |
| msiexec bzw. Bootstrapper bei laufender Installation (1618) | mit gehaltenem `_MSIExecute`-Mutex nicht reproduzierbar, siehe `tests/e2e/README.md` | SKIP (2), manuell M6 |

## Bestätigung

Ein Update von einer Installation mit apps.json Version 1 auf den Prüfling ändert den Schutz nicht. Der Dienst liest Version 1 weiter (`false` wie `blocked`, `true` wie `all`). Die erste Änderung durch das Setup schreibt Version 2 mit demselben Modus und denselben Rechten auf apps.json. Die beiden SKIP-Punkte betreffen nicht diesen Change und bleiben manuell (M6).
