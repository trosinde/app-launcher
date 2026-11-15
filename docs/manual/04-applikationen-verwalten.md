# Applikationen verwalten

Alle Änderungen gehen über den Assistenten («AppStart – Einrichtung») oder die Kommandozeile `AppStart.Setup.exe` mit Admin-Rechten. `apps.json` von Hand zu bearbeiten ist nicht nötig. Nach jeder Änderung läuft automatisch `verify`; der Bericht liegt unter `C:\ProgramData\AppStart\Reports`.

In den Beispielen steht `%SETUP%` für `"C:\Program Files\AppStart\current\AppStart.Setup.exe"`.

## Freigegebene Applikationen anzeigen

```text
%SETUP% list
```

Ausgabe je Applikation: AppId und Name, darunter EXE und Prüfart («Signatur» oder «SHA-256»). Danach die Mitglieder der Gruppe und der Zustand des Dienstes:

```text
app-a                Applikation A
                     C:\Program Files\Hersteller\AppA\AppA.exe (Signatur)
appstart-testapp     AppStart TestApp
                     C:\Program Files\AppStart\current\TestApp\AppStart.TestApp.exe (SHA-256)

Gruppe AppStart Users: PC-014\appstart-user, DOMAIN\bediener01
Dienst AppStart: läuft
```

Ohne Freigabe steht dort «Keine Applikation freigegeben.», ohne Mitglieder «Gruppe AppStart Users: –», bei gestopptem Dienst «Dienst AppStart: läuft nicht».

## Applikation hinzufügen

**Im Assistenten:** «AppStart – Einrichtung» öffnen, in Schritt 1 «EXE hinzufügen…», Datenordner und Registry-Key eintragen, «Weiter», Schritt 2 unverändert lassen, «Weiter». Bestehende Applikationen bleiben angehakt und unverändert.

**Über die Kommandozeile:**

```text
%SETUP% add --exe "C:\Program Files\Hersteller\AppA\AppA.exe" --data-dir "C:\ProgramData\Hersteller\AppA" --reg-key "HKLM\SOFTWARE\Hersteller\AppA" --name "Applikation A"
```

Was das Setup dabei tut:

1. EXE lesen: Ist sie gültig signiert, wird der Thumbprint des Zertifikats erfasst (Prüfart «Signatur»), sonst die SHA-256-Prüfsumme.
2. AppId aus dem Dateinamen bilden (`AppA.exe` → `appa`, `AppStart.TestApp.exe` → `appstart-testapp`); bei Gleichstand mit Zusatz `-2`, `-3` …
3. Eintrag in `apps.json` schreiben, Datei geschützt (nur SYSTEM und Administratoren).
4. Datenordner und Registry-Key sperren: Zugriff nur für `appstart-app`, SYSTEM und Administratoren. Die ursprünglichen Rechte werden gesichert und bei `remove` und `uninstall` zurückgestellt.
5. Verknüpfungen, die direkt auf die EXE zeigen (öffentliches Startmenü, öffentlicher Desktop, Desktop und Startmenü jedes Benutzerprofils), nach `C:\ProgramData\AppStart\ShortcutBackup` sichern und entfernen.
6. Neue Verknüpfung `<Name>.lnk` im öffentlichen Startmenü (`C:\ProgramData\Microsoft\Windows\Start Menu\Programs`) und auf dem öffentlichen Desktop (`C:\Users\Public\Desktop`) anlegen: Ziel `AppStart.Launcher.exe --app <AppId>`, Icon aus der EXE, Arbeitsordner = Ordner der EXE.
7. Dienst prüfen und bei Bedarf starten, danach `verify`.

Ist die EXE schon freigegeben, aktualisiert `add` die Freigabe: neue Signatur bzw. Prüfsumme, und wenn angegeben neuer Name, Datenordner oder Registry-Key. AppId und bisherige Angaben bleiben sonst erhalten.

> **Hinweis:** Existiert am Zielort bereits eine Verknüpfung mit demselben Namen, die nicht von AppStart stammt, überschreibt das Setup sie nicht und meldet «Verknüpfungen konnten nicht angelegt werden: Die Verknüpfung «…» existiert bereits und gehört nicht zu dieser Applikation. Sie wird nicht überschrieben. Bitte die Datei prüfen und von Hand sichern oder entfernen.» Datei prüfen, entfernen und den Befehl wiederholen, oder mit `--name` einen anderen Namen wählen.

## Datenordner oder Registry-Key ändern

```text
%SETUP% add --exe "C:\Program Files\Hersteller\AppA\AppA.exe" --data-dir "D:\Daten\AppA"
```

Ein neu angegebener Ordner oder Key wird zusätzlich gesperrt. Ein früher gesperrter Ordner bleibt gesperrt, bis die Applikation entfernt wird. Soll der alte Ordner wieder frei sein: Applikation mit `remove` entfernen und mit den neuen Angaben neu hinzufügen.

## Applikation entfernen

**Im Assistenten:** In Schritt 1 die Applikation markieren und «Entfernen» klicken (oder den Haken entfernen), dann «Weiter», «Weiter».

**Über die Kommandozeile:**

```text
%SETUP% remove --app app-a
```

Das Setup entzieht die Freigabe und stellt zurück:

- Rechte von Datenordner und Registry-Key auf den Stand vor der Freigabe,
- die gesicherten Verknüpfungen, die direkt auf die EXE zeigten,
- die AppStart-Verknüpfungen der Applikation werden gelöscht.

Die Applikation selbst bleibt installiert. Unbekannte AppId: «Applikation «app-x» ist nicht freigegeben», Exit-Code 1. Die AppIds zeigt `list`.

## Nach einem Update der Applikation

| Prüfart | Was tun |
|---|---|
| «Signatur» | Nichts, solange der Hersteller mit demselben Zertifikat signiert. Hat er ein neues Zertifikat (z.B. nach Ablauf), lehnt der Dienst den Start ab (Ereignis 1201, «Signatur gültig, aber fremdes Zertifikat»). Dann `rehash` ausführen. |
| «SHA-256» | **Immer** `rehash` ausführen, sonst lehnt der Dienst jeden Start ab (Meldung «Start verweigert», Ereignis 1201). |

```text
%SETUP% rehash --app app-a
```

`rehash` liest die EXE neu und erfasst Signatur oder Prüfsumme neu. Ist die EXE inzwischen signiert, wechselt die Prüfart auf «Signatur». Danach läuft `verify`.

> **Achtung:** `rehash` erst ausführen, wenn feststeht, dass die neue EXE vom Hersteller stammt (Update-Protokoll, Signatur in den Dateieigenschaften). Ohne bekanntes Update ist eine veränderte EXE ein Sicherheitsvorfall.

Prüfen nach dem Update auch:

- Hat das Update der Applikation eigene Verknüpfungen angelegt, die direkt auf die EXE zeigen? `verify` meldet sie unter «Verknüpfungen». Behebung: Einrichtung erneut ausführen («AppStart – Einrichtung», «Weiter», «Weiter») oder `rehash`; das Setup sichert und entfernt solche Verknüpfungen.
- Hat das Update die Rechte von Datenordner oder Registry-Key zurückgesetzt? `verify` meldet es unter «Datenordner je App» bzw. «Registry-Key je App». Behebung wie oben.
- Liegt die EXE an einem neuen Pfad? Dann mit `remove` die alte Freigabe entziehen und mit `add` die neue EXE freigeben.

## Weitere Programme (Hilfsprogramme)

Manche Applikationen starten eigene Hilfsprogramme, etwa für Export, Update oder PDF-Anzeige. Je Applikation gilt einer von drei Modi:

| Modus | Anzeige | Wirkung |
|---|---|---|
| `blocked` | gesperrt | Standard. Die Applikation kann keine weiteren Programme starten. |
| `allowlist` | nur erlaubte Hilfsprogramme | Nur Programme aus der Liste starten, geprüft mit Signatur oder SHA-256. Jedes andere Programm beendet der Dienst sofort (Ereignis 1206). |
| `all` | alle erlaubt (unsicher) | Jedes Programm startet, auch eine Eingabeaufforderung **mit Admin-Rechten**. Nur nach Abwägung und mit Vermerk im Service and Security Manual. |

Das Konsolenfenster von Windows (`conhost.exe` aus `C:\Windows\System32`) ist immer erlaubt; ohne es zeigen Konsolen-Hilfsprogramme kein Fenster.

Hilfsprogramm erlauben, Modus wird «nur erlaubte Hilfsprogramme»:

```text
%SETUP% allow-child --app app-a --exe "C:\Program Files\Hersteller\AppA\Export.exe"
```

Ausgabe: «Hilfsprogramm … für «app-a» erlaubt (Signatur)» bzw. «(SHA-256)» und «Weitere Programme für «app-a»: nur erlaubte Hilfsprogramme». Ist das Programm gültig signiert, gilt die Signatur, sonst die Prüfsumme. Ein Eintrag mit demselben Pfad wird ersetzt.

Hilfsprogramm entfernen: «Hilfsprogramm … für «app-a» entfernt». Beim letzten Eintrag zusätzlich «Letztes Hilfsprogramm entfernt: weitere Programme für «app-a» wieder gesperrt».

Nach einem Update unsignierter Hilfsprogramme stimmt die Prüfsumme nicht mehr und der Dienst beendet sie. `rehash` erfasst sie neu: «Prüfsummen der Hilfsprogramme von «app-a» neu erfasst (1)».

Meldungen, bei denen nichts geändert wird:

| Meldung | Bedeutung |
|---|---|
| «… ist eine Shell oder ein Skript-Host. Nur mit ausdrücklicher Bestätigung (--allow-shell) erlaubt.» | Eingabeaufforderung, PowerShell, Explorer, Skript-Hosts, `rundll32.exe`, `regsvr32.exe` und `mshta.exe` erlaubt AppStart nur, wenn Sie es ausdrücklich bestätigen. Damit kann jeder Bediener jedes Programm mit Admin-Rechten starten. |
| «Hilfsprogramm nicht erlaubt: … liegt in einem Ordner, den Bediener beschreiben können» | Ein Bediener könnte die Datei austauschen. Programm in einen geschützten Ordner (z.B. unter `C:\Program Files`) installieren. |
| «… ist nicht gültig signiert. Prüfung «signature» nicht möglich, «sha256» verwenden.» | Mit `--check signature` angegeben, die Datei hat aber keine gültige Signatur. |
| ««…» ist kein Pfad auf eine EXE.», «… existiert nicht.» | Pfad prüfen. |
| ««nur erlaubte Hilfsprogramme» braucht mindestens ein Hilfsprogramm. Zuerst allow-child ausführen.» | Der Modus lässt sich erst mit mindestens einem Eintrag einstellen. |
| «Hilfsprogramm … steht nicht in der Liste von «app-a»» | Beim Entfernen oder `rehash --child`: Pfad wie in `list` angeben. |

Modus direkt setzen, z.B. alle erlauben: «Weitere Programme für «app-a»: alle erlaubt (unsicher)». Der Dienst liest `apps.json` bei jedem Start neu; ein Neustart des Dienstes ist nicht nötig.
