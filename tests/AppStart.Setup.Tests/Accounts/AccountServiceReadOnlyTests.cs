using System.Security.Principal;
using AppStart.Setup.Engine.Accounts;
using Windows.Win32.Security;

namespace AppStart.Setup.Tests.Accounts;

/// <summary>
/// Read-only calls against the real system, also without admin rights. Nothing here creates, changes or deletes
/// accounts, groups or rights; those paths are tested on the test VM only.
/// </summary>
public class AccountServiceReadOnlyTests
{
    [Fact]
    public void Denied_rights_are_interactive_and_rdp_but_never_batch()
    {
        Assert.Equal(["SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight"], AccountService.AppAccountDeniedRights);
        Assert.DoesNotContain(AccountService.AppAccountDeniedRights, r => r.Contains("Batch", StringComparison.Ordinal));
    }

    // spec: fix-wizard-findings/verification «Admin ohne Passwort», «Alle Admins mit Passwort»
    [Theory]
    [InlineData(0u, "Empty")]
    [InlineData(1327u, "Empty")] // blank password limited to console logon
    [InlineData(1385u, "Empty")] // logon type not granted
    [InlineData(1330u, "Empty")] // password expired
    [InlineData(1907u, "Empty")] // password must change
    [InlineData(1326u, "HasPassword")]
    [InlineData(1909u, "Unknown")] // locked out
    [InlineData(5u, "Unknown")]
    public void Empty_password_logon_result_is_classified(uint error, string expected) =>
        Assert.Equal(expected, AccountService.ClassifyEmptyPasswordLogon(error).ToString());

    [Fact]
    public void Builtin_groups_are_addressed_by_sid()
    {
        Assert.Equal("S-1-5-32-544", AccountService.AdministratorsSid.Value);
        Assert.Equal("S-1-5-32-545", AccountService.UsersSid.Value);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unknown_account_does_not_exist()
    {
        var facts = new AccountService().GetFacts("appstart-test-" + Guid.NewGuid().ToString("N")[..6]);

        Assert.False(facts.Exists);
        Assert.Null(facts.Sid);
        Assert.False(facts.IsLocalAdministrator);
        Assert.Empty(facts.AccountRights);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Current_user_admin_state_matches_the_token()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var resolved = new AccountService().Resolve(identity.User!.Value);

        Assert.NotNull(resolved);
        Assert.Equal(identity.User, resolved.Sid);
        Assert.Equal(SID_NAME_USE.SidTypeUser, resolved.Use);

        // A UAC-filtered admin has Administrators only as deny-only SID in the token, but is a local administrator.
        // Expectation: enabled in the token, or direct member of the group (no domain nesting on the dev PC).
        var administratorsName = AccountService.AdministratorsSid.Translate(typeof(NTAccount)).Value;
        var directMember = (LocalAccountsNative.GetMembers(administratorsName[(administratorsName.LastIndexOf('\\') + 1)..]) ?? [])
            .Any(m => m.Sid == identity.User);
        var expected = directMember || identity.Groups!.Contains(AccountService.AdministratorsSid);
        Assert.Equal(expected, AccountService.IsLocalAdministrator(resolved));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Current_user_is_found_by_name()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var resolved = new AccountService().Resolve(identity.Name);

        Assert.NotNull(resolved);
        Assert.Equal(identity.User, resolved.Sid);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Builtin_groups_resolve_by_sid_regardless_of_language()
    {
        var service = new AccountService();
        var administrators = service.Resolve("S-1-5-32-544");
        var users = service.Resolve("S-1-5-32-545");

        Assert.NotNull(administrators);
        Assert.NotNull(users);
        Assert.Equal(SID_NAME_USE.SidTypeAlias, administrators.Use);
        Assert.True(AccountService.IsLocalAdministrator(administrators));
        Assert.False(AccountService.IsLocalAdministrator(users));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Builtin_administrator_account_is_administrator()
    {
        // RID 500 of this computer: the built-in administrator (may be disabled, still a member).
        var service = new AccountService();
        var machineSid = FindMachineSid();
        Assert.SkipWhen(machineSid is null, "Computer-SID nicht ermittelbar.");

        var administrator = service.Resolve(new SecurityIdentifier(WellKnownSidType.AccountAdministratorSid, machineSid).Value);

        Assert.NotNull(administrator);
        Assert.True(AccountService.IsLocalAdministrator(administrator));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Facts_are_correct_or_access_denied_in_German()
    {
        // Elevated, the facts are always readable. Not elevated, it depends on the Windows build: on the earlier dev
        // machine LsaOpenPolicy was denied; on build 26300 (test VM) the call succeeds without elevation.
        // Either way: correct facts, or a clean German access-denied error, never anything else.
        using var identity = WindowsIdentity.GetCurrent();
        AppStart.Setup.Engine.AccountFacts facts;
        try
        {
            facts = new AccountService().GetFacts(identity.User!.Value);
        }
        catch (UnauthorizedAccessException ex) when (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Assert.Contains("Zugriff verweigert", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Administratorrechte", ex.Message, StringComparison.Ordinal);
            return;
        }

        Assert.True(facts.Exists);
        Assert.Equal(identity.User, facts.Sid);
    }

    [Fact]
    public void Access_denied_says_admin_rights_are_needed_in_German()
    {
        var win32 = AccountErrors.FromWin32((uint)Windows.Win32.Foundation.WIN32_ERROR.ERROR_ACCESS_DENIED, "Konto lesen");
        var ntStatus = AccountErrors.FromNtStatus(new Windows.Win32.Foundation.NTSTATUS(unchecked((int)0xC0000022)), "Lokale Sicherheitsrichtlinie öffnen");

        foreach (var ex in new[] { win32, ntStatus })
        {
            var denied = Assert.IsType<UnauthorizedAccessException>(ex);
            Assert.Contains("Zugriff verweigert", denied.Message, StringComparison.Ordinal);
            Assert.Contains("Administratorrechte", denied.Message, StringComparison.Ordinal);
        }
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Operators_can_be_listed_without_admin_rights()
    {
        var operators = new AccountService().GetOperators();

        Assert.NotNull(operators);
        Assert.All(operators, o => Assert.False(string.IsNullOrWhiteSpace(o)));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Invalid_name_is_rejected_before_any_system_call()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AccountService().GetFacts("a\\b\\c"));
        Assert.Contains("Ungültiger Kontoname", ex.Message, StringComparison.Ordinal);
    }

    // Domain SID of this computer, from a local member of the Administrators or Users group.
    private static SecurityIdentifier? FindMachineSid()
    {
        foreach (var group in new[] { "S-1-5-32-544", "S-1-5-32-545" })
        {
            var name = new SecurityIdentifier(group).Translate(typeof(NTAccount)).Value;
            var members = LocalAccountsNative.GetMembers(name[(name.LastIndexOf('\\') + 1)..]) ?? [];
            foreach (var member in members)
            {
                if (member.Qualified?.StartsWith(Environment.MachineName + "\\", StringComparison.OrdinalIgnoreCase) == true
                    && member.Sid.AccountDomainSid is { } domainSid)
                {
                    return domainSid;
                }
            }
        }

        return null;
    }
}
