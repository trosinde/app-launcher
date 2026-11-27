# Images of the manual

Screenshots come from the **finished software on the test VM**, not from the UI designs. Format PNG, windows without shadow, 100 % scaling, Windows 11. No real customer or domain names and no names of persons.

> **To do (translate-to-english, task 4.2):** The existing screenshots still show the German user interface. They must be retaken from the English build with the same script. The "Content" column below already describes the English texts the new screenshots must show.

They are captured with `tests/e2e/Capture-ManualScreenshots.ps1` (suite `Manual` in the UAT runner). The test VM is named `WIN11`; this name appears in the images instead of `PC-014` from the UI designs.

Post-processing, so that no person's name appears in the manual:

- `pruefansicht-ok.png`, `pruefansicht-abweichung.png`: The value "Service technician" shows the signed-in account. The account name after `WIN11\` is replaced with "technician".
- `testapp.png`: The title bar is cut off. The window runs under `appstart-app` and cannot be brought to the foreground from the test session; the title bar then appears faded.

As long as a file is missing, `build/ManualBuilder` inserts a placeholder with the image title in the PDF. The test `AppStart.Documentation.Tests` requires that every image used in the manual either exists or is listed here.

## Available

| File | Chapter | Content | Version |
|---|---|---|---|
| `funktionsweise.svg` | 1 Overview | How it works in one picture, drawn by hand | – |
| `bootstrapper.png` | 3 Installation | `AppStart-Setup.exe` during the MSI installation | 0.3.0-uat.5 (German UI) |
| `setup-1.png` | 3 Installation | Wizard step 1, TestApp with data folder and registry key, "Other programs: Blocked (recommended)" | 0.3.0-uat.5 (German UI) |
| `setup-2.png` | 3 Installation | Wizard step 2, operator account, example member `DOMAIN\operator01` | 0.3.0-uat.5 (German UI) |
| `setup-3-laeuft.png` | 3 Installation | Wizard step 3 during the setup | older version (before add-allowed-child-processes, German UI); the setup is too fast on the test VM for a new capture |
| `setup-3.png` | 3 Installation | Wizard step 3 completed, "Verification passed – 9 of 9 checks passed" | 0.3.0-uat.5 (German UI) |
| `testapp.png` | 3 Installation | TestApp as operator, "Start blocked by job object" | 0.3.0-uat.5 (German UI) |
| `setup-weitere-programme.png` | 4 Applications | Step 1 with "Allowed helper programs only", "Helper programs (1)…", column "1 allowed" | 0.3.0-uat.5 (German UI) |
| `hilfsprogramme.png` | 4 Applications | Dialog "Allowed helper programs" with TestHelper (SHA-256, not signed) | 0.3.0-uat.5 (German UI) |
| `lernmodus.png` | 4 Applications | "Learning mode running" with TestHelper and cmd.exe recorded | 0.3.0-uat.5 (German UI) |
| `lernergebnis.png` | 4 Applications | "Apply suggestions": TestHelper checked, cmd.exe "command prompt – manual only" | 0.3.0-uat.5 (German UI) |
| `pruefansicht-ok.png` | 6 Verification | Verification view without deviation, 9 checks | 0.3.0-uat.5 (German UI), anonymized |
| `pruefansicht-abweichung.png` | 6 Verification | Deviation at "Accounts": `appstart-user` in Administrators | 0.3.0-uat.5 (German UI), anonymized |
| `verify-konsole.png` | 6 Verification | `AppStart.Setup.exe verify` with the same deviation | 0.3.0-uat.5 (German UI) |
| `launcher-nicht-erreichbar.png` | 7 Messages | Service stopped: "Launch service not reachable" | 0.3.0-uat.5 (German UI) |
| `launcher-verweigert.png` | 7 Messages | One byte of the EXE changed: "Launch denied" with event ID | 0.3.0-uat.5 (German UI) |

## Missing screenshots

Both show windows of Windows itself. Capture them with the same script on the test VM, which has an English user interface.

| File | Chapter | Screen | State |
|---|---|---|---|
| `startmenue.png` | 3 Installation | Start menu, folder "AppStart" expanded | Entries "AppStart – Setup", "AppStart – Verify setup", "AppStart – Manual" visible |
| `ereignisanzeige.png` | 7 Messages | Event Viewer, log "Application", filtered on source AppStart | Entry 1201 selected, text with "Incident: …" visible; Windows activated (no watermark) |
