using AppStart.Core.Configuration;
using AppStart.Core.Learning;
using AppStart.Setup.Cli;
using AppStart.Setup.Engine;
using AppStart.Setup.Tests.Orchestration;

namespace AppStart.Setup.Tests.Cli;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;

/// <summary>
/// add-allowed-child-processes, task 4.3: every new command and exit code of design D4 (0 success, 1 failure or
/// deviation, 2 invalid input found by AppStart). Real SetupEngine with fakes; nothing on the system changes.
/// </summary>
[Collection("Console")]
public sealed class CommandLineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"appstart-cli-{Guid.NewGuid():N}");
    private readonly List<string> _writable = [];
    private readonly SetupEngine _engine;

    public CommandLineTests()
    {
        Directory.CreateDirectory(_dir);
        _engine = new SetupEngine(new FakeAccounts(), new FakeProtection(), new FakeShortcuts(), new FakeVerifier(), new FakeState(),
            new SetupPaths(@"C:\Program Files\AppStart\current", Path.Combine(_dir, "apps.json"), Path.Combine(_dir, "Reports")),
            exe => _writable.Contains(exe, StringComparer.OrdinalIgnoreCase) ? ["Benutzer hat Schreibrechte."] : [],
            null, (_, _) => { }, () => new SetupStepResult("Dienst AppStart läuft, Start automatisch", true));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string name, string content = "MZ test")
    {
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var code = CommandLine.Build(() => _engine).Parse(args).Invoke();
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    private AppEntry Saved(string id) => AppsConfigSerializer.Parse(System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json"))).Config!.Find(id)!;

    private string Released()
    {
        var exe = File("AppA.exe");
        Assert.Equal(0, Run("add", "--exe", exe, "--id", "appa").Code);
        return exe;
    }

    // ------------------------------------------------ add --id / --child-processes

    [Fact]
    public void Add_with_id_and_mode_all()
    {
        var exe = File("Something.exe");
        var r = Run("add", "--exe", exe, "--id", "my-app", "--child-processes", "all");

        Assert.Equal(0, r.Code);
        Assert.Equal(ChildProcessMode.All, Saved("my-app").ChildProcesses);
        Assert.Contains("Automatische Prüfung", r.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--id", "My App")]
    [InlineData("--child-processes", "allowlist")]
    public void Add_with_invalid_input_is_exit_2(string option, string value)
    {
        var r = Run("add", "--exe", File("AppA.exe"), option, value);

        Assert.Equal(2, r.Code);
        Assert.NotEmpty(r.Err);
        Assert.False(System.IO.File.Exists(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Add_cannot_change_the_id_of_a_release()
    {
        var exe = Released();
        Assert.Equal(2, Run("add", "--exe", exe, "--id", "other").Code);
    }

    // ------------------------------------------------ set-children

    [Fact]
    public void Set_children_allowlist_without_entries_is_exit_2() =>
        Assert.Equal(2, (Released(), Run("set-children", "--app", "appa", "--mode", "allowlist")).Item2.Code);

    [Fact]
    public void Set_children_all_and_blocked()
    {
        Released();
        Assert.Equal(0, Run("set-children", "--app", "appa", "--mode", "all").Code);
        Assert.Equal(ChildProcessMode.All, Saved("appa").ChildProcesses);
        Assert.Equal(0, Run("set-children", "--app", "appa", "--mode", "blocked").Code);
        Assert.Equal(ChildProcessMode.Blocked, Saved("appa").ChildProcesses);
    }

    [Fact]
    public void Unknown_app_is_exit_1() =>
        Assert.Equal(1, Run("set-children", "--app", "nope", "--mode", "all").Code);

    [Fact]
    public void Syntax_error_is_exit_1_as_before() =>
        Assert.Equal(1, Run("set-children", "--mode", "all").Code); // --app missing

    // ------------------------------------------------ allow-child / deny-child / rehash --child

    // Spec scenario «Hilfsprogramm per Kommandozeile erlauben».
    [Fact]
    public void Allow_child_is_exit_0_with_automatic_check()
    {
        Released();
        var export = File("Export.exe");

        var r = Run("allow-child", "--app", "appa", "--exe", export);

        Assert.Equal(0, r.Code);
        Assert.Equal(Path.GetFullPath(export), Assert.Single(Saved("appa").AllowedChildren!).Exe);
        Assert.Contains("Automatische Prüfung", r.Out, StringComparison.Ordinal);
    }

    // Spec scenario «Shell ohne Bestätigung»: exit 2, apps.json unchanged.
    [Fact]
    public void Shell_without_allow_shell_is_exit_2()
    {
        Released();
        var before = System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json"));

        var r = Run("allow-child", "--app", "appa", "--exe", @"C:\Windows\System32\cmd.exe");

        Assert.Equal(2, r.Code);
        Assert.Contains("--allow-shell", r.Err, StringComparison.Ordinal);
        Assert.Equal(before, System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Shell_with_allow_shell_is_exit_0()
    {
        Released();
        Assert.Equal(0, Run("allow-child", "--app", "appa", "--exe", File("cmd.exe"), "--allow-shell").Code);
        Assert.True(Saved("appa").AllowedChildren![0].AllowShell);
    }

    // Spec scenario «Beschreibbarer Ordner»: exit 1, apps.json unchanged.
    [Fact]
    public void Writable_folder_is_exit_1()
    {
        Released();
        var export = File("Export.exe");
        _writable.Add(Path.GetFullPath(export));

        Assert.Equal(1, Run("allow-child", "--app", "appa", "--exe", export).Code);
        Assert.Equal(ChildProcessMode.Blocked, Saved("appa").ChildProcesses);
    }

    [Fact]
    public void Signature_check_for_unsigned_file_is_exit_2()
    {
        Released();
        Assert.Equal(2, Run("allow-child", "--app", "appa", "--exe", File("Export.exe"), "--check", "signature").Code);
    }

    [Fact]
    public void Deny_child_and_unknown_entry()
    {
        Released();
        var export = File("Export.exe");
        Run("allow-child", "--app", "appa", "--exe", export);

        var r = Run("deny-child", "--app", "appa", "--exe", export);
        Assert.Equal(0, r.Code);
        Assert.Contains("wieder gesperrt", r.Out, StringComparison.Ordinal);
        Assert.Equal(2, Run("deny-child", "--app", "appa", "--exe", export).Code);
    }

    [Fact]
    public void Rehash_child_renews_its_checksum()
    {
        Released();
        var updater = File("Updater.exe");
        Run("allow-child", "--app", "appa", "--exe", updater);
        var before = Saved("appa").AllowedChildren![0].Sha256;
        System.IO.File.WriteAllText(updater, "MZ new version");

        Assert.Equal(0, Run("rehash", "--app", "appa", "--child", updater).Code);
        Assert.NotEqual(before, Saved("appa").AllowedChildren![0].Sha256);
    }

    // ------------------------------------------------ learn

    private void Recording(params (string Exe, string? Thumb)[] programs)
    {
        var rec = new LearnRecording { AppId = "appa" };
        foreach (var (exe, thumb) in programs)
        {
            rec = rec.Record(exe, thumb, new string('E', 64), null, DateTimeOffset.UtcNow);
        }

        Directory.CreateDirectory(Path.Combine(_dir, "Learn"));
        System.IO.File.WriteAllText(Path.Combine(_dir, "Learn", "appa.json"), LearnRecordingSerializer.Serialize(rec));
    }

    [Fact]
    public void Learn_start_show_stop()
    {
        Released();

        var start = Run("learn", "start", "--app", "appa", "--minutes", "10");
        Assert.Equal(0, start.Code);
        Assert.DoesNotContain("Automatische Prüfung", start.Out, StringComparison.Ordinal); // design D4

        Recording((@"C:\Program Files\H\Export.exe", new string('B', 40)), (@"C:\Windows\System32\cmd.exe", new string('C', 40)));
        var show = Run("learn", "show", "--app", "appa");
        Assert.Equal(0, show.Code);
        Assert.Contains("aktiv bis", show.Out, StringComparison.Ordinal);
        Assert.Contains(@"cmd.exe – Shell oder Skript-Host", show.Out, StringComparison.Ordinal);

        Assert.Equal(0, Run("learn", "stop", "--app", "appa").Code);
        Assert.Contains("nicht aktiv", Run("learn", "show", "--app", "appa").Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("121")]
    public void Learn_minutes_out_of_range_is_exit_2(string minutes)
    {
        Released();
        Assert.Equal(2, Run("learn", "start", "--app", "appa", "--minutes", minutes).Code);
    }

    [Fact]
    public void Learn_start_unknown_app_is_exit_1() =>
        Assert.Equal(1, Run("learn", "start", "--app", "nope").Code);

    [Fact]
    public void Learn_apply_all_takes_everything_but_shells()
    {
        Released();
        Recording((@"C:\Program Files\H\Export.exe", new string('B', 40)), (@"C:\Windows\System32\cmd.exe", new string('C', 40)));

        var r = Run("learn", "apply", "--app", "appa", "--all");

        Assert.Equal(0, r.Code);
        var app = Saved("appa");
        Assert.Equal(ChildProcessMode.Allowlist, app.ChildProcesses);
        Assert.Equal(@"C:\Program Files\H\Export.exe", Assert.Single(app.AllowedChildren!).Exe);
        Assert.Contains("Automatische Prüfung", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Learn_apply_by_exe()
    {
        Released();
        Recording((@"C:\Program Files\H\Export.exe", new string('B', 40)), (@"C:\Program Files\H\Updater.exe", null));

        Assert.Equal(0, Run("learn", "apply", "--app", "appa", "--exe", @"C:\Program Files\H\Updater.exe").Code);
        Assert.Equal(IntegrityCheck.Sha256, Assert.Single(Saved("appa").AllowedChildren!).Check);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Learn_apply_needs_exactly_all_or_exe(bool all, bool exe)
    {
        Released();
        Recording((@"C:\Program Files\H\Export.exe", new string('B', 40)));
        List<string> args = ["learn", "apply", "--app", "appa"];
        if (all)
        {
            args.Add("--all");
        }

        if (exe)
        {
            args.AddRange(["--exe", @"C:\Program Files\H\Export.exe"]);
        }

        Assert.Equal(2, Run([.. args]).Code);
    }

    [Fact]
    public void Learn_apply_shell_or_without_recording_is_exit_2()
    {
        Released();
        Assert.Equal(2, Run("learn", "apply", "--app", "appa", "--all").Code); // nothing recorded
        Recording((@"C:\Windows\System32\cmd.exe", new string('C', 40)));
        Assert.Equal(2, Run("learn", "apply", "--app", "appa", "--exe", @"C:\Windows\System32\cmd.exe").Code);
    }

    // ------------------------------------------------ apply --config (task 4.4, design D5)

    private static string Json(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);

    private string AnswerFile(string content) => File("appstart.json", content);

    private string TwoApps()
    {
        var a = File("AppA.exe");
        var b = File("AppB.exe");
        var export = File("Export.exe");
        return AnswerFile($$"""
            {
              "version": 1,
              "apps": [
                { "exe": "{{Json(a)}}", "id": "appa", "name": "Applikation A", "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
                  "childProcesses": "allowlist", "allowedChildren": [ { "exe": "{{Json(export)}}" } ] },
                { "exe": "{{Json(b)}}", "id": "appb" }
              ],
              "operators": [ "LABOR\\Bediener" ]
            }
            """);
    }

    // Spec scenario «Erste Einrichtung».
    [Fact]
    public void Apply_sets_up_two_apps_and_checks()
    {
        var r = Run("apply", "--config", TwoApps());

        Assert.Equal(0, r.Code);
        Assert.Contains("Automatische Prüfung", r.Out, StringComparison.Ordinal);
        var a = Saved("appa");
        Assert.Equal("Applikation A", a.Name);
        Assert.Equal(@"C:\ProgramData\Hersteller\AppA", a.DataDir);
        Assert.Equal(ChildProcessMode.Allowlist, a.ChildProcesses);
        Assert.Equal(IntegrityCheck.Sha256, Assert.Single(a.AllowedChildren!).Check); // value read from the file on this machine
        Assert.Equal(ChildProcessMode.Blocked, Saved("appb").ChildProcesses);
    }

    // Spec scenario «Zweite Anwendung»: nothing changes.
    [Fact]
    public void Apply_twice_changes_nothing()
    {
        var file = TwoApps();
        Run("apply", "--config", file);
        var first = System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json"));

        var r = Run("apply", "--config", file);

        Assert.Equal(0, r.Code);
        Assert.Equal(first, System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json")));
    }

    // Spec scenario «Ungültige Datei»: nothing changes, output names field or path, exit 2.
    [Theory]
    [InlineData("""{ "version": 1, "apps": [], "password": "geheim" }""", "password")]
    [InlineData("""{ "version": 1, "apps": [ { "exe": "C:\\nicht\\da\\App.exe" } ] }""", "apps[0].exe")]
    [InlineData("""{ "version": 1, "apps": [ { "exe": "C:\\nicht\\da\\App.exe", "registryKey": "HKCU\\X" } ] }""", "apps[0].registryKey")]
    [InlineData("""{ "version": 1, "apps": [], }""", "JSON")]
    public void Invalid_file_changes_nothing_and_is_exit_2(string content, string named)
    {
        Released();
        var before = System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json"));

        var r = Run("apply", "--config", AnswerFile(content));

        Assert.Equal(2, r.Code);
        Assert.Contains(named, r.Err, StringComparison.Ordinal);
        Assert.Equal(before, System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json")));
    }

    // Format errors (e.g. a shell without allowShell, see AnswerFileTests) come first; then every missing file at once.
    [Fact]
    public void Missing_helpers_are_reported_together()
    {
        var a = File("AppA.exe");
        var r = Run("apply", "--config", AnswerFile($$"""
            { "version": 1, "apps": [ { "exe": "{{Json(a)}}", "childProcesses": "allowlist", "allowedChildren": [
              { "exe": "C:\\nicht\\da\\Export.exe" }, { "exe": "C:\\nicht\\da\\Updater.exe" } ] } ] }
            """));

        Assert.Equal(2, r.Code);
        Assert.Contains("allowedChildren[0]", r.Err, StringComparison.Ordinal);
        Assert.Contains("allowedChildren[1]", r.Err, StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Missing_answer_file_is_exit_2() =>
        Assert.Equal(2, Run("apply", "--config", Path.Combine(_dir, "fehlt.json")).Code);

    [Fact]
    public void Writable_helper_folder_is_exit_1_and_changes_nothing()
    {
        var file = TwoApps();
        _writable.Add(Path.Combine(_dir, "Export.exe"));

        Assert.Equal(1, Run("apply", "--config", file).Code);
        Assert.False(System.IO.File.Exists(Path.Combine(_dir, "apps.json")));
    }

    [Fact]
    public void Apps_not_in_the_file_stay_unless_replace()
    {
        Released(); // appa from AppA.exe
        var c = File("AppC.exe");
        var file = AnswerFile($$"""{ "version": 1, "apps": [ { "exe": "{{Json(c)}}", "id": "appc" } ] }""");

        Assert.Equal(0, Run("apply", "--config", file).Code);
        Assert.NotNull(Saved("appa"));

        Assert.Equal(0, Run("apply", "--config", file, "--replace").Code);
        Assert.Null(AppsConfigSerializer.Parse(System.IO.File.ReadAllText(Path.Combine(_dir, "apps.json"))).Config!.Find("appa"));
        Assert.NotNull(Saved("appc"));
    }

    // ------------------------------------------------ list

    [Fact]
    public void List_shows_mode_and_helpers()
    {
        Released();
        var export = File("Export.exe");
        Run("allow-child", "--app", "appa", "--exe", export);

        var r = Run("list");

        Assert.Equal(0, r.Code);
        Assert.Contains("Weitere Programme: nur erlaubte Hilfsprogramme", r.Out, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(export) + " (SHA-256)", r.Out, StringComparison.Ordinal);
    }
}
