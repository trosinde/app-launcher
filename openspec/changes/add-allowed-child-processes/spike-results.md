# Spike-Ergebnisse (tasks.md Abschnitt 1)

Stand: 15.11.2026. Code (Abschnitte 2 bis 6) beginnt erst, wenn 1.1 hier dokumentiert und die Entscheidung in design.md nachgetragen ist.

| Spike | Stand | Ergebnis |
|---|---|---|
| 1.1 Job mit Completion Port | erledigt (15.11.2026), Gegenprobe im Dienstkontext auf der Test-VM in 7.1 | Überwachung funktioniert, conhost.exe muss immer erlaubt sein, WerFault.exe erscheint nicht im Job |
| 1.2 Lernmodus mit Ziel-Applikationen | offen | wartet auf die Liste der realen Applikationen (add-appstart-service 1.4) |

## 1.1 – Job mit Completion Port

Werkzeug: `spikes/AppStart.Spike.ChildJob` (Wegwerf-Code, kein Produktcode), `spikes/Invoke-ChildJobSpike.ps1`.

Ort: Entwicklungsrechner (Windows 11 Pro 10.0.26300, deutsch) statt Test-VM, als normaler Benutzer ohne Admin-Rechte. Der Spike installiert nichts und ändert weder Konten noch Rechte, deshalb ist das mit project.md vereinbar. Abweichung zur Aufgabe: Statt der TestApp startet ein eigener Prüfprozess (`probe`) die Kindprozesse. Er ist wie die TestApp ein GUI-Prozess (Subsystem Windows) ohne eigene Konsole, das ist für conhost.exe entscheidend. Nicht geprüft wurde der Dienstkontext (SYSTEM, S4U-Token `appstart-app`, `CreateProcessAsUser` in die Bediener-Session). Die Job-Semantik hängt davon nicht ab; die Gegenprobe läuft mit dem echten Dienst in 7.1.

Aufbau wie in design.md D2: Job ohne Prozesslimit, `KILL_ON_JOB_CLOSE`, ohne `BREAKAWAY_OK`, Completion Port, Prüfprozess suspended gestartet, zugewiesen, fortgesetzt. Bei `JOB_OBJECT_MSG_NEW_PROCESS`: `OpenProcess(QUERY_LIMITED_INFORMATION | TERMINATE | SYNCHRONIZE)`, `QueryFullProcessImageName`, Vergleich mit der Positivliste (im Spike nur Dateiname), sonst `TerminateProcess`. Gemessen: Zeit von der Erstellung des Prozesses (`GetProcessTimes`) bis zum Eingang der Meldung und bis zum Prozessende.

| Lauf | Inhalt |
|---|---|
| A Lernen | nichts beenden, nur aufzeichnen: Konsolen-Programm mit und ohne Fenster, `ping.exe`, Absturz eines Kindprozesses, Enkelprozess, PDF per ShellExecute |
| B Erzwingen | dieselben Szenarien, nur der Prüfprozess erlaubt |
| C Dienst-Stopp | erlaubtes `ping -n 120` läuft, Überwachungsprozess wird hart beendet (`Stop-Process -Force`) |
| D Applikation endet zuerst | Prüfprozess startet erlaubtes `ping -n 6` und endet sofort |
| E Zeitmessung | 20 nicht erlaubte Starts von `ping.exe` |

### Ergebnis vom 15.11.2026

| Frage | Ergebnis |
|---|---|
| Erscheint conhost.exe als NEW_PROCESS? | **Ja**, bei jedem Konsolen-Programm, das ein GUI-Prozess startet, auch mit `CreateNoWindow` (unsichtbar). Pfad `C:\Windows\System32\conhost.exe`. Die Standard-Terminal-Delegation (Windows Terminal) war nicht gesetzt; conhost blieb im Job. Wird conhost beendet, läuft das Konsolen-Programm trotzdem weiter (Lauf C und D), hat aber keine Konsole mehr. Ein Konsolen-Hilfsprogramm mit sichtbarem Fenster würde ohne conhost kein Fenster zeigen. |
| Erscheint WerFault.exe als NEW_PROCESS? | **Nein.** Ein Kindprozess mit unbehandelter Ausnahme (Exit-Code `0xE0434352`) erzeugte keinen weiteren Prozess im Job. Windows-Fehlerberichterstattung war aktiv (WerSvc läuft, Ereignis 1001 «CLR20r3» zum Absturz vorhanden); WerFault läuft also ausserhalb des Jobs über den Dienst WerSvc. |
| Wie lange läuft ein nicht erlaubter Prozess? | **0,7 bis 4,7 ms** von der Erstellung bis zum Ende (26 ausgewertete Fälle aus Lauf B bis E, Median etwa 1,5 ms), einmal 16 ms, als der Überwachungsprozess gleichzeitig andere Meldungen verarbeitete. Die Meldung kommt meist 0,03 bis 0,2 ms nach der Erstellung an. Gemessene CPU-Zeit jeweils 0 (unter der Messauflösung). Bei allen beendeten Programmen, die der Prüfprozess selbst startete, schlug `CreateProcess` beim Elternprozess mit «Zugriff verweigert» (ERROR_ACCESS_DENIED) fehl: Der Prozess war beendet, bevor `CreateProcess` zurückkehrte. Das ist eine Beobachtung, keine Garantie; das Restrisiko aus design.md bleibt. |
| Greift KILL_ON_JOB_CLOSE beim Dienst-Stopp? | **Ja.** Nach dem harten Beenden des Überwachungsprozesses waren das erlaubte `ping -n 120` und der Prüfprozess innerhalb von 2 s weg (vorher 1, nachher 0). |
| Applikation endet vor dem Hilfsprogramm (D2) | Funktioniert: Nach dem Ende des Prüfprozesses lief das erlaubte `ping` weiter, die Überwachung endete erst mit `ACTIVE_PROCESS_ZERO`. |
| Enkelprozess | Ein erlaubter Kindprozess, der `ping.exe` startet: `ping.exe` erscheint im selben Job und wird beendet. |
| PDF-Anzeige | ShellExecute auf eine PDF-Datei startet `msedge.exe` im Job (Lauf A). Edge übergab die Datei an eine bereits laufende Edge-Instanz **ausserhalb des Jobs** und endete nach etwa 30 ms. Im Erzwingen-Modus wurde `msedge.exe` beendet, ShellExecute meldete «Zugriff verweigert». Siehe neues Restrisiko unten. |

### Entscheide (in design.md nachgetragen)

1. **conhost.exe immer erlaubt**, ohne Eintrag in apps.json: nur `%SystemRoot%\System32\conhost.exe` mit gültiger Microsoft-Signatur (Kette und Herausgeber «Microsoft Windows»). Im Lernmodus wird conhost nicht aufgezeichnet, verify zeigt es nicht. Begründung: Ohne conhost haben Konsolen-Hilfsprogramme kein Fenster; conhost selbst führt keinen fremden Code aus.
2. **WerFault.exe: keine Ausnahme.** Erscheint nicht im Job. Taucht es unter Windows 10 doch auf (Gegenprobe 7.1), wird es wie jeder andere Prozess beendet (fail-closed); der Fehlerbericht entsteht trotzdem über WerSvc.
3. **Zeit bis zum Beenden**: Restrisiko wie in design.md beschrieben, mit den gemessenen Werten ergänzt.
4. **KILL_ON_JOB_CLOSE** wie geplant.

### Neu entdeckt: Übergabe an Prozesse ausserhalb des Jobs

Ein Programm im Job kann eine Aufgabe an einen Prozess übergeben, der ausserhalb des Jobs läuft: an eine bereits laufende Instanz desselben Programms (Edge im Spike) oder an einen COM-Server, den Windows über den Dienst DcomLaunch startet. Solche Prozesse sieht weder `blocked` (ActiveProcessLimit) noch `allowlist`. Im Dienstkontext läuft die Applikation als `appstart-app`, eine Übergabe kann also nur an Prozesse dieses Kontos in derselben Session gehen. Das gilt schon heute für `blocked` (add-appstart-service). Eingetragen in design.md, Risks; Prüfung im Dienstkontext in 7.1.
