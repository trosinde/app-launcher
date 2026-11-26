# Konten und Gruppe

## Übersicht

| Konto / Gruppe | Wird angelegt | Eigenschaften |
|---|---|---|
| `appstart-app` | immer | Lokales Konto, Mitglied der Administratoren. Benutzerrechte «Lokal anmelden verweigern» (`SeDenyInteractiveLogonRight`) und «Anmelden über Remotedesktopdienste verweigern» (`SeDenyRemoteInteractiveLogonRight`). Zufallspasswort pro PC, das niemand kennt und das nirgends gespeichert wird. Beschreibung: «AppStart: Konto für freigegebene Applikationen (keine interaktive Anmeldung)». |
| `AppStart Users` | immer | Lokale Gruppe. Nur Mitglieder dürfen die Pipe des Dienstes öffnen, also Applikationen über AppStart starten. Sonst hat die Gruppe keine Rechte. |
| `appstart-user` | auf Wunsch | Lokales Standardkonto ohne Admin-Rechte, Mitglied von `AppStart Users`. Passwort vergibt der Installateur. Beschreibung: «AppStart: Bedienerkonto». |

Die Applikationen laufen als `appstart-app`. Der Dienst meldet das Konto per S4U an, ohne Passwort. Voraussetzung ist das Recht «Als Batchauftrag anmelden», das `appstart-app` über die Administratoren hat (Windows-Standard). Das Profil des Kontos liegt unter `C:\Users\appstart-app`; dort speichert die Applikation ihre Benutzereinstellungen (HKCU). Bediener können es nicht lesen.

## Bedienerkonto anlegen

**Im Assistenten:** Schritt 2, «Bedienerkonto appstart-user anlegen» anhaken, Passwort zweimal eingeben, «Weiter».

**Über die Kommandozeile:**

```text
%SETUP% add-user --name appstart-user
```

Der Befehl fragt «Passwort für appstart-user: » und «Passwort wiederholen: ». Die Eingabe bleibt unsichtbar. Sind beide leer oder verschieden: «Passwort leer oder nicht identisch.», Exit-Code 2.

Für Skripte das Passwort als eine Zeile über die Standardeingabe übergeben:

```text
%SETUP% add-user --name appstart-user --password-stdin < passwort.txt
```

Hinweise:

- Mit `--name` lässt sich nur `appstart-user` anlegen. Ein anderer Name ergibt «Mit --name kann nur das Konto appstart-user angelegt werden.», Exit-Code 2.
- Existiert `appstart-user` bereits, bleibt das Konto mit seinem Passwort unverändert.
- Das Passwort muss die Kennwortrichtlinie des PCs erfüllen, sonst «Konten und Gruppe konnten nicht eingerichtet werden: … Das Passwort erfüllt die Kennwortrichtlinie dieses Computers nicht (Länge, Komplexität oder Verlauf).»
- Passwort später ändern: wie bei jedem lokalen Konto, z.B. «Computerverwaltung» → «Lokale Benutzer und Gruppen» → `appstart-user` → «Kennwort festlegen».

## Weitere Benutzer in die Gruppe aufnehmen

Bediener können statt `appstart-user` ihr eigenes lokales oder Domänen-Konto verwenden.

**Im Assistenten:** Schritt 2, unter «Weitere Mitglieder der Gruppe AppStart Users» das Konto als `DOMAIN\Benutzer`, `.\Benutzer` (lokal) oder `Benutzer` eintragen, «Hinzufügen», «Weiter».

**Über die Kommandozeile:**

```text
%SETUP% add-user --member "DOMAIN\bediener01"
```

Genau eine Angabe ist erlaubt: `--name appstart-user` **oder** `--member "<Konto>"`. Sonst: «Bitte genau eine Angabe machen: --name appstart-user oder --member "DOMAIN\User".», Exit-Code 2.

Nur Benutzerkonten können aufgenommen werden, keine Gruppen wie «Jeder» oder «Benutzer»: Sonst stünde AppStart allen offen. Ein Konto, das nicht existiert, oder eine Gruppe führt zu «Konten und Gruppe konnten nicht eingerichtet werden» mit dem Grund, z.B. «Das Konto «DOMAIN\xy» wurde nicht gefunden.» oder «… ist kein Benutzerkonto. Nur Benutzer können in «AppStart Users» aufgenommen werden.»

> **Wichtig:** Die Mitgliedschaft wirkt erst nach einer **neuen Anmeldung** des Benutzers. Vorher zeigt der Launcher «Keine Berechtigung».

## Benutzer aus der Gruppe entfernen

- **Im Assistenten:** Schritt 2, beim Konto auf ✕ («Mitglied entfernen») klicken, dann «Weiter». Das gilt für jedes Mitglied der Gruppe, auch für Konten aus früheren Einrichtungen und für von Hand eingetragene. Unter der Liste steht: «Mit ✕ entfernte Mitglieder nimmt das Setup beim Übernehmen aus der Gruppe. Die Änderung wirkt nach der nächsten Anmeldung des Benutzers.» `appstart-user` steht nicht in der Liste; es bleibt Mitglied, solange das Konto existiert.
- **Von Hand:** «Computerverwaltung» → «Lokale Benutzer und Gruppen» → «Gruppen» → `AppStart Users` → Mitglied entfernen. Oder in PowerShell als Administrator: `Remove-LocalGroupMember -Group "AppStart Users" -Member "DOMAIN\bediener01"`.

Die Kommandozeile hat keinen Befehl zum Entfernen einzelner Mitglieder.

## Warnung: Administratoren ohne Passwort

Hat ein aktiviertes Konto in der Gruppe Administratoren kein Passwort, zeigt Schritt 2 einen gelben Kasten, z.B. «Administratoren ohne Passwort: PC-014\service. Damit kann sich jeder an diesem PC als Administrator anmelden und die Absicherung aufheben. Passwort setzen oder Konto deaktivieren; die Prüfung meldet es sonst als Abweichung.» Die Einrichtung ist trotzdem möglich. Passwort setzen: `net user <Konto> *` in einer Eingabeaufforderung «Als Administrator ausführen», oder das Konto deaktivieren. Siehe Prüfpunkt [Administratoren mit Passwort](06-pruefung-nach-serviceeinsatz.md#adminpasswort).

## Regeln für den Betrieb

- **Bediener nie in die Administratoren aufnehmen**, auch nicht vorübergehend für einen Serviceeinsatz, ohne die Mitgliedschaft danach zu entfernen. `verify` meldet jedes Mitglied von `AppStart Users`, das Administrator ist.
- `appstart-app` nicht umbenennen, deaktivieren oder dessen Passwort setzen. Das Konto wird nur von AppStart genutzt.
- Die Anmeldesperren von `appstart-app` nicht entfernen. Setzt eine Domänen-Gruppenrichtlinie die Rechte «Lokal anmelden verweigern» oder «Anmelden über Remotedesktopdienste verweigern», muss sie `appstart-app` (bzw. `<PC>\appstart-app`) enthalten.
- Fehlt ein Konto oder die Gruppe (z.B. versehentlich gelöscht), die Einrichtung erneut ausführen («AppStart – Einrichtung», «Weiter», «Weiter»). Das Setup legt Fehlendes neu an. Nach dem Neuanlegen der Gruppe den Dienst neu starten (siehe Prüfpunkt [Pipe](06-pruefung-nach-serviceeinsatz.md)).
