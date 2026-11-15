namespace AppStart.Bootstrapper;

/// <summary>
/// All texts AppStart-Setup.exe shows (German, Swiss spelling). The manual completeness test (task 10.4)
/// reads this class and checks that every message is described in the user manual.
/// Placeholders: {0}, {1} are filled by <see cref="BootstrapperLogic"/>.
/// </summary>
public static class BootstrapperTexts
{
    public const string Title = "AppStart Setup";

    /// <summary>Prefix of the measure line under the cause.</summary>
    public const string MeasureLabel = "Massnahme: ";

    /// <summary>Footer with the msiexec log («Protokoll: C:\ProgramData\AppStart\Logs\install-….log»).</summary>
    public const string LogFooter = "Protokoll: {0}";

    // ---------------------------------------------------------------- progress
    public const string ProgressHeading = "AppStart wird installiert …";
    public const string ProgressText = "Bitte warten. Die Installation kann einige Minuten dauern. Danach startet die Einrichtung.";

    // ---------------------------------------------------------------- success / information
    public const string RestartHeading = "Neustart erforderlich";
    public const string RestartText = "AppStart wurde installiert. Damit alle Änderungen wirksam werden, ist ein Neustart erforderlich.";
    public const string RestartMeasure = "Führen Sie zuerst die Einrichtung durch und starten Sie den Rechner danach neu.";

    public const string AlreadyInstalledHeading = "AppStart ist bereits installiert";
    public const string AlreadyInstalledText = "Installiert ist Version {0}, dieses Setup enthält Version {1}. Die Installation wird übersprungen.";
    public const string AlreadyInstalledMeasure = "Keine. Die Einrichtung startet direkt.";

    // ---------------------------------------------------------------- msiexec exit codes
    public const string CancelledHeading = "Installation abgebrochen";
    public const string CancelledText = "Die Installation wurde abgebrochen (Code 1602). AppStart wurde nicht geändert.";
    public const string CancelledMeasure = "Starten Sie AppStart-Setup.exe erneut, um die Installation zu wiederholen.";

    public const string BusyHeading = "Eine andere Installation läuft gerade";
    public const string BusyText = "Eine andere Installation läuft gerade. Bitte warten und erneut versuchen. (Code 1618)";
    public const string BusyMeasure = "Warten Sie, bis die andere Installation (zum Beispiel Windows Update) abgeschlossen ist, und starten Sie AppStart-Setup.exe dann erneut.";

    public const string FailedHeading = "Installation fehlgeschlagen";
    public const string FailedFatalText = "Bei der Installation ist ein schwerwiegender Fehler aufgetreten (Code 1603). AppStart wurde nicht installiert.";
    public const string FailedOtherText = "Das Installationsprogramm hat mit dem Fehler {0} geendet. AppStart wurde nicht installiert.";
    public const string FailedMeasure = "Prüfen Sie das Protokoll. Wenden Sie sich mit dem Protokoll an den Service.";

    // ---------------------------------------------------------------- before msiexec
    public const string NoPackageHeading = "Kein Installationspaket enthalten";
    public const string NoPackageText = "Diese AppStart-Setup.exe enthält kein Installationspaket (Entwicklungs-Build) und kann AppStart nicht installieren.";
    public const string NoPackageMeasure = "Verwenden Sie die AppStart-Setup.exe aus dem offiziellen Release.";

    public const string NotAdminHeading = "Administratorrechte erforderlich";
    public const string NotAdminText = "AppStart-Setup.exe läuft ohne Administratorrechte und kann AppStart nicht installieren.";
    public const string NotAdminMeasure = "Starten Sie AppStart-Setup.exe mit einem Administratorkonto und bestätigen Sie die Abfrage der Benutzerkontensteuerung.";

    public const string UnsafeFolderHeading = "Ordner nicht geschützt";
    public const string UnsafeFolderText = "Der Ordner {0} kann von Benutzern ohne Administratorrechte verändert werden oder ist ein Verweis. Aus Sicherheitsgründen wird nicht installiert.";
    public const string UnsafeFolderMeasure = "Lassen Sie den Ordner durch einen Administrator prüfen oder löschen und starten Sie AppStart-Setup.exe dann erneut.";

    public const string ExtractFailedHeading = "Installationspaket konnte nicht entpackt werden";
    public const string ExtractFailedText = "Das Installationspaket konnte nicht nach {0} geschrieben werden: {1}";
    public const string ExtractFailedMeasure = "Prüfen Sie den freien Speicherplatz auf Laufwerk C: und starten Sie AppStart-Setup.exe erneut.";

    public const string MsiexecFailedHeading = "Installationsprogramm nicht startbar";
    public const string MsiexecFailedText = "Windows Installer (msiexec.exe) konnte nicht gestartet werden: {0}";
    public const string MsiexecFailedMeasure = "Starten Sie den Rechner neu und starten Sie AppStart-Setup.exe dann erneut. Hilft das nicht, wenden Sie sich an den Service.";

    // ---------------------------------------------------------------- starting the wizard
    public const string SetupMissingHeading = "Einrichtung nicht gefunden";
    public const string SetupMissingText = "AppStart ist installiert, aber {0} fehlt. Die Einrichtung kann nicht starten.";
    public const string SetupMissingMeasure = "Starten Sie AppStart-Setup.exe erneut. Hilft das nicht, wenden Sie sich an den Service.";

    public const string SetupUntrustedHeading = "Einrichtung nicht vertrauenswürdig";
    public const string SetupUntrustedText = "Die Signatur von {0} ist ungültig oder stammt nicht vom Herausgeber dieses Setups. Die Einrichtung wird aus Sicherheitsgründen nicht gestartet.";
    public const string SetupUntrustedMeasure = "Starten Sie die Einrichtung nicht von Hand. Wenden Sie sich an den Service.";

    public const string SetupStartFailedHeading = "Einrichtung konnte nicht gestartet werden";
    public const string SetupStartFailedText = "AppStart ist installiert, aber {0} konnte nicht gestartet werden: {1}";
    public const string SetupStartFailedMeasure = "Starten Sie die Einrichtung über das Startmenü (AppStart – Einrichtung).";

    // ---------------------------------------------------------------- command line
    public const string HelpHeading = "AppStart-Setup.exe";
    public const string HelpText =
        "AppStart-Setup.exe\n" +
        "    Installiert AppStart und startet danach die Einrichtung.\n\n" +
        "AppStart-Setup.exe /quiet\n" +
        "    Installiert nur, ohne Oberfläche und ohne Einrichtung. Der Exit-Code ist der von msiexec (0 oder 3010 = erfolgreich).\n\n" +
        "AppStart-Setup.exe /quiet /config <Antwortdatei>\n" +
        "    Installiert und richtet danach ohne Oberfläche nach der Antwortdatei ein (AppStart.Setup.exe apply). Exit-Code 0 oder 3010 = erfolgreich, 10 = Einrichtung mit Fehler oder Abweichung, 11 = Antwortdatei ungültig.\n\n" +
        "AppStart-Setup.exe /?\n" +
        "    Zeigt diese Hilfe.";
    public const string HelpFooter = "Protokolle: C:\\ProgramData\\AppStart\\Logs";

    public const string InvalidArgumentHeading = "Unbekannter Parameter";
    public const string InvalidArgumentText = "Der Parameter «{0}» ist unbekannt.";
    public const string InvalidArgumentMeasure = "Starten Sie AppStart-Setup.exe ohne Parameter, mit /quiet, mit /quiet /config <Antwortdatei> (absoluter Pfad) oder mit /? für die Hilfe.";

    public const string UnexpectedHeading = "Unerwarteter Fehler";
    public const string UnexpectedText = "Bei der Installation ist ein unerwarteter Fehler aufgetreten: {0}";
    public const string UnexpectedMeasure = "Wenden Sie sich mit dem Protokoll an den Service.";
}
