namespace AppStart.Setup.Engine;

/// <summary>Result and progress texts of the setup engine (wizard step 3 and command line). Checked by task 10.4.</summary>
public static class SetupEngineTexts
{
    public const string ProgressAccounts = "Setting up accounts and group …";
    public const string ProgressConfig = "Writing configuration …";
    public const string ProgressProtection = "Locking data folders and registry keys …";
    public const string ProgressShortcuts = "Creating shortcuts …";
    public const string ProgressService = "Verifying service …";

    public const string AccountsWithOperator = "Accounts appstart-app and appstart-user, group AppStart Users created";
    public const string AccountsOnly = "Account appstart-app and group AppStart Users created";
    public const string AccountsFailed = "Accounts and group could not be set up";

    public const string AppsReleased = "{0} applications approved, configuration protected";
    public const string AppReleasedOne = "1 application approved, configuration protected";
    public const string ConfigFailed = "Configuration could not be written";
    public const string ConfigUnreadable = "apps.json is invalid: {0}";
    public const string UnknownApp = "Application \"{0}\" is not approved";

    public const string DataDirInProgramFolder = "Warning: Data folder {1} of {0} lies in the program folder of the application or contains it. appstart-app can then change the EXE and DLLs; the hash and signature check does not detect replaced DLLs. Recommended: a folder under C:\\ProgramData, e.g. C:\\ProgramData\\<Vendor>\\<App>";
    public const string DataProtected = "Data folders and registry keys locked";
    public const string DataProtectionFailed = "Data folders or registry keys could not be locked";

    public const string ShortcutsCreated = "Shortcuts with app icon created, {0} old shortcuts backed up";
    public const string ShortcutsFailed = "Shortcuts could not be created";

    public const string ServiceRunning = "Service AppStart is running, starts automatically";
    public const string ServiceFailed = "Service AppStart is not running";
    public const string PipeMissing = "The pipe of the service is not reachable. Check the Event Log (source AppStart).";

    // add-allowed-child-processes: helper programs (design D4)
    public const string ModeSet = "Other programs for \"{0}\": {1}";
    public const string ModeBlocked = "blocked";
    public const string ModeAllowlist = "allowed helper programs only";
    public const string ModeAll = "all allowed (unsafe)";
    public const string AllowlistNeedsEntries = "\"allowed helper programs only\" needs at least one helper program. Run allow-child first.";
    public const string ChildAllowed = "Helper program {0} allowed for \"{1}\" ({2})";
    public const string ChildDenied = "Helper program {0} removed for \"{1}\"";
    public const string ChildUnknown = "Helper program {0} is not in the list of \"{1}\"";
    public const string LastChildRemoved = "Last helper program removed: other programs for \"{0}\" blocked again";
    public const string ChildNotExe = "\"{0}\" is not a path to an EXE.";
    public const string ChildMissing = "{0} does not exist.";
    public const string ChildIsShell = "{0} is a shell or a script host. Allowed only with explicit confirmation (--allow-shell).";
    public const string ChildNotSigned = "{0} is not validly signed. Check \"signature\" not possible, use \"sha256\".";
    public const string ChildFolderWritable = "Helper program not allowed: {0} lies in a folder that operators can write to";
    public const string ChildrenRehashed = "Checksums of the helper programs of \"{0}\" recorded again ({1})";

    public const string IdInvalid = "AppId \"{0}\" is invalid. Allowed are a-z, 0-9 and -, 1 to {1} characters.";
    public const string IdFixed = "The application is already approved as \"{0}\"; the AppId cannot be changed (remove, then add).";
    public const string IdTaken = "AppId \"{0}\" is already taken.";

    public const string AnswerFileMissing = "Answer file {0} not found.";

    // Learn mode (design D3)
    public const string LearnStarted = "Learning mode for \"{0}\" started, ends automatically at {1}";
    public const string LearnStopped = "Learning mode for \"{0}\" ended";
    public const string LearnNotActive = "Learning mode for \"{0}\" was not active";
    public const string LearnMinutesInvalid = "Learning mode duration: {0} to {1} minutes.";
    public const string LearnNothingRecorded = "Nothing was recorded for \"{0}\". Run learn start first and use the application.";
    public const string LearnNotRecorded = "{0} is not in the recording.";
    public const string LearnShellNotSelectable = "{0} is a shell or a script host and is never applied. If needed, by hand: allow-child --allow-shell.";
    public const string LearnNothingToApply = "There are no suggestions to apply for \"{0}\" (only shells or script hosts recorded).";
    public const string LearnApplied = "{0} helper program(s) from learning mode applied for \"{1}\", recording deleted";

    public const string UninstallShortcuts = "Shortcuts restored, data folders and registry keys reset";
    public const string UninstallAccounts = "Accounts and group removed";
    public const string UninstallConfig = "Configuration removed (verification reports are kept)";
}
