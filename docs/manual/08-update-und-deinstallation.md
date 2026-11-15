# Update und Deinstallation

## Update von AppStart

AppStart aktualisiert sich nie selbst. Updates installiert der Servicetechniker mit dem neuen Release.

**Interaktiv:** Neue `AppStart-Setup.exe` starten, UAC bestätigen. Das Installationsprogramm aktualisiert das MSI und öffnet danach den Assistenten mit der bestehenden Konfiguration. Dort ohne Änderung zweimal «Weiter»; am Ende läuft die Prüfung. Ist dieselbe oder eine neuere Version schon installiert, meldet es «AppStart ist bereits installiert» und öffnet nur den Assistenten.

**Still:**

```text
msiexec /i AppStart-<neue Version>.msi /qn /l*v C:\Windows\Temp\AppStart-update.log
"C:\Program Files\AppStart\current\AppStart.Setup.exe" verify
```

Oder `AppStart-Setup.exe /quiet` (Exit-Code wie `msiexec`).

Was beim Update geschieht:

1. Windows Installer erkennt die ältere Version und ersetzt sie (Major Upgrade).
2. Der Dienst `AppStart` wird gestoppt und gelöscht, die Startmenü-Einträge von AppStart werden entfernt.
3. Die neuen Programmdateien werden nach `C:\Program Files\AppStart\current` installiert.
4. Der Dienst wird neu registriert (automatisch, LocalSystem, Neustart nach Absturz) und gestartet, die Startmenü-Einträge werden neu angelegt.
5. Die Einrichtung wird automatisch geprüft; der Bericht liegt unter `C:\ProgramData\AppStart\Reports`.

**Unverändert bleiben:** `apps.json` mit allen Freigaben, Konten `appstart-app` und `appstart-user`, Gruppe `AppStart Users` mit ihren Mitgliedern, Verknüpfungen der Applikationen, Rechte auf Datenordnern und Registry-Keys, Prüfberichte.

Während des Updates (wenige Sekunden) können Bediener keine Applikationen starten («Start-Dienst nicht erreichbar»). Laufende Applikationen bleiben offen.

Nach dem Update: Prüfbericht ansehen («AppStart – Einrichtung prüfen»), Ergebnis «Keine Abweichung».

## Update einer Applikation

Siehe [Applikationen verwalten](04-applikationen-verwalten.md): bei Prüfart «SHA-256» immer, bei «Signatur» nur nach Zertifikatswechsel `AppStart.Setup.exe rehash --app <AppId>`, danach Prüfung. Updates der Applikation können Verknüpfungen und Rechte überschreiben; `verify` zeigt das.

## Deinstallation

Die Deinstallation hat zwei Schritte in **dieser Reihenfolge**. Der zweite Schritt allein entfernt nur Dienst und Programmdateien, nicht die Einrichtung.

**1. Einrichtung zurückstellen** (Eingabeaufforderung «Als Administrator ausführen»):

```text
"C:\Program Files\AppStart\current\AppStart.Setup.exe" uninstall
```

Das Setup stellt zurück, was es geändert hat:

| Was | Zustand danach |
|---|---|
| Verknüpfungen | AppStart-Verknüpfungen gelöscht; ursprüngliche Verknüpfungen, die direkt auf die EXE zeigten, an ihrem alten Ort wiederhergestellt |
| Datenordner und Registry-Keys | Rechte wie vor der Freigabe (Besitzer und Zugriffsliste des Ordners bzw. Keys; darunterliegende Einträge erben wieder) |
| Konten und Gruppe | `appstart-app` und `AppStart Users` gelöscht. `appstart-user` nur, wenn das Setup es angelegt hat. Aufgenommene Domänen-Benutzer bleiben unberührt (nur ihre Gruppenmitgliedschaft entfällt mit der Gruppe). |
| `apps.json` | gelöscht |
| Prüfberichte | bleiben unter `C:\ProgramData\AppStart\Reports` erhalten |

Erwartete Ausgabe:

```text
✓ Verknüpfungen zurückgestellt, Datenordner und Registry-Keys zurückgesetzt
✓ Konten und Gruppe entfernt
✓ Konfiguration entfernt (Prüfberichte bleiben erhalten)
```

Das Profil `C:\Users\appstart-app` bleibt nach dem Löschen des Kontos liegen. Es enthält Benutzereinstellungen der Applikationen; bei Bedarf über «Systemeigenschaften» → «Benutzerprofile» löschen.

**2. MSI deinstallieren:** «Einstellungen» → «Apps» → «Installierte Apps» → «AppStart» → «Deinstallieren», oder still:

```text
msiexec /x AppStart-<Version>.msi /qn
```

Dabei wird der Dienst gestoppt und gelöscht, der Startmenü-Ordner «AppStart» und der Programmordner `C:\Program Files\AppStart` werden entfernt.

**Übrig bleibt** `C:\ProgramData\AppStart` mit den Prüfberichten (`Reports`), Protokollen des Installationsprogramms (`Logs`) und dem Ordner `ShortcutBackup`. Nach Ablage der Berichte kann der Ordner als Administrator gelöscht werden. Die Quelle «AppStart» im Ereignisprotokoll bleibt bestehen, damit ältere Einträge lesbar bleiben.

> **Hinweis:** Wurde das MSI zuerst deinstalliert, ist `AppStart.Setup.exe` nicht mehr vorhanden. Dann AppStart erneut installieren, `uninstall` ausführen und das MSI erneut deinstallieren.

Nach der Deinstallation starten Bediener die Applikationen wieder wie vorher über die ursprünglichen Verknüpfungen, mit den Rechten ihres eigenen Kontos.
