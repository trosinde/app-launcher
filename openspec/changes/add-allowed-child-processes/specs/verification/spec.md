## ADDED Requirements

### Requirement: Prüfung der Hilfsprogramme
verify SHALL je Applikation mit childProcesses allowlist für jeden Eintrag in allowedChildren prüfen: Datei vorhanden, Signatur oder SHA-256 gültig, Ordner für Bediener nicht beschreibbar. Applikationen mit childProcesses all SHALL im Prüfpunkt mit dem Hinweis «Alle weiteren Programme erlaubt» erscheinen, ohne Abweichung, weil der Modus bewusst gewählt ist. Ein aktiver Lernmodus SHALL als Abweichung erscheinen.

#### Scenario: Hilfsprogramm verändert
- **WHEN** die SHA-256 von Updater.exe nicht mehr zum Eintrag passt
- **THEN** meldet verify eine Abweichung mit Pfad und dem Hinweis rehash --app appa --child <Pfad>

#### Scenario: Alle weiteren Programme erlaubt
- **WHEN** eine Applikation childProcesses all hat
- **THEN** ist der Prüfpunkt erfüllt und nennt den Hinweis «Alle weiteren Programme erlaubt» mit der AppId

#### Scenario: Lernmodus vergessen
- **WHEN** beim Prüfen der Lernmodus für eine Applikation noch aktiv ist
- **THEN** meldet verify eine Abweichung mit Ablaufzeit und dem Hinweis learn stop --app <AppId>
