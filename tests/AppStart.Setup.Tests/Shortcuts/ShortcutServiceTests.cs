using System.IO;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Shortcuts;

namespace AppStart.Setup.Tests.Shortcuts;

/// <summary>
/// Works only in its own folder under %TEMP% (injected roots); never touches the real Start menu,
/// desktop or C:\Users.
/// </summary>
public sealed class ShortcutServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appstart-shortcuts-{Guid.NewGuid():N}");
    private readonly string _startMenu;
    private readonly string _desktop;
    private readonly string _profiles;
    private readonly string _backup;
    private readonly string _appDir;
    private readonly string _exe;
    private readonly string _launcher;
    private readonly ShortcutService _service;

    public ShortcutServiceTests()
    {
        _startMenu = Path.Combine(_root, "StartMenu", "Programs");
        _desktop = Path.Combine(_root, "PublicDesktop");
        _profiles = Path.Combine(_root, "Users");
        _backup = Path.Combine(_root, "ProgramData", "AppStart", "ShortcutBackup");
        _appDir = Path.Combine(_root, "Vendor", "Waage");
        _exe = Path.Combine(_appDir, "Waage.exe");
        _launcher = Path.Combine(_root, "AppStart", "AppStart.Launcher.exe");

        Directory.CreateDirectory(_appDir);
        Directory.CreateDirectory(Path.GetDirectoryName(_launcher)!);
        File.WriteAllBytes(_exe, "MZ"u8.ToArray());
        File.WriteAllBytes(_launcher, "MZ"u8.ToArray());

        _service = new ShortcutService(_startMenu, _desktop, _profiles, _backup);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AppEntry App(string id = "waage", string name = "Waage Pro") => new()
    {
        Id = id,
        Name = name,
        Exe = _exe,
        WorkingDir = _appDir,
        Check = IntegrityCheck.Sha256,
        Sha256 = new string('0', 64),
    };

    private string DirectShortcut(string folder, string name = "Waage.lnk", string? target = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        ShellLink.Save(path, target ?? _exe, string.Empty, _appDir, target ?? _exe, 0, "direct");
        return path;
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Creates_shortcuts_in_start_menu_and_desktop_and_reads_them_back()
    {
        var created = _service.CreateAppShortcuts(App(), _launcher);

        Assert.Equal(
            [Path.Combine(_startMenu, "Waage Pro.lnk"), Path.Combine(_desktop, "Waage Pro.lnk")],
            created);
        foreach (var path in created)
        {
            var info = _service.Read(path);
            Assert.NotNull(info);
            Assert.Equal(path, info.Path);
            Assert.Equal(_launcher, info.TargetPath, ignoreCase: true);
            Assert.Equal("--app waage", info.Arguments);
            Assert.Equal($"{_exe},0", info.IconLocation, ignoreCase: true);
            Assert.Equal(_appDir, info.WorkingDirectory, ignoreCase: true);
        }

        Assert.Empty(Directory.GetFiles(_startMenu, "~appstart-*"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Sanitises_the_file_name()
    {
        var created = _service.CreateAppShortcuts(App(name: "Waage: Export."), _launcher);

        Assert.Equal(Path.Combine(_startMenu, "Waage_ Export.lnk"), created[0]);
        Assert.True(File.Exists(created[0]));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Overwrites_own_shortcut_but_not_a_foreign_one()
    {
        _service.CreateAppShortcuts(App(), _launcher);
        _service.CreateAppShortcuts(App(), _launcher); // own: fine

        // Another app with the same display name must not silently take over the shortcut.
        var ex = Assert.Throws<InvalidOperationException>(() => _service.CreateAppShortcuts(App(id: "other"), _launcher));
        Assert.Contains("not overwritten", ex.Message, StringComparison.Ordinal);
        Assert.Equal("--app waage", _service.Read(Path.Combine(_desktop, "Waage Pro.lnk"))!.Arguments);

        // A foreign file on the desktop only: nothing is written, also not in the Start menu.
        _service.Delete([Path.Combine(_startMenu, "Waage Pro.lnk")]);
        DirectShortcut(_desktop, "Neu App.lnk");
        Assert.Throws<InvalidOperationException>(() => _service.CreateAppShortcuts(App(id: "neu", name: "Neu App"), _launcher));
        Assert.False(File.Exists(Path.Combine(_startMenu, "Neu App.lnk")));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Finds_direct_shortcuts_in_public_folders_and_profiles_but_not_launcher_shortcuts()
    {
        var expected = new[]
        {
            DirectShortcut(Path.Combine(_startMenu, "Vendor")),
            DirectShortcut(_desktop),
            DirectShortcut(Path.Combine(_profiles, "alice", "Desktop")),
            DirectShortcut(Path.Combine(_profiles, "bob", "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Vendor", "Sub")),
        };

        // Same EXE, other spelling of the path: still direct.
        var upper = DirectShortcut(Path.Combine(_profiles, "carol", "Desktop"), target: _exe.ToUpperInvariant());

        // Not direct or not searched.
        DirectShortcut(_desktop, "Other.lnk", target: _launcher);
        _service.CreateAppShortcuts(App(), _launcher);
        DirectShortcut(Path.Combine(_profiles, "Public", "Desktop"));
        DirectShortcut(Path.Combine(_profiles, "Default", "Desktop"));
        DirectShortcut(Path.Combine(_profiles, "alice", "Documents"));
        File.WriteAllText(Path.Combine(_desktop, "Broken.lnk"), "no shortcut");

        var found = _service.FindDirectShortcuts(Path.Combine(_appDir, "..", "Waage", "waage.EXE"));

        Assert.Equal(
            expected.Append(upper).Order(StringComparer.OrdinalIgnoreCase),
            found.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_roots_yield_no_shortcuts()
    {
        var service = new ShortcutService(
            Path.Combine(_root, "none1"), Path.Combine(_root, "none2"), Path.Combine(_root, "none3"), _backup);

        Assert.Empty(service.FindDirectShortcuts(_exe));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Backup_and_restore_roundtrip()
    {
        var original = DirectShortcut(Path.Combine(_profiles, "alice", "Desktop"));
        var content = File.ReadAllBytes(original);

        var backup = _service.BackupAndRemove(original, "waage");

        Assert.False(File.Exists(original));
        Assert.Equal(original, backup.OriginalPath);
        Assert.Equal("waage", backup.AppId);
        Assert.Equal(_backup, Path.GetDirectoryName(backup.BackupPath));
        Assert.EndsWith("-Waage.lnk", backup.BackupPath, StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllBytes(backup.BackupPath));

        // Same name from another folder gets its own backup file.
        var second = _service.BackupAndRemove(DirectShortcut(_desktop), "waage");
        Assert.NotEqual(backup.BackupPath, second.BackupPath);

        Directory.Delete(Path.GetDirectoryName(original)!); // restore re-creates the folder
        _service.Restore(backup);
        _service.Restore(second);

        Assert.Equal(content, File.ReadAllBytes(original));
        Assert.Equal(_exe, _service.Read(original)!.TargetPath, ignoreCase: true);
        Assert.True(File.Exists(Path.Combine(_desktop, "Waage.lnk")));
        Assert.Empty(Directory.GetFiles(_backup));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Restore_does_not_overwrite_an_existing_file()
    {
        var original = DirectShortcut(_desktop);
        var backup = _service.BackupAndRemove(original, "waage");
        File.WriteAllText(original, "new file");

        var ex = Assert.Throws<IOException>(() => _service.Restore(backup));

        Assert.Contains("A file already exists", ex.Message, StringComparison.Ordinal);
        Assert.Equal("new file", File.ReadAllText(original));
        Assert.True(File.Exists(backup.BackupPath));
    }

    [Fact]
    public void Restore_reports_missing_backup()
    {
        var backup = new ShortcutBackup(Path.Combine(_desktop, "x.lnk"), Path.Combine(_backup, "gone.lnk"), "waage");

        var ex = Assert.Throws<FileNotFoundException>(() => _service.Restore(backup));
        Assert.Contains("is missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Backup_of_missing_shortcut_fails()
    {
        Assert.Throws<FileNotFoundException>(() => _service.BackupAndRemove(Path.Combine(_desktop, "none.lnk"), "waage"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Delete_removes_shortcuts_and_ignores_missing_ones()
    {
        var created = _service.CreateAppShortcuts(App(), _launcher);

        _service.Delete([.. created, Path.Combine(_desktop, "none.lnk"), Path.Combine(_root, "nofolder", "x.lnk")]);

        Assert.All(created, p => Assert.False(File.Exists(p)));
    }

    [Fact]
    public void Delete_refuses_non_shortcuts()
    {
        Assert.Throws<ArgumentException>(() => _service.Delete([_exe]));
        Assert.True(File.Exists(_exe));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Read_returns_null_for_missing_or_invalid_files()
    {
        var invalid = Path.Combine(_root, "invalid.lnk");
        File.WriteAllText(invalid, "no shortcut");

        Assert.Null(_service.Read(Path.Combine(_root, "none.lnk")));
        Assert.Null(_service.Read(invalid));
    }

    [Fact]
    public void Relative_paths_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => _service.CreateAppShortcuts(App(), "AppStart.Launcher.exe"));
        Assert.Throws<ArgumentException>(() => _service.FindDirectShortcuts("Waage.exe"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void CreateShortcut_writes_folder_and_overwrites()
    {
        var lnk = Path.Combine(_startMenu, "AppStart", "AppStart – Verify setup.lnk");

        _service.CreateShortcut(lnk, _exe, "verify", string.Empty, _appDir);
        _service.CreateShortcut(lnk, _launcher, "verify --ui", $"{_exe},2", _root, "Verifies the setup");

        var info = _service.Read(lnk);
        Assert.NotNull(info);
        Assert.Equal(_launcher, info.TargetPath, ignoreCase: true);
        Assert.Equal("verify --ui", info.Arguments);
        Assert.Equal($"{_exe},2", info.IconLocation, ignoreCase: true);
        Assert.Equal(_root, info.WorkingDirectory, ignoreCase: true);
        Assert.Throws<ArgumentException>(() => _service.CreateShortcut(Path.Combine(_root, "x.txt"), _exe, "", "", _root));
    }

    [Theory]
    [InlineData(@"C:\a\app.exe,3", @"C:\a\app.exe", 3)]
    [InlineData(@"C:\a\app.exe, -101", @"C:\a\app.exe", -101)]
    [InlineData(@"C:\a\app.ico", @"C:\a\app.ico", 0)]
    [InlineData(@"C:\a,b\app.ico", @"C:\a,b\app.ico", 0)]
    [InlineData("", "", 0)]
    public void Parses_icon_location(string location, string path, int index) =>
        Assert.Equal((path, index), ShortcutService.ParseIconLocation(location));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Default_constructor_uses_real_roots_without_touching_them()
    {
        // Construction only resolves paths; nothing is written.
        _ = ShortcutService.CreateDefault();
        _ = new ShortcutService();
    }

    [Theory(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    [InlineData(ApartmentState.STA)]
    [InlineData(ApartmentState.MTA)]
    public void Works_on_sta_and_mta_threads(ApartmentState apartment)
    {
        ShortcutInfo? info = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var created = _service.CreateAppShortcuts(App(), _launcher);
                info = _service.Read(created[0]);
                _service.FindDirectShortcuts(_exe);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(apartment);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal("--app waage", info?.Arguments);
    }
}
