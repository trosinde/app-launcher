## Context

Konzept: `docs/konzept/konzept.md`. Spike-Ergebnisse mit Logs und Begründungen: `spike-results.md`.
Dieses Dokument hält die technischen Entscheide fest, die aus den Spikes folgen und vom Konzept abweichen oder es präzisieren.

## Goals / Non-Goals

**Goals:**
- Freigegebene Applikationen starten für Bediener mit Admin-Rechten, ohne Passwort und ohne UAC.
- Kein gespeichertes Passwort für das App-Konto.
- Installation interaktiv (Wizard) und still (Softwareverteilung) mit demselben MSI.

**Non-Goals:**
- Änderungen an den Applikationen.
- Automatische Updates.

## Decisions

### D1 Token für appstart-app per S4U, Logon-Typ Batch
`LsaRegisterLogonProcess` + `LsaLogonUser` mit `MSV1_0_S4U_LOGON`, Logon-Typ Batch. Liefert sofort ein volles Admin-Token (kein UAC-Split). Kein Passwort, kein DPAPI.
Voraussetzung: `appstart-app` hat «Als Batchauftrag anmelden» (über die Administratoren). `verify` prüft das.
Verworfen: Interactive (von S4U nicht unterstützt, 0xC00000A7), Network (kein Vorteil), Passwort mit DPAPI (unnötig, Geheimnis auf Platte).
Einschränkung: S4U-Token haben keine Netzwerk-Anmeldedaten. Applikationen mit Netzwerkzugriff per Windows-Anmeldung je Applikation prüfen (Spike 1.4).

### D2 Start in der Bediener-Session
`TokenSessionId` = Session des Anfragenden, `LoadUserProfile`, `CreateEnvironmentBlock`, `CreateProcessAsUser` auf `winsta0\default`, suspended, in das Job-Objekt, dann fortsetzen.
Desktop-DACL: Ein kurzlebiger SYSTEM-Hilfsprozess in der Ziel-Session ergänzt `WinSta0` und `Default` um die SID von `appstart-app`. Aus Session 0 ist die Window Station einer anderen Session nicht erreichbar. Unter Windows 11 nicht nötig, bleibt für Windows 10 und ältere Builds.

### D3 Job-Objekt mit ActiveProcessLimit = 1
Bei `allowChildProcesses: false`. Bestätigt: `cmd.exe` scheitert mit ERROR_NOT_ENOUGH_QUOTA. Ohne Job startet `cmd.exe` mit Admin-Rechten, deshalb ist das Job-Objekt Pflicht.

### D4 Paket: Velopack MSI per-machine
`vpk pack --msi --instLocation PerMachine --packTitle AppStart` installiert nach `C:\Program Files\AppStart`, die Rechte werden von Program Files geerbt. Ein neues MSI über ein altes wirkt als Major Upgrade: Uninstall-Hook alt, dann Install-Hook neu.
Verworfen: WiX MSI als Fallback, da Velopack alle Prüfpunkte aus Spike 1.3 erfüllt.

### D5 Hooks nur für den Dienst
Install-Hook registriert und startet den Dienst. Uninstall-Hook stoppt und löscht ihn. Da der Uninstall-Hook auch bei jedem Update läuft, entfernt er nichts weiter. Konten, Gruppe, Verknüpfungen, Rechte und `apps.json` räumt nur `AppStart.Setup.exe uninstall` auf.

### D6 Bootstrapper für die interaktive Installation
Velopack-Hooks dürfen keine Oberfläche zeigen. Gängige Praxis bei Software mit Einrichtungsdialog ist eine Setup.exe, die ein MSI einbettet (wie WiX Burn, InstallShield). Umsetzung als eigener, dünner Bootstrapper:
- `AppStart-Setup.exe`, Native AOT, Manifest `requireAdministrator`, MSI als eingebettete Ressource.
- Installiert das MSI still (`msiexec /i … /qn /l*v`), wertet Exit-Codes aus (0, 3010, 1602, 1618, sonstige) und legt das Log ab.
- Startet danach das **installierte** `C:\Program Files\AppStart\AppStart.Setup.exe` nach Signaturprüfung. Erbt die Admin-Rechte, keine zweite UAC-Abfrage.
- Begründung für die dünne Variante: Eine WPF-Einzeldatei entpackt native Bibliotheken nach `%TEMP%`, das Bediener beschreiben können. Mit Admin-Rechten wäre das ein Einfallstor. Der Bootstrapper selbst lädt nichts nach.
Verworfen: UI aus dem Hook starten (Anti-Pattern, bei Verteilung als SYSTEM unsichtbar), WiX Burn (zweites Paketwerkzeug), MSI-eigene Dialoge (können die Entwürfe nicht abbilden).
Lieferumfang: `AppStart-Setup.exe` (interaktiv) und `AppStart.msi` (still), beide signiert.

### D7 Geprüfte Datei bleibt gesperrt bis zum Start
Der Dienst öffnet die EXE mit `FileShare.Read` (kein Schreiben, kein Löschen/Umbenennen), prüft Signatur (WinVerifyTrust über das Datei-Handle) oder SHA-256 über dasselbe Handle und hält es bis nach `CreateProcessAsUser`. So kann zwischen Prüfung und Start nichts ausgetauscht werden (TOCTOU).
Signatur: Kette muss gültig sein, Thumbprint muss passen (Pinning). Keine Online-Sperrprüfung, weil Arbeitsplatzrechner oft offline sind; das Pinning auf den Thumbprint ersetzt sie.

### D8 Antwortprotokoll
Anfrage `START <AppId>` (ASCII, max. 64 Byte). Antwort `OK` oder `DENIED <grund> <ereignis-id>`, max. 64 Byte. Gründe: `unknown-app`, `integrity`, `config`, `busy`, `error`. Details nur im Ereignisprotokoll, nie an den Client.

### D9 Pipe-Rechte ohne FILE_CREATE_PIPE_INSTANCE
Das SDDL-Beispiel im Konzept gibt der Gruppe `GRGW`. GENERIC_WRITE enthält bei Pipes `FILE_APPEND_DATA` = `FILE_CREATE_PIPE_INSTANCE`: Ein Bediener könnte weitere Server-Instanzen derselben Pipe anlegen und Anfragen anderer abfangen oder Antworten fälschen. Die Gruppe erhält deshalb nur `0x0012018B` = FILE_GENERIC_READ | FILE_WRITE_DATA | FILE_WRITE_ATTRIBUTES (Lesemodus setzen).
SDDL: `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x12018b;;;<SID AppStart Users>)(D;;GA;;;NU)`. Die Pipe wird mit `CreateNamedPipe` direkt angelegt (`FILE_FLAG_FIRST_PIPE_INSTANCE`, `PIPE_REJECT_REMOTE_CLIENTS`, Nachrichtenmodus, Puffer 64 Byte), nicht über die .NET-Klassen, damit alle Flags sichtbar und prüfbar sind.

### D10 Build, Signatur und Release
Ein Skript `build/Build-Release.ps1 -Version x.y.z` baut, testet, publiziert Dienst und TestApp, packt mit `vpk` (lokales Tool, `.config/dotnet-tools.json`) und benennt das MSI `AppStart-<Version>.msi`. Lokal und in GitHub Actions läuft dasselbe Skript; Azure DevOps entfällt.
- Haupt-EXE des Pakets ist `AppStart.Service.exe`; sie enthält die Velopack-Hooks (D5).
- Velopacks per-user `Setup.exe` und `Portable.zip` werden aus dem Release entfernt: sie installieren in einen für Benutzer beschreibbaren Ordner.
- Signatur mit dem eigenen PFX-Zertifikat: Import in `CurrentUser\My` für die Dauer des Builds (Passwort als SecureString), signtool signiert per Thumbprint (`/sha1`), danach wird das Zertifikat wieder entfernt. So steht das Passwort in keiner Befehlszeile und in keinem Log.
- Reguläre Releases ohne Zertifikat schlagen fehl; Prereleases dürfen unsigniert sein.

## Risks / Trade-offs

- [Eigene Installationssteuerung statt Burn] → Exit-Codes, Logs und Abbruch sind in Spec `installation` als Szenarien festgelegt und in 8.5 getestet.
- [Velopack-Verhalten nicht dokumentiert, nur durch Spike belegt] → Installationstests 8.5 bei jedem Velopack-Update wiederholen.
- [S4U ohne Netzwerk-Anmeldedaten] → Prüfung je Applikation in Spike 1.4.
- [Desktop-DACL unter Windows 11 nicht nötig, unter Windows 10 ungeprüft] → Test 8.4 unter Windows 10.
