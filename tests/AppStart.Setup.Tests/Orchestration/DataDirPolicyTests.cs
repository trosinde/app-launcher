using AppStart.Setup.Engine;

namespace AppStart.Setup.Tests.Orchestration;

// spec: fix-wizard-findings/installation «Datenordner gleich Programmordner», «Datenordner ausserhalb»
public class DataDirPolicyTests
{
    private const string Exe = @"C:\Program Files\Hersteller\AppA\AppA.exe";

    [Theory(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    [InlineData(@"C:\Program Files\Hersteller\AppA")]         // same
    [InlineData(@"C:\Program Files\Hersteller\AppA\")]        // trailing backslash
    [InlineData(@"c:\program files\hersteller\appa")]         // case
    [InlineData(@"C:\Program Files\Hersteller\AppA\Daten")]   // below
    [InlineData(@"C:\Program Files\Hersteller")]              // contains it
    [InlineData(@" C:\Program Files\Hersteller\AppA\Daten ")] // as typed, with blanks
    public void Program_folder_and_its_relatives_overlap(string dataDir) =>
        Assert.True(DataDirPolicy.OverlapsProgramFolder(dataDir, Exe));

    [Theory(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    [InlineData(@"C:\ProgramData\Hersteller\AppA")]
    [InlineData(@"C:\Program Files\Hersteller\AppA2")]        // same prefix, other folder
    [InlineData(@"C:\Program Files\Hersteller\AppB")]
    [InlineData(@"D:\Program Files\Hersteller\AppA")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"relative\folder")]
    public void Other_folders_do_not_overlap(string? dataDir) =>
        Assert.False(DataDirPolicy.OverlapsProgramFolder(dataDir, Exe));
}
