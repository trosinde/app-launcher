## ADDED Requirements

### Requirement: Administratoren mit Passwort
verify SHALL jedes aktivierte lokale Konto, das direkt oder über eine verschachtelte Gruppe Mitglied der Administratoren ist und kein Passwort hat, als Abweichung melden und im Bericht namentlich nennen.

#### Scenario: Admin ohne Passwort
- **WHEN** ein aktiviertes lokales Konto in der Gruppe Administratoren ein leeres Passwort hat
- **THEN** meldet verify den Prüfpunkt «Administratoren mit Passwort» als nicht erfüllt mit dem Kontonamen und liefert einen Exit-Code ungleich 0

#### Scenario: Alle Admins mit Passwort
- **WHEN** jedes aktivierte Konto in der Gruppe Administratoren ein Passwort hat
- **THEN** ist der Prüfpunkt «Administratoren mit Passwort» erfüllt

#### Scenario: Deaktiviertes Konto ohne Passwort
- **WHEN** ein deaktiviertes Konto in der Gruppe Administratoren kein Passwort hat
- **THEN** zählt es nicht als Abweichung
