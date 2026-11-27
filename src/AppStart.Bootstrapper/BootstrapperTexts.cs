namespace AppStart.Bootstrapper;

/// <summary>
/// All texts AppStart-Setup.exe shows (US English). The manual completeness test (task 10.4)
/// reads this class and checks that every message is described in the user manual.
/// Placeholders: {0}, {1} are filled by <see cref="BootstrapperLogic"/>.
/// </summary>
public static class BootstrapperTexts
{
    public const string Title = "AppStart Setup";

    /// <summary>Prefix of the measure line under the cause.</summary>
    public const string MeasureLabel = "Action: ";

    /// <summary>Footer with the msiexec log ("Log: C:\ProgramData\AppStart\Logs\install-….log").</summary>
    public const string LogFooter = "Log: {0}";

    // ---------------------------------------------------------------- progress
    public const string ProgressHeading = "Installing AppStart …";
    public const string ProgressText = "Please wait. The installation can take a few minutes. Afterwards the setup starts.";

    // ---------------------------------------------------------------- success / information
    public const string RestartHeading = "Restart required";
    public const string RestartText = "AppStart has been installed. A restart is required for all changes to take effect.";
    public const string RestartMeasure = "Complete the setup first and then restart the computer.";

    public const string AlreadyInstalledHeading = "AppStart is already installed";
    public const string AlreadyInstalledText = "Version {0} is installed, this setup contains version {1}. The installation is skipped.";
    public const string AlreadyInstalledMeasure = "None. The setup starts directly.";

    // ---------------------------------------------------------------- msiexec exit codes
    public const string CancelledHeading = "Installation canceled";
    public const string CancelledText = "The installation was canceled (code 1602). AppStart was not changed.";
    public const string CancelledMeasure = "Start AppStart-Setup.exe again to repeat the installation.";

    public const string BusyHeading = "Another installation is in progress";
    public const string BusyText = "Another installation is in progress. Please wait and try again. (Code 1618)";
    public const string BusyMeasure = "Wait until the other installation (for example Windows Update) has finished, then start AppStart-Setup.exe again.";

    public const string FailedHeading = "Installation failed";
    public const string FailedFatalText = "A fatal error occurred during the installation (code 1603). AppStart was not installed.";
    public const string FailedOtherText = "The installer ended with error {0}. AppStart was not installed.";
    public const string FailedMeasure = "Check the log. Contact service support with the log.";

    // ---------------------------------------------------------------- before msiexec
    public const string NoPackageHeading = "No installation package included";
    public const string NoPackageText = "This AppStart-Setup.exe contains no installation package (development build) and cannot install AppStart.";
    public const string NoPackageMeasure = "Use the AppStart-Setup.exe from the official release.";

    public const string NotAdminHeading = "Administrator rights required";
    public const string NotAdminText = "AppStart-Setup.exe is running without administrator rights and cannot install AppStart.";
    public const string NotAdminMeasure = "Start AppStart-Setup.exe with an administrator account and confirm the User Account Control prompt.";

    public const string UnsafeFolderHeading = "Folder not protected";
    public const string UnsafeFolderText = "The folder {0} can be changed by users without administrator rights or is a reparse point. For security reasons nothing is installed.";
    public const string UnsafeFolderMeasure = "Have an administrator check or delete the folder, then start AppStart-Setup.exe again.";

    public const string ExtractFailedHeading = "Installation package could not be extracted";
    public const string ExtractFailedText = "The installation package could not be written to {0}: {1}";
    public const string ExtractFailedMeasure = "Check the free disk space on drive C: and start AppStart-Setup.exe again.";

    public const string MsiexecFailedHeading = "Installer cannot be started";
    public const string MsiexecFailedText = "Windows Installer (msiexec.exe) could not be started: {0}";
    public const string MsiexecFailedMeasure = "Restart the computer, then start AppStart-Setup.exe again. If that does not help, contact service support.";

    // ---------------------------------------------------------------- starting the wizard
    public const string SetupMissingHeading = "Setup not found";
    public const string SetupMissingText = "AppStart is installed, but {0} is missing. The setup cannot start.";
    public const string SetupMissingMeasure = "Start AppStart-Setup.exe again. If that does not help, contact service support.";

    public const string SetupUntrustedHeading = "Setup not trustworthy";
    public const string SetupUntrustedText = "The signature of {0} is invalid or does not come from the publisher of this setup. For security reasons the setup is not started.";
    public const string SetupUntrustedMeasure = "Do not start the setup by hand. Contact service support.";

    public const string SetupStartFailedHeading = "Setup could not be started";
    public const string SetupStartFailedText = "AppStart is installed, but {0} could not be started: {1}";
    public const string SetupStartFailedMeasure = "Start the setup from the Start menu (AppStart – Setup).";

    // ---------------------------------------------------------------- command line
    public const string HelpHeading = "AppStart-Setup.exe";
    public const string HelpText =
        "AppStart-Setup.exe\n" +
        "    Installs AppStart and then starts the setup.\n\n" +
        "AppStart-Setup.exe /quiet\n" +
        "    Installs only, without user interface and without setup. The exit code is that of msiexec (0 or 3010 = success).\n\n" +
        "AppStart-Setup.exe /quiet /config <answer file>\n" +
        "    Installs and then sets up without user interface from the answer file (AppStart.Setup.exe apply). Exit code 0 or 3010 = success, 10 = setup with error or deviation, 11 = answer file invalid.\n\n" +
        "AppStart-Setup.exe /?\n" +
        "    Shows this help.";
    public const string HelpFooter = "Logs: C:\\ProgramData\\AppStart\\Logs";

    public const string InvalidArgumentHeading = "Unknown parameter";
    public const string InvalidArgumentText = "The parameter \"{0}\" is unknown.";
    public const string InvalidArgumentMeasure = "Start AppStart-Setup.exe without parameters, with /quiet, with /quiet /config <answer file> (absolute path) or with /? for help.";

    public const string UnexpectedHeading = "Unexpected error";
    public const string UnexpectedText = "An unexpected error occurred during the installation: {0}";
    public const string UnexpectedMeasure = "Contact service support with the log.";
}
