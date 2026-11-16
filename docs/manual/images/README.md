# Bilder des Handbuchs

Bildschirmfotos stammen aus der **fertigen Software auf der Test-VM**, nicht aus den UI-Entwürfen. Format PNG, Fenster ohne Schatten, 100 % Skalierung, Windows 11. Keine echten Kunden- oder Domänennamen und keine Namen von Personen.

Aufgenommen werden sie mit `tests/e2e/Capture-ManualScreenshots.ps1` (im UAT-Runner Suite `Manual`). Die Test-VM heisst `WIN11`; dieser Name erscheint in den Bildern statt `PC-014` aus den UI-Entwürfen.

Nachbearbeitung, damit kein Personenname im Handbuch steht:

- `pruefansicht-ok.png`, `pruefansicht-abweichung.png`: Der Wert «Servicetechniker» zeigt das angemeldete Konto. Der Kontoname nach `WIN11\` ist durch «techniker» ersetzt.
- `testapp.png`: Die Titelleiste ist abgeschnitten. Das Fenster läuft unter `appstart-app` und lässt sich aus der Testsitzung nicht in den Vordergrund holen, die Titelleiste erscheint dann blass.

Solange eine Datei fehlt, setzt `build/ManualBuilder` im PDF einen Platzhalter mit dem Bildtitel ein. Der Test `AppStart.Documentation.Tests` verlangt, dass jedes im Handbuch verwendete Bild entweder vorhanden ist oder hier aufgeführt wird.

## Vorhanden

| Datei | Kapitel | Inhalt | Stand |
|---|---|---|---|
| `funktionsweise.svg` | 1 Überblick | Funktionsweise in einem Bild, von Hand gezeichnet | – |
| `bootstrapper.png` | 3 Installation | `AppStart-Setup.exe` während der MSI-Installation | 0.3.0-uat.5 |
| `setup-1.png` | 3 Installation | Assistent Schritt 1, TestApp mit Datenordner und Registry-Key, «Weitere Programme: Gesperrt (empfohlen)» | 0.3.0-uat.5 |
| `setup-2.png` | 3 Installation | Assistent Schritt 2, Bedienerkonto, Beispielmitglied `DOMAIN\bediener01` | 0.3.0-uat.5 |
| `setup-3-laeuft.png` | 3 Installation | Assistent Schritt 3 während der Einrichtung | älterer Stand (vor add-allowed-child-processes); die Einrichtung ist auf der Test-VM zu schnell für eine neue Aufnahme |
| `setup-3.png` | 3 Installation | Assistent Schritt 3 abgeschlossen, «Prüfung bestanden – 9 von 9 Prüfpunkten erfüllt» | 0.3.0-uat.5 |
| `testapp.png` | 3 Installation | TestApp als Bediener, «Start blockiert durch Job-Objekt» | 0.3.0-uat.5 |
| `setup-weitere-programme.png` | 4 Applikationen | Schritt 1 mit «Nur erlaubte Hilfsprogramme», «Hilfsprogramme (1)…», Spalte «1 erlaubt» | 0.3.0-uat.5 |
| `hilfsprogramme.png` | 4 Applikationen | Dialog «Erlaubte Hilfsprogramme» mit TestHelper (SHA-256, nicht signiert) | 0.3.0-uat.5 |
| `lernmodus.png` | 4 Applikationen | «Lernmodus läuft» mit TestHelper und cmd.exe aufgezeichnet | 0.3.0-uat.5 |
| `lernergebnis.png` | 4 Applikationen | «Vorschläge übernehmen»: TestHelper angehakt, cmd.exe «Eingabeaufforderung – nur von Hand» | 0.3.0-uat.5 |
| `pruefansicht-ok.png` | 6 Prüfung | Prüfansicht ohne Abweichung, 9 Prüfpunkte | 0.3.0-uat.5, anonymisiert |
| `pruefansicht-abweichung.png` | 6 Prüfung | Abweichung bei «Konten»: `appstart-user` in den Administratoren | 0.3.0-uat.5, anonymisiert |
| `verify-konsole.png` | 6 Prüfung | `AppStart.Setup.exe verify` mit derselben Abweichung | 0.3.0-uat.5 |
| `launcher-nicht-erreichbar.png` | 7 Meldungen | Dienst gestoppt: «Start-Dienst nicht erreichbar» | 0.3.0-uat.5 |
| `launcher-verweigert.png` | 7 Meldungen | Ein Byte der EXE geändert: «Start verweigert» mit Ereignis-ID | 0.3.0-uat.5 |

## Fehlende Bildschirmfotos

Beide zeigen Fenster von Windows selbst. Die Test-VM hat eine englische Oberfläche, das Handbuch braucht deutsches Windows. Aufnahme auf einer VM mit deutscher Oberfläche, mit demselben Skript.

| Datei | Kapitel | Bildschirm | Zustand |
|---|---|---|---|
| `startmenue.png` | 3 Installation | Startmenü, Ordner «AppStart» aufgeklappt | Einträge «AppStart – Einrichtung», «AppStart – Einrichtung prüfen», «AppStart – Handbuch» sichtbar |
| `ereignisanzeige.png` | 7 Meldungen | Ereignisanzeige, Protokoll «Anwendung», gefiltert auf Quelle AppStart | Eintrag 1201 markiert, Text mit «Vorgang: …» sichtbar; Windows aktiviert (kein Wasserzeichen) |
