namespace AppStart.Setup.Engine.Verification;

/// <summary>
/// Every text verify shows (check titles, target state, findings, report). The manual completeness test
/// reads the constants of this class; placeholders {0}, {1} ... are filled with string.Format.
/// </summary>
public static class VerificationTexts
{
    // ------------------------------------------------------------------ Check points (concept table "Prüfbefehl")

    public const string ServiceTitle = "Dienst";
    public const string ServiceExpected = "installiert, läuft, Start automatisch, LocalSystem";

    public const string ProgramFolderTitle = "Programmordner";
    public const string ProgramFolderExpected = "nur SYSTEM und Administratoren schreiben";

    public const string AccountsTitle = "Konten";
    public const string AccountsExpected = "appstart-app vorhanden, keine interaktive Anmeldung; Bediener ohne Admin-Rechte";

    public const string PipeTitle = "Pipe";
    public const string PipeExpected = "Rechte gemäss SDDL, keine Fernzugriffe";

    public const string AppsJsonTitle = "apps.json";
    public const string AppsJsonExpected = "geschützt, jede EXE besteht die Prüfung";

    public const string DataDirTitle = "Datenordner je App";
    public const string DataDirExpected = "nur appstart-app, SYSTEM, Administratoren";

    public const string RegistryTitle = "Registry-Key je App";
    public const string RegistryExpected = "für Bediener nicht lesbar";

    public const string HelpersTitle = "Hilfsprogramme je App";
    public const string HelpersExpected = "vorhanden, Prüfung bestanden, Ordner für Bediener nicht beschreibbar; kein Lernmodus aktiv; «Alle erlaubt» als Hinweis";
    public const string HelpersAllAllowed = "Hinweis: Alle weiteren Programme erlaubt ({0})";
    public const string HelperMissing = "{0}: Hilfsprogramm {1} fehlt";
    public const string HelperCheckFailed = "{0}: {1} besteht die Prüfung nicht ({2}). Nach einem Update: AppStart.Setup.exe rehash --app {0} --child \"{1}\"";
    public const string HelperWritable = "{0}: Ordner oder Datei von {1} ist für Bediener beschreibbar";
    public const string LearnActive = "{0}: Lernmodus aktiv bis {1}. Beenden: AppStart.Setup.exe learn stop --app {0}";
    public const string LearnStateInvalid = "Steuerdatei des Lernmodus wird vom Dienst ignoriert: {0}";

    public const string ShortcutsTitle = "Verknüpfungen";
    public const string ShortcutsExpected = "zeigen auf den Launcher, keine direkte Verknüpfung auf die App-EXE";

    // ------------------------------------------------------------------ Findings: general

    /// <summary>{0}: error message.</summary>
    public const string CheckFailed = "Prüfung nicht möglich: {0}";

    public const string ConfigUnavailable = "apps.json ist nicht lesbar, Prüfpunkt kann nicht geprüft werden";

    public const string AppAccountUnavailable = "Konto «appstart-app» fehlt, Rechte können nicht geprüft werden";

    // ------------------------------------------------------------------ Findings: Dienst

    /// <summary>{0}: service name.</summary>
    public const string ServiceMissing = "Dienst «{0}» ist nicht installiert";

    /// <summary>{0}: service name, {1}: state.</summary>
    public const string ServiceNotRunning = "Dienst «{0}» läuft nicht (Status: {1})";

    /// <summary>{0}: service name, {1}: start type.</summary>
    public const string ServiceNotAutomatic = "Dienst «{0}» startet nicht automatisch (Starttyp: {1})";

    /// <summary>{0}: service name, {1}: account.</summary>
    public const string ServiceWrongAccount = "Dienst «{0}» läuft unter «{1}» statt LocalSystem";

    public const string ServiceStateStopped = "beendet";
    public const string ServiceStateStartPending = "wird gestartet";
    public const string ServiceStateStopPending = "wird beendet";
    public const string ServiceStateContinuePending = "wird fortgesetzt";
    public const string ServiceStatePausePending = "wird angehalten";
    public const string ServiceStatePaused = "angehalten";

    /// <summary>{0}: numeric state.</summary>
    public const string ServiceStateUnknown = "unbekannt ({0})";

    public const string StartTypeBoot = "Boot";
    public const string StartTypeSystem = "System";
    public const string StartTypeManual = "manuell";
    public const string StartTypeDisabled = "deaktiviert";

    /// <summary>{0}: numeric start type.</summary>
    public const string StartTypeUnknown = "unbekannt ({0})";

    // ------------------------------------------------------------------ Findings: Programmordner

    /// <summary>{0}: folder.</summary>
    public const string ProgramFolderMissing = "Programmordner {0} fehlt";

    /// <summary>{0}: folder, {1}: violation.</summary>
    public const string ProgramFolderViolation = "{0}: {1}";

    // ------------------------------------------------------------------ Findings: Konten

    /// <summary>{0}: account name.</summary>
    public const string AppAccountMissing = "Konto «{0}» fehlt";

    /// <summary>{0}: account name.</summary>
    public const string AppAccountNotAdmin = "{0} ist nicht Mitglied der Administratoren";

    /// <summary>{0}: account name, {1}: right, {2}: right described.</summary>
    public const string AppAccountRightMissing = "{0}: Recht {1} fehlt ({2})";

    public const string DenyInteractiveLogonDescription = "lokale Anmeldung verweigern";
    public const string DenyRemoteInteractiveLogonDescription = "Anmeldung über Remotedesktop verweigern";

    /// <summary>{0}: operator account.</summary>
    public const string OperatorIsAdmin = "{0} ist Mitglied der Administratoren";

    /// <summary>{0}: group name, {1}: error message.</summary>
    public const string OperatorGroupUnreadable = "Gruppe «{0}» ist nicht lesbar: {1}";

    // ------------------------------------------------------------------ Findings: Pipe

    /// <summary>{0}: pipe path.</summary>
    public const string PipeMissing = "{0} existiert nicht (läuft der Dienst?)";

    /// <summary>{0}: pipe path, {1}: error message.</summary>
    public const string PipeUnreadable = "Rechte von {0} sind nicht lesbar: {1}";

    public const string PipeNoDacl = "Pipe hat keine Zugriffsliste (NULL DACL): jeder hat Vollzugriff";

    public const string PipeNotProtected = "Zugriffsliste der Pipe ist nicht geschützt (erbt Rechte)";

    /// <summary>{0}: group name.</summary>
    public const string PipeGroupMissing = "Gruppe «{0}» fehlt, Rechte der Pipe können nicht geprüft werden";

    /// <summary>{0}: described entry.</summary>
    public const string PipeAceMissing = "Eintrag fehlt: {0}";

    /// <summary>{0}: allow/deny, {1}: principal, {2}: access mask.</summary>
    public const string PipeAceUnexpected = "Unerwarteter Eintrag: {0} für {1} (Maske 0x{2:X8})";

    public const string AceAllow = "Erlaubt";
    public const string AceDeny = "Verweigert";

    public const string PipeEntrySystem = "SYSTEM Vollzugriff";
    public const string PipeEntryAdministrators = "Administratoren Vollzugriff";

    /// <summary>{0}: group name.</summary>
    public const string PipeEntryOperators = "{0} Verbinden (Maske 0x0012018B, ohne FILE_CREATE_PIPE_INSTANCE)";

    public const string PipeEntryNetworkDenied = "Netzwerk-Anmeldungen verweigert (keine Fernzugriffe)";

    // ------------------------------------------------------------------ Findings: apps.json

    /// <summary>{0}: app name, {1}: exe path, {2}: detail.</summary>
    public const string ExeFailed = "{0} ({1}): {2}";

    public const string ExeMissing = "Datei fehlt";

    /// <summary>{0}: error message.</summary>
    public const string ExeNotReadable = "Datei nicht lesbar: {0}";

    // ------------------------------------------------------------------ Findings: Datenordner, Registry

    /// <summary>{0}: folder.</summary>
    public const string DataDirMissing = "{0}: Ordner fehlt";

    /// <summary>{0}: folder or key, {1}: principal and access.</summary>
    public const string ForeignAccess = "{0}: {1}";

    /// <summary>{0}: registry key.</summary>
    public const string RegistryKeyMissing = "{0}: Schlüssel fehlt";

    // ------------------------------------------------------------------ Findings: Verknüpfungen

    /// <summary>{0}: app name, {1}: shortcut file name.</summary>
    public const string ShortcutMissing = "{0}: keine Verknüpfung «{1}» auf den Launcher im öffentlichen Startmenü oder auf dem öffentlichen Desktop";

    /// <summary>{0}: shortcut path, {1}: target, {2}: arguments, {3}: AppId.</summary>
    public const string ShortcutWrongTarget = "{0}: zeigt auf «{1} {2}» statt auf den Launcher mit --app {3}";

    /// <summary>{0}: shortcut path.</summary>
    public const string ShortcutUnreadable = "{0}: keine lesbare Verknüpfung";

    /// <summary>{0}: shortcut path, {1}: exe path.</summary>
    public const string DirectShortcut = "{0}: zeigt direkt auf {1}";

    // ------------------------------------------------------------------ Report

    public const string ReportTitle = "Prüfbericht AppStart";
    public const string ReportKicker = "PRÜFBERICHT";
    public const string ReportHeading = "AppStart – Prüfung nach Serviceeinsatz";
    public const string BadgeNoDeviation = "Keine Abweichung";
    public const string BadgeOneDeviation = "1 Abweichung";

    /// <summary>{0}: number of deviations.</summary>
    public const string BadgeDeviations = "{0} Abweichungen";

    public const string LabelComputer = "Rechner";
    public const string LabelDate = "Datum";
    public const string LabelTechnician = "Servicetechniker";
    public const string LabelVersion = "Version";

    /// <summary>{0}: version.</summary>
    public const string VersionValue = "AppStart {0}";

    public const string ColumnCheck = "Prüfpunkt";
    public const string ColumnExpected = "Soll";
    public const string ColumnResult = "Ergebnis";
    public const string ResultPassed = "Erfüllt";
    public const string ResultDeviation = "Abweichung";
    public const string ExpectedPrefix = "Soll:";

    public const string FailedTitle = "Einsatz nicht abschliessen";
    public const string FailedHint = "Korrigieren und Prüfung wiederholen:";
    public const string VerifyCommand = "AppStart.Setup.exe verify";

    /// <summary>{0}: passed checks, {1}: all checks.</summary>
    public const string PassedSummary = "Prüfung bestanden – {0} von {1} Prüfpunkten erfüllt";

    public const string SignatureTechnician = "Unterschrift Servicetechniker";
    public const string SignatureCustomer = "Unterschrift Kunde";
}
