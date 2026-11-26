## 1. Icon (Bug)
- [ ] 1.1 (Vermutung: Datenordner = Programmordner sperrt den Ordner der EXE für Bediener; Explorer läuft als Bediener, kann das Icon aus der EXE nicht lesen und zeigt das Icon des Launchers bzw. ein Standard-Icon. Seit 6.2 hat der Launcher das AppStart-Icon, damit ist der Fall erkennbar) Auf der Test-VM: Verknüpfung einer betroffenen App prüfen (IconLocation per Shell auslesen, Icon-Cache, Icon der alten Verknüpfung) und Ursache in diesem Change notieren
- [ ] 1.2 Ursache beheben; Test, der IconLocation der erzeugten Verknüpfung gegen die erwartete Quelle prüft

## 2. Administratoren mit Passwort
- [x] 2.1 AccountService: aktivierte Admin-Konten (inkl. verschachtelter Gruppen) ohne Passwort ermitteln; Unit-Tests mit Fakes
- [x] 2.2 Verifier: Prüfpunkt «Administratoren mit Passwort», Texte, Bericht; Tests
- [x] 2.3 Assistent: Warnung im Schritt «Konten und Gruppe»; apply über das automatische verify; Tests

## 3. Datenordner und Registry-Key
- [x] 3.1 Erklärtexte und Beispiele an beiden Feldern (SetupTexts, WizardView)
- [x] 3.2 Prüfung «Datenordner im/über Programmordner» als reine Funktion; Unit-Tests (gleich, darunter, darüber, anderer Ordner, Gross-/Kleinschreibung, abschliessender Backslash)
- [x] 3.3 Warnung mit Bestätigung im Assistenten; Warnzeile in der Zusammenfassung, bei add, apply und Antwortdatei; Tests

## 4. Bediener entfernen
- [x] 4.1 WizardViewModel: alle Mitglieder ausser appstart-user entfernbar
- [x] 4.2 SetupEngine: alle Gruppenmitglieder entfernen, die nicht mehr in der Liste stehen (nicht nur AddedGroupMembers); Tests

## 5. Abschluss
- [ ] 5.1 Handbuch: neuer Prüfpunkt, Warnungen, Entfernen von Bedienern; Vollständigkeitstest grün
- [ ] 5.2 (CI unter Windows grün am 2026-11-26, Lauf 45803972265; offen: E2E auf der Test-VM) Alle Tests grün, E2E auf der Test-VM

## 6. AppStart-Icon
- [x] 6.1 Icon als SVG-Quelle in assets/ und daraus AppStart.ico (16, 20, 24, 32, 40, 48, 64, 256 px); Skript zum Neuerzeugen
- [x] 6.2 ApplicationIcon in Setup, Service, Launcher, Bootstrapper; Window.Icon in Assistent und Prüfansicht; vpk --icon im Release-Skript
- [x] 6.3 Test: jede AppStart-EXE enthält das Icon; Startmenü-Einträge verweisen auf AppStart.Setup.exe,0; README mit Icon
