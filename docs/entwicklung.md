# Entwicklung

## Voraussetzungen

| Werkzeug | Version / Hinweis |
|---|---|
| Windows | 11 x64. WPF, Native AOT, Dienst und MSI lassen sich nur unter Windows bauen und testen. |
| .NET SDK | laut [`global.json`](../global.json) (10.0.100 oder neueres Feature-Band) |
| Git | – |
| Velopack CLI `vpk` | als lokales Tool: `dotnet tool restore` |
| Visual Studio C++-Build-Tools | nur für den Launcher mit Native AOT; ohne sie `Build-Release.ps1 -NoAot` (nur für lokale Tests, nicht für Releases) |
| Test-VM | Windows 10 und 11 x64 mit Snapshot |

> ⚠️ **Setup, Dienst und Integrationstests nie auf dem Entwicklungsrechner ausführen.** Sie legen Konten an, registrieren einen Dienst und ändern Rechte. Nur auf der Test-VM, vor jedem Lauf auf den Snapshot zurücksetzen.

## Bauen und testen

```powershell
dotnet restore AppStart.slnx
dotnet build AppStart.slnx -c Release
dotnet test --solution AppStart.slnx -c Release
```

Die Unit-Tests laufen ohne Admin-Rechte und ändern nichts am System.

## End-to-End-Test auf der Test-VM

Prüft den Dienst auf echtem Windows: Installation, Pipe-Rechte, Start in der Bediener-Session, Job-Objekt, alle Ablehnungsgründe, gefälschte Pipe, Ereignisprotokoll, Deinstallation. Anleitung: [`tests/e2e/README.md`](../tests/e2e/README.md).

## Release bauen

```powershell
./build/Build-Release.ps1 -Version 0.1.0                 # unsigniert
./build/Build-Release.ps1 -Version 0.1.0 -CertificateThumbprint <Thumbprint>   # mit installiertem Zertifikat
```

Ergebnis in `artifacts/release`: `AppStart-<Version>.msi`, `AppStart-Setup.exe`, `AppStart-Handbuch-<Version>.pdf` und `SHA256SUMS.txt`. Das Skript entfernt das per-user-Setup von Velopack bewusst, weil es in einen für Benutzer beschreibbaren Ordner installiert.

Über GitHub Actions:

| Workflow | Auslöser | Ergebnis |
|---|---|---|
| [CI](../.github/workflows/ci.yml) | Push auf `main`, Pull Request | Job «Build and test» auf `windows-latest`: Build, Tests (fehlgeschlagene als Annotationen), Handbuch-HTML (Artefakt `manual-html`). Job «OpenSpec validate» auf `ubuntu-latest`: `openspec validate --all --strict`. Beide sind Pflicht-Checks für `main`. |
| [Release](../.github/workflows/release.yml) | Tag `v*.*.*` | signiertes MSI, AppStart-Setup.exe, Handbuch-PDF und SHA256SUMS als GitHub Release (seit v0.3.0 im Einsatz). Zertifikat aus den Secrets `SIGNING_PFX_BASE64` und `SIGNING_PFX_PASSWORD`. Reguläre Releases ohne Zertifikat schlagen fehl, Prereleases (`v0.1.0-dev.1`) dürfen unsigniert sein. |

## Projektstruktur

```text
src/
  AppStart.Core/        Konfiguration, Integritätsprüfung, Pipe-Protokoll, Konstanten
  AppStart.Service/     Windows-Dienst: Pipe, Prüfung, Start in der Bediener-Session
  AppStart.Launcher/    Native-AOT-EXE hinter den Verknüpfungen, Meldungen
  AppStart.Setup/       Einrichtung (WPF und Kommandozeile) – in Arbeit
  AppStart.TestApp/     Testapplikation für Abnahme und E2E-Tests
tests/                  Unit-Tests je Projekt, e2e/ für die Test-VM
spikes/                 Wegwerf-Code der Machbarkeitsprüfungen, kein Produktcode
build/                  Build-Release.ps1
docs/                   Dokumentation, UI-Entwürfe, Bilder
openspec/               Anforderungen, Entscheide, Aufgaben (spec-driven)
```

## Arbeitsweise: spec-driven mit OpenSpec

Jede Funktion beginnt als Anforderung mit Szenarien, nicht als Code.

1. Anforderung in `openspec/changes/<change>/specs/` ergänzen oder ändern.
2. Entscheid mit Begründung in `design.md` festhalten, wenn er nicht offensichtlich ist.
3. Aufgabe in `tasks.md` umsetzen und abhaken.
4. Mindestens ein automatisierter Test je Anforderung.

Aktiver Change: [`add-appstart-service`](../openspec/changes/add-appstart-service/) mit [Proposal](../openspec/changes/add-appstart-service/proposal.md), [Design](../openspec/changes/add-appstart-service/design.md), [Aufgaben](../openspec/changes/add-appstart-service/tasks.md) und [Spike-Ergebnissen](../openspec/changes/add-appstart-service/spike-results.md).

## Konventionen

- Code und Kommentare Englisch, Dokumentation und Oberflächen Deutsch (Schweizer Rechtschreibung).
- Alle Texte, die Benutzer sehen, stehen zentral in `Texts.cs` des jeweiligen Projekts.
- Jede Meldung, jedes Ereignis und jeder Befehl muss im Benutzerhandbuch vorkommen. Ein Test prüft das (geplant).
- Mit Admin-Rechten nur Code aus Ordnern ausführen, die Bediener nicht beschreiben können. Nie aus Downloads oder `%TEMP%`.
- Keine Geheimnisse im Repository.

## Beitragen

Siehe [CONTRIBUTING.md](../CONTRIBUTING.md).
