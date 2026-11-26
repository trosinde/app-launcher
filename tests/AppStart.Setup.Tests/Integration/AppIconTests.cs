namespace AppStart.Setup.Tests.Integration;

/// <summary>
/// spec: fix-wizard-findings/installation «Startmenü zeigt das AppStart-Icon», «Applikationen behalten ihr Icon».
/// The Start menu entries point to AppStart.Setup.exe,0 (Cli/StartMenu.cs), so the EXEs must carry the AppStart
/// icon; app shortcuts use the icon of the app EXE (ShortcutService), never the launcher's.
/// Checks the repository sources; skipped when the tests run outside the repository (published test EXE).
/// </summary>
public class AppIconTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AppStart.slnx")))
            {
                return dir.FullName;
            }
        }

        Assert.Skip("Nur im Repository (AppStart.slnx nicht gefunden).");
        return string.Empty;
    }

    [Fact]
    public void Icon_file_has_all_sizes_windows_uses()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "assets", "icon", "AppStart.ico"));

        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // type icon
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + (i * 16)] is 0 ? 256 : bytes[6 + (i * 16)]).Order().ToList();
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], sizes);
    }

    [Theory]
    [InlineData("AppStart.Setup")]
    [InlineData("AppStart.Service")]
    [InlineData("AppStart.Launcher")]
    [InlineData("AppStart.Bootstrapper")]
    public void AppStart_programs_carry_the_icon(string project)
    {
        var root = RepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "src", project, project + ".csproj"));
        Assert.Contains("<ApplicationIcon>$(AppStartIcon)</ApplicationIcon>", csproj, StringComparison.Ordinal);
        Assert.Contains(@"assets\icon\AppStart.ico", File.ReadAllText(Path.Combine(root, "Directory.Build.props")), StringComparison.Ordinal);
    }

    [Fact]
    public void Release_passes_the_icon_to_the_installer()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "build", "Build-Release.ps1"));
        Assert.Contains("--icon (Join-Path $root 'assets/icon/AppStart.ico')", script, StringComparison.Ordinal);
    }
}
