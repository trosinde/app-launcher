using System.IO;
using System.Reflection;
using System.Security.Principal;
using AppStart.Core;
using AppStart.Core.Configuration;

namespace AppStart.Setup.Engine.Verification;

/// <summary>
/// verify (spec verification, concept table "Prüfbefehl"): eight check points, each with concrete German
/// findings. All system access goes through <see cref="ISystemProbe"/> and the area services, so every check
/// runs against fakes in tests. Nothing is changed.
/// </summary>
public sealed class Verifier : IVerifier
{
    public const string ServiceCheckId = "dienst";
    public const string ProgramFolderCheckId = "programmordner";
    public const string AccountsCheckId = "konten";
    public const string AdminPasswordCheckId = "adminpasswort";
    public const string PipeCheckId = "pipe";
    public const string AppsJsonCheckId = "appsjson";
    public const string DataDirCheckId = "datenordner";
    public const string RegistryCheckId = "registry";
    public const string ShortcutsCheckId = "verknuepfungen";
    public const string HelpersCheckId = "hilfsprogramme";

    public const string DenyInteractiveLogonRight = "SeDenyInteractiveLogonRight";
    public const string DenyRemoteInteractiveLogonRight = "SeDenyRemoteInteractiveLogonRight";

    private readonly IAccountService _accounts;
    private readonly IProtectionService _protection;
    private readonly IShortcutService _shortcuts;
    private readonly ISystemProbe _probe;
    private readonly string _installationFolder;
    private readonly string _launcherPath;
    private readonly string _configPath;
    private readonly string _version;
    private readonly TimeProvider _time;

    /// <param name="installationFolder">Program folder, normally <see cref="DefaultInstallationFolder"/>.</param>
    /// <param name="launcherPath">AppStart.Launcher.exe the shortcuts must point to.</param>
    /// <param name="configPath">apps.json, normally <see cref="AppStartConstants.ConfigPath"/>.</param>
    /// <param name="version">Shown in the report; default: informational version of the Setup assembly.</param>
    /// <param name="time">Clock for the report time stamp; default: system clock.</param>
    public Verifier(
        IAccountService accounts,
        IProtectionService protection,
        IShortcutService shortcuts,
        ISystemProbe probe,
        string installationFolder,
        string launcherPath,
        string configPath,
        string? version = null,
        TimeProvider? time = null)
    {
        _accounts = accounts;
        _protection = protection;
        _shortcuts = shortcuts;
        _probe = probe;
        _installationFolder = installationFolder;
        _launcherPath = launcherPath;
        _configPath = configPath;
        _version = version ?? DefaultVersion();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>C:\Program Files\AppStart</summary>
    public static string DefaultInstallationFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AppStart");

    public VerifyReport Run()
    {
        var strictConfig = Guarded(() => _probe.LoadConfig(_configPath));
        var apps = strictConfig.IsValid
            ? strictConfig.Config!.Apps
            : Guarded(() => _probe.ParseConfigWithoutAclCheck(_configPath)).Config?.Apps;

        AccountFacts? appAccount = null;
        try
        {
            appAccount = _accounts.GetFacts(AppStartConstants.AppAccountName);
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            // reported by the accounts check
        }

        var checks = new List<CheckResult>
        {
            Check(ServiceCheckId, VerificationTexts.ServiceTitle, VerificationTexts.ServiceExpected, CheckService),
            Check(ProgramFolderCheckId, VerificationTexts.ProgramFolderTitle, VerificationTexts.ProgramFolderExpected, CheckProgramFolder),
            Check(AccountsCheckId, VerificationTexts.AccountsTitle, VerificationTexts.AccountsExpected, CheckAccounts),
            Check(AdminPasswordCheckId, VerificationTexts.AdminPasswordTitle, VerificationTexts.AdminPasswordExpected, CheckAdminPasswords),
            Check(PipeCheckId, VerificationTexts.PipeTitle, VerificationTexts.PipeExpected, CheckPipe),
            Check(AppsJsonCheckId, VerificationTexts.AppsJsonTitle, VerificationTexts.AppsJsonExpected, () => CheckAppsJson(strictConfig)),
            Check(DataDirCheckId, VerificationTexts.DataDirTitle, VerificationTexts.DataDirExpected, () => CheckDataDirs(apps, appAccount)),
            CheckWithNotes(HelpersCheckId, VerificationTexts.HelpersTitle, VerificationTexts.HelpersExpected, () => CheckHelpers(apps)),
            Check(RegistryCheckId, VerificationTexts.RegistryTitle, VerificationTexts.RegistryExpected, () => CheckRegistryKeys(apps, appAccount)),
            Check(ShortcutsCheckId, VerificationTexts.ShortcutsTitle, VerificationTexts.ShortcutsExpected, () => CheckShortcuts(apps)),
        };

        return new VerifyReport(_time.GetLocalNow(), _probe.MachineName, _probe.CurrentUser, _version, checks);
    }

    public string WriteReport(VerifyReport report, string directory) => VerifyReportWriter.Write(report, directory);

    // ------------------------------------------------------------------ Checks

    private List<string> CheckService()
    {
        var name = AppStartConstants.ServiceName;
        var service = _probe.GetService(name);
        if (!service.Exists)
        {
            return [Format(VerificationTexts.ServiceMissing, name)];
        }

        var findings = new List<string>();
        if (service.State != ServiceFacts.StateRunning)
        {
            findings.Add(Format(VerificationTexts.ServiceNotRunning, name, DescribeState(service.State)));
        }

        if (service.StartType != ServiceFacts.StartTypeAutomatic)
        {
            findings.Add(Format(VerificationTexts.ServiceNotAutomatic, name, DescribeStartType(service.StartType)));
        }

        if (!IsLocalSystem(service.Account))
        {
            findings.Add(Format(VerificationTexts.ServiceWrongAccount, name, service.Account ?? string.Empty));
        }

        return findings;
    }

    private List<string> CheckProgramFolder()
    {
        if (!_probe.DirectoryExists(_installationFolder))
        {
            return [Format(VerificationTexts.ProgramFolderMissing, _installationFolder)];
        }

        return _probe.FindWriteViolations(_installationFolder)
            .Select(v => Format(VerificationTexts.ProgramFolderViolation, _installationFolder, v))
            .ToList();
    }

    private List<string> CheckAccounts()
    {
        var findings = new List<string>();
        var name = AppStartConstants.AppAccountName;
        var app = _accounts.GetFacts(name);
        if (!app.Exists)
        {
            findings.Add(Format(VerificationTexts.AppAccountMissing, name));
        }
        else
        {
            if (!app.IsLocalAdministrator)
            {
                findings.Add(Format(VerificationTexts.AppAccountNotAdmin, name));
            }

            foreach (var (right, description) in new[]
                     {
                         (DenyInteractiveLogonRight, VerificationTexts.DenyInteractiveLogonDescription),
                         (DenyRemoteInteractiveLogonRight, VerificationTexts.DenyRemoteInteractiveLogonDescription),
                     })
            {
                if (!app.AccountRights.Contains(right, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(Format(VerificationTexts.AppAccountRightMissing, name, right, description));
                }
            }
        }

        IReadOnlyList<string> operators;
        try
        {
            operators = _accounts.GetOperators();
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            findings.Add(Format(VerificationTexts.OperatorGroupUnreadable, AppStartConstants.OperatorGroupName, ex.Message));
            return findings;
        }

        foreach (var op in operators)
        {
            if (_accounts.GetFacts(op).IsLocalAdministrator)
            {
                findings.Add(Format(VerificationTexts.OperatorIsAdmin, op));
            }
        }

        return findings;
    }

    // spec fix-wizard-findings, verification: Administratoren mit Passwort
    private List<string> CheckAdminPasswords() =>
        _accounts.FindAdministratorsWithoutPassword()
            .Select(f => f.Win32Error is { } error
                ? Format(VerificationTexts.AdminPasswordUncheckable, f.Account, error)
                : Format(VerificationTexts.AdminWithoutPassword, f.Account))
            .ToList();

    private List<string> CheckPipe()
    {
        var path = @"\\.\pipe\" + AppStartConstants.PipeName;
        var group = _probe.TryResolveSid(AppStartConstants.OperatorGroupName);
        if (group is null)
        {
            return [Format(VerificationTexts.PipeGroupMissing, AppStartConstants.OperatorGroupName)];
        }

        var pipe = _probe.GetPipe(AppStartConstants.PipeName);
        if (!pipe.Exists)
        {
            return [Format(VerificationTexts.PipeMissing, path)];
        }

        if (pipe.Descriptor is null)
        {
            return [Format(VerificationTexts.PipeUnreadable, path, pipe.Error ?? string.Empty)];
        }

        return PipeRules.Evaluate(pipe.Descriptor, group, AppStartConstants.OperatorGroupName, DescribeSid).ToList();
    }

    private List<string> CheckAppsJson(ConfigLoadResult config)
    {
        if (!config.IsValid)
        {
            return [config.Error ?? VerificationTexts.ConfigUnavailable];
        }

        var findings = new List<string>();
        foreach (var app in config.Config!.Apps)
        {
            var result = _probe.VerifyExecutable(app);
            if (!result.Success)
            {
                findings.Add(Format(VerificationTexts.ExeFailed, app.Name, app.Exe, result.Detail));
            }
        }

        return findings;
    }

    private List<string> CheckDataDirs(IReadOnlyList<AppEntry>? apps, AccountFacts? appAccount)
    {
        if (apps is null)
        {
            return [VerificationTexts.ConfigUnavailable];
        }

        var withDataDir = apps.Where(a => a.DataDir is not null).ToList();
        if (withDataDir.Count == 0)
        {
            return [];
        }

        if (appAccount?.Sid is not { } sid)
        {
            return [VerificationTexts.AppAccountUnavailable];
        }

        var findings = new List<string>();
        foreach (var app in withDataDir)
        {
            var dir = app.DataDir!;
            if (!_probe.DirectoryExists(dir))
            {
                findings.Add(Format(VerificationTexts.DataDirMissing, dir));
                continue;
            }

            findings.AddRange(_protection.FindForeignAccess(AclTarget.Directory, dir, sid)
                .Select(f => Format(VerificationTexts.ForeignAccess, dir, f)));
        }

        return findings;
    }

    private List<string> CheckRegistryKeys(IReadOnlyList<AppEntry>? apps, AccountFacts? appAccount)
    {
        if (apps is null)
        {
            return [VerificationTexts.ConfigUnavailable];
        }

        var withKey = apps.Where(a => a.RegistryKey is not null).ToList();
        if (withKey.Count == 0)
        {
            return [];
        }

        if (appAccount?.Sid is not { } sid)
        {
            return [VerificationTexts.AppAccountUnavailable];
        }

        var findings = new List<string>();
        foreach (var app in withKey)
        {
            var key = app.RegistryKey!;
            if (!_probe.RegistryKeyExists(key))
            {
                findings.Add(Format(VerificationTexts.RegistryKeyMissing, key));
                continue;
            }

            findings.AddRange(_protection.FindForeignAccess(AclTarget.RegistryKey, key, sid)
                .Select(f => Format(VerificationTexts.ForeignAccess, key, f)));
        }

        return findings;
    }

    private List<string> CheckShortcuts(IReadOnlyList<AppEntry>? apps)
    {
        if (apps is null)
        {
            return [VerificationTexts.ConfigUnavailable];
        }

        var findings = new List<string>();
        foreach (var app in apps)
        {
            var fileName = Shortcuts.ShortcutNames.ToFileName(app.Name); // same rule as ShortcutService
            var good = false;
            foreach (var folder in new[] { _probe.CommonStartMenuPrograms, _probe.CommonDesktop })
            {
                var path = Path.Combine(folder, fileName);
                if (!_probe.FileExists(path))
                {
                    continue;
                }

                var info = _shortcuts.Read(path);
                if (info is null)
                {
                    findings.Add(Format(VerificationTexts.ShortcutUnreadable, path));
                }
                else if (PointsToLauncher(info, app.Id))
                {
                    good = true;
                }
                else
                {
                    findings.Add(Format(VerificationTexts.ShortcutWrongTarget, path, info.TargetPath, info.Arguments, app.Id));
                }
            }

            if (!good)
            {
                findings.Add(Format(VerificationTexts.ShortcutMissing, app.Name, fileName));
            }

            findings.AddRange(_shortcuts.FindDirectShortcuts(app.Exe)
                .Select(s => Format(VerificationTexts.DirectShortcut, s, app.Exe)));
        }

        return findings;
    }

    private bool PointsToLauncher(ShortcutInfo info, string appId)
    {
        if (!SamePath(info.TargetPath, _launcherPath))
        {
            return false;
        }

        var tokens = info.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 2
               && string.Equals(tokens[0], "--app", StringComparison.Ordinal)
               && string.Equals(tokens[1], appId, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Helpers

    // add-allowed-child-processes, spec verification «Prüfung der Hilfsprogramme».
    private (List<string> Deviations, List<string> Notes) CheckHelpers(IReadOnlyList<AppEntry>? apps)
    {
        if (apps is null)
        {
            return ([VerificationTexts.ConfigUnavailable], []);
        }

        var deviations = new List<string>();
        var notes = new List<string>();
        foreach (var app in apps)
        {
            if (app.ChildProcessMode == ChildProcessMode.All)
            {
                notes.Add(Format(VerificationTexts.HelpersAllAllowed, app.Id));
                continue;
            }

            foreach (var child in app.ChildProcessMode == ChildProcessMode.Allowlist ? app.AllowedChildren ?? [] : [])
            {
                if (!_probe.FileExists(child.Exe))
                {
                    deviations.Add(Format(VerificationTexts.HelperMissing, app.Id, child.Exe));
                    continue;
                }

                var check = _probe.VerifyChild(child);
                if (!check.Success)
                {
                    deviations.Add(Format(VerificationTexts.HelperCheckFailed, app.Id, child.Exe, check.Detail));
                }

                var folder = Path.GetDirectoryName(child.Exe)!;
                if (_probe.FindWriteViolations(folder).Count > 0 || _probe.FindWriteViolations(child.Exe).Count > 0)
                {
                    deviations.Add(Format(VerificationTexts.HelperWritable, app.Id, child.Exe));
                }
            }
        }

        var learnPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "learn.json");
        var learn = _probe.LoadLearnState(learnPath);
        if (!learn.IsValid)
        {
            deviations.Add(Format(VerificationTexts.LearnStateInvalid, learn.Error!));
        }

        var now = _time.GetUtcNow();
        foreach (var session in learn.State.Apps.Where(s => s.IsActive(now)))
        {
            deviations.Add(Format(VerificationTexts.LearnActive, session.AppId,
                session.ExpiresUtc.ToOffset(_time.GetLocalNow().Offset).ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return (deviations, notes);
    }

    /// <summary>Like <see cref="Check"/>, but notes are shown without making the check a deviation.</summary>
    private static CheckResult CheckWithNotes(string id, string title, string expected, Func<(List<string> Deviations, List<string> Notes)> run)
    {
        try
        {
            var (deviations, notes) = run();
            return new CheckResult(id, title, expected, deviations.Count == 0 ? CheckOutcome.Passed : CheckOutcome.Deviation, [.. deviations, .. notes]);
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            return new CheckResult(id, title, expected, CheckOutcome.Deviation, [Format(VerificationTexts.CheckFailed, ex.Message)]);
        }
    }

    private static CheckResult Check(string id, string title, string expected, Func<List<string>> run)
    {
        List<string> findings;
        try
        {
            findings = run();
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            findings = [Format(VerificationTexts.CheckFailed, ex.Message)];
        }

        return new CheckResult(id, title, expected, findings.Count == 0 ? CheckOutcome.Passed : CheckOutcome.Deviation, findings);
    }

    private static ConfigLoadResult Guarded(Func<ConfigLoadResult> load)
    {
        try
        {
            return load();
        }
        catch (Exception ex) when (IsCheckError(ex))
        {
            return ConfigLoadResult.Fail(Format(VerificationTexts.CheckFailed, ex.Message));
        }
    }

    // A failing probe must become a deviation, never abort verify (and never pass).
    private static bool IsCheckError(Exception ex) => ex is not (OutOfMemoryException or StackOverflowException);

    private string DescribeSid(SecurityIdentifier sid) =>
        _probe.TryTranslateSid(sid) is { } name ? $"{name} ({sid.Value})" : sid.Value;

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsLocalSystem(string? account) =>
        account is not null
        && (account.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)
            || account.Equals(@".\LocalSystem", StringComparison.OrdinalIgnoreCase)
            || account.Equals(@"NT AUTHORITY\SYSTEM", StringComparison.OrdinalIgnoreCase));

    private static string DescribeState(int state) => state switch
    {
        1 => VerificationTexts.ServiceStateStopped,
        2 => VerificationTexts.ServiceStateStartPending,
        3 => VerificationTexts.ServiceStateStopPending,
        5 => VerificationTexts.ServiceStateContinuePending,
        6 => VerificationTexts.ServiceStatePausePending,
        7 => VerificationTexts.ServiceStatePaused,
        _ => Format(VerificationTexts.ServiceStateUnknown, state),
    };

    private static string DescribeStartType(int startType) => startType switch
    {
        0 => VerificationTexts.StartTypeBoot,
        1 => VerificationTexts.StartTypeSystem,
        3 => VerificationTexts.StartTypeManual,
        4 => VerificationTexts.StartTypeDisabled,
        _ => Format(VerificationTexts.StartTypeUnknown, startType),
    };

    private static string Format(string format, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);

    private static string DefaultVersion()
    {
        var version = typeof(Verifier).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? typeof(Verifier).Assembly.GetName().Version?.ToString()
                      ?? string.Empty;
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? version[..plus] : version;
    }
}
