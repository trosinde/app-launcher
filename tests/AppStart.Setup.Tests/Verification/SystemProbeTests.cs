using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Verification;

namespace AppStart.Setup.Tests.Verification;

/// <summary>Real <see cref="SystemProbe"/>, read-only, without elevation. Nothing on the machine is changed.</summary>
public class SystemProbeTests
{
    private readonly SystemProbe _probe = new();

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unknown_service_is_missing() =>
        Assert.False(_probe.GetService("AppStartVerifyTest-" + Guid.NewGuid().ToString("N")).Exists);

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Winmgmt_is_running_automatic_as_localsystem()
    {
        var service = _probe.GetService("Winmgmt");

        Assert.True(service.Exists);
        Assert.Equal(ServiceFacts.StateRunning, service.State);
        Assert.Equal(ServiceFacts.StartTypeAutomatic, service.StartType);
        Assert.Equal("LocalSystem", service.Account, ignoreCase: true);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unknown_pipe_is_missing() =>
        Assert.False(_probe.GetPipe("AppStartVerifyTest-" + Guid.NewGuid().ToString("N")).Exists);

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_created_with_the_D9_sddl_passes_the_pipe_rules()
    {
        // BUILTIN\Users stands in for "AppStart Users" (the test user is a member, so it may read the descriptor).
        var group = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm(PipeRules.BuildSddl(group));
        var name = "AppStartVerifyTest-" + Guid.NewGuid().ToString("N");

        using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message,
            PipeOptions.None, 64, 64, security);

        var pipe = _probe.GetPipe(name);

        Assert.True(pipe.Exists);
        Assert.Null(pipe.Error);
        Assert.Empty(PipeRules.Evaluate(pipe.Descriptor!, group, "Users", s => s.Value));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Pipe_with_group_GRGW_is_reported()
    {
        var group = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;{group.Value})(D;;GA;;;NU)");
        var name = "AppStartVerifyTest-" + Guid.NewGuid().ToString("N");

        using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message,
            PipeOptions.None, 64, 64, security);

        var findings = PipeRules.Evaluate(_probe.GetPipe(name).Descriptor!, group, "Users", s => s.Value);

        Assert.Contains("Eintrag fehlt: Users Verbinden (Maske 0x0012018B, ohne FILE_CREATE_PIPE_INSTANCE)", findings);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Registry_keys_under_hklm_only()
    {
        Assert.True(_probe.RegistryKeyExists(@"HKLM\SOFTWARE\Microsoft"));
        Assert.False(_probe.RegistryKeyExists(@"HKLM\SOFTWARE\AppStartVerifyTest-" + Guid.NewGuid().ToString("N")));
        Assert.False(_probe.RegistryKeyExists(@"HKCU\SOFTWARE"));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Sids_resolve_and_translate()
    {
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var name = _probe.TryTranslateSid(users);

        Assert.NotNull(name);
        Assert.Equal(users, _probe.TryResolveSid(name!));
        Assert.Null(_probe.TryResolveSid("AppStartVerifyTest-" + Guid.NewGuid().ToString("N")));
        Assert.False(string.IsNullOrEmpty(_probe.CurrentUser));
        Assert.True(Directory.Exists(_probe.CommonStartMenuPrograms));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Missing_exe_fails_its_integrity_check()
    {
        var result = _probe.VerifyExecutable(Machine.AppA with { Exe = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") });

        Assert.False(result.Success);
        Assert.Equal("Datei fehlt", result.Detail);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Config_parses_without_acl_check_but_strict_load_rejects_a_temp_folder()
    {
        var dir = Machine.TempDirectory();
        try
        {
            var path = Path.Combine(dir, "apps.json");
            File.WriteAllText(path, AppsConfigSerializer.Serialize(new AppsConfig { Version = 1, Apps = [Machine.AppA] }));

            Assert.True(_probe.ParseConfigWithoutAclCheck(path).IsValid);
            Assert.False(_probe.LoadConfig(path).IsValid); // %TEMP% is writable by the user
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Verifier_with_real_probe_reports_an_uninstalled_machine_without_throwing()
    {
        var dir = Machine.TempDirectory();
        try
        {
            var fakes = new Machine();
            var verifier = new Verifier(fakes.Accounts, fakes.Protection, fakes.Shortcuts, _probe,
                Path.Combine(dir, "AppStart"), Path.Combine(dir, "AppStart.Launcher.exe"), Path.Combine(dir, "apps.json"));

            var report = verifier.Run();

            Assert.Equal(8, report.Checks.Count);
            Assert.Equal(1, report.ExitCode);
            Assert.Equal(Environment.MachineName, report.Computer);
            Assert.False(string.IsNullOrEmpty(report.Version));
            Assert.Equal(CheckOutcome.Deviation, report.Checks.Single(c => c.Id == Verifier.ProgramFolderCheckId).Outcome);
            Assert.Equal(CheckOutcome.Deviation, report.Checks.Single(c => c.Id == Verifier.AppsJsonCheckId).Outcome);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
