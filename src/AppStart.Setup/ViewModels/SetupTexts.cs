using System.Globalization;

namespace AppStart.Setup.ViewModels;

/// <summary>
/// All visible texts of the wizard and the verify view (German, Swiss spelling «ss»).
/// The manual completeness test reads them from here; views reference them via x:Static.
/// </summary>
public static class SetupTexts
{
    // ------------------------------------------------------------------ Window, navigation, buttons
    public const string WindowTitle = "AppStart Setup";
    public const string NavApps = "Applikationen";
    public const string NavAccounts = "Konten und Gruppe";
    public const string NavSummary = "Zusammenfassung";
    public const string NavDoneMark = " ✓";

    public const string ButtonCancel = "Abbrechen";
    public const string ButtonBack = "Zurück";
    public const string ButtonNext = "Weiter";
    public const string ButtonFinish = "Fertig";
    public const string ButtonOpenReport = "Prüfbericht öffnen";
    public const string ButtonVerifySetup = "Einrichtung prüfen";

    // ------------------------------------------------------------------ Step 1: applications
    public const string AppsHeading = "Applikationen auswählen";
    public const string AppsIntro = "Diese Applikationen starten Bediener künftig über den Start-Dienst. Jede bekommt eine eigene Verknüpfung mit ihrem Icon.";
    public const string AppsColumnApp = "Applikation";
    public const string AppsColumnCheck = "Prüfung";
    public const string CheckSignature = "Signatur";
    public const string CheckSha256 = "SHA-256";
    public const string ButtonAddExe = "EXE hinzufügen…";
    public const string ButtonRemove = "Entfernen";
    public const string AppsEmpty = "Noch keine Applikation. Mit «EXE hinzufügen…» eine Programmdatei wählen.";
    public const string ProtectionHeadingFormat = "Schutz für {0}";
    public const string DataDirLabel = "Datenordner";
    public const string ButtonBrowse = "Durchsuchen…";
    public const string RegistryKeyLabel = "Registry-Key";
    public const string RegistryKeyNote = "wird gesperrt";
    public const string RegistryKeyInvalid = "Der Registry-Key muss mit HKLM\\ beginnen.";
    public const string DataDirInvalid = "Der Datenordner muss ein vollständiger Pfad sein, z.B. C:\\ProgramData\\Hersteller\\App.";
    public const string NoAppSelected = "Mindestens eine Applikation auswählen.";
    public const string OpenExeTitle = "Programmdatei auswählen";
    public const string OpenExeFilter = "Programme (*.exe)|*.exe";
    public const string DataDirDialogTitle = "Datenordner auswählen";
    public const string LoadFailedFormat = "Die bestehende Konfiguration konnte nicht gelesen werden: {0}";
    public const string InspectFailedFormat = "{0} kann nicht hinzugefügt werden: {1}";

    // ------------------------------------------------------------------ Step 1: further programs (docs/ui-designs/Main, add-allowed-child-processes)
    public const string AppsColumnChildren = "Weitere Programme";
    public const string ChildrenLabel = "Weitere Programme";
    public const string ChildModeBlocked = "Gesperrt (empfohlen)";
    public const string ChildModeAllowlist = "Nur erlaubte Hilfsprogramme";
    public const string ChildModeAll = "Alle erlaubt (unsicher)";
    public const string ChildSummaryBlocked = "gesperrt";
    public const string ChildSummaryAllowlistFormat = "{0} erlaubt";
    public const string ChildSummaryAll = "alle erlaubt";
    public const string ButtonHelpersFormat = "Hilfsprogramme ({0})…";
    public const string ChildHintBlocked = "Applikation {0} kann keine weiteren Programme starten.";
    public const string ChildHintAllowlistFormat = "Nur Programme auf der Liste dürfen aus {0} starten. Alle anderen beendet der Dienst sofort.";
    public const string ChildHintAllFormat = "{0} darf jedes Programm starten, auch eine Eingabeaufforderung mit Admin-Rechten.";
    public const string ChildAllowlistEmpty = "Mindestens ein Hilfsprogramm hinzufügen («Hilfsprogramme…») oder «Gesperrt» wählen.";
    public const string ConfirmAllHeading = "Alle weiteren Programme erlauben?";
    public const string ConfirmAllTextFormat = "{0} darf dann jedes Programm starten, auch eine Eingabeaufforderung mit Admin-Rechten. Bediener können so den Schutz umgehen. Nur nach Abwägung und mit Vermerk im Service and Security Manual.";

    // ------------------------------------------------------------------ Dialog «Erlaubte Hilfsprogramme» (docs/ui-designs/Helpers)
    public const string HelpersTitleFormat = "Erlaubte Hilfsprogramme – {0}";
    public const string HelpersHeading = "Erlaubte Hilfsprogramme";
    public const string HelpersIntroFormat = "Diese Programme darf {0} starten. Sie laufen dann ebenfalls mit Admin-Rechten. Jedes andere Programm beendet der Dienst sofort und protokolliert es.";
    public const string HelpersColumnProgram = "Programm";
    public const string HelpersColumnCheck = "Prüfung";
    public const string HelpersColumnState = "Zustand";
    public const string HelperStateOk = "In Ordnung";
    public const string HelperStateRenew = "Prüfsumme neu";
    public const string HelperStateMissing = "Datei fehlt";
    public const string HelperStateInvalid = "Prüfung fehlgeschlagen";
    public const string ButtonLearnStart = "Lernmodus starten…";
    public const string ButtonRehash = "Prüfsumme aktualisieren";
    public const string ButtonApply = "Übernehmen";
    public const string HelpersUnsignedFormat = "{0} ist nicht signiert";
    public const string HelpersUnsignedText = "Nach jedem Update der Applikation stimmt die Prüfsumme nicht mehr und der Start wird blockiert. Dann hier «Prüfsumme aktualisieren».";
    public const string HelpersFooter = "Erlaubt sind nur Programme aus Ordnern, in die Bediener nicht schreiben können. Eingabeaufforderung, PowerShell, Explorer und Skript-Hosts nur mit ausdrücklicher Bestätigung.";
    public const string HelperWritableFormat = "{0} kann nicht erlaubt werden: Der Ordner ist für Bediener beschreibbar. Programm in einen geschützten Ordner installieren.";
    public const string ConfirmShellHeadingFormat = "{0} erlauben?";
    public const string ConfirmShellText = "Dies ist eine Shell oder ein Skript-Host. Wer die Applikation bedient, kann damit jedes Programm mit Admin-Rechten starten.";
    public const string LearnNeedsReleaseFormat = "Der Lernmodus ist erst möglich, wenn {0} eingerichtet ist. Zuerst den Assistenten abschliessen, dann erneut öffnen.";

    // ------------------------------------------------------------------ Dialog «Lernmodus läuft» (docs/ui-designs/Learn)
    public const string LearnTitleFormat = "Lernmodus – {0}";
    public const string LearnHeading = "Lernmodus läuft";
    public const string LearnIntroFormat = "Starten Sie {0} jetzt über ihre Verknüpfung und benutzen Sie alle Funktionen, die Hilfsprogramme brauchen: Export, Drucken, Update, Hilfe.";
    public const string LearnEndsIn = "endet automatisch in";
    public const string LearnWarningTitleFormat = "Während des Lernmodus ist der Schutz für {0} schwächer.";
    public const string LearnWarningText = "Programme werden nicht beendet, nur aufgezeichnet. Den PC dabei nicht unbeaufsichtigt lassen.";
    public const string LearnColumnPrograms = "Aufgezeichnete Programme";
    public const string LearnColumnStarts = "Starts";
    public const string LearnColumnLast = "Zuletzt";
    public const string LearnNothingYet = "Noch nichts aufgezeichnet.";
    public const string ButtonLearnCancel = "Abbrechen, nichts übernehmen";
    public const string ButtonLearnFinish = "Lernmodus beenden";
    public const string LearnStartFailedFormat = "Der Lernmodus konnte nicht gestartet werden: {0}";

    // ------------------------------------------------------------------ Dialog «Vorschläge übernehmen» (docs/ui-designs/LearnResult)
    public const string LearnResultHeading = "Vorschläge übernehmen";
    public const string LearnResultIntroFormat = "Der Lernmodus ist beendet, der Schutz wieder aktiv. Wählen Sie, welche Programme {0} künftig starten darf.";
    public const string LearnResultColumnHint = "Hinweis";
    public const string LearnHintUnsigned = "nicht signiert";
    public const string LearnHintOpensFiles = "kann Dateien öffnen – abwägen";
    public const string LearnHintShell = "Shell oder Skript-Host – nur von Hand";
    public const string LearnHintCmd = "Eingabeaufforderung – nur von Hand";
    public const string LearnHintWritable = "Ordner für Bediener beschreibbar";
    public const string LearnResultFooter = "Eingabeaufforderung, PowerShell, Explorer und Skript-Hosts werden nie vorgeschlagen. Wer sie trotzdem braucht, trägt sie in «Erlaubte Hilfsprogramme» von Hand ein und bestätigt die Warnung.";
    public const string ButtonDiscard = "Verwerfen";
    public const string ButtonTakeOverFormat = "{0} Programme übernehmen";
    public const string ButtonTakeOverOne = "1 Programm übernehmen";
    public const string LearnResultNothing = "Es wurde nichts aufgezeichnet. Wurde die Applikation während des Lernmodus über ihre Verknüpfung gestartet?";

    // ------------------------------------------------------------------ Step 2: accounts and group
    public const string AccountsHeading = "Konten und Gruppe";
    public const string AccountsIntro = "Die Applikationen laufen unter einem eigenen Konto. Nur Mitglieder der Gruppe dürfen den Launcher nutzen.";
    public const string AppAccountTitle = "App-Konto appstart-app";
    public const string AppAccountNew = "Wird angelegt. Admin-Rechte, Zufallspasswort pro PC, keine interaktive Anmeldung und kein Remote Desktop.";
    public const string AppAccountExisting = "Ist vorhanden und wird geprüft. Admin-Rechte, Zufallspasswort pro PC, keine interaktive Anmeldung und kein Remote Desktop.";
    public const string BadgeAutomatic = "automatisch";
    public const string CreateOperatorAccount = "Bedienerkonto appstart-user anlegen";
    public const string OperatorAccountExisting = "Bedienerkonto appstart-user ist bereits vorhanden";
    public const string BadgeExisting = "vorhanden";
    public const string PasswordLabel = "Passwort";
    public const string PasswordConfirmLabel = "Passwort bestätigen";
    public const string OperatorAccountNote = "Standard-Benutzer ohne Admin-Rechte, Mitglied der Gruppe AppStart Users.";
    public const string PasswordEmpty = "Bitte ein Passwort eingeben.";
    public const string PasswordMismatch = "Die Passwörter stimmen nicht überein.";
    public const string MembersHeading = "Weitere Mitglieder der Gruppe AppStart Users";
    public const string MemberPlaceholder = "DOMAIN\\Benutzer";
    public const string MemberInputName = "Benutzer hinzufügen";
    public const string ButtonAddMember = "Hinzufügen";
    public const string RemoveMember = "Mitglied entfernen";
    public const string MembersRemoveNote = "Mit ✕ entfernte Mitglieder nimmt das Setup beim Übernehmen aus der Gruppe. Die Änderung wirkt nach der nächsten Anmeldung des Benutzers.";
    public const string AdminsWithoutPasswordFormat = "Administratoren ohne Passwort: {0}. Damit kann sich jeder an diesem PC als Administrator anmelden und die Absicherung aufheben. Passwort setzen oder Konto deaktivieren; die Prüfung meldet es sonst als Abweichung.";
    public const string MemberDuplicate = "Dieses Konto ist bereits in der Liste.";
    public const string MemberInvalid = "Konto als DOMAIN\\Benutzer, .\\Benutzer oder Benutzer angeben.";

    // ------------------------------------------------------------------ Step 3: summary and check
    public const string SummaryRunningHeading = "Einrichtung läuft";
    public const string SummaryRunningIntro = "Die Schritte werden ausgeführt. Bitte warten.";
    public const string SummaryDoneHeading = "Einrichtung abgeschlossen";
    public const string SummaryDonePassedIntro = "Alle Schritte wurden ausgeführt. Die abschliessende Prüfung ist bestanden.";
    public const string SummaryDoneDeviationIntro = "Alle Schritte wurden ausgeführt. Die abschliessende Prüfung hat Abweichungen gefunden.";
    public const string SummaryFailedHeading = "Einrichtung unvollständig";
    public const string SummaryFailedIntro = "Nicht alle Schritte waren erfolgreich. Die Prüfung zeigt, was fehlt.";
    public const string ProgressStarting = "Einrichtung wird gestartet…";
    public const string ProgressVerifying = "Abschliessende Prüfung läuft…";
    public const string ApplyFailedFormat = "Einrichtung abgebrochen: {0}";
    public const string VerifyPassedFormat = "Prüfung bestanden – {0} von {1} Prüfpunkten erfüllt";
    public const string VerifyFailedOneFormat = "Prüfung nicht bestanden – 1 Abweichung, {0} von {1} Prüfpunkten erfüllt";
    public const string VerifyFailedManyFormat = "Prüfung nicht bestanden – {2} Abweichungen, {0} von {1} Prüfpunkten erfüllt";
    public const string VerifyErrorFormat = "Prüfung konnte nicht ausgeführt werden: {0}";
    public const string ReportPathFormat = "Bericht: {0}";
    public const string ReportWriteFailedFormat = "Bericht konnte nicht gespeichert werden: {0}";
    public const string OpenReportFailedFormat = "Der Bericht kann nicht geöffnet werden: {0}";

    // ------------------------------------------------------------------ Verify view (Prüfbericht)
    public const string VerifyWindowTitle = "AppStart – Einrichtung prüfen";
    public const string VerifyKicker = "PRÜFBERICHT";
    public const string VerifyHeading = "AppStart – Prüfung nach Serviceeinsatz";
    public const string VerifyRunning = "Prüfung läuft…";
    public const string MetaComputer = "Rechner";
    public const string MetaDate = "Datum";
    public const string MetaTechnician = "Servicetechniker";
    public const string MetaVersion = "Version";
    public const string VersionFormat = "AppStart {0}";
    public const string DateFormat = "dd.MM.yyyy HH:mm";
    public const string ColumnCheck = "Prüfpunkt";
    public const string ColumnExpected = "Soll";
    public const string ColumnResult = "Ergebnis";
    public const string ResultPassed = "Erfüllt";
    public const string ResultDeviation = "Abweichung";
    public const string BadgeNoDeviation = "Keine Abweichung";
    public const string BadgeOneDeviation = "1 Abweichung";
    public const string BadgeDeviationsFormat = "{0} Abweichungen";
    public const string DeviationBoxTitle = "Einsatz nicht abschliessen";
    public const string DeviationBoxText = "Abweichungen korrigieren und Prüfung wiederholen:";
    public const string VerifyCommand = "AppStart.Setup.exe verify";
    public const string PassedBoxTitle = "Einrichtung entspricht dem Soll";
    public const string PassedBoxFormat = "Alle {0} Prüfpunkte erfüllt. Der Einsatz kann abgeschlossen werden.";
    public const string SignatureTechnician = "Unterschrift Servicetechniker";
    public const string SignatureCustomer = "Unterschrift Kunde";
    public const string ButtonPrint = "Bericht drucken";
    public const string ButtonSaveAs = "Bericht speichern unter";
    public const string ButtonRepeat = "Prüfung wiederholen";
    public const string ButtonClose = "Schliessen";
    public const string ReportSavedFormat = "Bericht gespeichert: {0}";
    public const string ReportCopiedFormat = "Bericht kopiert nach {0}";
    public const string SaveAsTitle = "Bericht speichern unter";
    public const string SaveAsFilter = "Prüfbericht (*.html)|*.html";
    public const string SaveAsFailedFormat = "Der Bericht konnte nicht kopiert werden: {0}";
    public const string PrintFailedFormat = "Der Bericht konnte nicht gedruckt werden: {0}";
    public const string PrintJobName = "AppStart Prüfbericht";

    // ------------------------------------------------------------------ Helpers

    public static string Format(string format, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args);

    public static string CheckText(Core.Configuration.IntegrityCheck check) =>
        check == Core.Configuration.IntegrityCheck.Signature ? CheckSignature : CheckSha256;

    public static string DeviationBadge(int deviations) => deviations switch
    {
        0 => BadgeNoDeviation,
        1 => BadgeOneDeviation,
        _ => Format(BadgeDeviationsFormat, deviations),
    };

    public static string VerifySummary(int passed, int total)
    {
        var deviations = total - passed;
        return deviations switch
        {
            0 => Format(VerifyPassedFormat, passed, total),
            1 => Format(VerifyFailedOneFormat, passed, total),
            _ => Format(VerifyFailedManyFormat, passed, total, deviations),
        };
    }
}
