# Manuelle Tests

Diese Tests lassen sich nicht oder nicht zuverlässig automatisieren (echte Anmeldungen, Remote Desktop,
Sperrbildschirm, Neustart, zweites Betriebssystem). Durchführung auf der **Test-VM**, vor jedem Block
auf den Snapshot zurücksetzen. Vorbereitung, wenn nicht anders angegeben:

1. `AppStart-<Version>.msi` installieren (`msiexec /i AppStart-<Version>.msi /qn`).
2. Als Admin: `AppStart.Setup.exe add --exe "C:\Program Files\AppStart\current\TestApp\AppStart.TestApp.exe" --data-dir C:\ProgramData\AppStartTest --reg-key HKLM\SOFTWARE\AppStartTest --name "AppStart TestApp"`
3. Als Admin: `AppStart.Setup.exe add-user --name appstart-user` (Passwort eingeben und notieren).
4. `AppStart.Setup.exe verify` liefert Exit-Code 0.

Ergebnis je Test in die Tabelle am Ende eintragen (Datum, Windows-Version, AppStart-Version, OK/Abweichung).

## M1 Remote Desktop

Spec: app-launch «Start durch Bediener», installation «Keine Anmeldung als App-Konto», pipe-security «Fremde Session».

| Schritt | Erwartet |
|---|---|
| appstart-user in die Gruppe «Remotedesktopbenutzer» aufnehmen, per RDP als appstart-user anmelden | Anmeldung klappt |
| Verknüpfung «AppStart TestApp» auf dem Desktop starten | TestApp erscheint **in der RDP-Sitzung**, ohne Passwort- oder UAC-Abfrage; alle Anzeigen grün, Konto appstart-app |
| Gleichzeitig an der Konsole als Admin: Task-Manager, Details, Spalte «Sitzungs-ID» | AppStart.TestApp.exe läuft in der Sitzung des RDP-Benutzers, nicht in Sitzung 0 und nicht an der Konsole |
| Per RDP als `appstart-app` anmelden (Passwort per `net user appstart-app <neu>` als Admin setzen, danach `verify`) | Anmeldung verweigert («Anmeldung über Remotedesktop verweigern») |
| RDP-Sitzung trennen (nicht abmelden), neu verbinden | TestApp läuft weiter und ist bedienbar |

## M2 Sperrbildschirm

| Schritt | Erwartet |
|---|---|
| Als appstart-user anmelden, TestApp über die Verknüpfung starten | TestApp läuft |
| Win+L, 1 Minute warten, entsperren | TestApp läuft weiter, Anzeigen unverändert grün, «Erneut prüfen» weiterhin OK |
| Bei gesperrtem Bildschirm «Benutzer wechseln», als Admin anmelden, `Get-Process AppStart.TestApp` | TestApp gehört weiterhin zur Session des Bedieners |
| Interaktive Anmeldung an der Konsole als `appstart-app` versuchen | Anmeldung verweigert («lokale Anmeldung verweigern») |

## M3 Echte zweite Session (schneller Benutzerwechsel)

Automatisch (`Invoke-OperationsTests.ps1`) laufen beide Bediener in derselben Session; die Sperre je Session
lässt dort bei gleichzeitigem Start nur einen durch. Hier mit zwei echten Sessions:

| Schritt | Erwartet |
|---|---|
| Zweiten lokalen Benutzer anlegen, `AppStart.Setup.exe add-user --member "<PC>\<Benutzer>"` | verify Exit-Code 0 |
| appstart-user anmelden, Benutzer wechseln, zweiten Benutzer anmelden | zwei aktive Sessions (`query user`) |
| In beiden Sessions die Verknüpfung möglichst gleichzeitig starten | **beide** TestApps starten, jede in ihrer eigenen Session (Task-Manager, Sitzungs-ID); keine Meldung «Start läuft bereits» |
| In Session 1 zweimal schnell hintereinander doppelklicken | zweiter Klick zeigt «Start läuft bereits» (Sperrzeit 2 s je Session), Ereignis 1203 |
| Fenster der TestApp aus Session 1 ist in Session 2 nicht sichtbar | richtig |

## M4 Neustart

| Schritt | Erwartet |
|---|---|
| Rechner neu starten, **ohne** Admin-Anmeldung direkt als appstart-user anmelden | Dienst AppStart läuft (Starttyp automatisch) |
| TestApp über die Verknüpfung starten | startet, alle Anzeigen grün |
| Als Admin: Ereignisanzeige, Anwendung, Quelle AppStart | Ereignis 1000 (Dienst gestartet) nach dem Neustart, keine 1300/1301 |
| `AppStart.Setup.exe verify` | Exit-Code 0 |

## M5 Windows 10

Spec: Windows 10 und 11 x64 (Konzept, Testumgebung). Desktop-DACL-Hilfsprozess (Design D2) ist nur dort nötig.

| Schritt | Erwartet |
|---|---|
| Test-VM mit Windows 10 22H2 x64 | – |
| `Invoke-ServiceE2E.ps1`, `Invoke-SetupE2E.ps1`, `Invoke-AcceptanceTest.ps1`, `Invoke-InstallTests.ps1`, `Invoke-OperationsTests.ps1` ausführen | jeweils 0 FAIL |
| M1 bis M4 auf Windows 10 wiederholen | wie Windows 11 |
| TestApp als appstart-user starten: Fenster erscheint und ist bedienbar | kein leeres/unsichtbares Fenster (Zugriff auf WinSta0/Default) |

## M6 Bootstrapper interaktiv

Spec: installation «Erstinstallation», «Start aus beschreibbarem Ordner», «Fehler bei der MSI-Installation»,
«Abbruch im Wizard», «Update über bestehende Installation».

| Schritt | Erwartet |
|---|---|
| Als Standardbenutzer `AppStart-Setup.exe` nach `Downloads` kopieren, Doppelklick | **eine** UAC-Abfrage (Admin-Zugangsdaten) |
| Bestätigen | MSI wird installiert, danach Wizard Schritt 1 aus `C:\Program Files\AppStart\current` ohne zweite UAC-Abfrage (Task-Manager: Pfad von AppStart.Setup.exe) |
| Process Monitor (Filter: Prozess AppStart-Setup.exe, Pfad enthält `Downloads` oder `\Temp\`) | keine geladenen DLLs oder gestarteten Programme aus Downloads oder %TEMP% außer der Bootstrapper-EXE selbst |
| Wizard mit «Abbrechen» beenden | AppStart bleibt installiert; Startmenü «AppStart – Einrichtung» startet den Wizard erneut; `verify` meldet Abweichung (Exit-Code ≠ 0) |
| Während eine andere Installation läuft (z. B. ein anderes MSI mit UI offen lassen) Bootstrapper starten | Meldung mit Ursache (andere Installation läuft, 1618) und Massnahme; MSI-Log in `C:\ProgramData\AppStart\Logs`; kein Wizard |
| Eingerichteten Rechner: Bootstrapper einer neueren Version starten | MSI aktualisiert, Wizard zeigt die bestehende Konfiguration, am Ende läuft verify |

## M7 Prüfung über das Startmenü

Spec: verification «Servicetechniker nach dem Einsatz», «Bediener startet die Prüfung».

| Schritt | Erwartet |
|---|---|
| Als Admin «AppStart – Einrichtung prüfen» im Startmenü | UAC-Abfrage, Prüfansicht mit Ergebnis je Prüfpunkt, Bericht in `C:\ProgramData\AppStart\Reports` |
| Als appstart-user denselben Eintrag | UAC verlangt Admin-Zugangsdaten; ohne diese startet keine Prüfung |

## M8 Sicht des Bedieners in der Oberfläche

Spec: installation «Bediener liest Registry», test-app «Daten für Bediener gesperrt», installation «Icon übernommen».
Automatisch per API geprüft (`Invoke-AcceptanceTest.ps1`, Schritte 2 und 3); hier mit den echten Werkzeugen:

| Schritt | Erwartet |
|---|---|
| Als appstart-user `regedit` (ohne Admin) → `HKLM\SOFTWARE\AppStartTest` | «Zugriff verweigert» |
| Als appstart-user im Explorer `C:\ProgramData\AppStartTest` öffnen | «Zugriff verweigert»; die angebotene Schaltfläche «Fortsetzen» verlangt Admin-Zugangsdaten |
| Verknüpfung «AppStart TestApp» auf dem Desktop ansehen | zeigt das Icon der TestApp, nicht das des Launchers |

## M9 Zugriff über das Netzwerk

Spec: pipe-security «Zugriff über das Netzwerk».

| Schritt | Erwartet |
|---|---|
| Von einem zweiten Rechner im selben Netz mit einem Konto, das auf der Test-VM Mitglied von AppStart Users ist: `[System.IO.Pipes.NamedPipeClientStream]::new('<Test-VM>', 'AppStart').Connect(3000)` | Zugriff verweigert bzw. Verbindung abgewiesen (PIPE_REJECT_REMOTE_CLIENTS, NU verweigert) |

## Protokoll

| Test | Datum | Windows | AppStart | Ergebnis | Bemerkung |
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
