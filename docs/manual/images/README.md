# Bilder des Handbuchs

Bildschirmfotos stammen aus der **fertigen Software auf der Test-VM** (Task 10.2), nicht aus den UI-Entwürfen. Format PNG, Fenster ohne Schatten (Alt+Druck oder Snipping Tool «Fenster»), 100 % Skalierung, deutsches Windows 11. Rechnername `PC-014`, keine echten Kunden- oder Domänennamen.

Solange eine Datei fehlt, setzt `build/ManualBuilder` im PDF einen Platzhalter mit dem Bildtitel ein. Der Test `AppStart.Documentation.Tests` verlangt, dass jedes im Handbuch verwendete Bild entweder vorhanden ist oder hier aufgeführt wird.

## Vorhanden

| Datei | Inhalt |
|---|---|
| `funktionsweise.svg` | Funktionsweise in einem Bild (Kapitel 1), von Hand gezeichnet |

## Fehlende Bildschirmfotos

| Datei | Kapitel | Bildschirm | Zustand |
|---|---|---|---|
| `startmenue.png` | 3 Installation | Startmenü, Ordner «AppStart» aufgeklappt | Einträge «AppStart – Einrichtung», «AppStart – Einrichtung prüfen», «AppStart – Handbuch» sichtbar |
| `bootstrapper.png` | 3 Installation | Fenster von `AppStart-Setup.exe` | Während der MSI-Installation: «AppStart wird installiert …» |
| `setup-1.png` | 3 Installation | Assistent, Schritt 1 «Applikationen auswählen» | Zwei Applikationen in der Liste (eine mit «Signatur», die TestApp mit «SHA-256»), TestApp markiert, «Schutz für AppStart TestApp» mit Datenordner `C:\ProgramData\AppStartTest` und Registry-Key `HKLM\SOFTWARE\AppStartTest` ausgefüllt, Knopf «Einrichtung prüfen» sichtbar |
| `setup-2.png` | 3 Installation | Assistent, Schritt 2 «Konten und Gruppe» | Erstinstallation: App-Konto «Wird angelegt …», «Bedienerkonto appstart-user anlegen» angehakt, Passwortfelder gefüllt (Punkte), ein weiteres Mitglied `DOMAIN\bediener01` in der Liste |
| `setup-3-laeuft.png` | 3 Installation | Assistent, Schritt 3 | Während der Einrichtung: «Einrichtung läuft», Fortschrittszeile z.B. «Datenordner und Registry-Keys werden gesperrt …» |
| `setup-3.png` | 3 Installation | Assistent, Schritt 3 | Abgeschlossen: fünf Zeilen mit ✓, «Prüfung bestanden – 8 von 8 Prüfpunkten erfüllt», Pfad des Berichts, Knöpfe «Prüfbericht öffnen» und «Fertig» |
| `testapp.png` | 3 Installation | Fenster «AppStart TestApp» | Gestartet als Bediener über die Verknüpfung, alle Zeilen «OK», nach Klick auf «Eingabeaufforderung starten»: «Start blockiert durch Job-Objekt» |
| `pruefansicht-ok.png` | 6 Prüfung | Prüfansicht «AppStart – Einrichtung prüfen» | Keine Abweichung, grüner Kasten «Einrichtung entspricht dem Soll» |
| `pruefansicht-abweichung.png` | 6 Prüfung | Prüfansicht | Eine Abweichung bei «Konten»: `appstart-user` vorübergehend in die Administratoren aufgenommen, roter Kasten «Einsatz nicht abschliessen» |
| `verify-konsole.png` | 6 Prüfung | Eingabeaufforderung (Administrator) | Ausgabe von `AppStart.Setup.exe verify` mit einer Abweichung (wie `pruefansicht-abweichung.png`) |
| `launcher-nicht-erreichbar.png` | 7 Meldungen | Meldung des Launchers | Dienst gestoppt (`sc stop AppStart`), Start der TestApp: «Start-Dienst nicht erreichbar» |
| `launcher-verweigert.png` | 7 Meldungen | Meldung des Launchers | Byte der TestApp-EXE geändert: «Start verweigert» mit Zeile «Ereignis-ID … · Datum Uhrzeit» |
| `ereignisanzeige.png` | 7 Meldungen | Ereignisanzeige, Protokoll «Anwendung», gefiltert auf Quelle AppStart | Eintrag 1201 markiert, Text mit «Vorgang: …» sichtbar |
