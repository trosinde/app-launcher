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
    public void Correct_machine_passes_all_nine_checks_with_exit_code_0()
    {
        var report = new Machine().Run();

        Assert.Equal(
            ["dienst", "programmordner", "konten", "pipe", "appsjson", "datenordner", "hilfsprogramme", "registry", "verknuepfungen"],
            report.Checks.Select(c => c.Id));
        Assert.Equal(
            ["Dienst", "Programmordner", "Konten", "Pipe", "apps.json", "Datenordner je App", "Hilfsprogramme je App", "Registry-Key je App", "Verknüpfungen"],
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
        machine.Probe.ServiceError = new InvalidOperationException("Zugriff verweigert");

        var report = machine.Run();

        AssertDeviation(report.Checks[0], "Prüfung nicht möglich: Zugriff verweigert");
        Assert.All(report.Checks.Skip(1), AssertPassed);
    }

    // ------------------------------------------------------------------ 1 Dienst

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
        AssertDeviation(machine.Run("dienst"), "Dienst «AppStart» ist nicht installiert");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Service_stopped_manual_and_wrong_account_gives_three_findings()
    {
        var machine = new Machine();
        machine.Probe.Service = new ServiceFacts(true, 1, 3, @".\appstart-app");

        var check = machine.Run("dienst");

        Assert.Equal(
            [
                "Dienst «AppStart» läuft nicht (Status: beendet)",
                "Dienst «AppStart» startet nicht automatisch (Starttyp: manuell)",
                @"Dienst «AppStart» läuft unter «.\appstart-app» statt LocalSystem",
            ],
            check.Findings);
    }

    // ------------------------------------------------------------------ 2 Programmordner

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Program_folder_writable_by_users_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.WriteViolations[Machine.InstallFolder] = ["BUILTIN\\Users (S-1-5-32-545) hat Schreibrechte (Maske 0x001301BF)."];

        AssertDeviation(machine.Run("programmordner"),
            @"C:\Program Files\AppStart: BUILTIN\Users (S-1-5-32-545) hat Schreibrechte (Maske 0x001301BF).");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Program_folder_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Directories.Remove(Machine.InstallFolder);
        AssertDeviation(machine.Run("programmordner"), @"Programmordner C:\Program Files\AppStart fehlt");
    }

    // ------------------------------------------------------------------ 3 Konten

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Operator_with_admin_rights_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] = machine.Accounts.Facts[@"LAB-PC-014\appstart-user"] with { IsLocalAdministrator = true };

        AssertDeviation(machine.Run("konten"), @"LAB-PC-014\appstart-user ist Mitglied der Administratoren");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void App_account_without_deny_rights_and_admin_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts["appstart-app"] = new AccountFacts(true, Machine.AppSid, false, ["SeDenyInteractiveLogonRight"]);

        var check = machine.Run("konten");

        Assert.Equal(
            [
                "appstart-app ist nicht Mitglied der Administratoren",
                "appstart-app: Recht SeDenyRemoteInteractiveLogonRight fehlt (Anmeldung über Remotedesktop verweigern)",
            ],
            check.Findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void App_account_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.Facts.Remove("appstart-app");
        AssertDeviation(machine.Run("konten"), "Konto «appstart-app» fehlt");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unreadable_operator_group_is_a_deviation()
    {
        var machine = new Machine();
        machine.Accounts.OperatorsError = new InvalidOperationException("Gruppe nicht gefunden");
        AssertDeviation(machine.Run("konten"), "Gruppe «AppStart Users» ist nicht lesbar: Gruppe nicht gefunden");
    }

    // ------------------------------------------------------------------ 4 Pipe

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
        AssertDeviation(machine.Run("pipe"), @"\\.\pipe\AppStart existiert nicht (läuft der Dienst?)");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_with_concept_GRGW_and_extra_users_ace_and_no_network_deny_is_a_deviation()
    {
        var machine = new Machine();
        var sddl = $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;{Machine.GroupSid.Value})(A;;GR;;;BU)";
        machine.Probe.Pipe = new PipeFacts(true, new RawSecurityDescriptor(sddl), null);

        var check = machine.Run("pipe");

        Assert.Equal(CheckOutcome.Deviation, check.Outcome);
        Assert.Contains("Zugriffsliste der Pipe ist nicht geschützt (erbt Rechte)", check.Findings);
        Assert.Contains("Unerwarteter Eintrag: Erlaubt für AppStart Users (S-1-5-21-1000-2000-3000-1002) (Maske 0xC0000000)", check.Findings);
        Assert.Contains(@"Unerwarteter Eintrag: Erlaubt für BUILTIN\Users (S-1-5-32-545) (Maske 0x80000000)", check.Findings);
        Assert.Contains("Eintrag fehlt: AppStart Users Verbinden (Maske 0x0012018B, ohne FILE_CREATE_PIPE_INSTANCE)", check.Findings);
        Assert.Contains("Eintrag fehlt: Netzwerk-Anmeldungen verweigert (keine Fernzugriffe)", check.Findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_unreadable_or_group_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Pipe = new PipeFacts(true, null, "Alle Pipeinstanzen sind ausgelastet.");
        AssertDeviation(machine.Run("pipe"), @"Rechte von \\.\pipe\AppStart sind nicht lesbar: Alle Pipeinstanzen sind ausgelastet.");

        machine.Probe.Sids.Remove("AppStart Users");
        AssertDeviation(machine.Run("pipe"), "Gruppe «AppStart Users» fehlt, Rechte der Pipe können nicht geprüft werden");
    }

    // ------------------------------------------------------------------ 5 apps.json

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unprotected_apps_json_is_a_deviation_but_per_app_checks_still_run()
    {
        var machine = new Machine();
        var config = machine.Probe.Config;
        machine.Probe.Config = ConfigLoadResult.Fail(@"C:\ProgramData\AppStart\apps.json ist nicht geschützt: BUILTIN\Users hat Schreibrechte.");
        machine.Probe.UncheckedConfig = config;

        var report = machine.Run();

        AssertDeviation(report.Checks.Single(c => c.Id == "appsjson"),
            @"C:\ProgramData\AppStart\apps.json ist nicht geschützt: BUILTIN\Users hat Schreibrechte.");
        AssertPassed(report.Checks.Single(c => c.Id == "datenordner"));
        AssertPassed(report.Checks.Single(c => c.Id == "verknuepfungen"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unreadable_apps_json_fails_the_per_app_checks_too()
    {
        var machine = new Machine();
        machine.Probe.Config = ConfigLoadResult.Fail(@"C:\ProgramData\AppStart\apps.json fehlt.");

        var report = machine.Run();

        AssertDeviation(report.Checks.Single(c => c.Id == "appsjson"), @"C:\ProgramData\AppStart\apps.json fehlt.");
        foreach (var id in new[] { "datenordner", "registry", "verknuepfungen" })
        {
            AssertDeviation(report.Checks.Single(c => c.Id == id), "apps.json ist nicht lesbar, Prüfpunkt kann nicht geprüft werden");
        }
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Exe_failing_its_integrity_check_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Integrity[Machine.Exe] = IntegrityResult.Fail("SHA-256 weicht ab: erwartet AAAA, gefunden BBBB.");

        AssertDeviation(machine.Run("appsjson"),
            @"App A (C:\Program Files\Hersteller\AppA\AppA.exe): SHA-256 weicht ab: erwartet AAAA, gefunden BBBB.");
    }

    // ------------------------------------------------------------------ 6 Datenordner

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
        machine.Protection.Foreign[Machine.DataDir] = [@"BUILTIN\Users (S-1-5-32-545): Lesen"];

        AssertDeviation(machine.Run("datenordner"), @"C:\ProgramData\Hersteller\AppA: BUILTIN\Users (S-1-5-32-545): Lesen");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Data_dir_missing_or_app_account_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.Directories.Remove(Machine.DataDir);
        AssertDeviation(machine.Run("datenordner"), @"C:\ProgramData\Hersteller\AppA: Ordner fehlt");

        machine.Accounts.Facts.Remove("appstart-app");
        AssertDeviation(machine.Run("datenordner"), "Konto «appstart-app» fehlt, Rechte können nicht geprüft werden");
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
        machine.Protection.Foreign[Machine.RegistryKey] = [@"LAB-PC-014\AppStart Users (S-1-5-21-1000-2000-3000-1002): Lesen"];

        AssertDeviation(machine.Run("registry"),
            @"HKLM\SOFTWARE\Hersteller\AppA: LAB-PC-014\AppStart Users (S-1-5-21-1000-2000-3000-1002): Lesen");
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Registry_key_missing_is_a_deviation()
    {
        var machine = new Machine();
        machine.Probe.RegistryKeys.Clear();
        AssertDeviation(machine.Run("registry"), @"HKLM\SOFTWARE\Hersteller\AppA: Schlüssel fehlt");
    }

    // ------------------------------------------------------------------ 8 Verknüpfungen

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
            "App A: keine Verknüpfung «App A.lnk» auf den Launcher im öffentlichen Startmenü oder auf dem öffentlichen Desktop");
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
                @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\App A.lnk: zeigt auf «C:\Program Files\Hersteller\AppA\AppA.exe » statt auf den Launcher mit --app app-a",
                @"C:\Users\Public\Desktop\App A.lnk: zeigt auf «C:\Program Files\AppStart\current\AppStart.Launcher.exe --app app-b» statt auf den Launcher mit --app app-a",
                "App A: keine Verknüpfung «App A.lnk» auf den Launcher im öffentlichen Startmenü oder auf dem öffentlichen Desktop",
            ],
            check.Findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Direct_shortcut_to_the_app_exe_is_a_deviation()
    {
        var machine = new Machine();
        machine.Shortcuts.Direct[Machine.Exe] = [@"C:\Users\anna\Desktop\AppA.lnk"];

        AssertDeviation(machine.Run("verknuepfungen"),
            @"C:\Users\anna\Desktop\AppA.lnk: zeigt direkt auf C:\Program Files\Hersteller\AppA\AppA.exe");
    }
}
