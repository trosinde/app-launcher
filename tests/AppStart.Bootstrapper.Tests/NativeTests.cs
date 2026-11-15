using System.Runtime.Versioning;

namespace AppStart.Bootstrapper.Tests;

/// <summary>
/// Windows-only, non-elevated checks of the native parts. They never install, never write below
/// ProgramData or Program Files and never start a process (only the refusal paths are exercised).
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class NativeTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "AppStart.Bootstrapper.Tests", Guid.NewGuid().ToString("N"));

    public NativeTests() => Directory.CreateDirectory(scratch);

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    public void Test_build_has_no_embedded_package()
    {
        using var stream = MsiPackage.OpenEmbedded();
        Assert.Null(stream);
    }

    [Fact]
    public void Existing_folder_owned_by_a_user_is_rejected()
    {
        // The folder was created by the (non-admin) test user: owner and ACL are not trusted.
        var ex = Assert.Throws<UnsafeFolderException>(() => ProtectedFolders.Ensure(scratch));
        Assert.Equal(scratch, ex.Folder);
    }

    [Fact]
    public void New_folder_must_not_exist_yet() =>
        Assert.Throws<IOException>(() => ProtectedFolders.CreateNew(scratch));

    [Fact]
    public void Extract_writes_the_stream_and_never_overwrites()
    {
        var target = Path.Combine(scratch, "AppStart.msi");
        using (var source = new MemoryStream([1, 2, 3, 4]))
        {
            MsiPackage.Extract(source, target);
        }

        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(target));
        using var again = new MemoryStream([9]);
        Assert.Throws<IOException>(() => MsiPackage.Extract(again, target));
    }

    [Fact]
    public void Non_msi_file_yields_no_version()
    {
        var file = Path.Combine(scratch, "not.msi");
        File.WriteAllText(file, "no database");
        Assert.Equal(new MsiInfo(null, null), MsiPackage.ReadInfo(file));
    }

    [Fact]
    public void Unknown_upgrade_code_is_not_installed()
    {
        Assert.Null(MsiPackage.FindInstalled("{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}"));
        Assert.Null(MsiPackage.FindInstalled(null));
    }

    [Fact]
    public void Missing_setup_is_reported_not_started()
    {
        using var log = new BootstrapLog();
        var result = SetupStarter.VerifyAndStart(Path.Combine(scratch, "current", "AppStart.Setup.exe"), "AB12", log);
        Assert.Equal(SetupStartStatus.Missing, result.Status);
    }

    [Fact]
    public void Unsigned_setup_is_refused_when_bootstrapper_is_signed()
    {
        var fake = Path.Combine(scratch, "AppStart.Setup.exe");
        File.WriteAllText(fake, "not an executable");
        using var log = new BootstrapLog();
        var result = SetupStarter.VerifyAndStart(fake, "0000000000000000000000000000000000000000", log);
        Assert.Equal(SetupStartStatus.Untrusted, result.Status);
    }

    [Fact]
    public void Install_location_resolves_below_program_files()
    {
        var paths = BootstrapperPaths.ForMachine();
        var setup = SetupStarter.LocateSetup(paths, new InstalledProduct("{X}", "0.1.0", @"C:\Users\Public\AppStart"));
        Assert.Equal(BootstrapperPaths.SetupExecutable(paths.DefaultInstallRoot), setup);
    }

    [Fact]
    public void Log_buffers_until_opened()
    {
        var file = Path.Combine(scratch, "bootstrapper.log");
        using (var log = new BootstrapLog())
        {
            log.Write("first");
            log.Open(file);
            log.Write("second");
        }

        var lines = File.ReadAllLines(file);
        Assert.EndsWith("first", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("second", lines[1], StringComparison.Ordinal);
    }
}
