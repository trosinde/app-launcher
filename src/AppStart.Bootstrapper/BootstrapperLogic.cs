using System.Globalization;

namespace AppStart.Bootstrapper;

public enum MessageIcon
{
    Information,
    Warning,
    Error,
}

/// <summary>What a dialog shows: heading, cause, measure and optionally the log path (footer).</summary>
public sealed record BootstrapperMessage(MessageIcon Icon, string Heading, string Text, string? Measure = null, string? LogPath = null)
{
    /// <summary>Content of the dialog: cause, blank line, «Massnahme: …».</summary>
    public string Content => Measure is null ? Text : $"{Text}\n\n{BootstrapperTexts.MeasureLabel}{Measure}";

    public string? Footer => LogPath is null ? null : string.Format(CultureInfo.InvariantCulture, BootstrapperTexts.LogFooter, LogPath);
}

public enum RunMode
{
    /// <summary>No arguments: install with progress dialog, then start the wizard.</summary>
    Interactive,

    /// <summary>/quiet: install only, no UI, no wizard; exit code = msiexec exit code.</summary>
    Quiet,

    /// <summary>/?: short help as TaskDialog.</summary>
    Help,

    /// <summary>Unknown argument; <see cref="ParsedArguments.Invalid"/> holds it.</summary>
    Invalid,
}

/// <summary>
/// Parsed command line. <see cref="ShowUi"/> is false whenever /quiet was given, even next to an invalid argument.
/// <see cref="ConfigPath"/>: answer file of /config (only with /quiet, add-allowed-child-processes D6).
/// </summary>
public sealed record ParsedArguments(RunMode Mode, bool ShowUi, string? Invalid = null, string? ConfigPath = null);

/// <summary>Result of the msiexec run, mapped to what the bootstrapper does next.</summary>
public sealed record InstallOutcome(bool Success, bool RestartRequired, BootstrapperMessage? Message, int ExitCode);

/// <summary>Pure bootstrapper logic, unit tested; Program.cs only wires it to Windows.</summary>
public static class BootstrapperLogic
{
    public const int ExitOk = 0;
    public const int ExitMsiSuccessRestart = 3010;
    public const int ExitMsiCancelled = 1602;
    public const int ExitMsiBusy = 1618;
    public const int ExitMsiFatal = 1603;

    /// <summary>ERROR_FILE_NOT_FOUND: AppStart.Setup.exe missing after the installation.</summary>
    public const int ExitSetupMissing = 2;

    /// <summary>ERROR_ACCESS_DENIED: not elevated.</summary>
    public const int ExitNotAdmin = 5;

    /// <summary>ERROR_INVALID_PARAMETER: unknown command line argument.</summary>
    public const int ExitInvalidArgument = 87;

    /// <summary>ERROR_INVALID_IMAGE_HASH: AppStart.Setup.exe signature invalid or foreign signer.</summary>
    public const int ExitSetupUntrusted = 577;

    /// <summary>ERROR_INSTALL_PACKAGE_INVALID: no MSI embedded (development build).</summary>
    public const int ExitNoPackage = 1620;

    /// <summary>Folder unsafe, extraction or msiexec start failed, unexpected error (same as ERROR_INSTALL_FAILURE).</summary>
    public const int ExitInternalError = 1603;

    /// <summary>Generic failure: AppStart is installed but the wizard could not be started.</summary>
    public const int ExitSetupStartFailed = 1;

    /// <summary>/config: installed, but apply ended with 1 (setup with failure or deviation). New, 1 and 2 are taken (design D6).</summary>
    public const int ExitApplyFailed = 10;

    /// <summary>/config: installed, but apply ended with 2 (answer file invalid, nothing set up).</summary>
    public const int ExitApplyInvalidFile = 11;

    public const string MsiResourceName = "AppStart.msi";
    public const string SetupExeName = "AppStart.Setup.exe";

    private static readonly string[] QuietSwitches = ["/quiet", "/q", "-quiet", "--quiet"];
    private static readonly string[] HelpSwitches = ["/?", "/h", "/help", "-?", "-h", "--help"];
    private static readonly string[] ConfigSwitches = ["/config", "-config", "--config"];

    public static ParsedArguments ParseArguments(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var quiet = args.Any(a => QuietSwitches.Contains(a, StringComparer.OrdinalIgnoreCase));

        // /config <path>: the path is the next argument (design D6).
        string? configPath = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (ConfigSwitches.Contains(args[i], StringComparer.OrdinalIgnoreCase))
            {
                if (configPath is not null || i + 1 >= args.Count || !IsValidConfigPath(args[i + 1]))
                {
                    return new(RunMode.Invalid, !quiet, i + 1 < args.Count ? $"{args[i]} {args[i + 1]}" : args[i]);
                }

                configPath = args[++i];
                continue;
            }

            rest.Add(args[i]);
        }

        if (configPath is not null && !quiet)
        {
            return new(RunMode.Invalid, true, "/config"); // only together with /quiet: no wizard, no dialogs
        }

        var invalid = rest.FirstOrDefault(a =>
            !QuietSwitches.Contains(a, StringComparer.OrdinalIgnoreCase) && !HelpSwitches.Contains(a, StringComparer.OrdinalIgnoreCase));

        if (invalid is not null)
        {
            return new(RunMode.Invalid, !quiet, invalid);
        }

        if (configPath is not null)
        {
            return rest.Any(a => HelpSwitches.Contains(a, StringComparer.OrdinalIgnoreCase))
                ? new(RunMode.Invalid, false, "/?")
                : new(RunMode.Quiet, false, null, configPath);
        }

        if (args.Any(a => HelpSwitches.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            // "/quiet /?" asks for help but forbids UI: treat as invalid, no dialog.
            return quiet ? new(RunMode.Invalid, false, "/?") : new(RunMode.Help, true);
        }

        return quiet ? new(RunMode.Quiet, false) : new(RunMode.Interactive, true);
    }

    /// <summary>Absolute drive path without quotes (the path is handed on to AppStart.Setup.exe, design D6).</summary>
    public static bool IsValidConfigPath(string? path) =>
        path is { Length: >= 4 }
        && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
        && !path.Contains('"', StringComparison.Ordinal)
        && path.IndexOfAny(['\0', '\r', '\n']) < 0;

    /// <summary>Arguments for AppStart.Setup.exe after the installation: apply --config "&lt;path&gt;".</summary>
    public static string ApplyArguments(string configPath) =>
        IsValidConfigPath(configPath) ? $"apply --config \"{configPath}\"" : throw new ArgumentException("Invalid answer file path.", nameof(configPath));

    /// <summary>Output of apply: apply-yyyyMMdd-HHmmss.log in C:\ProgramData\AppStart\Logs.</summary>
    public static string ApplyLogFileName(DateTime now) =>
        "apply-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";

    /// <summary>
    /// Exit code of /quiet /config after a successful msiexec: apply 0 → 0 (3010 when msiexec asked for a restart),
    /// apply 1 → 10, apply 2 → 11, anything else → 10 (design D6).
    /// </summary>
    public static int MapApplyExit(int applyExit, int msiExit) => applyExit switch
    {
        0 => msiExit == ExitMsiSuccessRestart ? ExitMsiSuccessRestart : ExitOk,
        2 => ExitApplyInvalidFile,
        _ => ExitApplyFailed,
    };

    /// <summary>msiexec log file: install-yyyyMMdd-HHmmss.log (local time).</summary>
    public static string InstallLogFileName(DateTime now) =>
        "install-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";

    /// <summary>Bootstrapper's own log next to the msiexec log: bootstrapper-yyyyMMdd-HHmmss.log.</summary>
    public static string BootstrapperLogFileName(DateTime now) =>
        "bootstrapper-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";

    /// <summary>Arguments for %SystemRoot%\System32\msiexec.exe: silent install, no reboot, verbose log.</summary>
    public static string MsiexecArguments(string msiPath, string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msiPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        if (msiPath.Contains('"', StringComparison.Ordinal) || logPath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("Paths must not contain quotes.");
        }

        return $"/i \"{msiPath}\" /qn /norestart /l*v \"{logPath}\"";
    }

    /// <summary>Maps the msiexec exit code to message, exit code and whether the wizard may start.</summary>
    public static InstallOutcome EvaluateMsiexec(int exitCode, string logPath) => exitCode switch
    {
        ExitOk => new(true, false, null, ExitOk),
        ExitMsiSuccessRestart => new(true, true,
            new(MessageIcon.Information, BootstrapperTexts.RestartHeading, BootstrapperTexts.RestartText, BootstrapperTexts.RestartMeasure), ExitMsiSuccessRestart),
        ExitMsiCancelled => new(false, false,
            new(MessageIcon.Warning, BootstrapperTexts.CancelledHeading, BootstrapperTexts.CancelledText, BootstrapperTexts.CancelledMeasure, logPath), exitCode),
        ExitMsiBusy => new(false, false,
            new(MessageIcon.Warning, BootstrapperTexts.BusyHeading, BootstrapperTexts.BusyText, BootstrapperTexts.BusyMeasure, logPath), exitCode),
        ExitMsiFatal => new(false, false,
            new(MessageIcon.Error, BootstrapperTexts.FailedHeading, BootstrapperTexts.FailedFatalText, BootstrapperTexts.FailedMeasure, logPath), exitCode),
        _ => new(false, false,
            new(MessageIcon.Error, BootstrapperTexts.FailedHeading, Format(BootstrapperTexts.FailedOtherText, exitCode), BootstrapperTexts.FailedMeasure, logPath),
            exitCode),
    };

    /// <summary>
    /// True when the installed version is the same as or newer than the package: skip msiexec.
    /// Unknown versions (null or not numeric) never skip.
    /// </summary>
    public static bool ShouldSkipInstall(string? installedVersion, string? packageVersion) =>
        TryParseMsiVersion(installedVersion, out var installed)
        && TryParseMsiVersion(packageVersion, out var package)
        && installed >= package;

    /// <summary>MSI ProductVersion: 2 to 4 numeric parts (major.minor[.build[.revision]]).</summary>
    public static bool TryParseMsiVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text) || !Version.TryParse(text.Trim(), out var parsed))
        {
            return false;
        }

        // Normalise missing parts to 0 so that 1.2 == 1.2.0 == 1.2.0.0.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    /// <summary>
    /// Signer check for AppStart.Setup.exe. Bootstrapper unsigned (development build): accepted without
    /// check (logged by the caller). Bootstrapper signed: Setup must be validly signed with the same thumbprint.
    /// </summary>
    public static SignatureDecision DecideSignature(string? ownThumbprint, string? setupThumbprint)
    {
        if (ownThumbprint is null)
        {
            return SignatureDecision.SkippedUnsignedBootstrapper;
        }

        return setupThumbprint is not null && string.Equals(ownThumbprint, setupThumbprint, StringComparison.OrdinalIgnoreCase)
            ? SignatureDecision.Trusted
            : SignatureDecision.Untrusted;
    }

    public static BootstrapperMessage NoPackage { get; } =
        new(MessageIcon.Error, BootstrapperTexts.NoPackageHeading, BootstrapperTexts.NoPackageText, BootstrapperTexts.NoPackageMeasure);

    public static BootstrapperMessage NotAdmin { get; } =
        new(MessageIcon.Error, BootstrapperTexts.NotAdminHeading, BootstrapperTexts.NotAdminText, BootstrapperTexts.NotAdminMeasure);

    public static BootstrapperMessage Help { get; } =
        new(MessageIcon.Information, BootstrapperTexts.HelpHeading, BootstrapperTexts.HelpText);

    public static BootstrapperMessage InvalidArgument(string argument) =>
        new(MessageIcon.Warning, BootstrapperTexts.InvalidArgumentHeading,
            Format(BootstrapperTexts.InvalidArgumentText, argument) + "\n\n" + BootstrapperTexts.HelpText, BootstrapperTexts.InvalidArgumentMeasure);

    public static BootstrapperMessage AlreadyInstalled(string installedVersion, string packageVersion) =>
        new(MessageIcon.Information, BootstrapperTexts.AlreadyInstalledHeading,
            Format(BootstrapperTexts.AlreadyInstalledText, installedVersion, packageVersion), BootstrapperTexts.AlreadyInstalledMeasure);

    public static BootstrapperMessage UnsafeFolder(string folder, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.UnsafeFolderHeading, Format(BootstrapperTexts.UnsafeFolderText, folder), BootstrapperTexts.UnsafeFolderMeasure, logPath);

    public static BootstrapperMessage ExtractFailed(string folder, string reason, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.ExtractFailedHeading, Format(BootstrapperTexts.ExtractFailedText, folder, reason), BootstrapperTexts.ExtractFailedMeasure, logPath);

    public static BootstrapperMessage MsiexecFailed(string reason, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.MsiexecFailedHeading, Format(BootstrapperTexts.MsiexecFailedText, reason), BootstrapperTexts.MsiexecFailedMeasure, logPath);

    public static BootstrapperMessage SetupMissing(string setupPath, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.SetupMissingHeading, Format(BootstrapperTexts.SetupMissingText, setupPath), BootstrapperTexts.SetupMissingMeasure, logPath);

    public static BootstrapperMessage SetupUntrusted(string setupPath, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.SetupUntrustedHeading, Format(BootstrapperTexts.SetupUntrustedText, setupPath), BootstrapperTexts.SetupUntrustedMeasure, logPath);

    public static BootstrapperMessage SetupStartFailed(string setupPath, string reason, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.SetupStartFailedHeading, Format(BootstrapperTexts.SetupStartFailedText, setupPath, reason), BootstrapperTexts.SetupStartFailedMeasure, logPath);

    public static BootstrapperMessage Unexpected(string reason, string? logPath) =>
        new(MessageIcon.Error, BootstrapperTexts.UnexpectedHeading, Format(BootstrapperTexts.UnexpectedText, reason), BootstrapperTexts.UnexpectedMeasure, logPath);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}

public enum SignatureDecision
{
    Trusted,
    Untrusted,

    /// <summary>Development build: the bootstrapper itself carries no valid signature, so there is nothing to pin to.</summary>
    SkippedUnsignedBootstrapper,
}
