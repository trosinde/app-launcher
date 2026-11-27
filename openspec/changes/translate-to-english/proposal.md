## Why
AppStart is a public repository and is meant to be usable beyond German-speaking sites. Today the user interface, the manual, the README and the developer documents are German only (project.md: "Code und Kommentare Englisch, Doku Deutsch"). English is the common language of service technicians, IT departments and contributors.

## What Changes
- **BREAKING (texts):** every user-visible text becomes English only: setup wizard, verify view and report, command line, launcher messages, bootstrapper, service event log texts, TestApp. No German fallback, no localisation layer.
- Manual (docs/manual, 9 chapters, file names too, e.g. 01-ueberblick.md → 01-overview.md) and README in English; the manual completeness test keeps binding manual and software texts.
- Names: `AppStart-Handbuch.pdf` → `AppStart-Manual.pdf`, release asset `AppStart-Manual-<Version>.pdf`; Start menu entries «AppStart – Einrichtung» → "AppStart – Setup", «AppStart – Einrichtung prüfen» → "AppStart – Verify setup", «AppStart – Handbuch» → "AppStart – Manual". The Start menu folder is recreated on install/update, so old entries disappear.
- Developer documents in English: docs/ (concept, development, how it works, security model, docs/README), CONTRIBUTING, SECURITY, PR template, CLAUDE.md, openspec/project.md and the spec deltas of the open changes (add-appstart-service, add-allowed-child-processes, fix-wizard-findings). Archived changes stay as they are.
- Project rule becomes: code, comments, UI and documentation in English.
- Screenshots in the manual and the README show German UI until they are retaken from the English build (listed in docs/manual/images/README.md).

## Impact
- Affected: AppStart.Setup (SetupTexts, VerificationTexts, SetupEngineTexts, CliTexts, XAML), AppStart.Launcher (Texts), AppStart.Bootstrapper (BootstrapperTexts), AppStart.Service (event texts), AppStart.TestApp (Texts), build/ (manual name, release asset filter), .github/workflows/release.yml, docs/, README, tests asserting texts, e2e scripts matching output.
- Behaviour, exit codes, event IDs, file formats (apps.json, setup-state.json, reports' JSON keys) and check IDs stay unchanged.
- Scripts or monitoring that parse German output (e.g. "Prüfung bestanden") must switch to exit codes or the English text.
