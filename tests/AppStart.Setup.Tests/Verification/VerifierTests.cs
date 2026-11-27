using System.Security.AccessControl;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Verification;

namespace AppStart.Setup.Tests.Verification;

// SecurityIdentifier/RawSecurityDescriptor need Windows, hence the skip condition on every test.
public class VerifierTests
{
    private static void AssertDeviation(CheckResult check, string expectedFinding)
    {
        Assert.Equal(CheckOutcome.Deviation, check.Outcome);
        Assert.Contains(expectedFinding, check.Findings);
    }

    private static void AssertPassed(CheckResult check)
    {
        Assert.Empty(check.Findings);
        Assert.Equal(CheckOutcome.Passed, check.Outcome);
    }

    // ------------------------------------------------------------------ Whole run

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Correct_machine_passes_all_ten_checks_with_exit_code_0()
    {
        var report = new Machine().Run();

        Assert.Equal(
            ["dienst", "programmordner", "konten", "adminpasswort", "pipe", "appsjson", "datenordner", "hilfsprogramme", "registry", "verknuepfungen"],
            report.Checks.Select(c => c.Id));
        Assert.Equal(
            ["Service", "Program folder", "Accounts", "Administrators with password", "Pipe", "apps.json", "Data folder per app", "Helper programs per app", "Registry key per app", "Shortcuts"],
            report.Checks.Select(c => c.Title));
        Assert.All(report.Checks, AssertPassed);
        Assert.Equal(0, report.Deviations);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal("LAB-PC-014", report.Computer);
        Assert.Equal(@"LAB-PC-014\techniker", report.Technician);
        Assert.Equal("1.2.3", report.Version);
        Assert.Equal(Machine.Now, report.CreatedAt);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void One_deviation_gives_exit_code_1()
    {
        var machine = new Machine();
        machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] = machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] with { IsLocalAdministrator = true };

        var report = machine.Run();

        Assert.Equal(1, report.Deviations);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Failing_probe_becomes_a_deviation_and_other_checks_still_run()
    {
        var machine = new Machine();
        machine.Probe.ServiceError = new InvalidOperationException("access denied");

        var report = machine.Run();

        AssertDeviation(report.Checks[0], "Verification not possible: access denied");
        Assert.All(report.Checks.Skip(1), AssertPassed);
    }

    // ------------------------------------------------------------------ 1 Service

    [Theory(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    [InlineData("LocalSystem")]
    [InlineData(@"NT AUTHORITY\SYSTEM")]
    public void Service_running_automatic_localsystem_passes(string account)
    {
        var machine = new Machine();
        machine.Probe.Service = machine.Probe.Service with { Account = account };
        AssertPassed(machine.Run("dienst"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Service_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Service = ServiceFacts.Missing;
        AssertDeviation(machine.Run("dienst"), "Service \"AppStart\" is not installed");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Service_stopped_manual_and_wrong_account_gives_three_findings()
    {
        var machine = new Machine();
        machine.Probe.Service = new ServiceFacts(true, 1, 3, @".\appstart-app");

        var check = machine.Run("dienst");

        Assert.Equal(
            [
                "Service \"AppStart\" is not running (status: stopped)",
                "Service \"AppStart\" does not start automatically (start type: manual)",
                @"Service ""AppStart"" runs as "".\appstart-app"" instead of LocalSystem",
            ],
            check.Findings);
    }

    // ------------------------------------------------------------------ 2 Program folder

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Program_folder_writable_by_users_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.WriteViolations[Machine.InstallFolder] = ["BUILTIN\\Users (S-1-5-32-545) has write permissions (mask 0x001301BF)."];

        AssertDeviation(machine.Run("programmordner"),
            @"C:\Program Files\AppStart: BUILTIN\Users (S-1-5-32-545) has write permissions (mask 0x001301BF).");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Program_folder_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Directories.Remove(Machine.InstallFolder);
        AssertDeviation(machine.Run("programmordner"), @"Program folder C:\Program Files\AppStart is missing");
    }

    // ------------------------------------------------------------------ 3 Accounts

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Operator_with_admin_rights_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] = machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] with { IsLocalAdministrator = true };

        AssertDeviation(machine.Run("konten"), @"LAB-PC-014\appstart-user is a member of Administrators");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void App_account_without_deny_rights_and_admin_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts["appstart-app"] = new AccountFacts(true, Machine.AppSid, false, ["SeDenyInteractiveLogonRight"]);

        var check = machine.Run("konten");

        Assert.Equal(
            [
                "appstart-app is not a member of Administrators",
                "appstart-app: right SeDenyRemoteInteractiveLogonRight is missing (deny log on through Remote Desktop Services)",
            ],
            check.Findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void App_account_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts.Remove("appstart-app");
        AssertDeviation(machine.Run("konten"), "Account \"appstart-app\" is missing");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unreadable_operator_group_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.OperatorsError = new InvalidOperationException("group not found");
        AssertDeviation(machine.Run("konten"), "Group \"AppStart Users\" is not readable: group not found");
    }

    // ------------------------------------------------------------------ 4 Pipe

    // ------------------------------------------------------------------ Administrators with password

    // spec: fix-wizard-findings/verification "Admin without password"
    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Admin_without_password_is_a_deviation_with_exit_code_1()
    {
        var machine = new Machine();
        machine.Accounts.AdminsWithoutPassword.Add(new AdminPasswordFinding(@"LAB-PC-014\service", null));

        var report = machine.Run();

        AssertDeviation(report.Checks.Single(c => c.Id == "adminpasswort"),
            @"LAB-PC-014\service is an administrator and has no password. Set a password or disable the account");
        Assert.Equal(1, report.ExitCode);
    }

    // spec: fix-wizard-findings/verification "All admins with password", "Disabled account without password"
    // (the account service only reports enabled accounts)
    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void No_admin_without_password_passes() => AssertPassed(new Machine().Run("adminpasswort"));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Uncheckable_admin_password_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.AdminsWithoutPassword.Add(new AdminPasswordFinding(@"LAB-PC-014\admin2", 1909));

        AssertDeviation(machine.Run("adminpasswort"),
            @"Password of LAB-PC-014\admin2 cannot be verified (Windows error 1909), e.g. because the account is locked out");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_with_mapped_generic_rights_passes()
    {
        // The pipe file system stores GENERIC_ALL as FILE_ALL_ACCESS.
        var machine = new Machine();
        var sddl = $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x12018b;;;{Machine.GroupSid.Value})(D;;FA;;;NU)";
        machine.Probe.Pipe = new PipeFacts(true, new RawSecurityDescriptor(sddl), null);
        AssertPassed(machine.Run("pipe"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Pipe = PipeFacts.Missing;
        AssertDeviation(machine.Run("pipe"), @"\\.\pipe\AppStart does not exist (is the service running?)");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_with_concept_GRGW_and_extra_users_ace_and_no_network_deny_is_a_deviation()
    {
        var machine = new Machine();
        var sddl = $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;{Machine.GroupSid.Value})(A;;GR;;;BU)";
        machine.Probe.Pipe = new PipeFacts(true, new RawSecurityDescriptor(sddl), null);

        var check = machine.Run("pipe");

        Assert.Equal(CheckOutcome.Deviation, check.Outcome);
        Assert.Contains("Access control list of the pipe is not protected (inherits permissions)", check.Findings);
        Assert.Contains("Unexpected entry: Allow for AppStart Users (S-1-5-21-1000-2000-3000-1002) (mask 0xC0000000)", check.Findings);
        Assert.Contains(@"Unexpected entry: Allow for BUILTIN\Users (S-1-5-32-545) (mask 0x80000000)", check.Findings);
        Assert.Contains("Entry missing: AppStart Users connect (mask 0x0012018B, without FILE_CREATE_PIPE_INSTANCE)", check.Findings);
        Assert.Contains("Entry missing: Network logons denied (no remote access)", check.Findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_unreadable_or_group_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Pipe = new PipeFacts(true, null, "All pipe instances are busy.");
        AssertDeviation(machine.Run("pipe"), @"Permissions of \\.\pipe\AppStart are not readable: All pipe instances are busy.");

        machine.Probe.Sids.Remove("AppStart Users");
        AssertDeviation(machine.Run("pipe"), "Group \"AppStart Users\" is missing, permissions of the pipe cannot be verified");
    }

    // ------------------------------------------------------------------ 5 apps.json

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unprotected_apps_json_is_a_deviation_but_per_app_checks_still_run()
    {
        var machine = new Machine();
        var config = machine.Probe.Config;
        machine.Probe.Config = ConfigLoadResult.Fail(@"C:\ProgramData\AppStart\apps.json is not protected: BUILTIN\Users has write permissions.");
        machine.Probe.UncheckedConfig = config;

        var report = machine.Run();

        AssertDeviation(report.Checks.Single(c => c.Id == "appsjson"),
            @"C:\ProgramData\AppStart\apps.json is not protected: BUILTIN\Users has write permissions.");
        AssertPassed(report.Checks.Single(c => c.Id == "datenordner"));
        AssertPassed(report.Checks.Single(c => c.Id == "verknuepfungen"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unreadable_apps_json_fails_the_per_app_checks_too()
    {
        var machine = new Machine();
        machine.Probe.Config = ConfigLoadResult.Fail(@"C:\ProgramData\AppStart\apps.json is missing.");

        var report = machine.Run();

        AssertDeviation(report.Checks.Single(c => c.Id == "appsjson"), @"C:\ProgramData\AppStart\apps.json is missing.");
        foreach (var id in new[] { "datenordner", "registry", "verknuepfungen" })
        {
            AssertDeviation(report.Checks.Single(c => c.Id == id), "apps.json is not readable, check cannot be verified");
        }
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Exe_failing_its_integrity_check_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Integrity[Machine.Exe] = IntegrityResult.Fail("SHA-256 differs: expected AAAA, found BBBB.");

        AssertDeviation(machine.Run("appsjson"),
            @"App A (C:\Program Files\Hersteller\AppA\AppA.exe): SHA-256 differs: expected AAAA, found BBBB.");
    }

    // ------------------------------------------------------------------ 6 Data folder

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Data_dir_is_checked_with_the_app_account_sid()
    {
        var machine = new Machine();
        AssertPassed(machine.Run("datenordner"));
        Assert.Contains((AclTarget.Directory, Machine.DataDir, Machine.AppSid), machine.Protection.Calls);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Data_dir_readable_by_users_is_a_deviation()
    {
        var machine = new Machine();
        machine.Protection.Foreign[Machine.DataDir] = [@"BUILTIN\Users (S-1-5-32-545): Read"];

        AssertDeviation(machine.Run("datenordner"), @"C:\ProgramData\Hersteller\AppA: BUILTIN\Users (S-1-5-32-545): Read");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Data_dir_missing_or_app_account_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Directories.Remove(Machine.DataDir);
        AssertDeviation(machine.Run("datenordner"), @"C:\ProgramData\Hersteller\AppA: folder is missing");

        machine.Accounts.Facts.Remove("appstart-app");
        AssertDeviation(machine.Run("datenordner"), "Account \"appstart-app\" is missing, permissions cannot be verified");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Apps_without_data_dir_or_key_pass_without_account()
    {
        var machine = new Machine();
        machine.Accounts.Facts.Remove("appstart-app");
        machine.Probe.Config = ConfigLoadResult.Ok(new AppsConfig { Version = 1, Apps = [Machine.AppA with { DataDir = null, RegistryKey = null }] });

        AssertPassed(machine.Run("datenordner"));
        AssertPassed(machine.Run("registry"));
    }

    // ------------------------------------------------------------------ 7 Registry

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Registry_key_is_checked_with_the_app_account_sid()
    {
        var machine = new Machine();
        AssertPassed(machine.Run("registry"));
        Assert.Contains((AclTarget.RegistryKey, Machine.RegistryKey, Machine.AppSid), machine.Protection.Calls);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Registry_key_readable_by_operators_is_a_deviation()
    {
        var machine = new Machine();
        machine.Protection.Foreign[Machine.RegistryKey] = [@"LAB-PC-014\AppStart Users (S-1-5-21-1000-2000-3000-1002): Read"];

        AssertDeviation(machine.Run("registry"),
            @"HKLM\SOFTWARE\Hersteller\AppA: LAB-PC-014\AppStart Users (S-1-5-21-1000-2000-3000-1002): Read");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Registry_key_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.RegistryKeys.Clear();
        AssertDeviation(machine.Run("registry"), @"HKLM\SOFTWARE\Hersteller\AppA: key is missing");
    }

    // ------------------------------------------------------------------ 8 Shortcuts

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Desktop_shortcut_alone_is_enough()
    {
        var machine = new Machine();
        machine.Probe.Files.Remove(Machine.StartMenuShortcut);
        AssertPassed(machine.Run("verknuepfungen"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Missing_shortcuts_are_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Files.Clear();
        AssertDeviation(machine.Run("verknuepfungen"),
            "App A: no shortcut \"App A.lnk\" to the launcher in the public Start menu or on the public desktop");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Shortcut_with_wrong_target_or_arguments_is_a_deviation()
    {
        var machine = new Machine();
        machine.Shortcuts.Files[Machine.StartMenuShortcut] = new ShortcutInfo(Machine.StartMenuShortcut, Machine.Exe, "", Machine.Exe, "");
        machine.Shortcuts.Files[Machine.DesktopShortcut] = new ShortcutInfo(Machine.DesktopShortcut, Machine.Launcher, "--app app-b", "", "");

        var check = machine.Run("verknuepfungen");

        Assert.Equal(
            [
                @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\App A.lnk: points to ""C:\Program Files\Hersteller\AppA\AppA.exe "" instead of the launcher with --app app-a",
                @"C:\Users\Public\Desktop\App A.lnk: points to ""C:\Program Files\AppStart\current\AppStart.Launcher.exe --app app-b"" instead of the launcher with --app app-a",
                "App A: no shortcut \"App A.lnk\" to the launcher in the public Start menu or on the public desktop",
            ],
            check.Findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Direct_shortcut_to_the_app_exe_is_a_deviation()
    {
        var machine = new Machine();
        machine.Shortcuts.Direct[Machine.Exe] = [@"C:\Users\anna\Desktop\AppA.lnk"];

        AssertDeviation(machine.Run("verknuepfungen"),
            @"C:\Users\anna\Desktop\AppA.lnk: points directly to C:\Program Files\Hersteller\AppA\AppA.exe");
    }
}
