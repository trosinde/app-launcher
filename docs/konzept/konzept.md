# Concept – launch service "AppStart"

> **Historical document.** Handover at project start. The specs and decisions in the [OpenSpec change](../../openspec/changes/add-appstart-service/) now take precedence (e.g. GitHub Actions instead of Azure DevOps, S4U instead of DPAPI, bootstrapper for the installation). Current overview: [Documentation](../README.md).

AppStart is implemented as a standalone component in its own repository, spec-driven with OpenSpec. Applications and their installers remain unchanged.

## Goal and scope

- Standard users start approved applications with admin rights, without a password and without a UAC prompt.
- Exchange files and passwords in the registry are locked for standard users.
- During installation, one or more applications are selected. Each gets its own shortcut with its own icon.
- Own repository, own release cycle, own installation package.
- Not part of this component: changes to the applications.

## UI designs

Binding template for the user interfaces: [AppStart – UI designs](https://claude.ai/artifact/VSjT6YSVy9BbDpmqEMvwrW)

| Design | Component |
|---|---|
| Setup 1/3 – Select applications (multiple selection, data folder, registry key) | AppStart.Setup |
| Setup 2/3 – Accounts and group | AppStart.Setup |
| Setup 3/3 – Summary and verification | AppStart.Setup |
| Verification report (`verify`) | AppStart.Setup |
| Test application | AppStart.TestApp |
| Messages: service unreachable, start denied | AppStart.Launcher |

## Components

| Component | Type | Task | Runs with |
|---|---|---|---|
| AppStart.Service | Windows service (.NET Worker Service) | Accepts start requests, checks them and starts the application in the user's session | LocalSystem |
| AppStart.Launcher | EXE without a window (Native AOT) | Only sends `START <AppId>` to the service | user rights |
| AppStart.Setup | Configuration tool with WPF user interface and command line | Select applications, write the configuration, set shortcuts and rights, verify the setup (`verify`) | admin rights |
| AppStart.Core | Library | Configuration model, checksum and signature, pipe protocol, shared constants | – |
| Installation package | Velopack, MSI per-machine | Installs everything to `C:\Program Files\AppStart` | admin rights |

## Sequence when an application starts

1. The user clicks the application's shortcut. It points to the launcher with `--app <AppId>` and carries the application's icon.
1. The launcher checks the format of the AppId and sends `START <AppId>` to the service over the named pipe.
1. The service determines the requester's session, looks up the AppId in the list of approved applications and verifies the signature or checksum of the EXE.
1. The service starts the application as the account appstart-app in exactly this session, enclosed in a job object (see Security).
1. Every request is written to the Windows Event Log with time, user, session, AppId and result.

## Technology

| Building block | Decision | Rationale |
|---|---|---|
| Language and runtime | C# on .NET 10 LTS, self-contained | Existing stack. .NET 8 has been out of support since 2026-11-10, .NET 10 is supported until November 2028. No .NET needs to be installed on the customer PC. |
| Service | .NET Worker Service with `Microsoft.Extensions.Hosting.WindowsServices` | Standard way for Windows services in .NET, Event Log built in |
| Windows interfaces | CsWin32 (Microsoft code generator for Win32 calls) | Type-safe calls for token, session, pipe and job object |
| Launcher | Native AOT, WinExe without a window | Starts instantly, no dependencies |
| User interfaces | WPF (.NET 10), MVVM, Fluent theme – fixed | For setup and test application. Implements the UI designs exactly, logic in testable view models |
| Command line | System.CommandLine | Silent installation and verify command for service |
| Installation package | Velopack, as MSI with `--msi --instLocation PerMachine` | Requested tool. Only the per-machine MSI installs to Program Files. License: open source (MIT). |
| Signature | Authenticode with the publisher's code signing certificate for all EXEs and the MSI | Integrity and trust at the customer |
| Tests | xUnit, integration tests on a Windows VM | Service, pipe and rights can only be verified on real Windows |
| Build | Azure DevOps pipeline on a Windows agent | Existing environment |

## Accounts created by the installation

| Account | Type | Purpose | Rights |
|---|---|---|---|
| `appstart-app` | Local account, no interactive logon | The approved applications run under this account | Member of the local Administrators |
| `appstart-user` | Local standard user account | Account for the operators who start the applications through the launcher | No admin rights, member of the group `AppStart Users` |
| `AppStart Users` | Local group | Only members may use the launcher or the pipe. Further local or domain users can be added. | – |

Rules:

- `appstart-app` gets a random password per PC that nobody knows. Interactive logon and logon via Remote Desktop are denied by user right.
- The service preferably obtains the token for `appstart-app` via S4U logon (no stored password). If that is not possible, the password is stored encrypted with DPAPI (LocalMachine) in a file that only SYSTEM can read. To be clarified in the spike, task 1.
- The application therefore does not run as SYSTEM but as `appstart-app`. Its user part of the registry is in this account's own profile and is not readable for operators.
- So that the application appears on the operator's screen, the service gives the account access to `WinSta0\Default` of this session, as `runas` does.
- `appstart-user` is only created if chosen during installation. The installer sets the password. Existing users can be added to the group instead.

## Installation and selection of the applications

1. Install the Velopack MSI per-machine to `C:\Program Files\AppStart`. Never use Velopack's per-user setup: it installs into a folder the user can write to. The user could then replace the service EXE and gain SYSTEM rights.
1. Then `AppStart.Setup` starts with admin rights and shows the selection dialog.
1. The installer selects one or more application EXEs (multiple selection). For each application, they can specify the data folder and the registry key with the passwords.
1. The setup creates the accounts and the group (see above) if they do not exist yet.
1. For each application: assign an AppId, capture the signature or SHA-256, write an entry to `apps.json`.
1. For each application: its own shortcut in the public Start menu and on the public desktop. The target is the launcher with `--app <AppId>`, the icon comes from the application's EXE, the working folder is the application's folder.
1. Existing shortcuts that point directly to the application's EXE are backed up and replaced. They are restored on uninstallation.
1. Lock the data folder and registry key of each application: access only for `appstart-app`, SYSTEM and Administrators.
1. Register the service (automatic start, restart after crash) and start it.
1. Finally `verify` runs automatically. The verification report is saved.

Command line for silent installation and service:

```text
AppStart.Setup.exe add --exe "<path>" [--data-dir "<folder>"] [--reg-key "<key>"] [--name "<display name>"]
AppStart.Setup.exe add-user --name appstart-user | --member "<DOMAIN\User>"
AppStart.Setup.exe remove --app <AppId>
AppStart.Setup.exe list
AppStart.Setup.exe rehash --app <AppId>     # after an update of an application
AppStart.Setup.exe verify [--report "<path>"]
AppStart.Setup.exe uninstall
```

Updates: Velopack's automatic updates stay off. The service technician installs updates with a new MSI. The service is stopped beforehand, `verify` runs afterwards.

## Configuration

File `C:\ProgramData\AppStart\apps.json`, readable and writable only for SYSTEM and Administrators:

```json
{
  "version": 1,
  "apps": [
    {
      "id": "app-a",
      "name": "Application A",
      "exe": "C:\\Program Files\\Vendor\\AppA\\AppA.exe",
      "workingDir": "C:\\Program Files\\Vendor\\AppA",
      "check": "signature",
      "signerThumbprint": "<thumbprint>",
      "sha256": null,
      "allowChildProcesses": false,
      "dataDir": "C:\\ProgramData\\Vendor\\AppA",
      "registryKey": "HKLM\\SOFTWARE\\Vendor\\AppA"
    }
  ]
}
```

- `check`: `signature` if the EXE is signed (survives updates of the application), otherwise `sha256`.
- AppId: lowercase letters, digits and hyphens only, at most 32 characters.
- The service reads the file on every request. If non-admins can write to it, it refuses every start and reports an error.

## Security

**Named pipe** `\\.\pipe\AppStart`:

1. Rights: connect only for the group `AppStart Users`, full access for SYSTEM and Administrators, network explicitly denied. Example SDDL: `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;<SID ``AppStart Users>)(D;;GA;;;NU)`.
1. `PIPE_REJECT_REMOTE_CLIENTS` set.
1. The service creates the pipe at startup as the first instance (`FILE_FLAG_FIRST_PIPE_INSTANCE`). If it already exists, the service does not start and reports an error.
1. Session of the requester via `GetNamedPipeClientSessionId`. Start only in exactly this session.
1. A request has the fixed format `START <AppId>`, at most 64 bytes. Everything else is discarded. One request per session at a time, short lockout time after every start, timeout when reading.

**Starting the application:**

- Only applications from `apps.json`, after successful verification of the signature or checksum.
- Start in a job object. With `allowChildProcesses: false` the application may not start any further programs. This prevents an operator from opening a command prompt with admin rights through a file open dialog. Test for each application whether it works without child processes.

**Locking the registry key with passwords** (part of `AppStart.Setup`, here as a PowerShell reference):

```powershell
$RegKey = "HKLM:\SOFTWARE\Vendor\AppA"
$acl = New-Object System.Security.AccessControl.RegistrySecurity
$acl.SetAccessRuleProtection($true, $false)
$inh = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit
$prop = [System.Security.AccessControl.PropagationFlags]::None
foreach ($id in @("$env:COMPUTERNAME\appstart-app", "NT AUTHORITY\SYSTEM", "BUILTIN\Administrators")) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.RegistryAccessRule($id, "FullControl", $inh, $prop, "Allow")))
}
Set-Acl -Path $RegKey -AclObject $acl
```

**Program folder** `C:\Program Files\AppStart`: writable only for SYSTEM and Administrators.

**Residual risk:** Administrators can still read the passwords.

## Verify command for the Service and Security Manual

`AppStart.Setup.exe verify` verifies and creates a verification report (readable and JSON). Exit code other than 0 on every deviation.

| Check | Expected |
|---|---|
| Service | Installed, running, automatic start, account LocalSystem |
| Program folder | Only SYSTEM and Administrators can write |
| Accounts | `appstart-app` present, no interactive logon; operators without admin rights |
| Pipe | Rights as per SDDL, no remote access |
| `apps.json` | Only SYSTEM and Administrators, every EXE passes verification |
| Data folder per app | Only `appstart-app`, SYSTEM, Administrators |
| Registry key per app | Not readable for operators |
| Shortcuts | Point to the launcher, no direct shortcut to the app EXE |

The Service and Security Manual requires `verify` after the installation and at the end of every service visit. The report is attached to the service visit.

**How to start the verification:**

1. **Start menu (standard way for the service technician):** entry "AppStart – Verify setup" under AppStart. After the UAC prompt with admin credentials, the verification view opens. It shows the result per check, saves the report automatically and offers "Print report" and "Save report as".
1. **Setup user interface:** button "Verify setup", same verification view.
1. **Automatically:** at the end of every installation, every change in the setup and every update.
1. **Command line for scripts:** `AppStart.Setup.exe verify --report "<path>"`, exit code 0 only without deviation.

Operators cannot start the verification because it requires admin rights. Reports are stored under `C:\ProgramData\AppStart\Reports`, one file per verification with date and time.

## User manual (mandatory deliverable)

Part of the development is a complete user manual for installers and service technicians. Without the manual the implementation is not considered complete. It is also the basis for the Service and Security Manual.

| Chapter | Contents |
|---|---|
| Overview | Purpose, how it works in one picture, terms (app account, operator account, group, launcher) |
| Prerequisites | Windows versions, admin rights, required information per application (EXE, data folder, registry key) |
| Installation | Step by step with screenshots of all three setup steps, silent installation |
| Managing applications | Add, remove, verify again after an update of the application (`rehash`) |
| Accounts and group | Create the operator account, add further users to the group |
| Verification after a service visit | Start from the Start menu, read the verification report, every possible deviation with cause and fix, filing the report |
| Messages and troubleshooting | All launcher messages and Event Log entries with meaning and action |
| Update and uninstallation | Procedure, what is restored |
| Reference | All command line commands, expected configuration, paths |

- Source as Markdown in `docs/manual/` in the repository; the pipeline generates a PDF from it.
- The PDF is shipped in the package and is available in the Start menu under "AppStart – Manual".
- Screenshots come from the finished software on the test VM, not from the UI designs.
- Every message, every check and every command of the software appears in the manual. A test checks this against the software's resources.
- Language: German. English version open.

## Test application `AppStart.TestApp`

A small test application that allows AppStart to be verified completely without needing a real application. It behaves like the affected fat clients.

| Property | Implementation |
|---|---|
| Needs admin rights | Manifest `requireAdministrator`. Without admin rights it aborts with a message. |
| Stores in the registry | Writes test values including a test password in plain text to `HKLM\SOFTWARE\AppStartTest` |
| Uses files | Writes and reads exchange files in `C:\ProgramData\AppStartTest` |
| Shows its state | Window shows: running account, admin yes/no, registry read/write ok, file read/write ok |
| Breakout test | Button "Start command prompt" tries to start `cmd.exe`. With a job object this must fail. |
| Signature | Signed like all binaries so that signature verification can be tested |

The test application is shipped with AppStart but not approved automatically. The tester selects it in the setup like any other application, with the data folder and registry key above.

**Acceptance test with the test application:**

1. As `appstart-user`, start via the shortcut: the application runs as `appstart-app`, all indicators green.
1. As `appstart-user`, open the registry key in regedit: access denied.
1. As `appstart-user`, open the data folder in Explorer: access denied.
1. As `appstart-user`, start the TestApp EXE directly: aborts because there are no admin rights.
1. Button "Start command prompt": start fails.
1. Change one byte of the TestApp EXE: start through the launcher is denied and logged.
1. `verify` returns exit code 0, and other than 0 after a deliberate misconfiguration.

## Development and test environment

Development runs on Windows only. User interfaces (WPF), launcher (Native AOT), service, pipe, accounts, rights, MSI and signature can only be built, run and tested on Windows.

| Environment | Purpose | Requirement |
|---|---|---|
| Windows development machine or Windows VM | Claude Code, development, build, unit tests | Windows 11 x64, .NET 10 SDK, Git, OpenSpec CLI, Velopack CLI (`vpk`), Claude Code natively on Windows |
| Separate Windows test VM with snapshot | Installation, integration tests, acceptance test with the TestApp | Windows 10 and 11 x64, never your own workstation, revert to the snapshot before every test |
| Azure DevOps pipeline | Build, tests, package, signature | Windows agent |

The setup creates accounts, registers a service and changes rights. It is therefore run only on the test VM, never on the development machine.

## Repository

```text
appstart/
  CLAUDE.md
  openspec/
    project.md
    changes/add-appstart-service/
      proposal.md
      tasks.md
      specs/app-launch/spec.md
      specs/pipe-security/spec.md
      specs/installation/spec.md
      specs/verification/spec.md
      specs/test-app/spec.md
      specs/documentation/spec.md
  docs/manual/
  src/AppStart.Core/
  src/AppStart.Service/
  src/AppStart.Launcher/
  src/AppStart.Setup/
  src/AppStart.TestApp/
  tests/AppStart.Core.Tests/
  tests/AppStart.IntegrationTests/
  build/velopack/
  azure-pipelines.yml
```

## OpenSpec change `add-appstart-service`

### openspec/project.md

```markdown
# AppStart

## Purpose
Starts approved Windows applications for standard users with admin rights, without a password and without UAC. Standalone component; changes neither the applications nor their installers.

## Tech Stack
- C# / .NET 10 LTS, self-contained, x64, Windows 10 and 11
- Service: .NET Worker Service (Microsoft.Extensions.Hosting.WindowsServices)
- Win32: CsWin32
- Launcher: Native AOT, WinExe without a window
- UI: WPF (MVVM, Fluent theme) for setup and TestApp
- Command line: System.CommandLine
- Package: Velopack MSI, per-machine
- Tests: xUnit, integration tests on a Windows test VM with snapshot
- CI: Azure DevOps, Windows agent

## Environment
- Development, build and tests on Windows 11 x64 only
- Setup, service installation and integration tests only on the separate test VM, never on the development machine

## Conventions
- Security before convenience: every deviation from the expected configuration leads to rejection and an entry in the Event Log
- No paths or free parameters from the launcher, only an AppId from apps.json
- No plain-text passwords, no secrets in the repository
- All binaries Authenticode-signed
- Code and comments in English, documentation in German
- Every requirement has at least one automated test
```

### proposal.md

```markdown
## Why
Several applications need admin rights. That is why operators work as administrators today and can change exchange files and read passwords in the registry. A finding in audits and a deficiency under the EU CRA.

## What Changes
- New Windows service AppStart.Service (LocalSystem) starts approved applications as the account appstart-app in the operator's session
- Launcher without rights, one shortcut per application with its icon
- Setup with multiple selection of the applications; creates accounts, group, configuration, shortcuts and rights
- Verify command for the Service and Security Manual
- Installation package Velopack MSI per-machine

## Impact
- No changes to applications and their installers
- New components: Service, Launcher, Setup, Core, MSI
- Residual risk: administrators can still read passwords
```

### specs/app-launch/spec.md

```markdown
## ADDED Requirements

### Requirement: Start of approved applications
The service SHALL start only applications from apps.json, after successful verification of the signature or SHA-256, as the account appstart-app in the requesting user's session.

#### Scenario: Start by an operator
- **WHEN** a member of AppStart Users starts the shortcut of an approved application
- **THEN** the application runs as appstart-app on their screen, without a password or UAC prompt

#### Scenario: Modified EXE
- **WHEN** the signature or checksum of the EXE does not match
- **THEN** the start is denied and reported in the Event Log

#### Scenario: Unknown AppId
- **WHEN** an AppId is requested that is not in apps.json
- **THEN** the start is denied and logged

### Requirement: No child processes
The service SHALL start applications with allowChildProcesses false in a job object that prevents further processes.

#### Scenario: Breakout via file dialog
- **WHEN** the application tries to start cmd.exe
- **THEN** the start of the child process fails
```

### specs/pipe-security/spec.md

```markdown
## ADDED Requirements

### Requirement: Protected named pipe
The pipe SHALL be reachable only for AppStart Users, accept no remote access, be created as the first instance when the service starts and accept only requests in the format START <AppId> with at most 64 bytes.

#### Scenario: Access over the network
- **WHEN** another computer opens the pipe
- **THEN** the connection is rejected

#### Scenario: User outside the group
- **WHEN** a user without membership in AppStart Users opens the pipe
- **THEN** access is denied

#### Scenario: Fake pipe
- **WHEN** a pipe with the name already exists when the service starts
- **THEN** the service does not start and reports an error

#### Scenario: Invalid request
- **WHEN** a request has a different format or is longer than 64 bytes
- **THEN** it is discarded and logged

#### Scenario: Other session
- **WHEN** a request arrives
- **THEN** the application starts only in the requester's session
```

### specs/installation/spec.md

```markdown
## ADDED Requirements

### Requirement: Selection of the applications
The setup SHALL allow selecting one or more application EXEs, via dialog and via command line.

#### Scenario: Multiple selection
- **WHEN** the installer selects two applications
- **THEN** both are in apps.json and each has its own shortcut

### Requirement: Shortcut per application
The setup SHALL create a shortcut to the launcher with --app <AppId> for each application, with the icon from the application's EXE.

#### Scenario: Icon taken over
- **WHEN** the shortcut has been created
- **THEN** it shows the icon of the selected application

### Requirement: Accounts and group
The setup SHALL create the account appstart-app (admin, no interactive logon), the group AppStart Users and, on request, the standard account appstart-user.

#### Scenario: No logon as the app account
- **WHEN** someone tries to log on as appstart-app interactively or via Remote Desktop
- **THEN** the logon is denied

### Requirement: Rights on data and registry
The setup SHALL restrict the data folder and registry key of each application to appstart-app, SYSTEM and Administrators.

#### Scenario: Operator reads the registry
- **WHEN** an operator opens the registry key in regedit
- **THEN** access is denied

### Requirement: Installation location
The package SHALL install exclusively per-machine to Program Files.

#### Scenario: Program folder
- **WHEN** the installation is complete
- **THEN** only SYSTEM and Administrators can write to the program folder
```

### specs/verification/spec.md

```markdown
## ADDED Requirements

### Requirement: Verify command
verify SHALL check all checks of the expected setup, create a verification report and return an exit code other than 0 on a deviation.

#### Scenario: Correct setup
- **WHEN** verify runs on a correctly set up machine
- **THEN** the exit code is 0 and the report shows all checks as passed

#### Scenario: Deviation after service
- **WHEN** an operator is admin again or the registry key is readable again
- **THEN** verify reports the deviation with an exit code other than 0

### Requirement: Starting the verification
The verification SHALL be startable from a Start menu entry, a button in the setup and the command line, require admin rights and save every report with date and time.

#### Scenario: Service technician after the service visit
- **WHEN** the service technician chooses "AppStart – Verify setup" in the Start menu and confirms the UAC prompt
- **THEN** the verification view shows the result per check and the report is in C:\ProgramData\AppStart\Reports

#### Scenario: Operator starts the verification
- **WHEN** an operator without admin rights tries to start the verification
- **THEN** it runs only after admin credentials are entered
```

### specs/test-app/spec.md

```markdown
## ADDED Requirements

### Requirement: Test application like an affected fat client
AppStart.TestApp SHALL require admin rights, store values including a test password in HKLM\SOFTWARE\AppStartTest, read and write files in C:\ProgramData\AppStartTest and show its state.

#### Scenario: Start without admin rights
- **WHEN** a standard user starts the TestApp EXE directly
- **THEN** it aborts with a message

#### Scenario: Start through the launcher
- **WHEN** a member of AppStart Users starts the TestApp through its shortcut
- **THEN** it shows account appstart-app, admin yes and access to registry and files ok

#### Scenario: Data locked for operators
- **WHEN** the operator opens the TestApp's registry key or data folder directly
- **THEN** access is denied

#### Scenario: Breakout attempt
- **WHEN** "Start command prompt" is chosen in the TestApp
- **THEN** the start of cmd.exe fails
```

### specs/documentation/spec.md

```markdown
## ADDED Requirements

### Requirement: Complete user manual
The release SHALL contain a user manual for installers and service technicians, with installation, management of applications and accounts, verification after a service visit, messages and troubleshooting, update, uninstallation and command line reference.

#### Scenario: Manual in the package
- **WHEN** AppStart is installed
- **THEN** the manual is available as a PDF in the Start menu under AppStart – Manual

#### Scenario: Completeness
- **WHEN** the pipeline builds
- **THEN** a test checks that every message, every check and every command of the software is described in the manual

#### Scenario: Deviation in the verification report
- **WHEN** the verification report shows a deviation
- **THEN** the service technician finds the cause and the fix for exactly this check in the manual
```

### tasks.md

```markdown
## 1. Spike and clarification
- [ ] 1.1 Check S4U logon for appstart-app from the service; otherwise password with DPAPI LocalMachine
- [ ] 1.2 Check display of the application in the user session (WinSta0\Default) as appstart-app
- [ ] 1.3 Velopack MSI per-machine: running the setup after installation, stopping the service on update, uninstallation; document the result, otherwise propose WiX MSI as fallback
- [ ] 1.4 For each target application: does it run without child processes, where are the data and registry key

## 2. Test application (first, serves all further tests)
- [ ] 2.1 AppStart.TestApp: WPF (MVVM), manifest requireAdministrator
- [ ] 2.2 Registry values including test password in HKLM\SOFTWARE\AppStartTest
- [ ] 2.3 Exchange files in C:\ProgramData\AppStartTest
- [ ] 2.4 State display (account, admin, registry, files) and button "Start command prompt"

## 3. Core
- [ ] 3.1 Configuration model apps.json, AppId validation, check of the file rights
- [ ] 3.2 Verification of signature (WinVerifyTrust, thumbprint) and SHA-256
- [ ] 3.3 Pipe protocol START <AppId>, parser with length limit

## 4. Service
- [ ] 4.1 Worker Service, LocalSystem, Event Log source AppStart
- [ ] 4.2 Named pipe with SDDL, PIPE_REJECT_REMOTE_CLIENTS, FILE_FLAG_FIRST_PIPE_INSTANCE
- [ ] 4.3 Session of the requester, lockout time, timeout, one request per session
- [ ] 4.4 Token for appstart-app, access to WinSta0\Default, CreateProcessAsUser in the user session
- [ ] 4.5 Job object with process limit for allowChildProcesses false

## 5. Launcher
- [ ] 5.1 Native AOT WinExe, argument --app, format check, message when the service is unreachable

## 6. Setup
- [ ] 6.1 WPF user interface according to the UI designs (MVVM): multiple selection, data folder and registry key per app
- [ ] 6.2 Accounts appstart-app, appstart-user (optional), group AppStart Users; deny logon rights
- [ ] 6.3 Write apps.json, set rights on ProgramData, data folders, registry keys
- [ ] 6.4 Shortcuts per app with the EXE's icon, back up and replace originals
- [ ] 6.5 Register and start the service
- [ ] 6.6 Commands add, add-user, remove, list, rehash, verify, uninstall

## 7. Package and build
- [ ] 7.1 Velopack MSI per-machine, auto-update off, TestApp in the package
- [ ] 7.2 Authenticode signature of all binaries and the MSI
- [ ] 7.3 Azure DevOps pipeline: build, tests, package, signature

## 8. Tests
- [ ] 8.1 Unit tests Core
- [ ] 8.2 Integration tests on a Windows VM for all scenarios, including misuse attempts
- [ ] 8.3 Acceptance test with the TestApp (7 steps in the concept)
- [ ] 8.4 Multiple users, Remote Desktop, lock screen, restart, update of an application, uninstallation

## 9. Verification after a service visit
- [ ] 9.1 WPF verification view according to the UI design "Verification report", print and save report
- [ ] 9.2 Start menu entry "AppStart – Verify setup" with UAC prompt, button in the setup
- [ ] 9.3 Automatic verification after installation, change and update; save reports with date and time

## 10. User manual
- [ ] 10.1 Manual as Markdown in docs/manual/ with all chapters as in the concept
- [ ] 10.2 Screenshots from the finished software on the test VM
- [ ] 10.3 PDF generation in the pipeline, PDF in the package, Start menu entry "AppStart – Manual"
- [ ] 10.4 Completeness test: every message, every check, every command in the manual
```

## Handover to Claude Code

1. Create the repository `appstart` and run `openspec init`.
1. Put the contents above into `openspec/project.md` and `openspec/changes/add-appstart-service/`.
1. `openspec validate add-appstart-service --strict` must pass without errors.
1. `CLAUDE.md` in the repository:

```markdown
# CLAUDE.md
- Work spec-driven according to openspec/project.md and the active change.
- Development runs on Windows. Never install setup and service on this machine, only on the test VM.
- Implement tasks.md in order, check off completed tasks.
- No code without a requirement in the specs. If something is missing, add it to the spec first and validate.
- Document spike results (section 1) in the change before section 3 starts.
- The security rules in project.md are not negotiable.
- When done: all tests green, then openspec archive add-appstart-service.
```

1. Start prompt for Claude Code: "Read openspec/project.md and the change add-appstart-service. Validate it. Start with the spikes in section 1 of tasks.md and report the results before you continue."

## Open points

- Velopack: whether the setup runs reliably with admin rights after the MSI installation and whether updates stop the service cleanly is not described in the documentation. Spike 1.3, fallback WiX.
- S4U logon for the app account: spike 1.1.
- List of the affected applications with data folder and registry key: spike 1.4.
- Provide a code signing certificate for AppStart.
