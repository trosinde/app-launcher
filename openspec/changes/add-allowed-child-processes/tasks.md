## 1. Spike
- [x] 1.1 (auf dem Entwicklungsrechner ohne Dienstkontext, siehe spike-results.md; Gegenprobe im Dienstkontext in 7.1) Auf der Test-VM: Job mit Completion Port, TestApp startet Konsolen-Programm und PDF-Anzeige. Prüfen, ob conhost.exe und WerFault.exe als NEW_PROCESS erscheinen, wie lang ein nicht erlaubter Prozess läuft (Zeit bis TerminateProcess messen) und ob KILL_ON_JOB_CLOSE beim Dienst-Stopp greift. Ergebnis in spike-results.md, Entscheidung zu conhost/WerFault in design.md nachtragen
- [ ] 1.2 Je Ziel-Applikation aus Task 1.4 von add-appstart-service: Lernmodus-Ablauf von Hand durchspielen, benötigte Hilfsprogramme notieren

## 2. Core
- [x] 2.1 AppsConfig Version 2: ChildProcessMode (blocked, allowlist, all), AllowedChild (exe, check, signerThumbprint, sha256, allowShell); Validierung gemäss design.md D1; Version 1 lesen und abbilden; Unit-Tests
- [x] 2.2 ExecutableVerifier für AllowedChild wiederverwenden; Liste der Shells und Skript-Hosts als eine Konstante in Core
- [ ] 2.3 LearnState (learn.json) und LearnRecording (Learn\<AppId>.json) mit Serializer und Rechteprüfung wie ConfigStore; Unit-Tests
- [ ] 2.4 Antwortdatei-Modell und strikter Serializer (design.md D5); Unit-Tests für jedes Validierungsfeld

## 3. Dienst
- [ ] 3.1 SessionLauncher: Job je Modus (blocked wie bisher, allowlist mit Completion Port, KILL_ON_JOB_CLOSE, ohne Breakaway)
- [ ] 3.2 JobMonitor: Meldungen lesen, Prozess öffnen, Pfad, Datei sperren und prüfen, beenden; fail-closed; Ende bei ACTIVE_PROCESS_ZERO
- [ ] 3.3 Lernmodus: learn.json je Startanfrage lesen, aufzeichnen statt beenden, Ablauf auch in laufenden Instanzen
- [ ] 3.4 Neue EventIds (Kindprozess erlaubt, beendet mit Grund, Lernmodus Start/Ende, Steuerdatei ungeschützt); Tests für LaunchRequestHandler und JobMonitor mit Fakes

## 4. Setup
- [ ] 4.1 SetupEngine: Modus und Positivliste setzen, Migration Version 1 → 2, Schutz der neuen Dateien in ProtectionService
- [ ] 4.2 Assistent nach docs/ui-designs/Main, Helpers, Learn, LearnResult; Bestätigung bei «Alle erlaubt» und bei Shells; Texte in SetupTexts; Render- und ViewModel-Tests
- [ ] 4.3 Kommandozeile gemäss design.md D4 (add --id/--child-processes, set-children, allow-child, deny-child, rehash --child, learn start|stop|show|apply, list); CliTexts; Tests für jeden Befehl und jeden Exit-Code
- [ ] 4.4 apply --config [--replace] gemäss design.md D5: erst vollständig prüfen, dann schreiben, dann verify; Tests für Idempotenz und ungültige Dateien
- [ ] 4.5 verify und Prüfbericht nach docs/ui-designs/Verify: Hilfsprogramme, Modus all, aktiver Lernmodus

## 5. Bootstrapper
- [ ] 5.1 /config <Pfad> nur zusammen mit /quiet; nach erfolgreichem msiexec AppStart.Setup.exe apply --config ohne Oberfläche, signaturgeprüft, Ausgabe in Logs\apply-<Zeit>.log; Exit-Codes gemäss design.md D6 (10 und 11 neu); Hilfe-Text; Tests in BootstrapperLogic

## 6. TestApp
- [ ] 6.1 Knöpfe «Erlaubtes Hilfsprogramm starten» (eigene signierte Helper-EXE im TestApp-Ordner) und «Eingabeaufforderung starten» zeigen das Ergebnis je Modus

## 7. Tests auf der Test-VM
- [ ] 7.1 E2E (tests/e2e): jedes Szenario aus specs/app-launch, specs/installation und specs/verification, inkl. Dienst-Stopp während laufender Applikation und /quiet /config als SYSTEM
- [ ] 7.2 Update von einer Installation mit apps.json Version 1: Schutz unverändert

## 8. Dokumentation
- [ ] 8.1 Handbuch: 03-installation (Antwortdatei, /config), 04-applikationen-verwalten (Weitere Programme, Lernmodus statt Handbearbeitung), 07-meldungen (neue Ereignisse), 09-referenz (Befehle, apps.json v2, Antwortdatei, Exit-Codes); Bildschirmfotos
- [ ] 8.2 sicherheitsmodell.md und funktionsweise.md: Modus allowlist, Restrisiko aus design.md
- [ ] 8.3 Alle Tests grün. Erst wenn add-appstart-service archiviert ist (damit «Keine Kindprozesse» in openspec/specs steht): openspec archive add-allowed-child-processes
