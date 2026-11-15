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

### Im Assistenten

In Schritt 1 zeigt die Spalte «Weitere Programme» den Modus jeder Applikation, z.B. «2 erlaubt», «gesperrt» oder «alle erlaubt». Unter «Schutz für …» wählen Sie bei «Weitere Programme»:

- «Gesperrt (empfohlen)»: Hinweis «Applikation … kann keine weiteren Programme starten.»
- «Nur erlaubte Hilfsprogramme»: Hinweis «Nur Programme auf der Liste dürfen aus … starten. Alle anderen beendet der Dienst sofort.» Daneben öffnet «Hilfsprogramme (2)…» die Liste. Solange sie leer ist, steht dort «Mindestens ein Hilfsprogramm hinzufügen («Hilfsprogramme…») oder «Gesperrt» wählen.» und «Weiter» bleibt gesperrt.
- «Alle erlaubt (unsicher)»: Erst nach der Rückfrage «Alle weiteren Programme erlauben?» mit dem Text «Applikation A darf dann jedes Programm starten, auch eine Eingabeaufforderung mit Admin-Rechten. Bediener können so den Schutz umgehen. Nur nach Abwägung und mit Vermerk im Service and Security Manual.» Danach Hinweis «Applikation A darf jedes Programm starten, auch eine Eingabeaufforderung mit Admin-Rechten.»

Die Änderungen gelten erst, wenn Sie den Assistenten mit «Weiter» abschliessen.

**Dialog «Erlaubte Hilfsprogramme – Applikation A».** Überschrift «Erlaubte Hilfsprogramme», darunter «Diese Programme darf Applikation A starten. Sie laufen dann ebenfalls mit Admin-Rechten. Jedes andere Programm beendet der Dienst sofort und protokolliert es.» Die Liste zeigt je Programm die Prüfung und den Zustand:

| Zustand | Bedeutung |
|---|---|
| «In Ordnung» | Datei vorhanden, Signatur bzw. Prüfsumme stimmt. |
| «Prüfsumme neu» | Die Datei hat sich geändert, meist nach einem Update. «Prüfsumme aktualisieren» erfasst sie neu (bei markierten Einträgen, sonst bei allen mit SHA-256). |
| «Datei fehlt» | Programm deinstalliert oder verschoben. Entfernen oder neu hinzufügen. |
| «Prüfung fehlgeschlagen» | Signatur ungültig oder Datei nicht lesbar. Entfernen und neu hinzufügen. |

Knöpfe: «EXE hinzufügen…», «Lernmodus starten…», «Prüfsumme aktualisieren», «Entfernen» (markierte Einträge). Ist ein Programm nicht signiert, erscheint der gelbe Hinweis «Updater.exe ist nicht signiert» mit «Nach jedem Update der Applikation stimmt die Prüfsumme nicht mehr und der Start wird blockiert. Dann hier «Prüfsumme aktualisieren».» Unten steht: «Erlaubt sind nur Programme aus Ordnern, in die Bediener nicht schreiben können. Eingabeaufforderung, PowerShell, Explorer und Skript-Hosts nur mit ausdrücklicher Bestätigung.»

- Wählen Sie eine Shell, fragt der Dialog «cmd.exe erlauben?» mit «Dies ist eine Shell oder ein Skript-Host. Wer die Applikation bedient, kann damit jedes Programm mit Admin-Rechten starten.»
- Liegt das Programm in einem Ordner, den Bediener beschreiben können: «Export.exe kann nicht erlaubt werden: Der Ordner ist für Bediener beschreibbar. Programm in einen geschützten Ordner installieren.»

«Übernehmen» gibt die Liste an den Assistenten zurück, «Abbrechen» verwirft die Änderungen im Dialog.

### Lernmodus: Hilfsprogramme ermitteln

Welche Programme eine Applikation braucht, ist oft nicht bekannt. Der Lernmodus zeichnet es auf: Für eine begrenzte Zeit beendet der Dienst bei dieser Applikation nichts, sondern notiert jedes gestartete Programm (Ereignis 1102). Danach gilt wieder der eingestellte Modus, auch in einer noch laufenden Applikation.

Der Lernmodus ist erst möglich, wenn die Applikation eingerichtet ist. Sonst meldet der Dialog «Der Lernmodus ist erst möglich, wenn Applikation A eingerichtet ist. Zuerst den Assistenten abschliessen, dann erneut öffnen.»

1. Im Dialog «Erlaubte Hilfsprogramme» auf «Lernmodus starten…». Es öffnet sich «Lernmodus – Applikation A» mit der Überschrift «Lernmodus läuft» und dem Text «Starten Sie Applikation A jetzt über ihre Verknüpfung und benutzen Sie alle Funktionen, die Hilfsprogramme brauchen: Export, Drucken, Update, Hilfe.» Rechts oben läuft «endet automatisch in» 30:00 ab.
2. Der gelbe Hinweis «Während des Lernmodus ist der Schutz für Applikation A schwächer. Programme werden nicht beendet, nur aufgezeichnet. Den PC dabei nicht unbeaufsichtigt lassen.» gilt wörtlich.
3. Die Liste «Aufgezeichnete Programme» zeigt «Starts» und «Zuletzt» je Programm, anfangs «Noch nichts aufgezeichnet.»
4. «Lernmodus beenden» (oder Ablauf der Zeit) führt zu «Vorschläge übernehmen». «Abbrechen, nichts übernehmen» beendet den Lernmodus und löscht die Aufzeichnung. Kann der Lernmodus nicht starten, steht «Der Lernmodus konnte nicht gestartet werden: …» mit dem Grund im Dialog.

**Dialog «Vorschläge übernehmen».** «Der Lernmodus ist beendet, der Schutz wieder aktiv. Wählen Sie, welche Programme Applikation A künftig starten darf.» Gültig signierte Programme werden mit Signatur vorgeschlagen, alle anderen mit SHA-256. Die Spalte «Hinweis»:

| Hinweis | Vorauswahl |
|---|---|
| «–» | angehakt |
| «nicht signiert» | angehakt, Prüfung SHA-256 |
| «kann Dateien öffnen – abwägen» (z.B. PDF-Anzeige, Browser, Editor) | nicht angehakt. Mit Admin-Rechten kann das Programm beliebige Dateien öffnen und speichern. |
| «Eingabeaufforderung – nur von Hand», «Shell oder Skript-Host – nur von Hand» | nicht auswählbar |
| «Ordner für Bediener beschreibbar» | nicht auswählbar |

Unten: «Eingabeaufforderung, PowerShell, Explorer und Skript-Hosts werden nie vorgeschlagen. Wer sie trotzdem braucht, trägt sie in «Erlaubte Hilfsprogramme» von Hand ein und bestätigt die Warnung.» «2 Programme übernehmen» übernimmt die angehakten in die Liste, «Verwerfen» nichts; beide löschen die Aufzeichnung. Wurde nichts aufgezeichnet: «Es wurde nichts aufgezeichnet. Wurde die Applikation während des Lernmodus über ihre Verknüpfung gestartet?»

Meldungen des Lernmodus auf der Kommandozeile:

| Meldung | Bedeutung |
|---|---|
| «Lernmodus für «app-a» gestartet, endet automatisch um 10:55 Uhr» | Lernmodus läuft (Ereignis 1400). |
| «Lernmodus für «app-a» beendet» | Von Hand beendet (Ereignis 1401). |
| «Lernmodus für «app-a» war nicht aktiv» | Nichts zu beenden. |
| «Dauer des Lernmodus: 5 bis 120 Minuten.» | Ungültige Dauer, nichts geändert. |
| «Für «app-a» wurde nichts aufgezeichnet. Zuerst learn start und die Applikation benutzen.» | Keine Aufzeichnung vorhanden. |
| «… steht nicht in der Aufzeichnung.» | Pfad wie in der Aufzeichnung angeben. |
| «cmd.exe ist eine Shell oder ein Skript-Host und wird nie übernommen. Falls nötig von Hand: allow-child --allow-shell.» | Shells übernimmt der Lernmodus nie. |
| «Für «app-a» gibt es keine übernehmbaren Vorschläge (nur Shells oder Skript-Hosts aufgezeichnet).» | Nichts zu übernehmen. |
| «2 Hilfsprogramm(e) aus dem Lernmodus für «app-a» übernommen, Aufzeichnung gelöscht» | Erledigt, Modus «nur erlaubte Hilfsprogramme». |

### Auf der Kommandozeile

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
