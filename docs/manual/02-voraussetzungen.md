# Voraussetzungen

## Windows

| Punkt | Vorgabe |
|---|---|
| Betriebssystem | Windows 11 x64 oder Windows 10 x64, deutsch oder andere Sprache. Getestet unter Windows 11 Pro; der Test unter Windows 10 ist noch offen. |
| .NET | Nicht nötig. Alle Programme bringen ihre Laufzeit mit. |
| Installationsort | Immer `C:\Program Files\AppStart` (Installation für den ganzen Rechner). Ein Installationsort, den Benutzer beschreiben können, ist nicht zulässig. |
| Dienste | Der Dienst `AppStart` läuft als LocalSystem und startet automatisch. |
| Gruppenrichtlinien | Werden die Benutzerrechte «Lokal anmelden verweigern» oder «Anmelden über Remotedesktopdienste verweigern» per Domänen-Richtlinie gesetzt, überschreibt die Richtlinie die Einstellung des Setups. Das Konto `appstart-app` muss dann in der Richtlinie ergänzt werden (siehe [Prüfung nach Serviceeinsatz](06-pruefung-nach-serviceeinsatz.md)). Das Recht «Als Batchauftrag anmelden» muss den Administratoren erhalten bleiben (Windows-Standard), sonst kann der Dienst `appstart-app` nicht anmelden. |

## Rechte des Installateurs

- Installation, Einrichtung, Änderungen und `verify` brauchen **Admin-Rechte**. `AppStart-Setup.exe` und `AppStart.Setup.exe` fragen per UAC danach.
- Für die Kommandozeile eine **Eingabeaufforderung oder PowerShell «Als Administrator ausführen»** öffnen. Nur dann erscheinen die Ausgaben von `AppStart.Setup.exe` im selben Fenster.
- Die stille Installation per Softwareverteilung läuft als SYSTEM.
- Bediener brauchen keine Admin-Rechte. Sie müssen Mitglied der Gruppe `AppStart Users` sein.

## Angaben je Applikation

Vor der Einrichtung je Applikation klären und notieren:

| Angabe | Beispiel | Pflicht | Hinweis |
|---|---|---|---|
| Programmdatei (EXE) | `C:\Program Files\Hersteller\AppA\AppA.exe` | ja | Absoluter Pfad mit Laufwerksbuchstabe. Keine Netzwerkpfade (`\\server\…`). Die EXE muss in einem Ordner liegen, den Bediener nicht beschreiben können (z.B. unter `C:\Program Files`). |
| Anzeigename | `Applikation A` | nein | Name der Verknüpfung und Fenstertitel von Meldungen. Ohne Angabe: Dateibeschreibung bzw. Produktname der EXE, sonst der Dateiname. |
| Datenordner | `C:\ProgramData\Hersteller\AppA` | nein | Ordner mit Austauschdateien oder Passwörtern. Wird für Bediener gesperrt. Vollständiger Pfad, kein Laufwerk und kein Systemordner, keine Verknüpfung (Junction). |
| Registry-Key | `HKLM\SOFTWARE\Hersteller\AppA` | nein | Key mit Passwörtern der Applikation. Muss mit `HKLM\` beginnen. Wird für Bediener gesperrt. |
| Signiert? | ja / nein | – | Das Setup erkennt es selbst. Signierte EXE werden über das Zertifikat geprüft (übersteht Updates), unsignierte über SHA-256 (nach jedem Update `rehash`). |
| Startet die Applikation weitere Programme? | nein | – | Standard: Kindprozesse sind gesperrt. Vorher mit der Applikation testen: Drucken, Export, Hilfe, Dialog «Datei öffnen». |
| Netzwerkzugriff mit Windows-Anmeldung? | nein | – | Funktioniert über AppStart nicht (siehe [Überblick](01-ueberblick.md)). |

## Angaben zu den Bedienern

- Soll das lokale Bedienerkonto `appstart-user` angelegt werden? Dann ein Passwort festlegen, das der Kennwortrichtlinie des PCs entspricht.
- Welche bestehenden lokalen oder Domänen-Benutzer (`DOMAIN\Benutzer`) sollen in die Gruppe `AppStart Users`? Nur Benutzerkonten, keine Gruppen.
- Bediener dürfen **nicht** Mitglied der Administratoren sein; `verify` meldet das als Abweichung.

## Lieferumfang

| Datei | Verwendung |
|---|---|
| `AppStart-Setup.exe` | Interaktive Installation: installiert das MSI und öffnet die Einrichtung |
| `AppStart-<Version>.msi` | Stille Installation über Softwareverteilung (`msiexec`) |
| `SHA256SUMS.txt` | Prüfsummen der Lieferung |
| `AppStart-Handbuch-<Version>.pdf` | Dieses Handbuch, zum Lesen vor der Installation. Dieselbe Datei wird mit AppStart installiert und öffnet sich im Startmenü unter «AppStart – Handbuch». |

Alle Programmdateien und das MSI sind mit dem Zertifikat des Herausgebers signiert. Vor der Installation in den Eigenschaften der Datei (Register «Digitale Signaturen») prüfen.
