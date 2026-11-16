# UAT-Protokoll: Abnahmetest mit der Testapplikation

| | |
|---|---|
| Datum | 15.11.2026, 21:57–21:59 |
| Prüfling | MSI `AppStart-0.3.0-uat.1.msi` (unsigniert, Launcher ohne Native AOT), SHA-256 `AE565387F038CC3FC9D531C9CBCA6052EDD6A6B5C550889FDC1D1E9781B0EBB1` |
| Produktcode | Branch `spec/allowed-child-processes`, Stand `ba9c6f5` (add-appstart-service plus apps.json Version 2 aus add-allowed-child-processes 2.1/2.2) |
| Umgebung | Test-VM Windows 11 Pro 10.0.26300 (Proxmox), Session 1 |
| Ablauf | `tests/e2e/Invoke-Uat.ps1` über die geplante Aufgabe «AppStartUAT», Suite `Acceptance` = `tests/e2e/Invoke-AcceptanceTest.ps1` (Schritte als echter Bediener `appstart-user`) |
| Ergebnis | **30 PASS, 0 FAIL** |
| Bestätigt durch | Claude (automatisierte Auswertung von Protokoll und Bildschirmfotos) |

## Ergebnis je Prüfpunkt

| Spec / Konzept | Prüfpunkt | Ergebnis |
|---|---|---|
| Vorbereitung | MSI installiert, TestApp freigegeben (`add`), `appstart-user` angelegt (`add-user`), AppId und Verknüpfung vorhanden | PASS |
| Vorbereitung | Bediener-Token echt: nicht erhöht, nicht in Administratoren, in «AppStart Users» | PASS |
| app-launch «Start durch Bediener», test-app | Schritt 1: Start über die Verknüpfung, TestApp läuft als `appstart-app` in Session 1, alle Anzeigen grün | PASS |
| app-launch «Ausbruch über Dateidialog» | Schritt 5: «Eingabeaufforderung starten» → «Start blockiert durch Job-Objekt» ([Bild](acceptance-step5.png)) | PASS |
| installation «Bediener liest Registry» | Schritt 2: `HKLM\SOFTWARE\AppStartTest` für Bediener gesperrt | PASS |
| installation (Datenordner) | Schritt 3: `C:\ProgramData\AppStartTest` für Bediener gesperrt | PASS |
| test-app | Schritt 4: TestApp direkt → 740; mit RunAsInvoker Meldung und Exit-Code 1 | PASS |
| app-launch «Veränderte EXE», pipe-security «Start verweigert» | Schritt 6: ein Byte geändert → Launcher Exit-Code 3, «Start verweigert» mit Ereignis-ID ([Bild](acceptance-step6.png)), Ereignis 1201, `verify` ≠ 0 | PASS (4 Prüfpunkte) |
| verification | Schritt 7: `verify` 0; ≠ 0 bei `appstart-user` in Administratoren und bei lesbarem Registry-Key; danach wieder 0 | PASS (5 Prüfpunkte) |
| app-launch «Geschützte Konfiguration» | Missbrauch: apps.json lesen/überschreiben/ersetzen/löschen/umbenennen, neue Datei → verweigert | PASS |
| pipe-security «Bediener legt eigene Pipe-Instanz an» | Verbinden erlaubt, eigene Instanz verweigert | PASS |
| installation «Programmordner» | nichts in `C:\Program Files\AppStart` schreiben, ersetzen, umbenennen | PASS |
| verification «Bediener startet Prüfung» | `verify` als Bediener → 740 | PASS |
| app-launch «Unbekannte AppId» | Launcher → Exit-Code 3, «Start verweigert» ([Bild](acceptance-unknown.png)), Ereignis 1200 | PASS |
| app-launch «Ungültige Verknüpfung» | ohne `--app` bzw. mit Zusatzargument → Exit-Code 1, «Ungültige Verknüpfung» ([Bild](acceptance-noapp.png)) | PASS (2 Prüfpunkte) |
| Aufräumen | nach allen Versuchen `verify` 0, apps.json unverändert; Deinstallation und Rückbau im `finally` | PASS |

Die Meldungen auf den Bildern entsprechen dem UI-Entwurf `docs/ui-designs/Launcher.dc.html` (Titel, Überschrift, Text mit Massnahme, OK, Ereignis-ID mit Datum im Fuss) und die TestApp `docs/ui-designs/TestApp.dc.html`.

## Befunde auf dem Weg zum Ergebnis

Die ersten beiden Läufe ergaben je 26 PASS, 4 FAIL. Ursache war das Testgerüst, nicht AppStart:

1. `Invoke-Uat.ps1` startet die Skripte mit `powershell.exe -File`. Windows PowerShell 5.1 lässt dabei `$PSScriptRoot` in Parameter-Standardwerten leer, deshalb gibt der Runner `-StageDir` jetzt ausdrücklich mit.
2. Die Launcher-Meldung ist ein TaskDialog mit nur «OK» und ohne `TDF_ALLOW_DIALOG_CANCELLATION`. Sie reagiert nicht auf «Fenster schliessen» (WM_CLOSE, Alt+F4), und der OK-Knopf eines Prozesses des Bedieners ist per UI Automation aus dem Admin-Test nicht auslösbar. Der Test wartete deshalb 30 s, beendete den Launcher und hatte keinen Exit-Code. `Close-UiWindow` in `E2E.Common.ps1` sendet jetzt `TDM_CLICK_BUTTON(IDOK)` an Dialoge. Damit sind die Exit-Codes wieder direkt lesbar, nicht abgeleitet.

## Nicht Teil dieser Abnahme

- Manuelle Tests M1–M9 (`tests/e2e/MANUAL-TESTS.md`): Windows 10, Remote Desktop, Sperrbildschirm, Neustart, echte zweite Sitzung, Netzwerkzugriff auf die Pipe.
- Reale Ziel-Applikationen (add-appstart-service 1.4).
- Signierte Pakete (add-appstart-service 7.2).
- Neue Funktionen aus add-allowed-child-processes (Positivliste, Lernmodus): im Prüfling noch nicht enthalten, eigene UAT nach Abschnitt 7 dieses Changes.
- Kleiner Mangel im Testgerüst ohne Einfluss auf das Ergebnis: Das Bildschirmfoto zu Schritt 1 nimmt den Fensterbereich vom Bildschirm auf; dabei lag die Testkonsole über der TestApp. Das Bild zu Schritt 5 zeigt dasselbe Fenster vollständig.

## Bestätigung

Die Abnahme mit der Testapplikation ist für den genannten Prüfling auf der Test-VM bestanden: Alle 30 automatisierten Prüfpunkte sind erfüllt, die Meldungen stimmen mit den UI-Entwürfen überein. Die oben ausgenommenen Punkte sind damit nicht abgenommen.
