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

    public const string UninstallDescription = "Stellt alles zurück, was das Setup geändert hat (vor der Deinstallation des MSI ausführen).";
}
