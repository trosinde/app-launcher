using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Verification;

namespace AppStart.Setup.Tests.Verification;

internal sealed class FakeAccounts : IAccountService
{
    public Dictionary<string, AccountFacts> Facts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Operators { get; } = [];

    public Exception? OperatorsError { get; set; }

    public AccountFacts GetFacts(string accountName) =>
        Facts.TryGetValue(accountName, out var facts) ? facts : new AccountFacts(false, null, false, []);

    public IReadOnlyList<string> GetOperators() => OperatorsError is null ? Operators : throw OperatorsError;

    public SecurityIdentifier EnsureAppAccount() => throw new NotSupportedException();

    public SecurityIdentifier EnsureOperatorGroup() => throw new NotSupportedException();

    public bool EnsureOperatorAccount(SecureString password) => throw new NotSupportedException();

    public bool AddOperator(string account) => throw new NotSupportedException();

    public bool RemoveOperator(string account) => throw new NotSupportedException();

    public void RemoveAppAccount() => throw new NotSupportedException();

    public void RemoveOperatorAccount() => throw new NotSupportedException();

    public List<AdminPasswordFinding> AdminsWithoutPassword { get; } = [];

    public IReadOnlyList<AdminPasswordFinding> FindAdministratorsWithoutPassword() => AdminsWithoutPassword;

    public void RemoveOperatorGroup() => throw new NotSupportedException();
}

internal sealed class FakeProtection : IProtectionService
{
    public Dictionary<string, List<string>> Foreign { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(AclTarget Target, string Path, SecurityIdentifier Sid)> Calls { get; } = [];

    public IReadOnlyList<string> FindForeignAccess(AclTarget target, string path, SecurityIdentifier appAccount)
    {
        Calls.Add((target, path, appAccount));
        return Foreign.TryGetValue(path, out var list) ? list : [];
    }

    public void ProtectConfigDirectory() => throw new NotSupportedException();

    public void WriteProtectedFile(string path, string content) => throw new NotSupportedException();

    public AclBackup ProtectDataDirectory(string path, SecurityIdentifier appAccount, string appId) => throw new NotSupportedException();

    public AclBackup ProtectRegistryKey(string key, SecurityIdentifier appAccount, string appId) => throw new NotSupportedException();

    public void Restore(AclBackup backup) => throw new NotSupportedException();
}

internal sealed class FakeShortcuts : IShortcutService
{
    public Dictionary<string, ShortcutInfo?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> Direct { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ShortcutInfo? Read(string path) => Files.TryGetValue(path, out var info) ? info : null;

    public IReadOnlyList<string> FindDirectShortcuts(string exePath) => Direct.TryGetValue(exePath, out var list) ? list : [];

    public IReadOnlyList<string> CreateAppShortcuts(AppEntry app, string launcherPath) => throw new NotSupportedException();

    public ShortcutBackup BackupAndRemove(string shortcutPath, string appId) => throw new NotSupportedException();

    public void Restore(ShortcutBackup backup) => throw new NotSupportedException();

    public void Delete(IEnumerable<string> paths) => throw new NotSupportedException();
}

internal sealed class FakeProbe : ISystemProbe
{
    public string MachineName { get; set; } = "LAB-PC-014";

    public string CurrentUser { get; set; } = @"LAB-PC-014\techniker";

    public string CommonStartMenuPrograms { get; set; } = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs";

    public string CommonDesktop { get; set; } = @"C:\Users\Public\Desktop";

    public ServiceFacts Service { get; set; } = new(true, ServiceFacts.StateRunning, ServiceFacts.StartTypeAutomatic, "LocalSystem");

    public Exception? ServiceError { get; set; }

    public PipeFacts Pipe { get; set; } = PipeFacts.Missing;

    public Dictionary<string, SecurityIdentifier> Sids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RegistryKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> WriteViolations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConfigLoadResult Config { get; set; } = ConfigLoadResult.Fail("not set");

    public ConfigLoadResult? UncheckedConfig { get; set; }

    public Dictionary<string, IntegrityResult> Integrity { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ServiceFacts GetService(string serviceName) => ServiceError is null ? Service : throw ServiceError;

    public PipeFacts GetPipe(string pipeName) => Pipe;

    public SecurityIdentifier? TryResolveSid(string accountName) => Sids.GetValueOrDefault(accountName);

    public string? TryTranslateSid(SecurityIdentifier sid) =>
        Sids.FirstOrDefault(p => p.Value == sid).Key ?? (sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ? @"BUILTIN\Users" : null);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool FileExists(string path) => Files.Contains(path);

    public bool RegistryKeyExists(string key) => RegistryKeys.Contains(key);

    public IReadOnlyList<string> FindWriteViolations(string path) => WriteViolations.TryGetValue(path, out var list) ? list : [];

    public ConfigLoadResult LoadConfig(string path) => Config;

    public ConfigLoadResult ParseConfigWithoutAclCheck(string path) => UncheckedConfig ?? Config;

    public IntegrityResult VerifyExecutable(AppEntry app) =>
        Integrity.TryGetValue(app.Exe, out var result) ? result : IntegrityResult.Ok("ok");

    public IntegrityResult VerifyChild(AllowedChild child) =>
        Integrity.TryGetValue(child.Exe, out var result) ? result : IntegrityResult.Ok("ok");

    public AppStart.Core.Learning.LearnStateLoadResult LearnState { get; set; } = new(AppStart.Core.Learning.LearnStateSerializer.Empty, null, false);

    public AppStart.Core.Learning.LearnStateLoadResult LoadLearnState(string path) => LearnState;
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test", now.Offset, "Test", "Test");
}

/// <summary>A correctly configured machine; each test breaks exactly one thing.</summary>
internal sealed class Machine
{
    public const string InstallFolder = @"C:\Program Files\AppStart";
    public const string Launcher = @"C:\Program Files\AppStart\current\AppStart.Launcher.exe";
    public const string ConfigPath = @"C:\ProgramData\AppStart\apps.json";
    public const string DataDir = @"C:\ProgramData\Hersteller\AppA";
    public const string RegistryKey = @"HKLM\SOFTWARE\Hersteller\AppA";
    public const string Exe = @"C:\Program Files\Hersteller\AppA\AppA.exe";
    public const string StartMenuShortcut = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\App A.lnk";
    public const string DesktopShortcut = @"C:\Users\Public\Desktop\App A.lnk";

    public static readonly SecurityIdentifier AppSid = new("S-1-5-21-1000-2000-3000-1001");
    public static readonly SecurityIdentifier GroupSid = new("S-1-5-21-1000-2000-3000-1002");

    public FakeAccounts Accounts { get; } = new();

    public FakeProtection Protection { get; } = new();

    public FakeShortcuts Shortcuts { get; } = new();

    public FakeProbe Probe { get; } = new();

    public static AppEntry AppA { get; } = new()
    {
        Id = "app-a",
        Name = "App A",
        Exe = Exe,
        WorkingDir = @"C:\Program Files\Hersteller\AppA",
        Check = IntegrityCheck.Sha256,
        Sha256 = new string('A', 64),
        DataDir = DataDir,
        RegistryKey = RegistryKey,
    };

    public Machine()
    {
        Accounts.Facts["appstart-app"] = new AccountFacts(true, AppSid, true,
            ["SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight", "SeBatchLogonRight"]);
        Accounts.Facts[@"LAB-PC-014\appstart-user"] = new AccountFacts(true, new SecurityIdentifier("S-1-5-21-1000-2000-3000-1003"), false, []);
        Accounts.Operators.Add(@"LAB-PC-014\appstart-user");

        Probe.Sids["AppStart Users"] = GroupSid;
        Probe.Pipe = new PipeFacts(true, new RawSecurityDescriptor(PipeRules.BuildSddl(GroupSid)), null);
        Probe.Directories.Add(InstallFolder);
        Probe.Directories.Add(DataDir);
        Probe.RegistryKeys.Add(RegistryKey);
        Probe.Config = ConfigLoadResult.Ok(new AppsConfig { Version = 1, Apps = [AppA] });

        Probe.Files.Add(StartMenuShortcut);
        Probe.Files.Add(DesktopShortcut);
        Shortcuts.Files[StartMenuShortcut] = new ShortcutInfo(StartMenuShortcut, Launcher, "--app app-a", Exe + ",0", AppA.WorkingDir);
        Shortcuts.Files[DesktopShortcut] = new ShortcutInfo(DesktopShortcut, Launcher, "--app app-a", Exe + ",0", AppA.WorkingDir);
    }

    public static DateTimeOffset Now { get; } = new(2026, 11, 15, 10, 42, 7, TimeSpan.FromHours(1));

    public Verifier CreateVerifier() =>
        new(Accounts, Protection, Shortcuts, Probe, InstallFolder, Launcher, ConfigPath, "1.2.3", new FixedTime(Now));

    public VerifyReport Run() => CreateVerifier().Run();

    public CheckResult Run(string checkId) => Run().Checks.Single(c => c.Id == checkId);

    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AppStartVerifyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
