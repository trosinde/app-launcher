using System.Security.AccessControl;
using AppStart.Core.Security;

namespace AppStart.Bootstrapper.Tests;

public class BootstrapperPathsTests
{
    private static readonly BootstrapperPaths Paths = new(
        @"C:\ProgramData\AppStart",
        @"C:\Program Files",
        @"C:\Windows",
        @"C:\Users\op\AppData\Local\Temp\",
        @"C:\Users\op\Downloads");

    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Extraction_folder_is_below_programdata_installer() =>
        Assert.Equal(@"C:\ProgramData\AppStart\Installer\11111111-2222-3333-4444-555555555555", Paths.ExtractionDirectory(Id));

    [Fact]
    public void Extracted_msi_lies_in_the_extraction_folder() =>
        Assert.Equal(@"C:\ProgramData\AppStart\Installer\11111111-2222-3333-4444-555555555555\AppStart.msi", Paths.ExtractedMsi(Id));

    [Fact]
    public void Generated_extraction_folder_is_safe() =>
        Assert.True(Paths.IsSafeExtractionPath(Paths.ExtractionDirectory(Guid.NewGuid())));

    [Theory]
    [InlineData(@"C:\Users\op\AppData\Local\Temp\x")]
    [InlineData(@"C:\Users\op\AppData\Local\Temp")]
    [InlineData(@"C:\Users\op\Downloads\x")]
    [InlineData(@"C:\ProgramData\AppStart\Installer")]
    [InlineData(@"C:\ProgramData\AppStart\Logs\x")]
    [InlineData(@"C:\ProgramData\AppStart\Installer\..\..\Temp\x")]
    [InlineData(@"C:\ProgramData\AppStartX\Installer\x")]
    [InlineData(@"C:\ProgramData\x")]
    [InlineData(@"Installer\x")]
    [InlineData(@"\\server\share\AppStart\Installer\x")]
    [InlineData("")]
    public void Other_extraction_paths_are_rejected(string path) =>
        Assert.False(Paths.IsSafeExtractionPath(path));

    [Fact]
    public void Extraction_below_temp_is_rejected_even_when_temp_points_into_programdata()
    {
        var odd = new BootstrapperPaths(@"C:\ProgramData\AppStart", @"C:\Program Files", @"C:\Windows", @"C:\ProgramData\AppStart\Installer", null);
        Assert.False(odd.IsSafeExtractionPath(odd.ExtractionDirectory(Id)));
    }

    [Fact]
    public void Real_machine_extraction_path_is_not_in_temp()
    {
        var machine = BootstrapperPaths.ForMachine();
        var dir = machine.ExtractionDirectory(Id);
        Assert.True(machine.IsSafeExtractionPath(dir));
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), dir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logs_are_kept_in_programdata() =>
        Assert.Equal(@"C:\ProgramData\AppStart\Logs\install-20261115-105100.log", Paths.InstallLog(new DateTime(2026, 11, 15, 10, 51, 0)));

    [Fact]
    public void Msiexec_comes_from_system32_with_full_path() =>
        Assert.Equal(@"C:\Windows\System32\msiexec.exe", Paths.Msiexec);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Users\op\Downloads\AppStart")]
    [InlineData(@"C:\ProgramData\AppStart")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\..\Temp")]
    [InlineData(@"AppStart")]
    [InlineData(@"C:\Program Files (x86)\AppStart")]
    public void Install_root_outside_program_files_falls_back_to_default(string? reported) =>
        Assert.Equal(@"C:\Program Files\AppStart", Paths.ResolveInstallRoot(reported));

    [Theory]
    [InlineData(@"C:\Program Files\AppStart")]
    [InlineData(@"C:\Program Files\AppStart\")]
    [InlineData(@"""C:\Program Files\AppStart""")]
    public void Install_root_below_program_files_is_used(string reported) =>
        Assert.Equal(@"C:\Program Files\AppStart", Paths.ResolveInstallRoot(reported));

    [Fact]
    public void Setup_is_started_from_current_folder() =>
        Assert.Equal(@"C:\Program Files\AppStart\current\AppStart.Setup.exe", BootstrapperPaths.SetupExecutable(@"C:\Program Files\AppStart"));

    [Fact]
    public void Protected_folder_descriptor_allows_only_system_and_administrators()
    {
        var descriptor = new RawSecurityDescriptor(BootstrapperPaths.ProtectedFolderSddl);
        Assert.Empty(WriteAccessPolicy.FindViolations(descriptor, isDirectory: true));
        Assert.Equal(WriteAccessPolicy.Administrators, descriptor.Owner);
        Assert.True((descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0);
        Assert.Equal(2, descriptor.DiscretionaryAcl!.Count);
    }
}
