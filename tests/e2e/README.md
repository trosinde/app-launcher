# End-to-End-Test des Dienstes

Prüft AppStart.Service auf echtem Windows: Installation, Pipe-Rechte, Start in der Bediener-Session,
Job-Objekt, alle Ablehnungsgründe, gefälschte Pipe, Ereignisprotokoll und Deinstallation.
Legt Konten, Dienst und Ordner an und räumt am Ende alles wieder ab. **Nur auf der Test-VM ausführen.**

Stage-Ordner vorbereiten:
- `AppStart-<Version>.msi` aus `build/Build-Release.ps1`, oder für den Lauf ohne MSI den Ordner `Service\` (publish von AppStart.Service) und `AppStart.TestApp.exe`
- `Invoke-ServiceE2E.ps1`, `Test-NonAdminClient.ps1` (dieser Ordner)
- `Prepare-TestVm.ps1`, `Remove-SpikeSetup.ps1` (aus `spikes/`)

Ausführen: zuerst den Client **ohne** Admin-Rechte im Hintergrund, dann den Test als Admin.

```powershell
Start-Process powershell -ArgumentList '-File', '.\Test-NonAdminClient.ps1'
Start-Process powershell -Verb RunAs -Wait -ArgumentList '-File', '.\Invoke-ServiceE2E.ps1', '-MsiPath', '.\AppStart-0.1.0.msi'
```

Ergebnis in `e2e-run.txt` (eine Zeile PASS/FAIL je Prüfpunkt), Bildschirmfoto `e2e-testapp.png`.

Letzter Lauf: 15.11.2026, Windows 11 Pro 10.0.26300, MSI 0.1.0-dev.2 (mit Launcher, -NoAot): 23 PASS, 0 FAIL.
