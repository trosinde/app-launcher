using System.Security;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;

namespace AppStart.Setup.Tests.Engine;

public sealed class SetupEngineTests : IDisposable
{
    private static readonly SecurityIdentifier AppSid = new("S-1-5-21-1-2-3-1001");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"appstart-engine-{Guid.NewGuid():N}");
    private readonly FakeAccounts _accounts = new();
    private readonly FakeProtection _protection = new();
    private readonly FakeShortcuts _shortcuts = new();
    private readonly FakeState _state = new();
    private readonly SetupEngine _engine;

    public SetupEngineTests()
    {
        Directory.CreateDirectory(_dir);
        _engine = new SetupEngine(_accounts, _protection, _shortcuts, new FakeVerifier(), _state,
            new SetupPaths(@"C:\Program Files\AppStart\current", Path.Combine(_dir, "apps.json"), Path.Combine(_dir, "Reports")));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AppCandidate App(string id, string? dataDir = null, string? key = null) => new(
        $@"C:\Apps\{id}\{id}.exe", $"App {id}", id, IntegrityCheck.Sha256, null, new string('A', 64), dataDir, key);

    private SetupRequest Request(params AppCandidate[] apps) => new(apps, false, null, []);

    [Fact]
    public void Apply_creates_accounts_config_protection_and_shortcuts()
    {
        _shortcuts.Direct[@"C:\Apps\appa\appa.exe"] = [@"C:\Users\Public\Desktop\AppA alt.lnk"];

        var results = _engine.Apply(Request(App("appa", @"C:\Data\A", @"HKLM\SOFTWARE\A")));

        Assert.True(_accounts.AppAccountEnsured);
        Assert.True(_protection.ConfigDirProtected);
        var config = AppsConfigSerializer.Parse(File.ReadAllText(Path.Combine(_dir, "apps.json")));
        Assert.True(config.IsValid, config.Error);
        Assert.Equal("appa", Assert.Single(config.Config!.Apps).Id);
        Assert.Equal([@"C:\Data\A", @"HKLM\SOFTWARE\A"], _protection.Protected);
        Assert.Equal(2, _state.Current.AclBackups.Count);
        Assert.Single(_state.Current.ShortcutBackups);
        Assert.Contains(@"C:\Program Files\AppStart\current\AppStart.Launcher.exe", _shortcuts.LauncherTargets);
        Assert.All(results.Take(4), r => Assert.True(r.Success, r.Text + r.Detail));
        Assert.Equal("1 Applikationen freigegeben, Konfiguration geschützt", results[1].Text);
    }

    [Fact]
    public void Second_apply_keeps_the_first_acl_backup()
    {
        _engine.Apply(Request(App("appa", @"C:\Data\A")));
        _engine.Apply(Request(App("appa", @"C:\Data\A")));

        Assert.Single(_state.Current.AclBackups);
        Assert.Equal(2, _protection.Protected.Count); // re-applied, but backed up once
    }

    [Fact]
    public void Removed_app_gets_acls_and_shortcuts_restored()
    {
        _shortcuts.Direct[@"C:\Apps\appb\appb.exe"] = [@"C:\Users\Public\Desktop\B.lnk"];
        _engine.Apply(Request(App("appa"), App("appb", @"C:\Data\B")));

        _engine.Apply(Request(App("appa")));

        Assert.Equal([@"C:\Data\B"], _protection.Restored);
        Assert.Equal([@"C:\Users\Public\Desktop\B.lnk"], _shortcuts.RestoredOriginals);
        Assert.Empty(_state.Current.AclBackups);
        Assert.Empty(_state.Current.ShortcutBackups);
        Assert.DoesNotContain(_state.Current.CreatedShortcuts, p => p.Contains("App appb", StringComparison.Ordinal));
    }

    [Fact]
    public void Operator_members_added_by_setup_are_tracked_and_removed_again()
    {
        _engine.Apply(new SetupRequest([App("appa")], false, null, [@"LABOR\bediener01", @"LABOR\bediener02"]));
        Assert.Equal([@"LABOR\bediener01", @"LABOR\bediener02"], _state.Current.AddedGroupMembers);

        _engine.Apply(new SetupRequest([App("appa")], false, null, [@"LABOR\bediener02"]));

        Assert.Equal([@"LABOR\bediener01"], _accounts.Removed);
        Assert.Equal([@"LABOR\bediener02"], _state.Current.AddedGroupMembers);
    }

    [Fact]
    public void Operator_account_created_flag_is_kept_for_uninstall()
    {
        using var pw = new SecureString();
        pw.AppendChar('x');
        _engine.Apply(new SetupRequest([App("appa")], true, pw, []));
        Assert.True(_state.Current.OperatorAccountCreatedBySetup);

        _engine.Uninstall();

        Assert.True(_accounts.OperatorRemoved);
        Assert.True(_accounts.AppAccountRemoved);
        Assert.True(_accounts.GroupRemoved);
        Assert.False(File.Exists(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Uninstall_does_not_delete_an_operator_account_setup_did_not_create()
    {
        _engine.Apply(Request(App("appa")));
        _engine.Uninstall();
        Assert.False(_accounts.OperatorRemoved);
    }

    [Fact]
    public void Account_failure_stops_before_anything_else()
    {
        _accounts.Fail = true;
        var results = _engine.Apply(Request(App("appa")));
        Assert.False(Assert.Single(results).Success);
        Assert.False(_protection.ConfigDirProtected);
        Assert.False(File.Exists(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Invalid_registry_key_is_rejected_before_writing()
    {
        var results = _engine.Apply(Request(App("appa", key: @"HKCU\Software\A")));
        Assert.Contains(results, r => !r.Success && r.Detail!.Contains("registryKey", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Remove_of_unknown_app_reports_it()
    {
        var result = Assert.Single(_engine.Remove("nope"));
        Assert.False(result.Success);
    }

    // ------------------------------------------------------------------ fakes

    private sealed class FakeAccounts : IAccountService
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
            return AppSid;
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
        public AccountFacts GetFacts(string accountName) => new(AppAccountEnsured, AppSid, true, []);
    }

    private sealed class FakeProtection : IProtectionService
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

    private sealed class FakeShortcuts : IShortcutService
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

    private sealed class FakeState : ISetupStateStore
    {
        public SetupState Current { get; private set; } = new();
        public SetupState Load() => Current;
        public void Save(SetupState state) => Current = state;
    }

    private sealed class FakeVerifier : IVerifier
    {
        public VerifyReport Run() => new(DateTimeOffset.Now, "PC", "tech", "0.1.0", []);
        public string WriteReport(VerifyReport report, string directory) => Path.Combine(directory, "x.html");
    }
}
