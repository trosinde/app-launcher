# Manual tests

These tests cannot be automated, or not reliably (real logons, Remote Desktop,
lock screen, restart, second operating system). Run them on the **test VM** and revert to the snapshot before each block.
Preparation, unless stated otherwise:

1. Install `AppStart-<Version>.msi` (`msiexec /i AppStart-<Version>.msi /qn`).
2. As admin: `AppStart.Setup.exe add --exe "C:\Program Files\AppStart\current\TestApp\AppStart.TestApp.exe" --data-dir C:\ProgramData\AppStartTest --reg-key HKLM\SOFTWARE\AppStartTest --name "AppStart TestApp"`
3. As admin: `AppStart.Setup.exe add-user --name appstart-user` (enter the password at "Password for appstart-user:" and note it).
4. `AppStart.Setup.exe verify` returns exit code 0.

Enter the result of each test in the table at the end (date, Windows version, AppStart version, OK/deviation).

## M1 Remote Desktop

Spec: app-launch "Launch by operator", installation "No logon as app account", pipe-security "Foreign session".

| Step | Expected |
|---|---|
| Add appstart-user to the group "Remote Desktop Users", log on as appstart-user via RDP | Logon works |
| Start the shortcut "AppStart TestApp" on the desktop | TestApp appears **in the RDP session**, without a password or UAC prompt; all indicators green, account appstart-app |
| At the same time at the console as admin: Task Manager, Details, column "Session ID" | AppStart.TestApp.exe runs in the RDP user's session, not in session 0 and not at the console |
| Log on via RDP as `appstart-app` (set the password as admin with `net user appstart-app <new>`, then `verify`) | Logon denied ("Deny log on through Remote Desktop Services") |
| Disconnect the RDP session (do not sign out), reconnect | TestApp keeps running and can be operated |

## M2 Lock screen

| Step | Expected |
|---|---|
| Log on as appstart-user, start TestApp via the shortcut | TestApp runs |
| Win+L, wait 1 minute, unlock | TestApp keeps running, indicators still green, "Check again" still OK |
| With the screen locked, "Switch user", log on as admin, `Get-Process AppStart.TestApp` | TestApp still belongs to the operator's session |
| Try an interactive logon at the console as `appstart-app` | Logon denied ("Deny log on locally") |

## M3 Real second session (fast user switching)

Automatically (`Invoke-OperationsTests.ps1`) both operators run in the same session; there the per-session lock
lets only one through on a simultaneous start. Here with two real sessions:

| Step | Expected |
|---|---|
| Create a second local user, `AppStart.Setup.exe add-user --member "<PC>\<User>"` | verify exit code 0 |
| Log on appstart-user, switch user, log on the second user | two active sessions (`query user`) |
| Start the shortcut in both sessions as simultaneously as possible | **both** TestApps start, each in its own session (Task Manager, Session ID); no message "Start already in progress" |
| In session 1 double-click twice in quick succession | second click shows "Start already in progress" (lockout time 2 s per session), event 1203 |
| TestApp window from session 1 is not visible in session 2 | correct |

## M4 Restart

| Step | Expected |
|---|---|
| Restart the computer, log on directly as appstart-user **without** an admin logon | Service AppStart is running (startup type automatic) |
| Start TestApp via the shortcut | starts, all indicators green |
| As admin: Event Viewer, Application, source AppStart | Event 1000 (service started) after the restart, no 1300/1301 |
| `AppStart.Setup.exe verify` | Exit code 0 |

## M5 Windows 10

Spec: Windows 10 and 11 x64 (concept, test environment). The desktop DACL helper process (design D2) is only needed there.

| Step | Expected |
|---|---|
| Test VM with Windows 10 22H2 x64 | – |
| Run `Invoke-ServiceE2E.ps1`, `Invoke-SetupE2E.ps1`, `Invoke-AcceptanceTest.ps1`, `Invoke-InstallTests.ps1`, `Invoke-OperationsTests.ps1` | 0 FAIL each |
| Repeat M1 to M4 on Windows 10 | same as Windows 11 |
| Start TestApp as appstart-user: window appears and can be operated | no empty/invisible window (access to WinSta0/Default) |

## M6 Bootstrapper interactive

Spec: installation "First installation", "Start from a writable folder", "Error during MSI installation",
"Cancel in the wizard", "Update over an existing installation".

| Step | Expected |
|---|---|
| As a standard user copy `AppStart-Setup.exe` to `Downloads`, double-click | **one** UAC prompt (admin credentials) |
| Confirm | MSI is installed, then wizard step 1 from `C:\Program Files\AppStart\current` without a second UAC prompt (Task Manager: path of AppStart.Setup.exe) |
| Process Monitor (filter: process AppStart-Setup.exe, path contains `Downloads` or `\Temp\`) | no DLLs loaded or programs started from Downloads or %TEMP% except the bootstrapper EXE itself |
| Close the wizard with "Cancel" | AppStart stays installed; Start menu "AppStart – Setup" starts the wizard again; `verify` reports a deviation (exit code ≠ 0) |
| Start the bootstrapper while another installation is running (e.g. leave another MSI with UI open) | Message "Another installation is in progress" with cause (code 1618) and "Action:" line; MSI log in `C:\ProgramData\AppStart\Logs`; no wizard |
| On a set-up computer: start the bootstrapper of a newer version | MSI updated, wizard shows the existing configuration, verify runs at the end |

## M7 Verification via the Start menu

Spec: verification "Service technician after the service visit", "Operator starts the verification".

| Step | Expected |
|---|---|
| As admin "AppStart – Verify setup" in the Start menu | UAC prompt, verification view with result per check, report in `C:\ProgramData\AppStart\Reports` |
| The same entry as appstart-user | UAC asks for admin credentials; without them no verification starts |

## M8 Operator's view in the user interface

Spec: installation "Operator reads registry", test-app "Data blocked for operators", installation "Icon applied".
Checked automatically via API (`Invoke-AcceptanceTest.ps1`, steps 2 and 3); here with the real tools:

| Step | Expected |
|---|---|
| As appstart-user `regedit` (without admin) → `HKLM\SOFTWARE\AppStartTest` | "Access is denied" |
| As appstart-user open `C:\ProgramData\AppStartTest` in Explorer | access denied; the offered button "Continue" asks for admin credentials |
| Look at the shortcut "AppStart TestApp" on the desktop | shows the TestApp's icon, not the launcher's |

## M9 Access over the network

Spec: pipe-security "Access over the network".

| Step | Expected |
|---|---|
| From a second computer in the same network with an account that is a member of AppStart Users on the test VM: `[System.IO.Pipes.NamedPipeClientStream]::new('<Test-VM>', 'AppStart').Connect(3000)` | Access denied or connection rejected (PIPE_REJECT_REMOTE_CLIENTS, NU denied) |

## Log

| Test | Date | Windows | AppStart | Result | Remarks |
|---|---|---|---|---|---|
| M1 | | | | | |
| M2 | | | | | |
| M3 | | | | | |
| M4 | | | | | |
| M5 | | | | | |
| M6 | | | | | |
| M7 | | | | | |
| M8 | | | | | |
| M9 | | | | | |
