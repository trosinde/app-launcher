namespace AppStart.Setup.Engine.Verification;

/// <summary>
/// Every text verify shows (check titles, target state, findings, report). The manual completeness test
/// reads the constants of this class; placeholders {0}, {1} ... are filled with string.Format.
/// </summary>
public static class VerificationTexts
{
    // ------------------------------------------------------------------ Checks (concept table "verify command")

    public const string ServiceTitle = "Service";
    public const string ServiceExpected = "installed, running, starts automatically, LocalSystem";

    public const string ProgramFolderTitle = "Program folder";
    public const string ProgramFolderExpected = "only SYSTEM and Administrators can write";

    public const string AccountsTitle = "Accounts";
    public const string AccountsExpected = "appstart-app exists, no interactive sign-in; operators without admin rights";

    public const string AdminPasswordTitle = "Administrators with password";
    public const string AdminPasswordExpected = "every enabled account in the Administrators group has a password";
    public const string AdminWithoutPassword = "{0} is an administrator and has no password. Set a password or disable the account";
    public const string AdminPasswordUncheckable = "Password of {0} cannot be verified (Windows error {1}), e.g. because the account is locked out";

    public const string PipeTitle = "Pipe";
    public const string PipeExpected = "permissions as per SDDL, no remote access";

    public const string AppsJsonTitle = "apps.json";
    public const string AppsJsonExpected = "protected, every EXE passes the verification";

    public const string DataDirTitle = "Data folder per app";
    public const string DataDirExpected = "only appstart-app, SYSTEM, Administrators";

    public const string RegistryTitle = "Registry key per app";
    public const string RegistryExpected = "not readable by operators";

    public const string HelpersTitle = "Helper programs per app";
    public const string HelpersExpected = "present, verification passed, folder not writable by operators; no learning mode active; \"All allowed\" as a note";
    public const string HelpersAllAllowed = "Note: All other programs allowed ({0})";
    public const string HelperMissing = "{0}: helper program {1} is missing";
    public const string HelperCheckFailed = "{0}: {1} fails the verification ({2}). After an update: AppStart.Setup.exe rehash --app {0} --child \"{1}\"";
    public const string HelperWritable = "{0}: folder or file of {1} is writable by operators";
    public const string LearnActive = "{0}: learning mode active until {1}. To end it: AppStart.Setup.exe learn stop --app {0}";
    public const string LearnStateInvalid = "The service ignores the learning mode control file: {0}";

    public const string ShortcutsTitle = "Shortcuts";
    public const string ShortcutsExpected = "point to the launcher, no direct shortcut to the app EXE";

    // ------------------------------------------------------------------ Findings: general

    /// <summary>{0}: error message.</summary>
    public const string CheckFailed = "Verification not possible: {0}";

    public const string ConfigUnavailable = "apps.json is not readable, check cannot be verified";

    public const string AppAccountUnavailable = "Account \"appstart-app\" is missing, permissions cannot be verified";

    // ------------------------------------------------------------------ Findings: service

    /// <summary>{0}: service name.</summary>
    public const string ServiceMissing = "Service \"{0}\" is not installed";

    /// <summary>{0}: service name, {1}: state.</summary>
    public const string ServiceNotRunning = "Service \"{0}\" is not running (status: {1})";

    /// <summary>{0}: service name, {1}: start type.</summary>
    public const string ServiceNotAutomatic = "Service \"{0}\" does not start automatically (start type: {1})";

    /// <summary>{0}: service name, {1}: account.</summary>
    public const string ServiceWrongAccount = "Service \"{0}\" runs as \"{1}\" instead of LocalSystem";

    public const string ServiceStateStopped = "stopped";
    public const string ServiceStateStartPending = "starting";
    public const string ServiceStateStopPending = "stopping";
    public const string ServiceStateContinuePending = "resuming";
    public const string ServiceStatePausePending = "pausing";
    public const string ServiceStatePaused = "paused";

    /// <summary>{0}: numeric state.</summary>
    public const string ServiceStateUnknown = "unknown ({0})";

    public const string StartTypeBoot = "Boot";
    public const string StartTypeSystem = "System";
    public const string StartTypeManual = "manual";
    public const string StartTypeDisabled = "disabled";

    /// <summary>{0}: numeric start type.</summary>
    public const string StartTypeUnknown = "unknown ({0})";

    // ------------------------------------------------------------------ Findings: program folder

    /// <summary>{0}: folder.</summary>
    public const string ProgramFolderMissing = "Program folder {0} is missing";

    /// <summary>{0}: folder, {1}: violation.</summary>
    public const string ProgramFolderViolation = "{0}: {1}";

    // ------------------------------------------------------------------ Findings: accounts

    /// <summary>{0}: account name.</summary>
    public const string AppAccountMissing = "Account \"{0}\" is missing";

    /// <summary>{0}: account name.</summary>
    public const string AppAccountNotAdmin = "{0} is not a member of Administrators";

    /// <summary>{0}: account name, {1}: right, {2}: right described.</summary>
    public const string AppAccountRightMissing = "{0}: right {1} is missing ({2})";

    public const string DenyInteractiveLogonDescription = "deny log on locally";
    public const string DenyRemoteInteractiveLogonDescription = "deny log on through Remote Desktop Services";

    /// <summary>{0}: operator account.</summary>
    public const string OperatorIsAdmin = "{0} is a member of Administrators";

    /// <summary>{0}: group name, {1}: error message.</summary>
    public const string OperatorGroupUnreadable = "Group \"{0}\" is not readable: {1}";

    // ------------------------------------------------------------------ Findings: pipe

    /// <summary>{0}: pipe path.</summary>
    public const string PipeMissing = "{0} does not exist (is the service running?)";

    /// <summary>{0}: pipe path, {1}: error message.</summary>
    public const string PipeUnreadable = "Permissions of {0} are not readable: {1}";

    public const string PipeNoDacl = "Pipe has no access control list (NULL DACL): everyone has full access";

    public const string PipeNotProtected = "Access control list of the pipe is not protected (inherits permissions)";

    /// <summary>{0}: group name.</summary>
    public const string PipeGroupMissing = "Group \"{0}\" is missing, permissions of the pipe cannot be verified";

    /// <summary>{0}: described entry.</summary>
    public const string PipeAceMissing = "Entry missing: {0}";

    /// <summary>{0}: allow/deny, {1}: principal, {2}: access mask.</summary>
    public const string PipeAceUnexpected = "Unexpected entry: {0} for {1} (mask 0x{2:X8})";

    public const string AceAllow = "Allow";
    public const string AceDeny = "Deny";

    public const string PipeEntrySystem = "SYSTEM full access";
    public const string PipeEntryAdministrators = "Administrators full access";

    /// <summary>{0}: group name.</summary>
    public const string PipeEntryOperators = "{0} connect (mask 0x0012018B, without FILE_CREATE_PIPE_INSTANCE)";

    public const string PipeEntryNetworkDenied = "Network logons denied (no remote access)";

    // ------------------------------------------------------------------ Findings: apps.json

    /// <summary>{0}: app name, {1}: exe path, {2}: detail.</summary>
    public const string ExeFailed = "{0} ({1}): {2}";

    public const string ExeMissing = "File missing";

    /// <summary>{0}: error message.</summary>
    public const string ExeNotReadable = "File not readable: {0}";

    // ------------------------------------------------------------------ Findings: data folder, registry

    /// <summary>{0}: folder.</summary>
    public const string DataDirMissing = "{0}: folder is missing";

    /// <summary>{0}: folder or key, {1}: principal and access.</summary>
    public const string ForeignAccess = "{0}: {1}";

    /// <summary>{0}: registry key.</summary>
    public const string RegistryKeyMissing = "{0}: key is missing";

    // ------------------------------------------------------------------ Findings: shortcuts

    /// <summary>{0}: app name, {1}: shortcut file name.</summary>
    public const string ShortcutMissing = "{0}: no shortcut \"{1}\" to the launcher in the public Start menu or on the public desktop";

    /// <summary>{0}: shortcut path, {1}: target, {2}: arguments, {3}: AppId.</summary>
    public const string ShortcutWrongTarget = "{0}: points to \"{1} {2}\" instead of the launcher with --app {3}";

    /// <summary>{0}: shortcut path.</summary>
    public const string ShortcutUnreadable = "{0}: not a readable shortcut";

    /// <summary>{0}: shortcut path, {1}: exe path.</summary>
    public const string DirectShortcut = "{0}: points directly to {1}";

    // ------------------------------------------------------------------ Report

    public const string ReportTitle = "AppStart verification report";
    public const string ReportKicker = "VERIFICATION REPORT";
    public const string ReportHeading = "AppStart – Verification after a service visit";
    public const string BadgeNoDeviation = "No deviation";
    public const string BadgeOneDeviation = "1 deviation";

    /// <summary>{0}: number of deviations.</summary>
    public const string BadgeDeviations = "{0} deviations";

    public const string LabelComputer = "Computer";
    public const string LabelDate = "Date";
    public const string LabelTechnician = "Service technician";
    public const string LabelVersion = "Version";

    /// <summary>{0}: version.</summary>
    public const string VersionValue = "AppStart {0}";

    public const string ColumnCheck = "Check";
    public const string ColumnExpected = "Expected";
    public const string ColumnResult = "Result";
    public const string ResultPassed = "Passed";
    public const string ResultDeviation = "Deviation";
    public const string ExpectedPrefix = "Expected:";

    public const string FailedTitle = "Do not close the service visit";
    public const string FailedHint = "Correct and repeat the verification:";
    public const string VerifyCommand = "AppStart.Setup.exe verify";

    /// <summary>{0}: passed checks, {1}: all checks.</summary>
    public const string PassedSummary = "Verification passed – {0} of {1} checks passed";

    public const string SignatureTechnician = "Signature service technician";
    public const string SignatureCustomer = "Signature customer";
}
