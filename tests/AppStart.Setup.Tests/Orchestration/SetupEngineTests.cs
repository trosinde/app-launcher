using System.Security;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;

namespace AppStart.Setup.Tests.Orchestration;

public sealed class SetupEngineTests : IDisposable
{
    private static readonly SecurityIdentifier AppSid = new("S-1-5-21-1-2-3-1001");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"appstart-engine-{Guid.NewGuid():N}");
    private readonly FakeAccounts _accounts = new();
    private readonly FakeProtection _protection = new();
    private readonly FakeShortcuts _shortcuts = new();
    private readonly FakeState _state = new();
    private readonly SetupEngine _engine;

    private readonly List<string> _writable = [];

    public SetupEngineTests()
    {
        Directory.CreateDirectory(_dir);
        _engine = new SetupEngine(_accounts, _protection, _shortcuts, new FakeVerifier(), _state,
            new SetupPaths(@"C:\Program Files\AppStart\current", Path.Combine(_dir, "apps.json"), Path.Combine(_dir, "Reports")),
            exe => _writable.Contains(exe, StringComparer.OrdinalIgnoreCase) ? ["Benutzer hat Schreibrechte."] : []);
    }

    private string ConfigPath => Path.Combine(_dir, "apps.json");

    private AppEntry Saved(string id) => AppsConfigSerializer.Parse(File.ReadAllText(ConfigPath)).Config!.Find(id)!;

    private string Helper(string name, string content = "MZ helper")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
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
        Assert.True(_protection.ConfigDirProtected); // created first, so the state can be written
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

    // ------------------------------------------------------------------ add-allowed-child-processes (task 4.1)

    // Spec installation, «Migration der Konfiguration»: version 1 becomes version 2 with the same protection.
    [Theory]
    [InlineData(false, ChildProcessMode.Blocked)]
    [InlineData(true, ChildProcessMode.All)]
    public void First_write_migrates_version_1(bool allowChildProcesses, ChildProcessMode expected)
    {
        File.WriteAllText(ConfigPath, $$"""
            { "version": 1, "apps": [ { "id": "appa", "name": "App appa", "exe": "C:\\Apps\\appa\\appa.exe",
              "workingDir": "C:\\Apps\\appa", "check": "sha256", "sha256": "{{new string('A', 64)}}",
              "allowChildProcesses": {{(allowChildProcesses ? "true" : "false")}} } ] }
            """);

        _engine.Apply(new SetupRequest([.. _engine.LoadCurrent().Apps.Select(a => _engineCandidate(a))], false, null, []));

        var json = File.ReadAllText(ConfigPath);
        Assert.Contains("\"version\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("allowChildProcesses", json, StringComparison.Ordinal);
        Assert.Equal(expected, Saved("appa").ChildProcesses);
    }

    private static AppCandidate _engineCandidate(AppEntry a) =>
        new(a.Exe, a.Name, a.Id, a.Check, a.SignerThumbprint, a.Sha256, a.DataDir, a.RegistryKey, a.ChildProcessMode, a.AllowedChildren);

    // Spec installation, «Hilfsprogramm per Kommandozeile erlauben» (engine part).
    [Fact]
    public void Allow_child_adds_entry_with_sha256_and_sets_allowlist()
    {
        _engine.Apply(Request(App("appa")));
        var export = Helper("Export.exe");

        var results = _engine.AllowChild("appa", export, null, allowShell: false);

        Assert.All(results.Take(4), r => Assert.True(r.Success, r.Text + r.Detail));
        var app = Saved("appa");
        Assert.Equal(ChildProcessMode.Allowlist, app.ChildProcesses);
        var child = Assert.Single(app.AllowedChildren!);
        Assert.Equal(Path.GetFullPath(export), child.Exe);
        Assert.Equal(IntegrityCheck.Sha256, child.Check); // not signed
        Assert.Equal(64, child.Sha256!.Length);
    }

    [Fact]
    public void Allow_child_twice_replaces_the_entry()
    {
        _engine.Apply(Request(App("appa")));
        var export = Helper("Export.exe");
        _engine.AllowChild("appa", export, null, false);
        File.WriteAllText(export, "MZ changed");
        _engine.AllowChild("appa", export.ToUpperInvariant(), null, false);

        Assert.Single(Saved("appa").AllowedChildren!);
    }

    // Spec scenario «Shell ohne Bestätigung»: apps.json unchanged, invalid input.
    [Fact]
    public void Shell_without_confirmation_is_invalid_input_and_changes_nothing()
    {
        _engine.Apply(Request(App("appa")));
        var before = File.ReadAllText(ConfigPath);

        Assert.Throws<SetupInputException>(() => _engine.AllowChild("appa", @"C:\Windows\System32\cmd.exe", null, false));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Shell_with_confirmation_is_stored_with_allow_shell()
    {
        _engine.Apply(Request(App("appa")));
        var cmd = Helper("cmd.exe");

        _engine.AllowChild("appa", cmd, null, allowShell: true);

        Assert.True(Assert.Single(Saved("appa").AllowedChildren!).AllowShell);
    }

    // Spec scenario «Beschreibbarer Ordner»: refused (exit code 1), apps.json unchanged.
    [Fact]
    public void Helper_in_operator_writable_folder_is_refused()
    {
        _engine.Apply(Request(App("appa")));
        var before = File.ReadAllText(ConfigPath);
        var export = Helper("Export.exe");
        _writable.Add(Path.GetFullPath(export));

        var result = Assert.Single(_engine.AllowChild("appa", export, null, false));

        Assert.False(result.Success);
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Signature_check_for_unsigned_file_is_invalid_input()
    {
        _engine.Apply(Request(App("appa")));
        Assert.Throws<SetupInputException>(() => _engine.AllowChild("appa", Helper("Export.exe"), IntegrityCheck.Signature, false));
    }

    [Fact]
    public void Missing_helper_is_invalid_input() =>
        Assert.Throws<SetupInputException>(() =>
        {
            _engine.Apply(Request(App("appa")));
            _engine.AllowChild("appa", Path.Combine(_dir, "missing.exe"), null, false);
        });

    [Fact]
    public void Allowlist_without_entries_is_invalid_input()
    {
        _engine.Apply(Request(App("appa")));
        Assert.Throws<SetupInputException>(() => _engine.SetChildMode("appa", ChildProcessMode.Allowlist));
    }

    [Fact]
    public void Set_mode_all_and_back_to_blocked()
    {
        _engine.Apply(Request(App("appa")));
        _engine.AllowChild("appa", Helper("Export.exe"), null, false);

        _engine.SetChildMode("appa", ChildProcessMode.All);
        Assert.Equal(ChildProcessMode.All, Saved("appa").ChildProcesses);
        Assert.Null(Saved("appa").AllowedChildren);

        _engine.SetChildMode("appa", ChildProcessMode.Blocked);
        Assert.Equal(ChildProcessMode.Blocked, Saved("appa").ChildProcesses);
    }

    [Fact]
    public void Deny_last_child_sets_blocked()
    {
        _engine.Apply(Request(App("appa")));
        var export = Helper("Export.exe");
        var updater = Helper("Updater.exe");
        _engine.AllowChild("appa", export, null, false);
        _engine.AllowChild("appa", updater, null, false);

        _engine.DenyChild("appa", export);
        Assert.Equal(ChildProcessMode.Allowlist, Saved("appa").ChildProcesses);

        var results = _engine.DenyChild("appa", updater);
        Assert.Equal(ChildProcessMode.Blocked, Saved("appa").ChildProcesses);
        Assert.Contains(results, r => r.Text.Contains("wieder gesperrt", StringComparison.Ordinal));
        Assert.Throws<SetupInputException>(() => _engine.DenyChild("appa", updater));
    }

    [Fact]
    public void Rehash_renews_sha256_of_helpers()
    {
        _engine.Apply(Request(App("appa")));
        var updater = Helper("Updater.exe");
        _engine.AllowChild("appa", updater, null, false);
        var before = Saved("appa").AllowedChildren![0].Sha256;

        File.WriteAllText(updater, "MZ updated by the vendor");
        _engine.RehashChild("appa", updater);

        Assert.NotEqual(before, Saved("appa").AllowedChildren![0].Sha256);
    }

    [Fact]
    public void Child_operations_on_unknown_app_report_it()
    {
        Assert.False(Assert.Single(_engine.SetChildMode("nope", ChildProcessMode.All)).Success);
        Assert.False(Assert.Single(_engine.AllowChild("nope", Helper("Export.exe"), null, false)).Success);
    }

    // ------------------------------------------------------------------ learn mode (design D3, engine part of 4.2/4.3)

    private void Record(string appId, params (string Exe, string? Thumb)[] programs)
    {
        var rec = new Core.Learning.LearnRecording { AppId = appId };
        foreach (var (exe, thumb) in programs)
        {
            rec = rec.Record(exe, thumb, new string('E', 64), null, DateTimeOffset.UtcNow);
        }

        Directory.CreateDirectory(Path.Combine(_dir, "Learn"));
        File.WriteAllText(Path.Combine(_dir, "Learn", appId + ".json"), Core.Learning.LearnRecordingSerializer.Serialize(rec));
    }

    [Fact]
    public void Start_learning_writes_learn_json_and_an_event()
    {
        var events = new List<int>();
        var engine = new SetupEngine(_accounts, _protection, _shortcuts, new FakeVerifier(), _state,
            new SetupPaths(@"C:\Program Files\AppStart\current", ConfigPath, Path.Combine(_dir, "Reports")), _ => [], null, (id, _) => events.Add(id));
        engine.Apply(Request(App("appa")));

        var result = Assert.Single(engine.StartLearning("appa", 30));

        Assert.True(result.Success);
        Assert.NotNull(engine.GetLearnSession("appa"));
        Assert.Equal([1400], events);
        engine.StopLearning("appa");
        Assert.Null(engine.GetLearnSession("appa"));
        Assert.Equal([1400, 1401], events);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void Learn_duration_outside_5_to_120_is_invalid_input(int minutes)
    {
        _engine.Apply(Request(App("appa")));
        Assert.Throws<SetupInputException>(() => _engine.StartLearning("appa", minutes));
        Assert.False(File.Exists(Path.Combine(_dir, "learn.json")));
    }

    [Fact]
    public void Learn_for_unknown_app_is_refused() =>
        Assert.False(Assert.Single(_engine.StartLearning("nope", 30)).Success);

    [Fact]
    public void Apply_learned_takes_all_but_shells_and_cleans_up()
    {
        _engine.Apply(Request(App("appa")));
        _engine.StartLearning("appa", 30);
        Record("appa", (@"C:\Apps\appa\Export.exe", new string('B', 40)), (@"C:\Apps\appa\Updater.exe", null), (@"C:\Windows\System32\cmd.exe", new string('C', 40)));

        var results = _engine.ApplyLearned("appa", null);

        Assert.All(results.Take(3), r => Assert.True(r.Success, r.Text + r.Detail));
        var app = Saved("appa");
        Assert.Equal(ChildProcessMode.Allowlist, app.ChildProcesses);
        Assert.Equal([IntegrityCheck.Signature, IntegrityCheck.Sha256], app.AllowedChildren!.Select(c => c.Check));
        Assert.DoesNotContain(app.AllowedChildren!, c => c.Exe.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Null(_engine.GetLearnSession("appa"));
        Assert.Null(_engine.GetRecording("appa"));
    }

    [Fact]
    public void Apply_learned_shell_by_name_is_invalid_input()
    {
        _engine.Apply(Request(App("appa")));
        Record("appa", (@"C:\Windows\System32\cmd.exe", new string('C', 40)));

        Assert.Throws<SetupInputException>(() => _engine.ApplyLearned("appa", [@"C:\Windows\System32\cmd.exe"]));
        Assert.Throws<SetupInputException>(() => _engine.ApplyLearned("appa", null)); // nothing else to take
    }

    [Fact]
    public void Apply_learned_without_recording_is_invalid_input()
    {
        _engine.Apply(Request(App("appa")));
        Assert.Throws<SetupInputException>(() => _engine.ApplyLearned("appa", null));
    }

    [Fact]
    public void Apply_learned_refuses_writable_folders()
    {
        _engine.Apply(Request(App("appa")));
        Record("appa", (@"C:\Users\Public\Export.exe", null));
        _writable.Add(@"C:\Users\Public\Export.exe");

        Assert.False(Assert.Single(_engine.ApplyLearned("appa", null)).Success);
        Assert.Equal(ChildProcessMode.Blocked, Saved("appa").ChildProcesses);
    }
}
