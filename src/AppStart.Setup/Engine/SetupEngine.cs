using System.ServiceProcess;
using AppStart.Core;
using AppStart.Core.Configuration;

namespace AppStart.Setup.Engine;

/// <summary>Persistence of <see cref="SetupState"/> (implemented in Engine/Protection).</summary>
public interface ISetupStateStore
{
    SetupState Load();

    void Save(SetupState state);
}

/// <summary>Installation paths, injectable for tests.</summary>
public sealed record SetupPaths(string InstallDir, string ConfigPath, string ReportDir)
{
    /// <summary>Velopack layout: C:\Program Files\AppStart\current\ next to the running Setup EXE.</summary>
    public static SetupPaths Default { get; } = new(
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
        AppStartConstants.ConfigPath,
        AppStartConstants.ReportDirectory);

    public string LauncherPath => Path.Combine(InstallDir, "AppStart.Launcher.exe");
}

/// <summary>
/// Orchestration of section 6: accounts, configuration, protection, shortcuts and service, plus the
/// command line operations (add, remove, rehash, add-user, uninstall). Records every change in the
/// setup state so that uninstall and "app removed" can restore the original state.
/// </summary>
public sealed class SetupEngine(
    IAccountService accounts,
    IProtectionService protection,
    IShortcutService shortcuts,
    IVerifier verifier,
    ISetupStateStore stateStore,
    SetupPaths paths,
    Func<string, IReadOnlyList<string>>? writeViolationsOf = null) : ISetupEngine
{
    // Folder/file of a helper writable for operators (injectable: temp folders in tests are user-writable).
    private readonly Func<string, IReadOnlyList<string>> writeViolations = writeViolationsOf ?? ChildPrograms.WriteViolations;

    public SetupSnapshot LoadCurrent()
    {
        var config = ReadConfig();
        var app = accounts.GetFacts(AppStartConstants.AppAccountName);
        var op = accounts.GetFacts(AppStartConstants.OperatorAccountName);
        return new SetupSnapshot(
            config.Apps,
            app.Exists ? accounts.GetOperators() : [],
            app.Exists,
            op.Exists,
            ServiceRunning());
    }

    public AppCandidate Inspect(string exePath)
    {
        var config = ReadConfig();
        var existing = config.Apps.FirstOrDefault(a => string.Equals(a.Exe, Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase));
        return AppCandidates.Inspect(exePath, config.Apps.Select(a => a.Id), existing);
    }

    public IReadOnlyList<SetupStepResult> Apply(SetupRequest request, IProgress<string>? progress = null)
    {
        var results = new List<SetupStepResult>();
        try
        {
            // C:\ProgramData\AppStart must exist and be protected before the state can be written.
            protection.ProtectConfigDirectory();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            results.Add(new(SetupEngineTexts.ConfigFailed, false, ex.Message));
            return results;
        }

        var state = stateStore.Load();

        // 1. Accounts and group
        progress?.Report(SetupEngineTexts.ProgressAccounts);
        System.Security.Principal.SecurityIdentifier appSid;
        try
        {
            appSid = accounts.EnsureAppAccount();
            accounts.EnsureOperatorGroup();
            var created = request.CreateOperatorAccount && request.OperatorPassword is not null
                          && accounts.EnsureOperatorAccount(request.OperatorPassword);
            var added = state.AddedGroupMembers.ToList();
            foreach (var member in request.AdditionalOperators.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (accounts.AddOperator(member))
                {
                    added.Add(member);
                }
            }

            foreach (var former in state.AddedGroupMembers.Where(m => !request.AdditionalOperators.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                accounts.RemoveOperator(former);
                added.Remove(former);
            }

            state = state with { OperatorAccountCreatedBySetup = state.OperatorAccountCreatedBySetup || created, AddedGroupMembers = added };
            stateStore.Save(state);
            results.Add(new(request.CreateOperatorAccount ? SetupEngineTexts.AccountsWithOperator : SetupEngineTexts.AccountsOnly, true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            results.Add(new(SetupEngineTexts.AccountsFailed, false, ex.Message));
            return results; // nothing else makes sense without the app account
        }

        // 2. Configuration and protection
        progress?.Report(SetupEngineTexts.ProgressConfig);
        var previous = ReadConfig();
        var entries = request.Apps.Select(AppCandidates.ToEntry).ToList();
        var config = new AppsConfig { Version = AppsConfig.CurrentVersion, Apps = entries };
        var configError = AppsConfigSerializer.Validate(config);
        if (configError is not null)
        {
            results.Add(new(SetupEngineTexts.ConfigFailed, false, configError));
            return results;
        }

        try
        {
            foreach (var removed in previous.Apps.Where(p => entries.All(e => e.Id != p.Id)))
            {
                state = Unregister(removed.Id, state);
            }

            protection.WriteProtectedFile(paths.ConfigPath, AppsConfigSerializer.Serialize(config));
            stateStore.Save(state);
            results.Add(new(string.Format(SetupEngineTexts.AppsReleased, entries.Count), true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            results.Add(new(SetupEngineTexts.ConfigFailed, false, ex.Message));
            return results;
        }

        // 3. Data folders and registry keys
        progress?.Report(SetupEngineTexts.ProgressProtection);
        try
        {
            var backups = state.AclBackups.ToList();
            foreach (var app in entries)
            {
                if (app.DataDir is { } dir && !backups.Any(b => b.Target == AclTarget.Directory && SamePath(b.Path, dir)))
                {
                    backups.Add(protection.ProtectDataDirectory(dir, appSid, app.Id));
                }
                else if (app.DataDir is { } again)
                {
                    protection.ProtectDataDirectory(again, appSid, app.Id); // re-apply, keep the first backup
                }

                if (app.RegistryKey is { } key && !backups.Any(b => b.Target == AclTarget.RegistryKey && SamePath(b.Path, key)))
                {
                    backups.Add(protection.ProtectRegistryKey(key, appSid, app.Id));
                }
                else if (app.RegistryKey is { } keyAgain)
                {
                    protection.ProtectRegistryKey(keyAgain, appSid, app.Id);
                }
            }

            state = state with { AclBackups = backups };
            stateStore.Save(state);
            results.Add(new(SetupEngineTexts.DataProtected, true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stateStore.Save(state);
            results.Add(new(SetupEngineTexts.DataProtectionFailed, false, ex.Message));
        }

        // 4. Shortcuts
        progress?.Report(SetupEngineTexts.ProgressShortcuts);
        try
        {
            var created = state.CreatedShortcuts.ToList();
            var backedUp = state.ShortcutBackups.ToList();
            var moved = 0;
            foreach (var app in entries)
            {
                foreach (var direct in shortcuts.FindDirectShortcuts(app.Exe))
                {
                    backedUp.Add(shortcuts.BackupAndRemove(direct, app.Id));
                    moved++;
                }

                foreach (var path in shortcuts.CreateAppShortcuts(app, paths.LauncherPath))
                {
                    if (!created.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        created.Add(path);
                    }
                }
            }

            state = state with { CreatedShortcuts = created, ShortcutBackups = backedUp };
            stateStore.Save(state);
            results.Add(new(string.Format(SetupEngineTexts.ShortcutsCreated, moved), true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stateStore.Save(state);
            results.Add(new(SetupEngineTexts.ShortcutsFailed, false, ex.Message));
        }

        // 5. Service
        progress?.Report(SetupEngineTexts.ProgressService);
        results.Add(EnsureServiceRunning());
        return results;
    }

    public VerifyReport Verify() => verifier.Run();

    public string WriteReport(VerifyReport report)
    {
        // Reports may be the first thing written (automatic check right after the MSI installation), and
        // operators may create folders in C:\ProgramData: protect C:\ProgramData\AppStart before writing.
        protection.ProtectConfigDirectory();
        return verifier.WriteReport(report, paths.ReportDir);
    }

    // ------------------------------------------------------------------ command line operations

    /// <summary>add: releases one EXE (or updates it when already released) and applies everything.</summary>
    public IReadOnlyList<SetupStepResult> Add(string exePath, string? dataDir, string? registryKey, string? name)
    {
        var current = LoadCurrent();
        var candidate = Inspect(exePath);
        candidate = candidate with
        {
            Name = string.IsNullOrWhiteSpace(name) ? candidate.Name : name.Trim(),
            DataDir = dataDir ?? candidate.DataDir,
            RegistryKey = registryKey ?? candidate.RegistryKey,
        };

        var apps = current.Apps.Where(a => a.Id != candidate.Id).Select(ToCandidate).Append(candidate).ToList();
        return Apply(new SetupRequest(apps, false, null, stateStore.Load().AddedGroupMembers));
    }

    /// <summary>remove: withdraws the release of one app and restores its shortcuts and ACLs.</summary>
    public IReadOnlyList<SetupStepResult> Remove(string appId)
    {
        var current = LoadCurrent();
        if (current.Apps.All(a => a.Id != appId))
        {
            return [new(string.Format(SetupEngineTexts.UnknownApp, appId), false)];
        }

        var apps = current.Apps.Where(a => a.Id != appId).Select(ToCandidate).ToList();
        return Apply(new SetupRequest(apps, false, null, stateStore.Load().AddedGroupMembers));
    }

    /// <summary>rehash: after an update of the application, record its new signature or SHA-256.</summary>
    public IReadOnlyList<SetupStepResult> Rehash(string appId)
    {
        var current = LoadCurrent();
        var app = current.Apps.FirstOrDefault(a => a.Id == appId);
        if (app is null)
        {
            return [new(string.Format(SetupEngineTexts.UnknownApp, appId), false)];
        }

        // design D4: rehash also renews the SHA-256 entries of the helper programs.
        var fresh = AppCandidates.Inspect(app.Exe, [], app);
        var rehashed = fresh.AllowedChildren?.Count(c => c.Check == IntegrityCheck.Sha256) ?? 0;
        fresh = fresh with { AllowedChildren = fresh.AllowedChildren?.Select(ChildPrograms.Rehash).ToList() };
        var apps = current.Apps.Select(a => a.Id == appId ? fresh : ToCandidate(a)).ToList();
        IReadOnlyList<SetupStepResult> notes = rehashed > 0 ? [new(string.Format(SetupEngineTexts.ChildrenRehashed, appId, rehashed), true)] : [];
        return [.. notes, .. Apply(new SetupRequest(apps, false, null, stateStore.Load().AddedGroupMembers))];
    }

    // ------------------------------------------------------------------ helper programs (add-allowed-child-processes D4)

    /// <summary>set-children: mode of one app. allowlist without entries is invalid input.</summary>
    public IReadOnlyList<SetupStepResult> SetChildMode(string appId, ChildProcessMode mode) =>
        ChangeApp(appId, app =>
        {
            if (mode == ChildProcessMode.Allowlist && app.AllowedChildren is not { Count: > 0 })
            {
                throw new SetupInputException(SetupEngineTexts.AllowlistNeedsEntries);
            }

            return (app with
            {
                ChildProcesses = mode,
                AllowedChildren = mode == ChildProcessMode.Allowlist ? app.AllowedChildren : null,
            }, [new(string.Format(SetupEngineTexts.ModeSet, appId, ModeText(mode)), true)]);
        });

    /// <summary>
    /// allow-child: adds (or replaces) a helper and sets the mode to allowlist. A helper in a folder operators can
    /// write is refused (exit code 1); shells need <paramref name="allowShell"/> (else invalid input, exit code 2).
    /// </summary>
    public IReadOnlyList<SetupStepResult> AllowChild(string appId, string exePath, IntegrityCheck? check, bool allowShell)
    {
        if (LoadCurrent().Apps.All(a => a.Id != appId))
        {
            return [new(string.Format(SetupEngineTexts.UnknownApp, appId), false)];
        }

        var entry = ChildPrograms.Inspect(exePath, check, allowShell);
        var violations = writeViolations(entry.Exe);
        if (violations.Count > 0)
        {
            return [new(string.Format(SetupEngineTexts.ChildFolderWritable, entry.Exe), false, string.Join(" ", violations))];
        }

        return ChangeApp(appId, app =>
        {
            var children = (app.AllowedChildren ?? []).Where(c => !ChildPrograms.SamePath(c.Exe, entry.Exe)).Append(entry).ToList();
            var checkText = entry.Check == IntegrityCheck.Signature ? "Signatur" : "SHA-256";
            return (app with { ChildProcesses = ChildProcessMode.Allowlist, AllowedChildren = children },
            [
                new(string.Format(SetupEngineTexts.ChildAllowed, entry.Exe, appId, checkText), true),
                new(string.Format(SetupEngineTexts.ModeSet, appId, ModeText(ChildProcessMode.Allowlist)), true),
            ]);
        });
    }

    /// <summary>deny-child: removes a helper; the last one sets the mode back to blocked.</summary>
    public IReadOnlyList<SetupStepResult> DenyChild(string appId, string exePath) =>
        ChangeApp(appId, app =>
        {
            var children = app.AllowedChildren ?? [];
            if (!children.Any(c => ChildPrograms.SamePath(c.Exe, exePath)))
            {
                throw new SetupInputException(string.Format(SetupEngineTexts.ChildUnknown, exePath, appId));
            }

            var rest = children.Where(c => !ChildPrograms.SamePath(c.Exe, exePath)).ToList();
            List<SetupStepResult> notes = [new(string.Format(SetupEngineTexts.ChildDenied, Path.GetFullPath(exePath), appId), true)];
            if (rest.Count == 0)
            {
                notes.Add(new(string.Format(SetupEngineTexts.LastChildRemoved, appId), true));
                return (app with { ChildProcesses = ChildProcessMode.Blocked, AllowedChildren = null }, notes);
            }

            return (app with { AllowedChildren = rest }, notes);
        });

    /// <summary>rehash --child: only the SHA-256 of one helper (signature entries need no rehash).</summary>
    public IReadOnlyList<SetupStepResult> RehashChild(string appId, string exePath) =>
        ChangeApp(appId, app =>
        {
            var children = app.AllowedChildren ?? [];
            if (!children.Any(c => ChildPrograms.SamePath(c.Exe, exePath)))
            {
                throw new SetupInputException(string.Format(SetupEngineTexts.ChildUnknown, exePath, appId));
            }

            var updated = children.Select(c => ChildPrograms.SamePath(c.Exe, exePath) ? ChildPrograms.Rehash(c) : c).ToList();
            return (app with { AllowedChildren = updated }, [new(string.Format(SetupEngineTexts.ChildrenRehashed, appId, 1), true)]);
        });

    // Changes one app and applies everything (apps.json is written as version 2: migration on first write).
    private IReadOnlyList<SetupStepResult> ChangeApp(string appId, Func<AppCandidate, (AppCandidate App, IReadOnlyList<SetupStepResult> Notes)> change)
    {
        var current = LoadCurrent();
        if (current.Apps.All(a => a.Id != appId))
        {
            return [new(string.Format(SetupEngineTexts.UnknownApp, appId), false)];
        }

        IReadOnlyList<SetupStepResult> notes = [];
        var apps = current.Apps.Select(a =>
        {
            if (a.Id != appId)
            {
                return ToCandidate(a);
            }

            (var changed, notes) = change(ToCandidate(a));
            return changed;
        }).ToList();

        return [.. notes, .. Apply(new SetupRequest(apps, false, null, stateStore.Load().AddedGroupMembers))];
    }

    private static string ModeText(ChildProcessMode mode) => mode switch
    {
        ChildProcessMode.Allowlist => SetupEngineTexts.ModeAllowlist,
        ChildProcessMode.All => SetupEngineTexts.ModeAll,
        _ => SetupEngineTexts.ModeBlocked,
    };

    /// <summary>uninstall: everything setup changed goes back; reports are kept.</summary>
    public IReadOnlyList<SetupStepResult> Uninstall()
    {
        var results = new List<SetupStepResult>();
        var state = stateStore.Load();
        foreach (var app in ReadConfig().Apps)
        {
            state = Unregister(app.Id, state);
        }

        // Leftovers of apps no longer in apps.json
        foreach (var id in state.AclBackups.Select(b => b.AppId).Concat(state.ShortcutBackups.Select(b => b.AppId)).Distinct().ToList())
        {
            state = Unregister(id, state);
        }

        shortcuts.Delete(state.CreatedShortcuts);
        results.Add(new(SetupEngineTexts.UninstallShortcuts, true));

        Try(results, SetupEngineTexts.UninstallAccounts, () =>
        {
            if (state.OperatorAccountCreatedBySetup)
            {
                accounts.RemoveOperatorAccount();
            }

            accounts.RemoveOperatorGroup();
            accounts.RemoveAppAccount();
        });

        Try(results, SetupEngineTexts.UninstallConfig, () =>
        {
            File.Delete(paths.ConfigPath);
            stateStore.Save(new SetupState());
        });

        return results;
    }

    // Restores ACLs and shortcuts of one app and deletes its launcher shortcuts.
    private SetupState Unregister(string appId, SetupState state)
    {
        foreach (var backup in state.AclBackups.Where(b => b.AppId == appId))
        {
            protection.Restore(backup);
        }

        var app = ReadConfig().Find(appId);
        var own = app is null
            ? []
            : state.CreatedShortcuts.Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), app.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        shortcuts.Delete(own);

        foreach (var backup in state.ShortcutBackups.Where(b => b.AppId == appId))
        {
            shortcuts.Restore(backup);
        }

        return state with
        {
            AclBackups = state.AclBackups.Where(b => b.AppId != appId).ToList(),
            ShortcutBackups = state.ShortcutBackups.Where(b => b.AppId != appId).ToList(),
            CreatedShortcuts = state.CreatedShortcuts.Except(own, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private AppsConfig ReadConfig()
    {
        if (!File.Exists(paths.ConfigPath))
        {
            return new AppsConfig { Version = AppsConfig.CurrentVersion, Apps = [] };
        }

        var parsed = AppsConfigSerializer.Parse(File.ReadAllText(paths.ConfigPath));
        return parsed.Config ?? throw new InvalidOperationException(string.Format(SetupEngineTexts.ConfigUnreadable, parsed.Error));
    }

    private static AppCandidate ToCandidate(AppEntry a) => new(
        a.Exe, a.Name, a.Id, a.Check, a.SignerThumbprint, a.Sha256, a.DataDir, a.RegistryKey, a.ChildProcessMode, a.AllowedChildren);

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void Try(List<SetupStepResult> results, string text, Action action)
    {
        try
        {
            action();
            results.Add(new(text, true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            results.Add(new(text, false, ex.Message));
        }
    }

    private static bool ServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(AppStartConstants.ServiceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static SetupStepResult EnsureServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(AppStartConstants.ServiceName);
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            }

            // Right after setup created "AppStart Users" the service may still be waiting for the group
            // (it retries every 10 s). Wait for the pipe; restart the service once if it does not appear.
            if (!WaitForPipe(TimeSpan.FromSeconds(12)))
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                if (!WaitForPipe(TimeSpan.FromSeconds(10)))
                {
                    return new(SetupEngineTexts.ServiceFailed, false, SetupEngineTexts.PipeMissing);
                }
            }

            return new(SetupEngineTexts.ServiceRunning, true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException or System.ComponentModel.Win32Exception)
        {
            return new(SetupEngineTexts.ServiceFailed, false, ex.Message);
        }
    }

    private static bool WaitForPipe(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (Directory.GetFiles(@"\\.\pipe\").Any(p => string.Equals(Path.GetFileName(p), AppStartConstants.PipeName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            Thread.Sleep(500);
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }
}
