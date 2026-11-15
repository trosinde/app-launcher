# Funktionsweise

Diese Seite erklärt, was beim Start einer Applikation passiert, welche Konten und Dateien AppStart nutzt und welche Meldungen auftreten können.

## Begriffe

| Begriff | Bedeutung |
|---|---|
| **Bediener** | Person, die am PC arbeitet. Hat keine Admin-Rechte. |
| **Freigegebene Applikation** | Applikation, die in `apps.json` eingetragen ist und über AppStart starten darf |
| **AppId** | Kurze Kennung einer freigegebenen Applikation, z. B. `app-a`. Nur Kleinbuchstaben, Ziffern und Bindestrich, maximal 32 Zeichen. |
| **App-Konto** `appstart-app` | Lokales Konto mit Admin-Rechten, unter dem die Applikationen laufen. Niemand kann sich damit anmelden. |
| **Bedienerkonto** `appstart-user` | Optionales lokales Standardkonto für Bediener |
| **Gruppe** `AppStart Users` | Nur ihre Mitglieder dürfen Applikationen über AppStart starten |
| **Launcher** | Kleine EXE hinter jeder Verknüpfung, schickt die Startanfrage an den Dienst |
| **Named Pipe** | Lokaler Kanal zwischen Launcher und Dienst. Von anderen Rechnern aus nicht erreichbar. |
| **Job-Objekt** | Windows-Mechanismus, der verhindert, dass die Applikation weitere Programme startet |
| **S4U-Anmeldung** | Der Dienst meldet `appstart-app` an, ohne ein Passwort zu kennen |
| **Thumbprint** | Eindeutiger Fingerabdruck eines Zertifikats |

## Ablauf beim Start

```mermaid
sequenceDiagram
    actor B as Bediener
    participant L as Launcher
    participant S as AppStart-Dienst
    participant A as Applikation
    B->>L: Klick auf Verknüpfung (--app app-a)
    L->>L: Format der AppId prüfen
    L->>S: START app-a (Named Pipe)
    S->>S: Session und Benutzer des Anfragenden ermitteln
    S->>S: apps.json lesen und Dateirechte prüfen
    S->>S: EXE sperren, Signatur oder SHA-256 prüfen
    S->>A: als appstart-app in der Session des Bedieners starten
    S->>S: Eintrag ins Ereignisprotokoll
    S-->>L: OK
    A-->>B: Fenster erscheint
```

Im Detail:

1. **Verknüpfung.** Jede freigegebene Applikation hat eine eigene Verknüpfung im öffentlichen Startmenü und auf dem Desktop. Sie zeigt auf `AppStart.Launcher.exe --app <AppId>` und trägt das Icon der Applikation.
2. **Launcher.** Prüft nur das Format der AppId und sendet `START <AppId>` an die Named Pipe `\\.\pipe\AppStart`. Er übergibt keine Pfade, Argumente oder Umgebungsvariablen.
3. **Annahme.** Der Dienst ermittelt Windows-Session und Benutzer des Anfragenden. Pro Session wird eine Anfrage gleichzeitig bearbeitet. Nach jeder Anfrage, auch einer abgelehnten, gilt für die Session eine Sperrzeit von 2 Sekunden.
4. **Prüfung.** Der Dienst liest `apps.json` bei jeder Anfrage neu. Ist die Datei für Nicht-Administratoren beschreibbar, verweigert er jeden Start. Danach öffnet er die EXE so, dass sie bis zum Start nicht verändert, umbenannt oder gelöscht werden kann, und prüft Signatur oder SHA-256.
5. **Start.** Der Dienst meldet `appstart-app` ohne Passwort an (S4U-Anmeldung), lädt dessen Profil und startet die Applikation in der Session des Bedieners. Ist `allowChildProcesses` auf `false` (Standard), läuft die Applikation in einem Job-Objekt und kann keine weiteren Programme starten.
6. **Protokoll.** Jede Anfrage, ob erfolgreich oder abgelehnt, wird mit Zeit, Benutzer, Session, AppId und Ergebnis ins Ereignisprotokoll geschrieben.

## Konfiguration `apps.json`

Pfad: `C:\ProgramData\AppStart\apps.json`. Nur SYSTEM und Administratoren dürfen die Datei lesen und schreiben. Das Setup pflegt sie, von Hand muss sie nicht bearbeitet werden.

```json
{
  "version": 1,
  "apps": [
    {
      "id": "app-a",
      "name": "Applikation A",
      "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
      "workingDir": "C:\\Program Files\\Hersteller\\AppA",
      "check": "signature",
      "signerThumbprint": "<Thumbprint des Signaturzertifikats>",
      "sha256": null,
      "allowChildProcesses": false,
      "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
      "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA"
    }
  ]
}
```

| Feld | Bedeutung |
|---|---|
| `id` | AppId, steht auch in der Verknüpfung |
| `name` | Anzeigename, z. B. im Titel von Meldungen |
| `exe`, `workingDir` | Programmdatei und Arbeitsordner |
| `check` | `signature`: Signatur muss gültig sein und vom erwarteten Zertifikat stammen. Übersteht Updates der Applikation. `sha256`: Prüfsumme muss exakt stimmen. Für unsignierte EXE, nach jedem Update neu erfassen. |
| `signerThumbprint` / `sha256` | Erwartetes Zertifikat bzw. erwartete Prüfsumme |
| `allowChildProcesses` | `false` (Standard): Applikation darf keine weiteren Programme starten |
| `dataDir`, `registryKey` | Optional. Diese Ordner und Keys sperrt das Setup für Bediener. |

## Konten und Rechte

| Konto / Gruppe | Wird angelegt | Rechte |
|---|---|---|
| `appstart-app` | immer | Mitglied der Administratoren. Interaktive Anmeldung und Remote Desktop verweigert. Zufallspasswort, das niemand kennt und das nirgends gespeichert wird. |
| `AppStart Users` | immer | Darf die Pipe des Dienstes öffnen, sonst nichts |
| `appstart-user` | auf Wunsch | Standard-Benutzer, Mitglied von `AppStart Users`. Alternativ bestehende lokale oder Domänen-Benutzer in die Gruppe aufnehmen. |

Was das Setup sperrt:

| Objekt | Zugriff nur für |
|---|---|
| `C:\Program Files\AppStart` (schreiben) | SYSTEM, Administratoren |
| `C:\ProgramData\AppStart\apps.json` | SYSTEM, Administratoren |
| Datenordner je Applikation | `appstart-app`, SYSTEM, Administratoren |
| Registry-Key je Applikation | `appstart-app`, SYSTEM, Administratoren |

## Pipe-Protokoll

| Richtung | Format | Beispiel |
|---|---|---|
| Anfrage | `START <AppId>`, ASCII, max. 64 Byte | `START app-a` |
| Antwort Erfolg | `OK` | `OK` |
| Antwort Ablehnung | `DENIED <grund> <vorgangsnummer>` | `DENIED integrity 4821930571` |

Gründe: `unknown-app`, `integrity`, `config`, `busy`, `error`. Der Launcher erfährt nur den Grund. Details stehen ausschliesslich im Ereignisprotokoll. Eine Anfrage muss innerhalb von 2 Sekunden eintreffen, sonst wird die Verbindung geschlossen. Ungültige Anfragen beantwortet der Dienst nicht, sie erscheinen nur als Ereignis 1204.

Die **Vorgangsnummer** ist eine zufällige 10-stellige Zahl je Ablehnung. Sie steht im Text des Eintrags im Ereignisprotokoll («Vorgang: …») und verbindet so die Meldung beim Bediener mit dem Protokolleintrag.

## Meldungen des Launchers

Bei Erfolg zeigt der Launcher nichts. Sonst erscheint eine dieser Meldungen. Der Fenstertitel ist der Name der Verknüpfung, also der Name der Applikation.

Bei einer Ablehnung (ausser «Start läuft bereits») steht unten eine 10-stellige Nummer, derzeit beschriftet als «Ereignis-ID». Es ist die **Vorgangsnummer**, nicht die ID des Ereignistyps. So finden Sie den Eintrag: Ereignisanzeige → Windows-Protokolle → Anwendung, Quelle AppStart, mit «Suchen…» nach der Nummer suchen.

<p align="center">
  <img src="images/launcher-meldungen.png" width="420" alt="Zwei Launcher-Meldungen: Start-Dienst nicht erreichbar, und Start verweigert mit Vorgangsnummer">
  <br>
  <sub>Oben: Dienst läuft nicht. Unten: EXE verändert, mit Vorgangsnummer für die Suche im Ereignisprotokoll. (UI-Entwurf)</sub>
</p>

| Meldung | Ursache | Massnahme |
|---|---|---|
| **Start-Dienst nicht erreichbar** | Dienst `AppStart` läuft nicht | Dienst starten, Ereignisprotokoll prüfen |
| **Keine Berechtigung** | Benutzer ist nicht in der Gruppe `AppStart Users` | Benutzer in die Gruppe aufnehmen, neu anmelden |
| **Ungültige Verknüpfung** | Verknüpfung ohne oder mit falscher AppId | Verknüpfung über das Setup neu anlegen |
| **Start verweigert** – Applikation verändert | Signatur oder Prüfsumme stimmt nicht | Herkunft der EXE klären; nach legitimem Update `rehash` |
| **Start verweigert** – nicht freigegeben | AppId steht nicht in `apps.json` | Applikation im Setup freigeben |
| **Start verweigert** – Einrichtung nicht in Ordnung | `apps.json` fehlt, ist ungültig oder für Nicht-Admins beschreibbar | `verify` ausführen, Einrichtung reparieren |
| **Start verweigert** – sonstiger Fehler | Start ist technisch gescheitert | Eintrag zur Vorgangsnummer im Ereignisprotokoll prüfen |
| **Start läuft bereits** | Zweiter Klick während des Starts | kurz warten, erneut versuchen |
| **Unerwartete Antwort** | Antwort des Dienstes nicht lesbar | Ereignisprotokoll prüfen, Versionen von Launcher und Dienst vergleichen |

## Ereignisprotokoll

Protokoll **Anwendung**, Quelle **AppStart**.

| ID | Ereignis |
|---|---|
| 1000 | Dienst gestartet |
| 1001 | Dienst beendet |
| 1100 | Applikation gestartet |
| 1200 | Abgelehnt: AppId unbekannt |
| 1201 | Abgelehnt: Signatur oder Prüfsumme falsch |
| 1202 | Abgelehnt: Konfiguration fehlerhaft oder unsicher |
| 1203 | Abgelehnt: Start in dieser Session läuft bereits |
| 1204 | Ungültige Anfrage (Format, Länge, Zeitüberschreitung) |
| 1205 | Start technisch gescheitert |
| 1300 | Pipe existiert bereits – Dienst startet nicht |
| 1301 | Pipe konnte nicht angelegt werden |
| 1302 | Fehler an der Pipe |

Ereignis 1300 ist ein Sicherheitsereignis: Ein anderer Prozess hat die Pipe vor dem Dienst angelegt. Der Dienst startet dann bewusst nicht.

## Einrichtung

> In Arbeit. Die Bilder zeigen die verbindlichen UI-Entwürfe, nicht die fertige Software.

Das Setup führt in drei Schritten durch die Einrichtung. Dasselbe geht still über die Kommandozeile (siehe [README](../README.md#installation-vorschau)).

**1 · Applikationen auswählen.** EXE hinzufügen, je Applikation Datenordner und Registry-Key angeben. Das Setup erkennt, ob die EXE signiert ist, und wählt die Prüfart: Signatur oder SHA-256.

<p align="center"><img src="images/setup-1-applikationen.png" width="640" alt="Setup Schritt 1: Applikationen auswählen"></p>

**2 · Konten und Gruppe.** App-Konto und Gruppe werden immer angelegt. Bedienerkonto `appstart-user` neu anlegen oder bestehende Benutzer in die Gruppe aufnehmen.

<p align="center"><img src="images/setup-2-konten.png" width="640" alt="Setup Schritt 2: Konten und Gruppe"></p>

**3 · Zusammenfassung.** Zeigt, was eingerichtet wurde, und das Ergebnis der automatischen Prüfung mit Pfad zum Bericht.

<p align="center"><img src="images/setup-3-zusammenfassung.png" width="640" alt="Setup Schritt 3: Zusammenfassung mit bestandener Prüfung"></p>

## Einrichtung prüfen (`verify`)

> In Arbeit.

`AppStart.Setup.exe verify` prüft die gesamte Einrichtung gegen den Sollzustand und legt einen Bericht als HTML und JSON unter `C:\ProgramData\AppStart\Reports` ab. Exit-Code 0 nur, wenn alles stimmt. Gedacht für die Kontrolle nach der Installation und nach jedem Serviceeinsatz – etwa wenn ein Techniker vorübergehend Rechte geändert hat.

<p align="center">
  <img src="images/pruefbericht.png" width="640" alt="Prüfbericht mit einer Abweichung: Bedienerkonto ist Mitglied der Administratoren">
  <br>
  <sub>Prüfbericht mit einer Abweichung: Das Bedienerkonto ist wieder Administrator. (UI-Entwurf)</sub>
</p>

| Prüfpunkt | Soll |
|---|---|
| Dienst | installiert, läuft, Start automatisch, Konto LocalSystem |
| Programmordner | nur SYSTEM und Administratoren schreiben |
| Konten | `appstart-app` vorhanden, keine interaktive Anmeldung, Recht «Als Batchauftrag anmelden»; Bediener ohne Admin-Rechte |
| Pipe | Rechte wie vorgesehen, kein Fernzugriff |
| `apps.json` | nur SYSTEM und Administratoren; jede EXE besteht die Prüfung |
| Datenordner je App | nur `appstart-app`, SYSTEM, Administratoren |
| Registry-Key je App | für Bediener nicht lesbar |
| Verknüpfungen | zeigen auf den Launcher, keine direkte Verknüpfung auf die EXE |

## Update und Deinstallation

- **Update von AppStart:** neues MSI über das alte installieren. Der Dienst wird gestoppt, ersetzt und neu gestartet. Konfiguration, Konten und Verknüpfungen bleiben erhalten. Automatische Updates gibt es nicht.
- **Update einer Applikation:** Bei `check: signature` ist nichts zu tun, solange der Hersteller mit demselben Zertifikat signiert. Bei `check: sha256` danach `AppStart.Setup.exe rehash --app <AppId>`.
- **Deinstallation:** zuerst `AppStart.Setup.exe uninstall` (stellt ursprüngliche Verknüpfungen und Rechte wieder her, entfernt Konten), dann das MSI deinstallieren.
