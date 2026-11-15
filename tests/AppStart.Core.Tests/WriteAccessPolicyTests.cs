using System.Security.AccessControl;
using AppStart.Core.Security;

namespace AppStart.Core.Tests;

public class WriteAccessPolicyTests
{
    // Owner Administrators, protected DACL: SYSTEM and Administrators full, Users read.
    private const string Protected = "O:BAG:SYD:PAI(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)";

    private static IReadOnlyList<string> Check(string sddl, bool dir = false) =>
        WriteAccessPolicy.FindViolations(new RawSecurityDescriptor(sddl), dir);

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Protected_file_has_no_violations() => Assert.Empty(Check(Protected));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void TrustedInstaller_and_inherit_only_creator_owner_are_fine() =>
        Assert.Empty(Check("O:SYG:SYD:PAI(A;;FA;;;SY)(A;OICIIO;GA;;;CO)(A;;FA;;;S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464)", dir: true));

    [Theory(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    [InlineData("(A;;FW;;;BU)")]          // Users write
    [InlineData("(A;;0x1301bf;;;AU)")]    // Authenticated Users modify
    [InlineData("(A;;FA;;;WD)")]          // Everyone full
    [InlineData("(A;;0x40000;;;BU)")]     // Users WRITE_DAC
    [InlineData("(A;;0x10000;;;BU)")]     // Users DELETE
    [InlineData("(A;;GW;;;IU)")]          // Interactive GENERIC_WRITE
    public void Write_for_non_admins_is_a_violation(string ace) =>
        Assert.Single(Check(Protected + ace));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Folder_add_file_for_users_is_a_violation() =>
        Assert.Single(Check(Protected + "(A;;0x2;;;BU)", dir: true));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Deny_aces_are_ignored() => Assert.Empty(Check(Protected + "(D;;FA;;;BU)"));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Untrusted_owner_is_a_violation() =>
        Assert.Single(Check("O:BUG:SYD:PAI(A;;FA;;;SY)(A;;FA;;;BA)"));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Null_dacl_is_a_violation() =>
        Assert.Contains(Check("O:BAG:SYD:NO_ACCESS_CONTROL"), v => v.Contains("NULL DACL", StringComparison.Ordinal));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Real_file_in_temp_is_writable_by_current_user()
    {
        var file = Path.GetTempFileName();
        try
        {
            // %TEMP% belongs to the current (non-system) user: must be reported.
            Assert.NotEmpty(WriteAccessPolicy.FindViolations(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
