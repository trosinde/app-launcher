namespace AppStart.Setup.Cli;

/// <summary>Texts of the command line (reference chapter of the manual, task 10.4).</summary>
public static class CliTexts
{
    public const string RootDescription = "AppStart Setup: approve applications, set up accounts, verify the setup. Without a command the wizard opens.";

    public const string AddDescription = "Approves an application or updates its approval.";
    public const string ExeOption = "Path to the EXE of the application.";
    public const string DataDirOption = "Data folder (exchange files), locked for operators.";
    public const string RegKeyOption = "Registry key with passwords under HKLM\\, locked for operators.";
    public const string NameOption = "Display name (name of the shortcut).";

    public const string AddUserDescription = "Creates the operator account appstart-user or adds a user to the group AppStart Users.";
    public const string UserNameOption = "Only appstart-user: creates the local operator account.";
    public const string MemberOption = "Local or domain user (DOMAIN\\User) for the group AppStart Users.";
    public const string PasswordStdinOption = "Read the password for appstart-user from one line of standard input (silent installation).";
    public const string AddUserUsage = "Specify exactly one option: --name appstart-user or --member \"DOMAIN\\User\".";
    public const string AddUserNameOnly = "With --name only the account appstart-user can be created.";
    public const string PasswordPrompt = "Password for appstart-user: ";
    public const string PasswordRepeat = "Repeat password: ";
    public const string PasswordMismatch = "Password empty or not identical.";

    public const string RemoveDescription = "Revokes the approval of an application and restores its shortcuts and permissions.";
    public const string RehashDescription = "Records the signature or checksum again after an update of the application.";
    public const string AppOption = "AppId of the application (see list).";

    public const string ListDescription = "Shows approved applications, operators and service status.";
    public const string NoApps = "No application approved.";
    public const string OperatorsLine = "Group AppStart Users: {0}";
    public const string ServiceLine = "Service AppStart: {0}";
    public const string Running = "running";
    public const string NotRunning = "not running";

    public const string VerifyDescription = "Verifies the setup and saves a verification report. Exit code 0 only without deviation.";
    public const string ReportOption = "Additional location for the verification report (HTML).";
    public const string UiOption = "Open the verification view (Start menu entry \"AppStart – Verify setup\").";
    public const string VerifyPassed = "Verification passed – {0} of {0} checks passed.";
    public const string VerifyFailed = "Verification failed – {0} deviation(s). Do not close the service visit.";
    public const string ReportSaved = "Verification report: {0}";

    public const string AutoVerify = "Automatic verification after the change:";

    // add-allowed-child-processes, design D4
    public const string IdOption = "Set the AppId (a-z, 0-9, -; otherwise from the file name). Only for a new approval.";
    public const string ChildProcessesOption = "Other programs: blocked, allowlist (allowed helper programs only) or all (all allowed).";
    public const string SetChildrenDescription = "Sets which other programs an application may start.";
    public const string ModeOption = "blocked, allowlist or all. allowlist needs at least one helper program.";
    public const string AllowChildDescription = "Allows a helper program for an application (mode becomes \"allowed helper programs only\").";
    public const string DenyChildDescription = "Removes a helper program from the list (when the last one is removed, the mode becomes \"blocked\" again).";
    public const string ChildExeOption = "Path to the EXE of the helper program.";
    public const string CheckOption = "Check signature or sha256 (if omitted: signature if validly signed, otherwise SHA-256).";
    public const string AllowShellOption = "Confirms that a shell or a script host is to be allowed (e.g. cmd.exe, powershell.exe).";
    public const string RehashChildOption = "Record the checksum of this helper program only.";
    public const string ChildModeLine = "Other programs: {0}";
    public const string CheckSignature = "Signature";
    public const string CheckSha256 = "SHA-256";
    public const string ShellMark = " – shell or script host";

    public const string LearnDescription = "Learning mode: records which programs an application starts and applies them as helper programs.";
    public const string LearnStartDescription = "Starts learning mode for an application.";
    public const string MinutesOption = "Duration in minutes (5 to 120, default 30).";
    public const string LearnStopDescription = "Ends learning mode immediately.";
    public const string LearnShowDescription = "Shows what learning mode has recorded.";
    public const string LearnApplyDescription = "Applies recorded programs as helper programs and deletes the recording.";
    public const string LearnAllOption = "All recorded programs except shells and script hosts.";
    public const string LearnExeOption = "Only these recorded programs (can be repeated).";
    public const string LearnApplyUsage = "Specify exactly one option: --all or --exe <path> (can be repeated).";
    public const string LearnInactive = "Learning mode for \"{0}\": not active.";
    public const string LearnActiveUntil = "Learning mode for \"{0}\": active until {1}.";
    public const string LearnEmpty = "Nothing recorded.";
    public const string LearnProgramLine = "    {0} start(s), check {1}, last {2}";

    public const string ApplyDescription = "Sets up AppStart from an answer file (silent installation). Applying it twice changes nothing.";
    public const string ConfigOption = "Answer file (JSON) with applications, protection, other programs and operators.";
    public const string ReplaceOption = "Applications that are not in the answer file are removed.";

    public const string UninstallDescription = "Restores everything the setup changed (run before uninstalling the MSI).";
}
