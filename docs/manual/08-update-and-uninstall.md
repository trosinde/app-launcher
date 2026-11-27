# Update and uninstall

## Updating AppStart

AppStart never updates itself. The service technician installs updates with the new release.

**Interactive:** Start the new `AppStart-Setup.exe` and confirm the UAC prompt. The installer updates the MSI and then opens the wizard with the existing configuration. Click "Next" twice without changing anything; the verification runs at the end. If the same or a newer version is already installed, it reports "AppStart is already installed" and only opens the wizard.

**Silent:**

```text
msiexec /i AppStart-<new version>.msi /qn /l*v C:\Windows\Temp\AppStart-update.log
"C:\Program Files\AppStart\current\AppStart.Setup.exe" verify
```

Or `AppStart-Setup.exe /quiet` (exit code as for `msiexec`).

What happens during the update:

1. Windows Installer detects the older version and replaces it (major upgrade).
2. The `AppStart` service is stopped and deleted, and the AppStart Start menu entries are removed.
3. The new program files are installed to `C:\Program Files\AppStart\current`.
4. The service is registered again (automatic, LocalSystem, restart after a crash) and started, and the Start menu entries are created again.
5. The setup is verified automatically; the report is saved under `C:\ProgramData\AppStart\Reports`.

**Unchanged:** `apps.json` with all approvals, the accounts `appstart-app` and `appstart-user`, the group `AppStart Users` with its members, the shortcuts of the applications, the permissions on data folders and registry keys, and the verification reports.

During the update (a few seconds) operators cannot start applications ("Launch service not reachable"). Running applications stay open.

After the update: look at the verification report ("AppStart – Verify setup"); the result must be "No deviation".

## Updating an application

See [Managing applications](04-managing-applications.md): with check type "SHA-256" always, with "Signature" only after a certificate change, run `AppStart.Setup.exe rehash --app <AppId>`, then verify. Updates of the application can overwrite shortcuts and permissions; `verify` shows this.

## Uninstalling

Uninstalling has two steps, in **this order**. The second step on its own removes only the service and the program files, not the setup.

**1. Restore the setup** (command prompt "Run as administrator"):

```text
"C:\Program Files\AppStart\current\AppStart.Setup.exe" uninstall
```

The setup restores what it changed:

| What | State afterwards |
|---|---|
| Shortcuts | AppStart shortcuts deleted; original shortcuts that pointed directly to the EXE restored at their old location |
| Data folders and registry keys | Permissions as before the approval (owner and access control list of the folder or key; entries below inherit again) |
| Accounts and group | `appstart-app` and `AppStart Users` deleted. `appstart-user` only if the setup created it. Added domain users stay untouched (only their group membership goes away with the group). |
| `apps.json` | deleted |
| Verification reports | kept under `C:\ProgramData\AppStart\Reports` |

Expected output:

```text
✓ Shortcuts restored, data folders and registry keys reset
✓ Accounts and group removed
✓ Configuration removed (verification reports are kept)
```

The profile `C:\Users\appstart-app` remains after the account is deleted. It contains user settings of the applications; delete it if needed via "System Properties" → "User Profiles".

**2. Uninstall the MSI:** "Settings" → "Apps" → "Installed apps" → "AppStart" → "Uninstall", or silently:

```text
msiexec /x AppStart-<Version>.msi /qn
```

This stops and deletes the service and removes the Start menu folder "AppStart" and the program folder `C:\Program Files\AppStart`.

**What remains** is `C:\ProgramData\AppStart` with the verification reports (`Reports`), the installer logs (`Logs`) and the folder `ShortcutBackup`. Once the reports are filed, an administrator can delete the folder. The source "AppStart" in the Event Log remains so that older entries stay readable.

> **Note:** If the MSI was uninstalled first, `AppStart.Setup.exe` is no longer present. Then install AppStart again, run `uninstall` and uninstall the MSI again.

After uninstalling, operators start the applications as before through the original shortcuts, with the rights of their own account.

### Versions and pre-release versions

Windows Installer compares only the first three parts of the version and knows no pre-release versions. Both `0.2.0-dev.7` and `0.2.0-dev.8` become `0.2.0.0` for the MSI: the second MSI is then **not an update** of the first. Always ship updates to customers with a higher version (e.g. `0.2.0` → `0.2.1`). Uninstall pre-release versions of the same version first.
