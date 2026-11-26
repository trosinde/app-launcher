## Why
Beim Test des Assistenten auf der Test-VM sind vier Punkte aufgefallen:
1. Ein Konto in der Gruppe Administratoren kann ohne Passwort existieren. Dann kann sich jeder als Admin anmelden und die ganze Absicherung aushebeln; der Prüfbericht nach einem Service-Einsatz meldet das nicht.
2. Die neue Verknüpfung zeigt nicht das Icon der gewählten Applikation, obwohl die Spec das fordert (Bug, Ursache noch offen).
3. Im Schritt «Applikation auswählen» ist nicht klar, was Datenordner und Registry-Key bewirken. Wählt der Techniker als Datenordner den Programmordner der App, bekommt appstart-app Schreibrechte auf die eigenen EXE- und DLL-Dateien.
4. Im Schritt «Konten und Gruppe» lassen sich Bediener nur hinzufügen. Wer bei einem früheren Lauf eingetragen wurde, lässt sich im Assistenten nicht mehr entfernen.

## What Changes
- verify: neuer Prüfpunkt «Administratoren mit Passwort». Jedes aktivierte lokale Konto, das direkt oder über eine Gruppe Mitglied der Administratoren ist und kein Passwort hat, ist eine Abweichung (Exit-Code ≠ 0). Der Bericht nennt die Konten.
- Assistent: Warnung im Schritt «Konten und Gruppe», wenn solche Konten existieren (keine Blockade, Einrichtung bleibt möglich). Kommandozeile und Antwortdatei: abgedeckt durch das automatische verify nach jedem ändernden Befehl.
- Erkennung: je Admin-Konto eine Netzwerk-Anmeldung mit leerem Passwort (LogonUser). Bei Konten mit Passwort zählt das als eine fehlgeschlagene Anmeldung (Ereignis 4625, Kontosperrungszähler); im Handbuch beschrieben.
- Assistent: Erklärtext zu Datenordner und Registry-Key direkt am Feld (was gesperrt wird, wer danach Zugriff hat, was bei leerem Feld passiert).
- Assistent: Warnung, wenn der Datenordner der Programmordner der Applikation ist, darin liegt oder ihn enthält. Text: appstart-app bekäme Schreibrechte auf EXE und DLLs; ausgetauschte DLLs erkennt die Hash-/Signaturprüfung nicht; Empfehlung C:\ProgramData\<Hersteller>\<App>. Einrichtung bleibt nach Bestätigung möglich.
- Assistent: Jedes Mitglied der Gruppe AppStart Users ausser appstart-user lässt sich entfernen, auch Mitglieder aus früheren Läufen oder von Hand eingetragene. Der Assistent übergibt die entfernten Konten ausdrücklich (SetupRequest.RemovedOperators); Kommandozeile und Antwortdatei entfernen weiterhin nur vom Setup eingetragene Mitglieder.
- Icon: Ursache auf der Test-VM klären, dann beheben.
- Neu: eigenes AppStart-Icon für die AppStart-Programme, Fenster, Startmenü-Einträge und das MSI. Die bestehende Anforderung «Verknüpfung je Applikation» bleibt unverändert.

## Offene Entscheidungen (bitte bei Freigabe bestätigen)
- Admin ohne Passwort im Setup: Warnung statt Blockade.
- Entfernen: alle Gruppenmitglieder ausser appstart-user, nicht nur vom Setup eingetragene.
- Datenordner im Programmordner: Warnung mit Bestätigung statt Verbot.
- AppStart-Icon: Variante nach Wahl des Maintainers.
- Kein automatischer Default für den Datenordner (Vorschlag aus dem Lernmodus ist ein späterer Change).

## Impact
- Betroffen: AppStart.Setup (Verifier, AccountService, WizardViewModel/WizardView, SetupEngine), Handbuch (Prüfpunkte, Assistent), Tests
- Kein Format-Wechsel in apps.json oder setup-state
