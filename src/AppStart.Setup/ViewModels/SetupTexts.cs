using System.Globalization;

namespace AppStart.Setup.ViewModels;

/// <summary>
/// All visible texts of the wizard and the verify view (US English).
/// The manual completeness test reads them from here; views reference them via x:Static.
/// </summary>
public static class SetupTexts
{
    // ------------------------------------------------------------------ Window, navigation, buttons
    public const string WindowTitle = "AppStart Setup";
    public const string NavApps = "Applications";
    public const string NavAccounts = "Accounts and group";
    public const string NavSummary = "Summary";
    public const string NavDoneMark = " ✓";

    public const string ButtonCancel = "Cancel";
    public const string ButtonBack = "Back";
    public const string ButtonNext = "Next";
    public const string ButtonFinish = "Finish";
    public const string ButtonOpenReport = "Open verification report";
    public const string ButtonVerifySetup = "Verify setup";

    // ------------------------------------------------------------------ Step 1: applications
    public const string AppsHeading = "Select application";
    public const string AppsIntro = "Operators will start these applications through the AppStart service. Each one gets its own shortcut with its icon.";
    public const string AppsColumnApp = "Application";
    public const string AppsColumnCheck = "Verification";
    public const string CheckSignature = "Signature";
    public const string CheckSha256 = "SHA-256";
    public const string ButtonAddExe = "Add EXE…";
    public const string ButtonRemove = "Remove";
    public const string AppsEmpty = "No application yet. Choose a program file with \"Add EXE…\".";
    public const string ProtectionHeadingFormat = "Protection for {0}";
    public const string DataDirLabel = "Data folder";
    public const string ButtonBrowse = "Browse…";
    public const string RegistryKeyLabel = "Registry key";
    public const string RegistryKeyNote = "will be locked";
    public const string ProtectionExplanation = "The data folder and registry key are locked when the setup is applied: afterwards only appstart-app, SYSTEM and Administrators have access, operators do not. If the target is missing, the setup creates it. On uninstall the original permissions are restored. Leave empty if the application stores nothing there; then nothing is locked. Examples: C:\\ProgramData\\<Vendor>\\<App> and HKLM\\SOFTWARE\\<Vendor>\\<App>.";
    public const string DataDirInProgramFolderWarning = "Caution: This data folder is the program folder of the application, lies inside it or contains it. appstart-app would get write access to the EXE and DLLs. The verification detects a replaced EXE, but not replaced DLLs. A folder under C:\\ProgramData is recommended, e.g. C:\\ProgramData\\<Vendor>\\<App>.";
    public const string DataDirConfirmPendingFormat = "The data folder of {0} lies in the program folder: change it or confirm the risk.";
    public const string DataDirAcceptRisk = "I understand the risk, use this folder anyway";
    public const string RegistryKeyInvalid = "The registry key must start with HKLM\\.";
    public const string DataDirInvalid = "The data folder must be a full path, e.g. C:\\ProgramData\\Vendor\\App.";
    public const string NoAppSelected = "Select at least one application.";
    public const string OpenExeTitle = "Select program file";
    public const string OpenExeFilter = "Programs (*.exe)|*.exe";
    public const string DataDirDialogTitle = "Select data folder";
    public const string LoadFailedFormat = "The existing configuration could not be read: {0}";
    public const string InspectFailedFormat = "{0} cannot be added: {1}";

    // ------------------------------------------------------------------ Step 1: other programs (docs/ui-designs/Main, add-allowed-child-processes)
    public const string AppsColumnChildren = "Other programs";
    public const string ChildrenLabel = "Other programs";
    public const string ChildModeBlocked = "Blocked (recommended)";
    public const string ChildModeAllowlist = "Allowed helper programs only";
    public const string ChildModeAll = "All allowed (unsafe)";
    public const string ChildSummaryBlocked = "blocked";
    public const string ChildSummaryAllowlistFormat = "{0} allowed";
    public const string ChildSummaryAll = "all allowed";
    public const string ButtonHelpersFormat = "Helper programs ({0})…";
    public const string ChildHintBlocked = "Application {0} cannot start other programs.";
    public const string ChildHintAllowlistFormat = "Only programs on the list may start from {0}. The service ends all others immediately.";
    public const string ChildHintAllFormat = "{0} may start any program, including a command prompt with admin rights.";
    public const string ChildAllowlistEmpty = "Add at least one helper program (\"Helper programs…\") or choose \"Blocked\".";
    public const string ConfirmAllHeading = "Allow all other programs?";
    public const string ConfirmAllTextFormat = "{0} may then start any program, including a command prompt with admin rights. Operators can use this to bypass the protection. Only after careful consideration and with a note in the Service and Security Manual.";

    // ------------------------------------------------------------------ Dialog "Allowed helper programs" (docs/ui-designs/Helpers)
    public const string HelpersTitleFormat = "Allowed helper programs – {0}";
    public const string HelpersHeading = "Allowed helper programs";
    public const string HelpersIntroFormat = "{0} may start these programs. They then also run with admin rights. The service ends any other program immediately and logs it.";
    public const string HelpersColumnProgram = "Program";
    public const string HelpersColumnCheck = "Verification";
    public const string HelpersColumnState = "State";
    public const string HelperStateOk = "OK";
    public const string HelperStateRenew = "New checksum";
    public const string HelperStateMissing = "File missing";
    public const string HelperStateInvalid = "Verification failed";
    public const string ButtonLearnStart = "Start learning mode…";
    public const string ButtonRehash = "Update checksum";
    public const string ButtonApply = "Apply";
    public const string HelpersUnsignedFormat = "{0} is not signed";
    public const string HelpersUnsignedText = "After every update of the application the checksum no longer matches and the start is blocked. Then click \"Update checksum\" here.";
    public const string HelpersFooter = "Only programs from folders that operators cannot write to are allowed. Command prompt, PowerShell, Explorer and script hosts only with explicit confirmation.";
    public const string HelperWritableFormat = "{0} cannot be allowed: operators can write to its folder. Install the program in a protected folder.";
    public const string ConfirmShellHeadingFormat = "Allow {0}?";
    public const string ConfirmShellText = "This is a shell or a script host. Whoever operates the application can use it to start any program with admin rights.";
    public const string LearnNeedsReleaseFormat = "Learning mode is only possible once {0} is set up. Finish the wizard first, then open this again.";

    // ------------------------------------------------------------------ Dialog "Learning mode running" (docs/ui-designs/Learn)
    public const string LearnTitleFormat = "Learning mode – {0}";
    public const string LearnHeading = "Learning mode running";
    public const string LearnIntroFormat = "Start {0} now through its shortcut and use all functions that need helper programs: export, print, update, help.";
    public const string LearnEndsIn = "ends automatically in";
    public const string LearnWarningTitleFormat = "During learning mode the protection for {0} is weaker.";
    public const string LearnWarningText = "Programs are not ended, only recorded. Do not leave the PC unattended meanwhile.";
    public const string LearnColumnPrograms = "Recorded programs";
    public const string LearnColumnStarts = "Starts";
    public const string LearnColumnLast = "Last";
    public const string LearnNothingYet = "Nothing recorded yet.";
    public const string ButtonLearnCancel = "Cancel, apply nothing";
    public const string ButtonLearnFinish = "End learning mode";
    public const string LearnStartFailedFormat = "Learning mode could not be started: {0}";

    // ------------------------------------------------------------------ Dialog "Apply suggestions" (docs/ui-designs/LearnResult)
    public const string LearnResultHeading = "Apply suggestions";
    public const string LearnResultIntroFormat = "Learning mode has ended, the protection is active again. Choose which programs {0} may start from now on.";
    public const string LearnResultColumnHint = "Note";
    public const string LearnHintUnsigned = "not signed";
    public const string LearnHintOpensFiles = "can open files – consider carefully";
    public const string LearnHintShell = "shell or script host – manual only";
    public const string LearnHintCmd = "command prompt – manual only";
    public const string LearnHintWritable = "folder writable by operators";
    public const string LearnResultFooter = "Command prompt, PowerShell, Explorer and script hosts are never suggested. Whoever still needs them adds them by hand in \"Allowed helper programs\" and confirms the warning.";
    public const string ButtonDiscard = "Discard";
    public const string ButtonTakeOverFormat = "Apply {0} programs";
    public const string ButtonTakeOverOne = "Apply 1 program";
    public const string LearnResultNothing = "Nothing was recorded. Was the application started through its shortcut during learning mode?";

    // ------------------------------------------------------------------ Step 2: accounts and group
    public const string AccountsHeading = "Accounts and group";
    public const string AccountsIntro = "The applications run under a dedicated account. Only members of the group may use the launcher.";
    public const string AppAccountTitle = "App account appstart-app";
    public const string AppAccountNew = "Will be created. Admin rights, random password per PC, no interactive sign-in and no Remote Desktop.";
    public const string AppAccountExisting = "Exists and will be verified. Admin rights, random password per PC, no interactive sign-in and no Remote Desktop.";
    public const string BadgeAutomatic = "automatic";
    public const string CreateOperatorAccount = "Create operator account appstart-user";
    public const string OperatorAccountExisting = "Operator account appstart-user already exists";
    public const string BadgeExisting = "exists";
    public const string PasswordLabel = "Password";
    public const string PasswordConfirmLabel = "Confirm password";
    public const string OperatorAccountNote = "Standard user without admin rights, member of the group AppStart Users.";
    public const string PasswordEmpty = "Please enter a password.";
    public const string PasswordMismatch = "The passwords do not match.";
    public const string MembersHeading = "Other members of the group AppStart Users";
    public const string MemberPlaceholder = "DOMAIN\\user";
    public const string MemberInputName = "Add user";
    public const string ButtonAddMember = "Add";
    public const string RemoveMember = "Remove member";
    public const string MembersRemoveNote = "Members removed with ✕ are taken out of the group when the setup is applied. The change takes effect at the user's next sign-in.";
    public const string AdminsWithoutPasswordFormat = "Administrators without a password: {0}. Anyone can then sign in to this PC as an administrator and remove the protection. Set a password or disable the account; otherwise the verification reports it as a deviation.";
    public const string MemberDuplicate = "This account is already in the list.";
    public const string MemberInvalid = "Enter the account as DOMAIN\\user, .\\user or user.";

    // ------------------------------------------------------------------ Step 3: summary and check
    public const string SummaryRunningHeading = "Setup running";
    public const string SummaryRunningIntro = "The steps are being carried out. Please wait.";
    public const string SummaryDoneHeading = "Setup complete";
    public const string SummaryDonePassedIntro = "All steps were carried out. The final verification passed.";
    public const string SummaryDoneDeviationIntro = "All steps were carried out. The final verification found deviations.";
    public const string SummaryFailedHeading = "Setup incomplete";
    public const string SummaryFailedIntro = "Not all steps succeeded. The verification shows what is missing.";
    public const string ProgressStarting = "Starting setup…";
    public const string ProgressVerifying = "Final verification running…";
    public const string ApplyFailedFormat = "Setup aborted: {0}";
    public const string VerifyPassedFormat = "Verification passed – {0} of {1} checks passed";
    public const string VerifyFailedOneFormat = "Verification failed – 1 deviation, {0} of {1} checks passed";
    public const string VerifyFailedManyFormat = "Verification failed – {2} deviations, {0} of {1} checks passed";
    public const string VerifyErrorFormat = "Verification could not be run: {0}";
    public const string ReportPathFormat = "Report: {0}";
    public const string ReportWriteFailedFormat = "Report could not be saved: {0}";
    public const string OpenReportFailedFormat = "The report cannot be opened: {0}";

    // ------------------------------------------------------------------ Verify view (verification report)
    public const string VerifyWindowTitle = "AppStart – Verify setup";
    public const string VerifyKicker = "VERIFICATION REPORT";
    public const string VerifyHeading = "AppStart – Verification after a service visit";
    public const string VerifyRunning = "Verification running…";
    public const string MetaComputer = "Computer";
    public const string MetaDate = "Date";
    public const string MetaTechnician = "Service technician";
    public const string MetaVersion = "Version";
    public const string VersionFormat = "AppStart {0}";
    public const string DateFormat = "dd.MM.yyyy HH:mm";
    public const string ColumnCheck = "Check";
    public const string ColumnExpected = "Expected";
    public const string ColumnResult = "Result";
    public const string ResultPassed = "Passed";
    public const string ResultDeviation = "Deviation";
    public const string BadgeNoDeviation = "No deviation";
    public const string BadgeOneDeviation = "1 deviation";
    public const string BadgeDeviationsFormat = "{0} deviations";
    public const string DeviationBoxTitle = "Do not close the service visit";
    public const string DeviationBoxText = "Correct the deviations and repeat the verification:";
    public const string VerifyCommand = "AppStart.Setup.exe verify";
    public const string PassedBoxTitle = "Setup matches the expected state";
    public const string PassedBoxFormat = "All {0} checks passed. The service visit can be closed.";
    public const string SignatureTechnician = "Signature service technician";
    public const string SignatureCustomer = "Signature customer";
    public const string ButtonPrint = "Print report";
    public const string ButtonSaveAs = "Save report as";
    public const string ButtonRepeat = "Repeat verification";
    public const string ButtonClose = "Close";
    public const string ReportSavedFormat = "Report saved: {0}";
    public const string ReportCopiedFormat = "Report copied to {0}";
    public const string SaveAsTitle = "Save report as";
    public const string SaveAsFilter = "Verification report (*.html)|*.html";
    public const string SaveAsFailedFormat = "The report could not be copied: {0}";
    public const string PrintFailedFormat = "The report could not be printed: {0}";
    public const string PrintJobName = "AppStart verification report";

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
