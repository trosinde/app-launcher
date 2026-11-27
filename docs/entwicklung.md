# Development

## Prerequisites

| Tool | Version / note |
|---|---|
| Windows | 11 x64. WPF, Native AOT, the service and the MSI can only be built and tested on Windows. |
| .NET SDK | as in [`global.json`](../global.json) (10.0.100 or a newer feature band) |
| Git | – |
| Velopack CLI `vpk` | as a local tool: `dotnet tool restore` |
| Visual Studio C++ build tools | only for the launcher with Native AOT; without them use `Build-Release.ps1 -NoAot` (only for local tests, not for releases) |
| Test VM | Windows 10 and 11 x64 with snapshot |

> ⚠️ **Never run setup, the service or integration tests on the development machine.** They create accounts, register a service and change rights. Only on the test VM, and revert to the snapshot before every run.

## Build and test

```powershell
dotnet restore AppStart.slnx
dotnet build AppStart.slnx -c Release
dotnet test --solution AppStart.slnx -c Release
```

The unit tests run without admin rights and do not change anything on the system.

## End-to-end test on the test VM

Verifies the service on real Windows: installation, pipe rights, start in the operator session, job object, all rejection reasons, fake pipe, Event Log, uninstallation. Instructions: [`tests/e2e/README.md`](../tests/e2e/README.md).

## Build a release

```powershell
./build/Build-Release.ps1 -Version 0.1.0                 # unsigned
./build/Build-Release.ps1 -Version 0.1.0 -CertificateThumbprint <Thumbprint>   # with installed certificate
```

Output in `artifacts/release`: `AppStart-<Version>.msi`, `AppStart-Setup.exe`, `AppStart-Manual-<Version>.pdf` and `SHA256SUMS.txt`. The script deliberately removes Velopack's per-user setup because it installs into a folder that users can write to.

Via GitHub Actions:

| Workflow | Trigger | Result |
|---|---|---|
| [CI](../.github/workflows/ci.yml) | Push to `main`, pull request | Job "Build and test" on `windows-latest`: build, tests (failures as annotations), manual HTML (artifact `manual-html`). Job "OpenSpec validate" on `ubuntu-latest`: `openspec validate --all --strict`. Both are required checks for `main`. |
| [Release](../.github/workflows/release.yml) | Tag `v*.*.*` | Signed MSI, AppStart-Setup.exe, manual PDF and SHA256SUMS as a GitHub release (in use since v0.3.0). Certificate from the secrets `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD`. Regular releases without a certificate fail; prereleases (`v0.1.0-dev.1`) may be unsigned. |

## Project structure

```text
src/
  AppStart.Core/        Configuration, integrity check, pipe protocol, constants
  AppStart.Service/     Windows service: pipe, verification, start in the operator session
  AppStart.Launcher/    Native AOT EXE behind the shortcuts, messages
  AppStart.Setup/       Setup (WPF and command line) – work in progress
  AppStart.TestApp/     Test application for acceptance and E2E tests
tests/                  Unit tests per project, e2e/ for the test VM
spikes/                 Throwaway code from the feasibility studies, not product code
build/                  Build-Release.ps1
docs/                   Documentation, UI designs, images
openspec/               Requirements, decisions, tasks (spec-driven)
```

## Way of working: spec-driven with OpenSpec

Every feature starts as a requirement with scenarios, not as code.

1. Add or change the requirement in `openspec/changes/<change>/specs/`.
2. Record the decision with its rationale in `design.md` if it is not obvious.
3. Implement the task in `tasks.md` and check it off.
4. At least one automated test per requirement.

Active change: [`add-appstart-service`](../openspec/changes/add-appstart-service/) with [proposal](../openspec/changes/add-appstart-service/proposal.md), [design](../openspec/changes/add-appstart-service/design.md), [tasks](../openspec/changes/add-appstart-service/tasks.md) and [spike results](../openspec/changes/add-appstart-service/spike-results.md).

## Conventions

- Code, comments, UI and documentation in English.
- All user-visible texts are kept centrally in the `Texts.cs` of each project.
- Every message, every event and every command must appear in the user manual. A test checks this (planned).
- Run code with admin rights only from folders that operators cannot write to. Never from Downloads or `%TEMP%`.
- No secrets in the repository.

## Contributing

See [CONTRIBUTING.md](../CONTRIBUTING.md).
