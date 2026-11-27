# Reference

## Command line AppStart.Setup.exe

Path: `C:\Program Files\AppStart\current\AppStart.Setup.exe`

```text
AppStart.Setup.exe [command] [options]
```

"AppStart Setup: approve applications, set up accounts, verify the setup. Without a command the wizard opens."

General:

- The program requires admin rights (UAC). For the command line, open a Command Prompt or PowerShell with "Run as administrator"; only then do output and the password prompt appear in the same window.
- `AppStart.Setup.exe` is a Windows program. In batch files, `cmd` waits for it to finish; in an interactive Command Prompt, prefix `start "" /wait` so that `%ERRORLEVEL%` contains the exit code. In PowerShell, use `Start-Process … -Wait -PassThru` (example in the chapter [Installation](03-installation.md)).
- Put paths with spaces in quotes.
- Changing commands (`add`, `add-user`, `remove`, `rehash`, `set-children`, `allow-child`, `deny-child`, `learn apply`, `apply`) automatically run `verify` afterwards and save a verification report. `learn start` and `learn stop` do not change `apps.json` and do not verify automatically.

Exit codes:

| Code | Meaning |
|---|---|
| 0 | Successful, verification without deviation |
| 1 | A step failed, the verification found deviations, or the command or options are invalid (e.g. a required option is missing) |
| 2 | Invalid input, nothing was changed: `add-user` (wrong or missing input, password empty or not identical), `add --id` invalid or taken, "allowed helper programs only" without an entry, shell without `--allow-shell`, check `signature` for an unsigned file, learning mode duration outside 5–120, `learn apply` with neither or both of `--all`/`--exe`. Wizard: canceled before the setup. `startmenu`: neither or both options. |

Options for all commands:

| Option | Effect |
|---|---|
| `--help`, `-h`, `/h`, `-?`, `/?` | Shows the help for the program or the command, e.g. `AppStart.Setup.exe add --help` |
| `--version` | Shows the version of AppStart.Setup |

Overview:

| Command | Purpose |
|---|---|
| (no command) | Open the wizard |
| `add` | Approve an application or update its approval |
| `add-user` | Create the operator account or add a user to the group |
| `remove` | Revoke an approval |
| `rehash` | Record the signature or checksum again after an update of the application |
| `set-children` | Other programs: blocked, allowed helper programs only, or all allowed |
| `allow-child` | Allow a helper program |
| `deny-child` | Remove a helper program |
| `learn start`, `learn stop`, `learn show`, `learn apply` | Learning mode |
| `apply` | Setup from an answer file (silent installation) |
| `list` | Show approvals, group and service |
| `verify` | Verify the setup, save a verification report |
| `uninstall` | Restore everything (before uninstalling the MSI) |
| `startmenu` | Internal: AppStart's Start menu entries |

### `add`

Approves an application or updates its approval.

```text
AppStart.Setup.exe add --exe "<path>" [--data-dir "<folder>"] [--reg-key "<key>"] [--name "<display name>"] [--id <AppId>] [--child-processes blocked|allowlist|all]
```

| Option | Required | Description |
|---|---|---|
| `--exe <path>` | yes | Path to the EXE of the application. |
| `--data-dir <folder>` | no | Data folder (exchange files), locked for operators. |
| `--reg-key <key>` | no | Registry key with passwords under HKLM\\, locked for operators. |
| `--name <display name>` | no | Display name (name of the shortcut). |
| `--id <AppId>` | no | Set the AppId (a-z, 0-9, -; otherwise from the file name). Only for a new approval. |
| `--child-processes <mode>` | no | Other programs: blocked, allowlist (allowed helper programs only) or all (all allowed). |

Without `--name`: the file description, otherwise the product name of the EXE, otherwise the file name. If the EXE is already approved, the signature or checksum is recorded again and the specified values are applied; the AppId and values not specified remain. Messages for `--id` (exit code 2): "AppId "My App" is invalid. Allowed are a-z, 0-9 and -, 1 to 32 characters.", "AppId "app-a" is already taken.", "The application is already approved as "app-a"; the AppId cannot be changed (remove, then add)." `--child-processes allowlist` only works if the application already has helper programs; otherwise run `allow-child` first.

Examples:

```text
AppStart.Setup.exe add --exe "C:\Program Files\Vendor\AppA\AppA.exe" --data-dir "C:\ProgramData\Vendor\AppA" --reg-key "HKLM\SOFTWARE\Vendor\AppA"
AppStart.Setup.exe add --exe "C:\Program Files\Vendor\AppB\AppB.exe" --name "Application B"
```

### `add-user`

Creates the operator account appstart-user or adds a user to the group AppStart Users.

```text
AppStart.Setup.exe add-user --name appstart-user [--password-stdin]
AppStart.Setup.exe add-user --member "<DOMAIN\User>"
```

| Option | Description |
|---|---|
| `--name appstart-user` | Only appstart-user: creates the local operator account. |
| `--member <account>` | Local or domain user (DOMAIN\User) for the group AppStart Users. |
| `--password-stdin` | Read the password for appstart-user from one line of standard input (silent installation). |

Specify exactly one of the options `--name` or `--member`. Without `--password-stdin`, the command prompts for the password twice. An existing `appstart-user` remains unchanged. Account notations for `--member`: `DOMAIN\User`, `.\User`, `User`; user accounts only.

Examples:

```text
AppStart.Setup.exe add-user --name appstart-user
AppStart.Setup.exe add-user --name appstart-user --password-stdin < password.txt
AppStart.Setup.exe add-user --member "DOMAIN\operator01"
AppStart.Setup.exe add-user --member ".\localuser"
```

### `remove`

Revokes the approval of an application and restores its shortcuts and permissions.

```text
AppStart.Setup.exe remove --app <AppId>
```

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |

Example: `AppStart.Setup.exe remove --app app-a`

### `rehash`

Records the signature or checksum again after an update of the application.

```text
AppStart.Setup.exe rehash --app <AppId> [--child "<path>"]
```

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |
| `--child <path>` | no | Record the checksum of this helper program only. |

Example: `AppStart.Setup.exe rehash --app appstart-testapp`. Also reapplies the protection and shortcuts of all applications. Without `--child`, the checksums of all helper programs are also recorded again with SHA-256; helper programs with a signature do not need this.

### `set-children`

Sets which other programs an application may start.

```text
AppStart.Setup.exe set-children --app <AppId> --mode blocked|allowlist|all
```

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |
| `--mode <mode>` | yes | blocked, allowlist or all. allowlist needs at least one helper program. |

Explanation of the modes: [Managing applications → Other programs](04-managing-applications.md). With `blocked` or `all`, the list of helper programs is discarded.

### `allow-child`

Allows a helper program for an application (mode becomes "allowed helper programs only").

```text
AppStart.Setup.exe allow-child --app <AppId> --exe "<path>" [--check signature|sha256] [--allow-shell]
```

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |
| `--exe <path>` | yes | Path to the EXE of the helper program. |
| `--check <check>` | no | Check signature or sha256 (if omitted: signature if validly signed, otherwise SHA-256). |
| `--allow-shell` | no | Confirms that a shell or a script host is to be allowed (e.g. cmd.exe, powershell.exe). |

Exit code 1 if the program is in a folder that operators can write to; exit code 2 for a shell without `--allow-shell`.

### `deny-child`

Removes a helper program from the list (when the last one is removed, the mode becomes "blocked" again).

```text
AppStart.Setup.exe deny-child --app <AppId> --exe "<path>"
```

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |
| `--exe <path>` | yes | Path to the EXE of the helper program. |

### `learn`

Learning mode: records which programs an application starts and applies them as helper programs. Procedure and notes: [Managing applications → Learning mode](04-managing-applications.md).

```text
AppStart.Setup.exe learn start --app <AppId> [--minutes <5–120>]
AppStart.Setup.exe learn stop  --app <AppId>
AppStart.Setup.exe learn show  --app <AppId>
AppStart.Setup.exe learn apply --app <AppId> (--all | --exe "<path>" [--exe "<path>" …])
```

#### `learn start`

Starts learning mode for an application.

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |
| `--minutes <minutes>` | no | Duration in minutes (5 to 120, default 30). |

#### `learn stop`

Ends learning mode immediately.

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |

#### `learn show`

Shows what learning mode has recorded.

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |

Output:

```text
Learning mode for "app-a": active until 15.11.2026 10:55.
C:\Program Files\Vendor\AppA\Export.exe
    3 start(s), check Signature, last 15.11.2026 10:41:07
C:\Windows\System32\cmd.exe – shell or script host
    1 start(s), check Signature, last 15.11.2026 10:44:10
```

Without a running learning mode, the first line is "Learning mode for "app-a": not active.", without a recording "Nothing recorded."

#### `learn apply`

Applies recorded programs as helper programs and deletes the recording.

| Option | Required | Description |
|---|---|---|
| `--app <AppId>` | yes | AppId of the application (see list). |
| `--all` | one of the two | All recorded programs except shells and script hosts. |
| `--exe <path>` | one of the two | Only these recorded programs (can be repeated). |

With neither or both: "Specify exactly one option: --all or --exe \<path> (can be repeated).", exit code 2. Sets the mode to "allowed helper programs only" and ends a learning mode that is still running.

### `apply`

Sets up AppStart from an answer file (silent installation). Applying it twice changes nothing.

```text
AppStart.Setup.exe apply --config "<file>" [--replace]
```

| Option | Required | Description |
|---|---|---|
| `--config <file>` | yes | Answer file (JSON) with applications, protection, other programs and operators. |
| `--replace` | no | Applications that are not in the answer file are removed. |

The command first checks the whole file. On an error, it changes nothing, lists all errors with their field (e.g. `apps[0].allowedChildren[1].exe`) and ends with exit code 2. If the file is missing: "Answer file … not found." If a helper program is in a folder that operators can write to, it also changes nothing (exit code 1). Then it sets up everything like the wizard and verifies automatically. Without `--replace`, other applications and existing members of the group remain; with `--replace`, only what is in the file applies.

Format of the answer file (JSON, without comments and without trailing commas; unknown fields are errors):

```json
{
  "version": 1,
  "apps": [{
    "exe": "C:\\Program Files\\Vendor\\AppA\\AppA.exe",
    "id": "appa", "name": "Application A",
    "dataDir": "C:\\ProgramData\\Vendor\\AppA",
    "registryKey": "HKLM\\SOFTWARE\\Vendor\\AppA",
    "childProcesses": "allowlist",
    "allowedChildren": [ { "exe": "C:\\Program Files\\Vendor\\AppA\\Export.exe" } ]
  }],
  "operators": [ "DOMAIN\\Operators-Group" ]
}
```

| Field | Required | Content |
|---|---|---|
| `version` | yes | `1` |
| `apps[].exe` | yes | Absolute path to the EXE; the file must exist. |
| `apps[].id`, `name` | no | AppId and display name as with `add --id` and `--name`. |
| `apps[].dataDir`, `registryKey` | no | as with `add` |
| `apps[].check` | no | `signature` or `sha256`; otherwise signature if validly signed. |
| `apps[].childProcesses` | no | `blocked` (default), `allowlist` or `all` |
| `apps[].allowedChildren[]` | with `allowlist` | `exe` (required), `check` (as above), `allowShell: true` for shells and script hosts |
| `operators` | no | Accounts or groups for "AppStart Users" |

Signature thumbprints and checksums are not in the file; the command reads them from the files on the target computer. Passwords do not belong in the file: the local operator account is still created with `add-user --name appstart-user --password-stdin`.

### `list`

Shows approved applications, operators and service status.

```text
AppStart.Setup.exe list
```

No options. For the output, see [Managing applications](04-managing-applications.md); for each application, there is also the line "Other programs: …" and below it the allowed helper programs with their check, shells with the suffix "– shell or script host". Exit code always 0.

### `verify`

Verifies the setup and saves a verification report. Exit code 0 only without deviation.

```text
AppStart.Setup.exe verify [--report "<path>"] [--ui]
```

| Option | Description |
|---|---|
| `--report <path>` | Additional location for the verification report (HTML). |
| `--ui` | Open the verification view (Start menu entry "AppStart – Verify setup"). |

The report is always saved under `C:\ProgramData\AppStart\Reports\verify-<YYYY-MM-DD-hhmmss>.html` and `.json`; `--report` also copies the HTML file. With `--ui`, `--report` is ignored; the verification view offers "Save report as". Checks, output and deviations: [Verification after a service visit](06-verification-after-a-service-visit.md).

Examples:

```text
AppStart.Setup.exe verify
AppStart.Setup.exe verify --report "E:\ServiceVisit\appstart-verification.html"
AppStart.Setup.exe verify --ui
```

### `uninstall`

Restores everything the setup changed (run before uninstalling the MSI).

```text
AppStart.Setup.exe uninstall
```

No options. Procedure and result: [Update and uninstall](08-update-and-uninstall.md).

### `startmenu`

Internal, not listed in the help. Called by the installation package during installation, update and uninstallation; creates or removes the Start menu folder "AppStart" under `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\AppStart`. Only needed by hand if the entries are missing.

```text
AppStart.Setup.exe startmenu --create
AppStart.Setup.exe startmenu --remove
```

| Option | Effect |
|---|---|
| `--create` | Creates "AppStart – Setup", "AppStart – Verify setup" and "AppStart – Manual" (existing ones are replaced) |
| `--remove` | Deletes the folder "AppStart" in the Start menu |

"AppStart – Manual" opens the included PDF `C:\Program Files\AppStart\current\AppStart-Manual.pdf`; the entry is only created if the file exists.

Specify exactly one option, otherwise exit code 2.

<a id="appstart-setup-exe"></a>
## Installer AppStart-Setup.exe

```text
AppStart-Setup.exe [/quiet [/config <answer file>] | /?]
```

| Parameter | Effect |
|---|---|
| (none) | Installs AppStart and then starts the setup |
| `/quiet` (also `/q`, `-quiet`, `--quiet`) | Installs only, without user interface and without setup. Exit code = exit code of `msiexec`. |
| `/config <answer file>` (also `-config`, `--config`) | Only together with `/quiet`: after a successful installation (or if the same version is already installed), it runs the installed, signature-checked `AppStart.Setup.exe apply --config <answer file>` without user interface and waits. The path must be absolute and must not contain quotes. Format of the file: [`apply`](#apply). Output in `C:\ProgramData\AppStart\Logs\apply-<YYYYMMDD-hhmmss>.log`. |
| `/?` (also `/h`, `/help`, `-?`, `-h`, `--help`) | Shows the help. Invalid together with `/quiet`. |

Exit codes:

| Code | Meaning |
|---|---|
| 0 | Installed (or the same or a newer version was already installed) and setup started, or finished with `/quiet` |
| 3010 | Installed, restart required |
| 1 | Installed, but the setup could not be started |
| 2 | Installed, but `AppStart.Setup.exe` is missing |
| 5 | Started without administrator rights |
| 10 | `/config`: installed, but the setup ended with an error or deviation (`apply` exit code 1) |
| 11 | `/config`: installed, but the answer file is invalid, nothing set up (`apply` exit code 2) |
| 87 | Unknown parameter, `/config` without `/quiet`, path missing, relative or containing quotes |
| 577 | `AppStart.Setup.exe` not signed by the same publisher |
| 1602 | Installation canceled |
| 1603 | Installation failed, `C:\ProgramData\AppStart` not protected, extracting or starting `msiexec` failed, unexpected error |
| 1618 | Another installation is in progress |
| 1620 | No installation package included (development build) |
| other | Exit code of `msiexec` |

Logs: `C:\ProgramData\AppStart\Logs\install-<YYYYMMDD-hhmmss>.log` (Windows Installer) and `bootstrapper-<YYYYMMDD-hhmmss>.log`. The MSI is only extracted to `C:\ProgramData\AppStart\Installer\<Guid>` and deleted afterwards. Messages: [Messages and troubleshooting](07-messages-and-troubleshooting.md#installationsprogramm).

## Installation package (MSI)

| Task | Command |
|---|---|
| Install / update | `msiexec /i AppStart-<Version>.msi /qn /l*v <log file>` |
| Uninstall | `msiexec /x AppStart-<Version>.msi /qn` (run `AppStart.Setup.exe uninstall` first) |

Always installs for the whole computer to `C:\Program Files\AppStart`. A new MSI over an old one acts as an update. After installation and update: service registered and started, Start menu entries created, automatic verification with report.

## Launcher AppStart.Launcher.exe

```text
"C:\Program Files\AppStart\current\AppStart.Launcher.exe" --app <AppId>
```

Accepts exactly `--app` and a valid AppId, otherwise "Invalid shortcut". Sends `START <AppId>` to the service, waits at most 3 seconds for the connection and 30 seconds for the response. Exit codes: 0 started, 1 invalid shortcut, 2 service not reachable, 3 denied or already starting, 4 no permission, 5 unexpected response. Messages: [Messages and troubleshooting](07-messages-and-troubleshooting.md).

## Expected configuration apps.json

Path: `C:\ProgramData\AppStart\apps.json`. Only SYSTEM and Administrators can read and write it (owner Administrators, access control list protected, not inherited). The service reads the file again on every start request. The setup writes it; editing it by hand is not necessary, the other programs are also set by the wizard and the command line (`set-children`, `allow-child`, `deny-child`, `learn`).

```json
{
  "version": 2,
  "apps": [
    {
      "id": "app-a",
      "name": "Application A",
      "exe": "C:\\Program Files\\Vendor\\AppA\\AppA.exe",
      "workingDir": "C:\\Program Files\\Vendor\\AppA",
      "check": "signature",
      "signerThumbprint": "3F2A9C0D8E7B6A5948372615F4E3D2C1B0A99887",
      "sha256": null,
      "childProcesses": "allowlist",
      "allowedChildren": [
        { "exe": "C:\\Program Files\\Vendor\\AppA\\Export.exe", "check": "signature", "signerThumbprint": "3F2A9C0D8E7B6A5948372615F4E3D2C1B0A99887" },
        { "exe": "C:\\Program Files\\Vendor\\AppA\\Updater.exe", "check": "sha256", "sha256": "0C1D2E3F405162738495A6B7C8D9EAFB0C1D2E3F405162738495A6B7C8D9EAFB" }
      ],
      "dataDir": "C:\\ProgramData\\Vendor\\AppA",
      "registryKey": "HKLM\\SOFTWARE\\Vendor\\AppA"
    },
    {
      "id": "appstart-testapp",
      "name": "AppStart TestApp",
      "exe": "C:\\Program Files\\AppStart\\current\\TestApp\\AppStart.TestApp.exe",
      "workingDir": "C:\\Program Files\\AppStart\\current\\TestApp",
      "check": "sha256",
      "signerThumbprint": null,
      "sha256": "9B74C9897BAC770FFC029102A200C5DE6A8FD2D3E5B4C6A1F0E9D8C7B6A59483",
      "childProcesses": "blocked",
      "dataDir": "C:\\ProgramData\\AppStartTest",
      "registryKey": "HKLM\\SOFTWARE\\AppStartTest"
    }
  ]
}
```

| Field | Required | Rule |
|---|---|---|
| `version` | yes | `2`. The service still reads version `1` (before other programs existed), see below. |
| `apps` | yes | List of approved applications, may be empty |
| `id` | yes | AppId: `a-z`, `0-9` and `-`, 1 to 32 characters, unique. Used in the shortcut (`--app`). |
| `name` | yes | Display name, not empty. Name of the shortcut `<name>.lnk`. |
| `exe` | yes | Absolute local path with drive letter, ends with `.exe`. No network paths, no `..`. |
| `workingDir` | yes | Working folder, absolute local path (folder of the EXE) |
| `check` | yes | `signature`: valid Authenticode signature and certificate with `signerThumbprint` (no online revocation check). `sha256`: checksum must match exactly. |
| `signerThumbprint` | with `signature` | 40 hex characters, otherwise `null` |
| `sha256` | with `sha256` | 64 hex characters, otherwise `null` |
| `childProcesses` | no | Other programs: `blocked` (default, also if the field is missing): job object, no other programs. `allowlist`: monitored job object, only programs from `allowedChildren`; the service ends all others immediately (event 1206). `all`: no job object; `verify` lists the application as a note. |
| `allowedChildren` | with `allowlist` | Only with `allowlist`, and there at least one entry. Each entry has `exe` (absolute local path ending with `.exe`, without `%`, unique per application), `check` and `signerThumbprint` or `sha256` by the same rules as for the application, optionally `allowShell: true`. |
| `allowShell` | for shells | In an entry of `allowedChildren`: required for `cmd.exe`, `powershell.exe`, `pwsh.exe`, `wscript.exe`, `cscript.exe`, `mshta.exe`, `rundll32.exe`, `regsvr32.exe` and `explorer.exe`; confirms that an operator can use it to start any program with admin rights. |
| `dataDir` | no | Data folder, absolute local path, or `null` |
| `registryKey` | no | Key under `HKLM\`, or `null` |

Unknown fields, comments and commas after the last element are not allowed; the file is then invalid.

**Version 1.** Installations from before other programs existed have `"version": 1` and, per application, `allowChildProcesses` instead of `childProcesses`. The service still reads version 1: `false` (or missing) acts like `blocked`, `true` like `all`. The first change by the setup (e.g. `rehash`, wizard) writes version 2 with the same mode. In version 2, `allowChildProcesses` is an error; in version 1, `childProcesses` and `allowedChildren` are errors.

## Paths

| Path | Content | Permissions |
|---|---|---|
| `C:\Program Files\AppStart` | Program folder (installation package) | Write only SYSTEM, Administrators, TrustedInstaller (inherited from "Program Files") |
| `C:\Program Files\AppStart\current\AppStart.Service.exe` | Service | as above |
| `C:\Program Files\AppStart\current\AppStart.Launcher.exe` | Launcher, target of all shortcuts | as above |
| `C:\Program Files\AppStart\current\AppStart.Setup.exe` | Setup (wizard, command line, verification) | as above |
| `C:\Program Files\AppStart\current\TestApp\AppStart.TestApp.exe` | Test application | as above |
| `C:\ProgramData\AppStart` | AppStart data | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\apps.json` | Approvals | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\learn.json` | Running learning modes (AppId, start, end). Written only by the setup (`learn start`/`stop`). If operators can write to it, the service ignores it (event 1207). | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\Learn\<AppId>.json` | Learning mode recording per application (programs, check values, starts). Deleted after `learn apply` or "Discard". | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\setup-state.json` | What the setup changed (saved permissions, created shortcuts), basis for `remove` and `uninstall`. Do not edit. | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\ShortcutBackup` | Saved shortcuts that pointed directly to an EXE | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\Reports` | Verification reports `verify-<YYYY-MM-DD-hhmmss>.html` and `.json` | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\Logs` | Installer logs | Only SYSTEM and Administrators |
| `C:\ProgramData\AppStart\Installer` | Extracted MSI during the installation (empty afterwards) | Only SYSTEM and Administrators |
| `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\AppStart` | Start menu folder "AppStart": "AppStart – Setup", "AppStart – Verify setup", "AppStart – Manual" | Windows default |
| `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\<Name>.lnk` | Shortcut per application (public Start menu) | Windows default |
| `C:\Users\Public\Desktop\<Name>.lnk` | Shortcut per application (public desktop) | Windows default |
| `C:\Users\appstart-app` | Profile of the app account (HKCU of the applications) | Only `appstart-app`, SYSTEM, Administrators |

<!-- Manual: the location of the PDF in the package is set by Build-Release.ps1 (Start menu entry "AppStart – Manual"). -->

## Accounts and group

| Name | Type | Permissions |
|---|---|---|
| `appstart-app` | Local account | Member of Administrators. `SeDenyInteractiveLogonRight` and `SeDenyRemoteInteractiveLogonRight`. Signed in by the service via S4U (logon type batch, right via Administrators). Random password, not stored anywhere. |
| `AppStart Users` | Local group | Connect to the pipe. Members: `appstart-user` and/or other users. |
| `appstart-user` | Local standard account (optional) | Member of `AppStart Users`, no admin rights |

## Service

| Property | Value |
|---|---|
| Name / display name | `AppStart` / AppStart |
| Description | "Starts approved applications for operators with admin rights (AppStart)." |
| Program | `C:\Program Files\AppStart\current\AppStart.Service.exe` |
| Account | LocalSystem |
| Startup type | Automatic |
| Recovery | Restart after 5 s, 5 s, 30 s; counter reset after 1 day |
| Event Log | Application, source `AppStart`, IDs 1000–1302 ([Messages and troubleshooting](07-messages-and-troubleshooting.md)) |

Commands: `sc query AppStart`, `sc start AppStart`, `sc stop AppStart`.

## Named pipe

| Property | Value |
|---|---|
| Name | `\\.\pipe\AppStart` |
| Access control list (SDDL) | `D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x12018b;;;<SID AppStart Users>)(D;;GA;;;NU)` |
| Meaning | Protected (`P`, no inherited permissions). SYSTEM (`SY`) and Administrators (`BA`) full access. `AppStart Users` only `0x0012018B` = read, write data, write attributes; without `FILE_CREATE_PIPE_INSTANCE`, so that no operator can create their own instance of the pipe. Network logons (`NU`) denied. |
| Flags | `FILE_FLAG_FIRST_PIPE_INSTANCE` (if the pipe already exists, the service does not start: event 1300), `PIPE_REJECT_REMOTE_CLIENTS`, message mode |
| Instances | at most 4 at the same time |
| Request | `START <AppId>`, ASCII, at most 64 bytes, must arrive within 2 s |
| Response | `OK` or `DENIED <reason> <incident number>`, at most 64 bytes |
| Reasons | `unknown-app` (1200), `integrity` (1201), `config` (1202), `busy` (1203), `error` (1205) |
| Limit | One request per session at a time, then a 2 s lockout. Start only in the requester's session. |

The service does not answer invalid requests (event 1204).
