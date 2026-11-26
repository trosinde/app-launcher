## 1. Icon (Bug)
- [ ] 1.1 Auf der Test-VM: Verknüpfung einer betroffenen App prüfen (IconLocation per Shell auslesen, Icon-Cache, Icon der alten Verknüpfung) und Ursache in diesem Change notieren
- [ ] 1.2 Ursache beheben; Test, der IconLocation der erzeugten Verknüpfung gegen die erwartete Quelle prüft

## 2. Administratoren mit Passwort
- [x] 2.1 (Build grün; Testlauf unter Windows in 5.2) AccountService: aktivierte Admin-Konten (inkl. verschachtelter Gruppen) ohne Passwort ermitteln; Unit-Tests mit Fakes
- [x] 2.2 (Build grün; Testlauf unter Windows in 5.2) Verifier: Prüfpunkt «Administratoren mit Passwort», Texte, Bericht; Tests
- [x] 2.3 (Build grün; Testlauf unter Windows in 5.2) Assistent: Warnung im Schritt «Konten und Gruppe»; apply über das automatische verify; Tests

## 3. Datenordner und Registry-Key
- [ ] 3.1 Erklärtexte und Beispiele an beiden Feldern (SetupTexts, WizardView)
- [ ] 3.2 Prüfung «Datenordner im/über Programmordner» als reine Funktion; Unit-Tests (gleich, darunter, darüber, anderer Ordner, Gross-/Kleinschreibung, abschliessender Backslash)
- [ ] 3.3 Warnung mit Bestätigung im Assistenten; Warnung in apply und Antwortdatei; Tests

## 4. Bediener entfernen
- [x] 4.1 (Build grün; Testlauf unter Windows in 5.2) WizardViewModel: alle Mitglieder ausser appstart-user entfernbar
- [x] 4.2 (Build grün; Testlauf unter Windows in 5.2) SetupEngine: alle Gruppenmitglieder entfernen, die nicht mehr in der Liste stehen (nicht nur AddedGroupMembers); Tests

## 5. Abschluss
- [ ] 5.1 Handbuch: neuer Prüfpunkt, Warnungen, Entfernen von Bedienern; Vollständigkeitstest grün
- [ ] 5.2 Alle Tests grün, E2E auf der Test-VM
