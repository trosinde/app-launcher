# End-to-end test of the service

Tests AppStart.Service on real Windows: installation, pipe rights, start in the operator's session,
job object, all rejection reasons, spoofed pipe, Event Log and uninstallation.
Creates accounts, service and folders and removes everything again at the end. **Run only on the test VM.**

Prepare the stage folder:
- `AppStart-<Version>.msi` from `build/Build-Release.ps1`, or for the run without MSI the folder `Service\` (publish of AppStart.Service) and `AppStart.TestApp.exe`
- `Invoke-ServiceE2E.ps1`, `Test-NonAdminClient.ps1` (this folder)
- `Prepare-TestVm.ps1`, `Remove-SpikeSetup.ps1` (from `spikes/`)

Run: first the client **without** admin rights in the background, then the test as admin.

```powershell
Start-Process powershell -ArgumentList '-File', '.\Test-NonAdminClient.ps1'
Start-Process powershell -Verb RunAs -Wait -ArgumentList '-File', '.\Invoke-ServiceE2E.ps1', '-MsiPath', '.\AppStart-0.1.0.msi'
```

Result in `e2e-run.txt` (one PASS/FAIL line per check), screenshot `e2e-testapp.png`.

Last run: 11/15/2026, Windows 11 Pro 10.0.26300, MSI 0.1.0-dev.2 (with launcher, -NoAot): 23 PASS, 0 FAIL.

## Setup via the command line

`Invoke-SetupE2E.ps1 -MsiPath <msi>` (as admin): install the MSI, `add` TestApp with data folder and
registry key, `add-user`, `list`, `verify` (10 of 10), start via the created shortcut, deliberate
misconfiguration, `uninstall` (accounts, shortcuts and rights restored), uninstall the MSI.

Last run: 11/15/2026, MSI 0.2.0-dev.3: 19 PASS, 0 FAIL.

## Acceptance, installation and operations tests (8.2 to 8.5)

Shared helper functions in `E2E.Common.ps1`. `Invoke-OperatorProbe.ps1` performs individual accesses
**as a real operator**: the scripts start processes of `appstart-user` (or a second
test user) with `Start-Process -Credential` (CreateProcessWithLogonW). This yields a real token
without admin rights with membership in "AppStart Users"; the result comes back via
`C:\ProgramData\AppStartE2E\out`. The password of `appstart-user` is generated randomly per run
and passed via pipe to `add-user --password-stdin`, never written to a file.

All three scripts: as admin, only on a fresh test VM (abort if AppStart is already installed),
one PASS/FAIL/SKIP line per check, summary "N PASS, M FAIL", complete cleanup
in `finally` (accounts incl. profiles, group, service, product, `C:\ProgramData\AppStart`,
`C:\Program Files\AppStart`, test data `C:\ProgramData\AppStartTest` and `HKLM\SOFTWARE\AppStartTest`,
event source, Start menu folder, shortcuts, `C:\ProgramData\AppStartE2E`). All scripts of this
folder must be together in the stage folder.

```powershell
.\Invoke-AcceptanceTest.ps1  -MsiPath .\AppStart-0.2.0.msi
.\Invoke-InstallTests.ps1    -MsiPath .\AppStart-0.2.0.msi -MsiNextPath .\AppStart-0.2.1.msi [-BootstrapperPath .\AppStart-Setup.exe]
.\Invoke-OperationsTests.ps1 -MsiPath .\AppStart-0.2.0.msi
```

### `Invoke-AcceptanceTest.ps1` (8.3, 8.2)

The 7 steps from the concept, each as appstart-user: (1) start via the shortcut (ShellExecute
on the `.lnk`), TestApp as appstart-app in the operator's session, all indicators green (UI Automation);
(2) reading the registry key denied; (3) reading/writing the data folder denied; (4) TestApp EXE directly
→ 740 ERROR_ELEVATION_REQUIRED, with RunAsInvoker additionally the TestApp's message and exit code 1;
(5) "Start command prompt" → "Start blocked by job object"; (6) one byte of the EXE changed
→ launcher exit code 3, "Launch denied" with event ID, event 1201, verify ≠ 0; (7) verify 0,
≠ 0 after appstart-user in Administrators and after a readable registry key, then 0 again.
Abuse attempts: read/overwrite/replace/delete/rename apps.json, create a pipe instance,
write/replace in `C:\Program Files\AppStart`, setup/verify without admin, unknown AppId (1200),
launcher without `--app` or with an extra argument. Output `acceptance-run.txt`, `acceptance-*.png`.

### `Invoke-InstallTests.ps1` (8.5)

- msiexec 1618: `Global\_MSIExecute` is held, then `msiexec /i /qn`. If that does not yield 1618
  (Windows Installer behavior varies by version), the script reports SKIP; the item then stays manual
  (MANUAL-TESTS.md, M6). A second installation running in parallel is not reliable in timing
  and is therefore not used.
- Silent installation as SYSTEM via a one-time scheduled task (`AppStartE2E-MsiSystem`):
  exit code 0, program folder, service runs automatically as LocalSystem, no user interface.
- Update with `-MsiNextPath` (higher version) over a set-up installation: hash and SDDL of
  apps.json, SDDL of ProgramData\AppStart, data folder and registry key, SIDs/password date of the accounts,
  group members, shortcuts (target, arguments, icon, SDDL) identical before/after; service restarted
  from the new file; exactly one product with the new version; verify 0; start as operator.
  For this the TestApp is approved from a copy in `C:\Program Files\AppStartTestApp` (like a
  real third-party application), because the update replaces the TestApp in the package.
- apps.json version 1 (add-allowed-child-processes 7.2), only if the first MSI still writes version 1
  (e.g. from `main` before this change): after the update the service starts the TestApp as `blocked`
  (cmd.exe blocked). The first change (`rehash --app`) writes version 2 with `blocked`, SDDL of
  apps.json unchanged. Then by hand version 1 with `allowChildProcesses: true`: cmd.exe runs, the
  migration yields `all`. Finally `set-children --mode blocked`. In the UAT runner as suite `Install`
  with `previousMsiPath` in request.json or `-PreviousMsiPath`.
- Optional `-BootstrapperPath` (copy to `C:\Users\Public\Downloads`): 1618 via the bootstrapper
  (exit ≠ 0, log in `Logs`, no wizard), interactive start with cancel in the wizard (wizard from the
  program folder, stays installed, Start menu entry, verify ≠ 0), `/quiet` (exit 0, MSI extracted to
  `C:\ProgramData\AppStart\Installer` and deleted afterwards, no MSI from Downloads/%TEMP%, log in
  `C:\ProgramData\AppStart\Logs`, no wizard). Output `install-run.txt`, `install-*.log`.

### `Invoke-OperationsTests.ps1` (8.4, automatable part)

Second test user `appstart-op2`: first "No permission" (exit code 4), then `add-user --member`.
Two operators 3 s apart: both TestApps run. Simultaneously: since both run via `-Credential` in
**the same** session, the per-session lock lets one through, the other gets "Start already in
progress" (1203). Kill the service process → SCM restarts it (7031), start works. Replace the EXE with a modified
copy → denied (1201), OK again after `rehash --app`. `uninstall` + MSI uninstallation →
accounts, groups, service, program folder, shortcuts, rights as before the installation.
Output `operations-run.txt`.

Not automatable (real second session, Remote Desktop, lock screen, restart, Windows 10,
bootstrapper with UAC, network access to the pipe): [MANUAL-TESTS.md](MANUAL-TESTS.md).

### Coverage of the spec scenarios

| Spec / scenario | Script |
|---|---|
| app-launch: Launch by operator | Acceptance (1), Operations, ServiceE2E; RDP/second session manual M1, M3 |
| app-launch: Modified EXE | Acceptance (6), Operations (update), ServiceE2E |
| app-launch: Unknown AppId | Acceptance, ServiceE2E |
| app-launch: Escape via file dialog | Acceptance (5), ServiceE2E |
| app-launch: Configuration writable by operators / Invalid configuration | ServiceE2E (writable); invalid: unit tests Core |
| app-launch: Swap between verification and launch | Acceptance (operator cannot open/rename the EXE); the race itself only unit tests |
| app-launch: Foreign signature | not covered (needs two signing certificates) |
| app-launch: launcher messages | Acceptance (denied, invalid), Operations (no permission), ServiceE2E; "not reachable" only unit tests |
| installation: Multiple selection, Icon applied | Setup E2E (one app); icon manual M8 |
| installation: No logon as app account | verify (check Accounts); real logon manual M1, M2 |
| installation: Operator reads registry | Acceptance (2); regedit manual M8 |
| installation: Program folder | Acceptance (abuse), ServiceE2E, verify |
| installation: bootstrapper (First installation, Start from a writable folder, Error during MSI installation, Cancel in the wizard, Update over an existing installation) | InstallTests with -BootstrapperPath; UAC and message text manual M6 |
| installation: Software distribution as SYSTEM | InstallTests |
| installation: Update keeps the setup | InstallTests |
| installation: Complete uninstallation | Operations, Setup E2E |
| pipe-security: Access over the network | manual M9 |
| pipe-security: User outside the group | Operations, Test-NonAdminClient |
| pipe-security: Spoofed pipe, Invalid request | ServiceE2E |
| pipe-security: Operator creates own pipe instance | Acceptance |
| pipe-security: Foreign session | Acceptance (session of the requester); real second session manual M3 |
| pipe-security: Launch denied, No internal details | Acceptance (6), ServiceE2E |
| test-app: all four scenarios | Acceptance (1 to 5) |
| verification: Correct setup, Deviation after service | Acceptance (7), Setup E2E |
| verification: Start menu with UAC, Operator starts the verification | Acceptance (setup without admin → 740); user interface manual M7 |
| documentation | not part of these scripts (10.4) |

## Last runs (11/15/2026, Windows 11 Pro 10.0.26300, packages 0.2.0-dev.6/7 and 0.2.1-dev.1, launcher/bootstrapper without Native AOT)

| Script | Result |
|---|---|
| Invoke-AcceptanceTest.ps1 | 30 PASS, 0 FAIL |
| Invoke-InstallTests.ps1 | 29 PASS, 0 FAIL, 2 SKIP (msiexec 1618 cannot be triggered artificially, check manually) |
| Invoke-OperationsTests.ps1 | 20–21 of 21 PASS per run. Varying individual cases in which the exit code of a launcher started via `Start-Process -Credential` cannot be read (-1) or the test harness aborts before reading it; the displayed message and the events were correct in all cases. Every check passed in at least one run. |
| Invoke-SetupE2E.ps1 | 19 PASS, 0 FAIL |
| Invoke-ServiceE2E.ps1 | 23 PASS, 0 FAIL |

Found and fixed by these tests: operators could not connect to the pipe (the launcher requested GENERIC_WRITE, which contains FILE_CREATE_PIPE_INSTANCE and is denied to operators according to D9). All earlier tests ran with an admin token and could not show this.
