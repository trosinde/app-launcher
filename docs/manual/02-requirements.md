# Requirements

## Windows

| Item | Requirement |
|---|---|
| Operating system | Windows 11 x64 or Windows 10 x64, German or another language. Tested on Windows 11 Pro; the test on Windows 10 is still pending. |
| .NET | Not required. All programs include their own runtime. |
| Installation location | Always `C:\Program Files\AppStart` (per-machine installation). An installation location that users can write to is not permitted. |
| Services | The service `AppStart` runs as LocalSystem and starts automatically. |
| Group Policy | If the user rights "Deny log on locally" or "Deny log on through Remote Desktop Services" are set by a domain policy, the policy overrides the setting of the setup. The account `appstart-app` must then be added to the policy (see [Verification after a service visit](06-verification-after-a-service-visit.md)). The right "Log on as a batch job" must remain granted to the Administrators (Windows default), otherwise the service cannot sign in `appstart-app`. |

## Rights of the installer

- Installation, setup, changes and `verify` require **admin rights**. `AppStart-Setup.exe` and `AppStart.Setup.exe` request them through UAC.
- For the command line, open a **command prompt or PowerShell with "Run as administrator"**. Only then does the output of `AppStart.Setup.exe` appear in the same window.
- The silent installation through software distribution runs as SYSTEM.
- Operators do not need admin rights. They must be members of the group `AppStart Users`.

## Information per application

Before the setup, clarify and write down for each application:

| Information | Example | Required | Note |
|---|---|---|---|
| Program file (EXE) | `C:\Program Files\Vendor\AppA\AppA.exe` | yes | Absolute path with drive letter. No network paths (`\\server\…`). The EXE must be in a folder that operators cannot write to (e.g. under `C:\Program Files`). |
| Display name | `Application A` | no | Name of the shortcut and window title of messages. If omitted: file description or product name of the EXE, otherwise the file name. |
| Data folder | `C:\ProgramData\Vendor\AppA` | no | Folder with exchange files or passwords. Locked for operators. Full path, not a drive and not a system folder, not a junction. |
| Registry key | `HKLM\SOFTWARE\Vendor\AppA` | no | Key with passwords of the application. Must start with `HKLM\`. Locked for operators. |
| Signed? | yes / no | – | The setup detects it automatically. Signed EXEs are verified through the certificate (survives updates), unsigned ones through SHA-256 (`rehash` after every update). |
| Does the application start other programs? | no | – | Default: child processes are blocked. Test with the application beforehand: printing, export, help, "Open file" dialog. |
| Network access with Windows sign-in? | no | – | Does not work through AppStart (see [Overview](01-overview.md)). |

## Information about the operators

- Should the local operator account `appstart-user` be created? Then choose a password that meets the password policy of the PC.
- Which existing local or domain users (`DOMAIN\user`) should be added to the group `AppStart Users`? Only user accounts, no groups.
- Operators must **not** be members of the Administrators group; `verify` reports this as a deviation.

## Scope of delivery

| File | Use |
|---|---|
| `AppStart-Setup.exe` | Interactive installation: installs the MSI and opens the setup |
| `AppStart-<Version>.msi` | Silent installation through software distribution (`msiexec`) |
| `SHA256SUMS.txt` | Checksums of the delivery |
| `AppStart-Manual-<Version>.pdf` | This manual, for reading before the installation. The same file is installed with AppStart and opens from the Start menu under "AppStart – Manual". |

All program files and the MSI are signed with the publisher's certificate. Check this before the installation in the file's properties ("Digital Signatures" tab).
