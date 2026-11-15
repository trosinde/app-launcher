# AppStart

## Purpose
Startet freigegebene Windows-Applikationen für Standard-User mit Admin-Rechten, ohne Passwort und ohne UAC. Eigenständige Komponente, ändert weder Applikationen noch deren Installer.

## Tech Stack
- C# / .NET 10 LTS, self-contained, x64, Windows 10 und 11
- Dienst: .NET Worker Service (Microsoft.Extensions.Hosting.WindowsServices)
- Win32: CsWin32
- Launcher: Native AOT, WinExe ohne Fenster
- Bootstrapper AppStart-Setup.exe: Native AOT, bettet das MSI ein, startet danach das installierte Setup
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
- Mit Admin-Rechten nur Code aus Ordnern ausführen, die Bediener nicht beschreiben können (Programmordner); nie aus Downloads oder %TEMP%
- Code und Kommentare Englisch, Doku Deutsch
- Jede Anforderung hat mindestens einen automatisierten Test
