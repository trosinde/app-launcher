using System.Security;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;

namespace AppStart.Setup.Tests.Orchestration;

/// <summary>Fakes for a real SetupEngine without system changes (engine and command line tests).</summary>
internal static class EngineFakes
{
    public static readonly SecurityIdentifier AppSid = new("S-1-5-21-1-2-3-1001");
}

// ------------------------------------------------------------------ fakes

internal sealed class FakeAccounts : IAccountService
{
    public bool Fail { get; set; }
    public bool AppAccountEnsured { get; private set; }
    public bool OperatorRemoved { get; private set; }
    public bool AppAccountRemoved { get; private set; }
    public bool GroupRemoved { get; private set; }
    public List<string> Members { get; } = [];
    public List<string> Removed { get; } = [];

    public SecurityIdentifier EnsureAppAccount()
    {
        if (Fail)
        {
            throw new InvalidOperationException("kein Zugriff");
        }

        AppAccountEnsured = true;
        return EngineFakes.AppSid;
    }

    public SecurityIdentifier EnsureOperatorGroup() => new("S-1-5-21-1-2-3-1002");
    public bool EnsureOperatorAccount(SecureString password) => true;
    public bool AddOperator(string account)
    {
        if (Members.Contains(account))
        {
            return false;
        }

        Members.Add(account);
        return true;
    }

    public bool RemoveOperator(string account)
    {
        Removed.Add(account);
        return Members.Remove(account);
    }

    public IReadOnlyList<string> GetOperators() => Members;
    public void RemoveAppAccount() => AppAccountRemoved = true;
    public void RemoveOperatorAccount() => OperatorRemoved = true;
    public void RemoveOperatorGroup() => GroupRemoved = true;
    public AccountFacts GetFacts(string accountName) => new(AppAccountEnsured, EngineFakes.AppSid, true, []);
    public List<AdminPasswordFinding> AdminsWithoutPassword { get; } = [];
    public IReadOnlyList<AdminPasswordFinding> FindAdministratorsWithoutPassword() => AdminsWithoutPassword;
}

internal sealed class FakeProtection : IProtectionService
{
    public bool ConfigDirProtected { get; private set; }
    public List<string> Protected { get; } = [];
    public List<string> Restored { get; } = [];

    public void ProtectConfigDirectory() => ConfigDirProtected = true;
    public void WriteProtectedFile(string path, string content) => File.WriteAllText(path, content);

    public AclBackup ProtectDataDirectory(string path, SecurityIdentifier appAccount, string appId)
    {
        Protected.Add(path);
        return new AclBackup(AclTarget.Directory, path, "D:", appId);
    }

    public AclBackup ProtectRegistryKey(string key, SecurityIdentifier appAccount, string appId)
    {
        Protected.Add(key);
        return new AclBackup(AclTarget.RegistryKey, key, "D:", appId);
    }

    public void Restore(AclBackup backup) => Restored.Add(backup.Path);
    public IReadOnlyList<string> FindForeignAccess(AclTarget target, string path, SecurityIdentifier appAccount) => [];
}

internal sealed class FakeShortcuts : IShortcutService
{
    public Dictionary<string, List<string>> Direct { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> LauncherTargets { get; } = [];
    public List<string> RestoredOriginals { get; } = [];

    public IReadOnlyList<string> CreateAppShortcuts(AppEntry app, string launcherPath)
    {
        LauncherTargets.Add(launcherPath);
        return [$@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\{app.Name}.lnk", $@"C:\Users\Public\Desktop\{app.Name}.lnk"];
    }

    public IReadOnlyList<string> FindDirectShortcuts(string exePath)
    {
        var found = Direct.TryGetValue(exePath, out var list) ? list.ToList() : [];
        Direct.Remove(exePath); // moved away: not found again
        return found;
    }

    public ShortcutBackup BackupAndRemove(string shortcutPath, string appId) => new(shortcutPath, @"C:\ProgramData\AppStart\ShortcutBackup\x.lnk", appId);
    public void Restore(ShortcutBackup backup) => RestoredOriginals.Add(backup.OriginalPath);
    public void Delete(IEnumerable<string> paths) { }
    public ShortcutInfo? Read(string path) => null;
}

internal sealed class FakeState : ISetupStateStore
{
    public SetupState Current { get; private set; } = new();
    public SetupState Load() => Current;
    public void Save(SetupState state) => Current = state;
}

internal sealed class FakeVerifier : IVerifier
{
    public VerifyReport Run() => new(DateTimeOffset.Now, "PC", "tech", "0.1.0", []);
    public string WriteReport(VerifyReport report, string directory) => Path.Combine(directory, "x.html");
}
