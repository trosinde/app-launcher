namespace AppStart.Setup.Cli;

/// <summary>Texts of the command line (reference chapter of the manual, task 10.4).</summary>
public static class CliTexts
{
    public const string RootDescription = "AppStart Setup: Applikationen freigeben, Konten einrichten, Einrichtung prüfen. Ohne Befehl öffnet sich der Assistent.";

    public const string AddDescription = "Gibt eine Applikation frei oder aktualisiert ihre Freigabe.";
    public const string ExeOption = "Pfad zur EXE der Applikation.";
    public const string DataDirOption = "Datenordner (Austauschdateien), wird für Bediener gesperrt.";
    public const string RegKeyOption = "Registry-Key mit Passwörtern unter HKLM\\, wird für Bediener gesperrt.";
    public const string NameOption = "Anzeigename (Name der Verknüpfung).";

    public const string AddUserDescription = "Legt das Bedienerkonto appstart-user an oder nimmt einen User in die Gruppe AppStart Users auf.";
    public const string UserNameOption = "Nur appstart-user: legt das lokale Bedienerkonto an.";
    public const string MemberOption = "Lokaler oder Domänen-User (DOMAIN\\User) für die Gruppe AppStart Users.";
    public const string PasswordStdinOption = "Passwort für appstart-user aus einer Zeile der Standardeingabe lesen (stille Installation).";
    public const string AddUserUsage = "Bitte genau eine Angabe machen: --name appstart-user oder --member \"DOMAIN\\User\".";
    public const string AddUserNameOnly = "Mit --name kann nur das Konto appstart-user angelegt werden.";
    public const string PasswordPrompt = "Passwort für appstart-user: ";
    public const string PasswordRepeat = "Passwort wiederholen: ";
    public const string PasswordMismatch = "Passwort leer oder nicht identisch.";

    public const string RemoveDescription = "Entzieht einer Applikation die Freigabe und stellt ihre Verknüpfungen und Rechte zurück.";
    public const string RehashDescription = "Erfasst nach einem Update der Applikation Signatur oder Prüfsumme neu.";
    public const string AppOption = "AppId der Applikation (siehe list).";

    public const string ListDescription = "Zeigt freigegebene Applikationen, Bediener und Dienststatus.";
    public const string NoApps = "Keine Applikation freigegeben.";
    public const string OperatorsLine = "Gruppe AppStart Users: {0}";
    public const string ServiceLine = "Dienst AppStart: {0}";
    public const string Running = "läuft";
    public const string NotRunning = "läuft nicht";

    public const string VerifyDescription = "Prüft die Einrichtung und legt einen Prüfbericht ab. Exit-Code 0 nur ohne Abweichung.";
    public const string ReportOption = "Zusätzlicher Speicherort für den Prüfbericht (HTML).";
    public const string UiOption = "Prüfansicht öffnen (Startmenü-Eintrag «AppStart – Einrichtung prüfen»).";
    public const string VerifyPassed = "Prüfung bestanden – {0} von {0} Prüfpunkten erfüllt.";
    public const string VerifyFailed = "Prüfung nicht bestanden – {0} Abweichung(en). Einsatz nicht abschliessen.";
    public const string ReportSaved = "Prüfbericht: {0}";

    public const string AutoVerify = "Automatische Prüfung nach der Änderung:";

    // add-allowed-child-processes, design D4
    public const string IdOption = "AppId festlegen (a-z, 0-9, -; sonst aus dem Dateinamen). Nur bei einer neuen Freigabe.";
    public const string ChildProcessesOption = "Weitere Programme: blocked (gesperrt), allowlist (nur erlaubte Hilfsprogramme) oder all (alle erlaubt).";
    public const string SetChildrenDescription = "Stellt ein, welche weiteren Programme eine Applikation starten darf.";
    public const string ModeOption = "blocked, allowlist oder all. allowlist braucht mindestens ein Hilfsprogramm.";
    public const string AllowChildDescription = "Erlaubt einer Applikation ein Hilfsprogramm (Modus wird «nur erlaubte Hilfsprogramme»).";
    public const string DenyChildDescription = "Entfernt ein Hilfsprogramm aus der Liste (beim letzten wird der Modus wieder «gesperrt»).";
    public const string ChildExeOption = "Pfad zur EXE des Hilfsprogramms.";
    public const string CheckOption = "Prüfung signature oder sha256 (ohne Angabe: Signatur, wenn gültig signiert, sonst SHA-256).";
    public const string AllowShellOption = "Bestätigt, dass eine Shell oder ein Skript-Host erlaubt werden soll (z.B. cmd.exe, powershell.exe).";
    public const string RehashChildOption = "Nur die Prüfsumme dieses Hilfsprogramms neu erfassen.";
    public const string ChildModeLine = "Weitere Programme: {0}";
    public const string CheckSignature = "Signatur";
    public const string CheckSha256 = "SHA-256";
    public const string ShellMark = " – Shell oder Skript-Host";

    public const string LearnDescription = "Lernmodus: zeichnet auf, welche Programme eine Applikation startet, und übernimmt sie als Hilfsprogramme.";
    public const string LearnStartDescription = "Startet den Lernmodus für eine Applikation.";
    public const string MinutesOption = "Dauer in Minuten (5 bis 120, Standard 30).";
    public const string LearnStopDescription = "Beendet den Lernmodus sofort.";
    public const string LearnShowDescription = "Zeigt, was der Lernmodus aufgezeichnet hat.";
    public const string LearnApplyDescription = "Übernimmt aufgezeichnete Programme als Hilfsprogramme und löscht die Aufzeichnung.";
    public const string LearnAllOption = "Alle aufgezeichneten Programme ausser Shells und Skript-Hosts.";
    public const string LearnExeOption = "Nur diese aufgezeichneten Programme (mehrfach möglich).";
    public const string LearnApplyUsage = "Bitte genau eine Angabe machen: --all oder --exe <Pfad> (mehrfach möglich).";
    public const string LearnInactive = "Lernmodus für «{0}»: nicht aktiv.";
    public const string LearnActiveUntil = "Lernmodus für «{0}»: aktiv bis {1}.";
    public const string LearnEmpty = "Nichts aufgezeichnet.";
    public const string LearnProgramLine = "    {0} Start(s), Prüfung {1}, zuletzt {2}";

    public const string ApplyDescription = "Richtet AppStart nach einer Antwortdatei ein (stille Installation). Zweimal angewendet ändert nichts.";
    public const string ConfigOption = "Antwortdatei (JSON) mit Applikationen, Schutz, weiteren Programmen und Bedienern.";
    public const string ReplaceOption = "Applikationen, die nicht in der Antwortdatei stehen, werden entfernt.";

    public const string UninstallDescription = "Stellt alles zurück, was das Setup geändert hat (vor der Deinstallation des MSI ausführen).";
}
