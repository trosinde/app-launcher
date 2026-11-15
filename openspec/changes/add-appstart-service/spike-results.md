# Spike-Ergebnisse (tasks.md Abschnitt 1)

Stand: 15.11.2026. Abschnitt 3 beginnt erst, wenn 1.1–1.4 hier dokumentiert sind.

| Spike | Stand | Ergebnis |
|---|---|---|
| 1.1 S4U-Anmeldung | erledigt (15.11.2026) | S4U funktioniert, kein gespeichertes Passwort nötig |
| 1.2 Anzeige in User-Session | erledigt (15.11.2026) | Applikation läuft sichtbar in der Bediener-Session |
| 1.3 Velopack MSI per-machine | erledigt (15.11.2026) | Velopack geeignet, zwei Spec-Anpassungen nötig |
| 1.4 Ziel-Applikationen | mit TestApp erledigt, reale Applikationen offen | TestApp läuft über den Startweg, mit Job-Objekt |

## 1.1 und 1.2 – App-Konto und Anzeige

Werkzeug: `spikes/AppStart.Spike.Launch` (Wegwerf-Code, kein Produktcode), `spikes/Prepare-TestVm.ps1`, `spikes/Run-Spike.ps1`.
Normalerweise nur auf der Test-VM mit Snapshot. Der Lauf vom 15.11.2026 erfolgte auf Anweisung des Auftraggebers auf dem Entwicklungsrechner (Windows 11 Pro 10.0.26300, deutsch), mit automatischem Rückbau durch `spikes/Remove-SpikeSetup.ps1`. Ablauf: `spikes/Invoke-AllSpikes.ps1`.

Ablauf im Spike (entspricht dem späteren Dienst):

1. Läuft als SYSTEM in Session 0 (geplanter Task statt Dienst).
2. `LsaRegisterLogonProcess` + `LsaLogonUser` mit `MSV1_0_S4U_LOGON` für `appstart-app` (ohne Passwort).
3. Ist das Token UAC-gefiltert (ElevationType Limited), wird das verknüpfte Voll-Token (`TokenLinkedToken`) genommen.
4. Primär-Token, `TokenSessionId` = Session des Bedieners.
5. Hilfsprozess als SYSTEM in der Bediener-Session ergänzt die DACL von `WinSta0` und `Default` um die SID von `appstart-app`. Aus Session 0 ist `WinSta0` einer anderen Session nicht erreichbar.
6. `LoadUserProfile`, `CreateEnvironmentBlock`, `CreateProcessAsUser` auf `winsta0\default`, optional im Job-Objekt mit `ActiveProcessLimit = 1`.
7. Der gestartete Prozess (`probe`) zeigt Konto, Session, Admin, HKCU-Pfad und versucht `cmd.exe` zu starten.

Zu klärende Punkte und Erwartung vor dem Lauf:

| Variante | Frage | Erwartung |
|---|---|---|
| S4UBatch | Liefert S4U ein volles Admin-Token? | Ja, ggf. über das verknüpfte Token. Voraussetzung: Recht «Als Batchauftrag anmelden» (Administratoren haben es standardmässig) |
| S4UNetwork | Alternative ohne Batch-Recht? | Token entsteht, Eignung für Desktop-Prozess prüfen |
| S4UInteractive | Wirkt die Anmeldesperre? | Muss scheitern, da interaktive Anmeldung verweigert. **Folge: der Dienst darf nicht den Logon-Typ Interactive verwenden.** |
| NoDesktopAcl | Ist die DACL-Ergänzung nötig? | Ohne sie Abbruch mit `0xC0000142` oder unsichtbares Fenster |
| Job | Verhindert das Job-Objekt Kindprozesse? | `cmd.exe` wird blockiert |

Bekannte Einschränkung von S4U (Doku): Das Token hat keine Netzwerk-Anmeldeinformationen. Applikationen, die mit dem App-Konto auf Netzlaufwerke oder Datenbanken mit Windows-Anmeldung zugreifen, funktionieren damit nicht. Das in 1.4 je Applikation prüfen. Wenn nötig, Fallback auf Passwort (DPAPI LocalMachine).

### Ergebnis vom 15.11.2026

| Variante | Ergebnis |
|---|---|
| S4UBatch | **OK.** `LsaLogonUser` S4U mit Logon-Typ Batch: Status 0. Token sofort voll (ElevationType Default, elevated), kein UAC-Split, Mitglied der Administratoren. Die Anmeldesperre (interaktiv und RDP verweigert) stört nicht. |
| S4UNetwork | OK, gleiches Token wie Batch. Kein Vorteil, nicht verwenden. |
| S4UInteractive | Scheitert mit `0xC00000A7` (STATUS_BAD_VALIDATION_CLASS). S4U unterstützt den Typ Interactive grundsätzlich nicht. Das ist also **kein** Nachweis der Anmeldesperre. Die Sperre muss `verify` direkt aus der Benutzerrechte-Richtlinie prüfen. |
| NoDesktopAcl | Prozess startet in Session 1, sein Fenster war sichtbar und bedienbar (vom Auftraggeber am Bildschirm bestätigt, ebenso bei allen anderen Varianten), auch ohne ergänzte DACL. Auf diesem Windows-11-Build reicht der Zugriff offenbar schon. Die DACL-Ergänzung funktioniert trotzdem (siehe unten) und bleibt im Dienst, weil sie auf älteren Builds und unter Windows 10 nötig sein kann. Nachtest unter Windows 10 in 8.4. |
| Desktop-DACL | Hilfsprozess als SYSTEM in der Bediener-Session ergänzt `WinSta0` und `Default` um die SID von `appstart-app`: OK. |
| Profil | `LoadUserProfile` OK. HKCU der Applikation liegt im eigenen Profil `C:\Users\appstart-app`, wie im Konzept vorgesehen. |
| Job | Job-Objekt mit `ActiveProcessLimit = 1`: Start von `cmd.exe` scheitert mit «Nicht genügend Quoten» (ERROR_NOT_ENOUGH_QUOTA). Ohne Job startet `cmd.exe`. Bestätigt die Ausbruchssperre. |

Entscheide für den Dienst (gehen in Abschnitt 4):

1. Token per S4U mit Logon-Typ **Batch**. Kein Passwort speichern, DPAPI-Fallback entfällt. Voraussetzung: `appstart-app` hat «Als Batchauftrag anmelden» (über die Administratoren gegeben). `verify` prüft das.
2. `TokenSessionId` auf die Session des Anfragenden setzen, `LoadUserProfile`, `CreateEnvironmentBlock`, `CreateProcessAsUser` auf `winsta0\default`, suspended starten, in den Job, dann fortsetzen.
3. Desktop-DACL über einen kurzlebigen SYSTEM-Hilfsprozess in der Ziel-Session ergänzen.
4. Logon-Typ Interactive nie verwenden.
5. Konsolen-Applikationen: Bei `ActiveProcessLimit = 1` wird ggf. kein `conhost.exe` zugelassen. Betroffene Applikationen sind GUI-Programme. In 1.4 je Applikation prüfen.

Offen aus dem Lauf: S4U-Token haben keine Netzwerk-Anmeldedaten. Ob Ziel-Applikationen Netzwerkzugriff mit Windows-Anmeldung brauchen, klärt 1.4.

## 1.3 – Velopack MSI per-machine

Praxistest am 15.11.2026 auf dem Entwicklungsrechner (auf Anweisung des Auftraggebers), Velopack/vpk 1.2.161.
Werkzeug: `spikes/AppStart.Spike.Velopack` (Dummy-App mit Dienst, protokolliert jeden Hook), `spikes/Invoke-VelopackSpike.ps1`.
Ablauf: MSI 1.0.0 installieren → MSI 1.0.1 darüber installieren → deinstallieren → mit `VELOPACK_INSTALLDIR` installieren und deinstallieren. Alles still (`msiexec /qn`).

| Prüfpunkt | Ergebnis |
|---|---|
| Installationsort | `C:\Program Files\{packTitle}`, nicht wie in der Doku `{Publisher}\{packTitle}`. Mit `--packTitle AppStart` ergibt das genau `C:\Program Files\AppStart` wie im Konzept. |
| Rechte Programmordner | Von Program Files geerbt: Benutzer nur Lesen/Ausführen, Schreiben nur SYSTEM, Administratoren, TrustedInstaller. Erfüllt die Vorgabe ohne eigenes Zutun. |
| Hook `--veloapp-install` | Läuft nach der Installation, **mit Admin-Rechten** unter dem installierenden Admin (nicht SYSTEM). Dienst anlegen per `sc create`, Wiederanlauf setzen, starten: OK, in < 1 s. |
| Dienst nach Installation | Läuft, Start automatisch, Konto LocalSystem, Pfad `…\current\<exe> --service`. |
| Neues MSI über altes | Wirkt als Major Upgrade: Dienst wird gestoppt, Hook `--veloapp-uninstall` der alten Version, dann `--veloapp-install` der neuen. Die Hooks `obsolete`/`updated` laufen dabei **nicht**. Danach ist genau ein Produkt installiert und der Dienst läuft in der neuen Version. |
| Deinstallation | Dienst wird gestoppt, Hook `--veloapp-uninstall` löscht ihn, Ordner und Produkt sind weg. |
| `VELOPACK_INSTALLDIR` | Wird beachtet. Nicht nötig, da der Standardpfad schon passt. |
| Einträge in «Programme» | Zwei Uninstall-Einträge in der Registry (Velopack-Eintrag und MSI-Produkt). Sichtbarkeit in «Apps und Features» in 7.1 prüfen. |

Entscheide und Folgen:

1. **Velopack bleibt.** Ein Wechsel auf WiX ist nicht nötig.
2. Paket mit `--msi --instLocation PerMachine --packTitle AppStart` bauen.
3. Der Dienst wird im Install-Hook registriert und gestartet und im Uninstall-Hook gestoppt und gelöscht. Limit 30 s, keine Oberfläche.
4. **Der Setup-Dialog kann nicht aus dem Installer starten.** Hooks dürfen keine Oberfläche zeigen. Nach dem MSI startet der Installateur «AppStart – Einrichtung» aus dem Startmenü oder ruft `AppStart.Setup.exe add …` auf. → **Abweichung vom Konzept** («Danach startet AppStart.Setup … und zeigt den Auswahldialog»). Spec `installation` muss angepasst werden.
5. **Der Uninstall-Hook läuft auch bei jedem Update.** Er darf deshalb nur den Dienst entfernen. Verknüpfungen zurückstellen, Konten löschen und Rechte zurücksetzen gehören in `AppStart.Setup.exe uninstall`, das der Techniker vor `msiexec /x` ausführt. Der Install-Hook findet `apps.json`, Konten und Verknüpfungen nach einem Update unverändert vor. → Spec `installation` ergänzen.
6. Nach jedem Update läuft `verify`. Der Install-Hook kann `verify` nicht mit Oberfläche zeigen. Er schreibt nur den Bericht und protokolliert ins Ereignisprotokoll.

## 1.4 – Ziel-Applikationen

Die Liste der realen Applikationen liegt noch nicht vor. Auf Wunsch des Auftraggebers wurde stattdessen `AppStart.TestApp` gebaut (tasks.md Abschnitt 2), die sich wie ein betroffener Fat Client verhält: Manifest `requireAdministrator`, Klartext-Passwort in `HKLM\SOFTWARE\AppStartTest`, Austauschdatei in `C:\ProgramData\AppStartTest`.

Test am 15.11.2026 mit `spikes/Invoke-TestAppSpike.ps1`: Start über den späteren Startweg (SYSTEM → S4U-Token `appstart-app` → Bediener-Session), Bedienung per UI Automation, Bildschirmfotos.

| Applikation | Signiert | Datenordner | Registry-Key Passwörter | Läuft ohne Kindprozesse | Netzwerk mit Windows-Anmeldung |
|---|---|---|---|---|---|
| AppStart.TestApp | nein (Zertifikat fehlt, 7.2) | `C:\ProgramData\AppStartTest` | `HKLM\SOFTWARE\AppStartTest` | ja | nein |
| _reale Applikationen_ | | | | | |

Ergebnis TestApp:

| Prüfung | Ergebnis |
|---|---|
| Start über Startweg mit Job-Objekt | Fenster in Session 1, Konto `appstart-app`, Admin ja, Registry und Datei lesen/schreiben OK, Speichern OK (LastWriter `appstart-app`). Manifest `requireAdministrator` ist kein Hindernis, da das S4U-Token voll ist. |
| «Eingabeaufforderung starten» mit Job-Objekt | Blockiert («Start blockiert durch Job-Objekt») |
| «Eingabeaufforderung starten» ohne Job-Objekt (Kontrolle) | `cmd.exe` startet als `appstart-app` mit Admin-Rechten. Das bestätigt, warum das Job-Objekt Pflicht ist. |
| Direkt ohne Admin-Rechte (Manifest per `__COMPAT_LAYER=RunAsInvoker` umgangen) | Meldung «Die Testapplikation benötigt Administratorrechte und wird beendet», Exit-Code 1 |
| Datei und Registry für Bediener gesperrt | offen, braucht `AppStart.Setup` (6.3) und das Bedienerkonto. Abnahmetest 8.3. |

Für jede reale Applikation dieselbe Prüfung mit `Invoke-TestAppSpike.ps1` (EXE anpassen) und alle Funktionen durchspielen: Drucken, Export, Hilfe, Datei-öffnen-Dialog.
