## 1. Spike und Klärung
- [x] 1.1 S4U-Anmeldung für appstart-app aus dem Dienst prüfen; sonst Passwort mit DPAPI LocalMachine
- [x] 1.2 Anzeige der Applikation in der User-Session (WinSta0\Default) als appstart-app prüfen
- [x] 1.3 Velopack MSI per-machine: Lauf von Setup nach Installation, Dienst stoppen bei Update, Deinstallation; Ergebnis dokumentieren, sonst Fallback WiX MSI vorschlagen
- [ ] 1.4 Je Ziel-Applikation prüfen: läuft sie ohne Kindprozesse, wo liegen Daten und Registry-Key

## 2. Testapplikation (zuerst, dient allen weiteren Tests)
- [x] 2.1 AppStart.TestApp: WPF (MVVM), Manifest requireAdministrator
- [x] 2.2 Registry-Werte inkl. Test-Passwort in HKLM\SOFTWARE\AppStartTest
- [x] 2.3 Austauschdateien in C:\ProgramData\AppStartTest
- [x] 2.4 Zustandsanzeige (Konto, Admin, Registry, Dateien) und Knopf «Eingabeaufforderung starten»

## 3. Core
- [x] 3.1 Konfigurationsmodell apps.json, Validierung AppId, Prüfung der Datei-Rechte
- [x] 3.2 Prüfung Signatur (WinVerifyTrust, Thumbprint) und SHA-256
- [x] 3.3 Pipe-Protokoll START <AppId>, Parser mit Längenlimit

## 4. Dienst
- [x] 4.1 Worker Service, LocalSystem, Ereignisprotokoll-Quelle AppStart
- [x] 4.2 Named Pipe mit SDDL, PIPE_REJECT_REMOTE_CLIENTS, FILE_FLAG_FIRST_PIPE_INSTANCE
- [x] 4.3 Session des Anfragenden, Sperrzeit, Timeout, eine Anfrage pro Session
- [x] 4.4 Token für appstart-app, Zugriff auf WinSta0\Default, CreateProcessAsUser in der User-Session
- [x] 4.5 Job-Objekt mit Prozesslimit bei allowChildProcesses false

## 5. Launcher
- [x] 5.1 Native AOT WinExe, Argument --app, Formatprüfung, Meldung bei nicht erreichbarem Dienst

## 6. Setup
- [x] 6.1 WPF-Oberfläche nach UI-Entwürfen (MVVM): Mehrfachauswahl, Datenordner und Registry-Key je App
- [x] 6.2 Konten appstart-app, appstart-user (optional), Gruppe AppStart Users; Anmelderechte verweigern
- [x] 6.3 apps.json schreiben, Rechte setzen auf ProgramData, Datenordner, Registry-Keys
- [x] 6.4 Verknüpfungen je App mit Icon der EXE, Originale sichern und ersetzen
- [x] 6.5 Dienst registrieren und starten
- [x] 6.6 Befehle add, add-user, remove, list, rehash, verify, uninstall
- [x] 6.7 Wizard erkennt bestehende Konfiguration (Update) und lässt sich nach Abbruch erneut starten; Startmenü-Eintrag «AppStart – Einrichtung»

## 7. Paket und Build
- [x] 7.1 Velopack MSI per-machine (--msi --instLocation PerMachine --packTitle AppStart), Auto-Update aus, TestApp im Paket; Einträge in «Apps und Features» prüfen
- [ ] 7.2 Authenticode-Signatur aller Binaries und des MSI mit dem eigenen PFX-Zertifikat des Herausgebers (build/Build-Release.ps1, Signatur per Thumbprint, Passwort nie in einer Befehlszeile)
- [ ] 7.3 GitHub Actions statt Azure DevOps: CI bei Push/PR (Build, Tests) und Release-Workflow bei Tag v*.*.* (build/Build-Release.ps1, Signatur aus den Secrets SIGNING_PFX_BASE64/SIGNING_PFX_PASSWORD, GitHub Release mit MSI)
- [x] 7.4 Velopack-Hooks: Install registriert und startet den Dienst, Uninstall stoppt und löscht ihn, keine Oberfläche, < 30 s
- [x] 7.5 AppStart.Bootstrapper (Native AOT, requireAdministrator): MSI eingebettet, msiexec still mit Log und Auswertung der Exit-Codes, danach installiertes AppStart.Setup nach Signaturprüfung starten
- [ ] 7.6 Lieferumfang AppStart-Setup.exe (interaktiv) und AppStart.msi (Softwareverteilung), beide signiert

## 8. Tests
- [x] 8.1 Unit-Tests Core
- [ ] 8.2 Integrationstests auf Windows-VM für alle Scenarios, inkl. Missbrauchsversuche
- [ ] 8.3 Abnahmetest mit der TestApp (7 Schritte im Konzept)
- [ ] 8.4 Mehrbenutzer, Remote Desktop, Sperrbildschirm, Neustart, Update einer Applikation, Deinstallation
- [ ] 8.5 Installationstests: Bootstrapper, stille MSI-Installation als SYSTEM, Update über eingerichtete Installation, Abbruch im Wizard, msiexec-Fehlercodes, Windows 10

## 9. Prüfung nach Serviceeinsatz
- [x] 9.1 WPF-Prüfansicht nach UI-Entwurf «Prüfbericht», Bericht drucken und speichern
- [x] 9.2 Startmenü-Eintrag «AppStart – Einrichtung prüfen» mit UAC-Abfrage, Knopf im Setup
- [x] 9.3 Automatische Prüfung nach Installation, Änderung und Update; Berichte mit Datum und Uhrzeit ablegen

## 10. Benutzerhandbuch
- [ ] 10.1 Handbuch als Markdown in docs/manual/ mit allen Kapiteln laut Konzept
- [ ] 10.2 Bildschirmfotos aus der fertigen Software auf der Test-VM
- [ ] 10.3 PDF-Erzeugung in der Pipeline, PDF im Paket, Startmenü-Eintrag «AppStart – Handbuch»
- [ ] 10.4 Test auf Vollständigkeit: jede Meldung, jeder Prüfpunkt, jeder Befehl im Handbuch
