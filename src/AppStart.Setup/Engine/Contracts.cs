using System.Security;
using System.Security.Principal;
using AppStart.Core.Configuration;

namespace AppStart.Setup.Engine;

// Shared contracts of the setup engine. Each area (Accounts, Protection, Shortcuts, Verification)
// implements its interface in its own folder; SetupEngine (orchestration) and the CLI/wizard use them.
// Change this file only in agreement: all areas depend on it.

/// <summary>
/// What setup changed and must be able to undo (C:\ProgramData\AppStart\setup-state.json, SYSTEM and
/// Administrators only, like apps.json). Uninstall uses it to restore the original state.
/// </summary>
public sealed record SetupState
{
    public int Version { get; init; } = 1;

    /// <summary>appstart-user was created by setup (and may therefore be deleted by uninstall).</summary>
    public bool OperatorAccountCreatedBySetup { get; init; }

    /// <summary>Accounts setup added to "AppStart Users" (DOMAIN\User or SID string).</summary>
    public IReadOnlyList<string> AddedGroupMembers { get; init; } = [];

    /// <summary>Original ACLs of data folders and registry keys before setup protected them.</summary>
    public IReadOnlyList<AclBackup> AclBackups { get; init; } = [];

    /// <summary>Shortcuts that pointed directly to an application EXE, moved away by setup.</summary>
    public IReadOnlyList<ShortcutBackup> ShortcutBackups { get; init; } = [];

    /// <summary>Shortcuts created by setup (launcher shortcuts per app).</summary>
    public IReadOnlyList<string> CreatedShortcuts { get; init; } = [];
}

public enum AclTarget
{
    Directory,
    RegistryKey,
}

/// <summary>Original security descriptor (SDDL, owner + DACL incl. protection flag) of a protected object.</summary>
public sealed record AclBackup(AclTarget Target, string Path, string OriginalSddl, string AppId);

/// <summary>A shortcut that pointed directly to an app EXE: where it was and where its copy is kept.</summary>
public sealed record ShortcutBackup(string OriginalPath, string BackupPath, string AppId);

// ------------------------------------------------------------------ Accounts (task 6.2)

/// <summary>Local accounts, group and logon rights (concept "Accounts the installation creates").</summary>
public interface IAccountService
{
    /// <summary>
    /// Creates appstart-app if missing: random password nobody knows (not stored), password never expires,
    /// member of the local Administrators (SID S-1-5-32-544), interactive and RDP logon denied
    /// (SeDenyInteractiveLogonRight, SeDenyRemoteInteractiveLogonRight). Idempotent; re-applies the denials.
    /// </summary>
    SecurityIdentifier EnsureAppAccount();

    /// <summary>Creates the local group "AppStart Users" if missing.</summary>
    SecurityIdentifier EnsureOperatorGroup();

    /// <summary>Creates appstart-user (standard user, member of Users and "AppStart Users"). False if it already existed.</summary>
    bool EnsureOperatorAccount(SecureString password);

    /// <summary>Adds a local or domain account ("DOMAIN\User", ".\user" or "user") to "AppStart Users". False if already member.</summary>
    bool AddOperator(string account);

    bool RemoveOperator(string account);

    /// <summary>Members of "AppStart Users" as "DOMAIN\User".</summary>
    IReadOnlyList<string> GetOperators();

    /// <summary>Removes appstart-app incl. its deny rights and its profile.</summary>
    void RemoveAppAccount();

    void RemoveOperatorAccount();

    void RemoveOperatorGroup();

    /// <summary>Facts for verify: does the account exist, which privileges/rights it holds, is it an admin.</summary>
    AccountFacts GetFacts(string accountName);

    /// <summary>
    /// Enabled local user accounts that are members of the Administrators (also through nested groups) and have an
    /// empty password, or whose password could not be checked (fail closed). Spec fix-wizard-findings
    /// "Administrators with password". Each check is one network logon with an empty password; for an account with
    /// a password it counts as one failed logon.
    /// </summary>
    IReadOnlyList<AdminPasswordFinding> FindAdministratorsWithoutPassword();
}

/// <summary>An administrator account with an empty password (<see cref="Win32Error"/> null) or one that could not be checked.</summary>
public sealed record AdminPasswordFinding(string Account, uint? Win32Error);

public sealed record AccountFacts(
    bool Exists,
    SecurityIdentifier? Sid,
    bool IsLocalAdministrator,
    IReadOnlyList<string> AccountRights);

// ------------------------------------------------------------------ Protection (task 6.3)

/// <summary>ACLs on C:\ProgramData\AppStart, apps.json, data folders and registry keys.</summary>
public interface IProtectionService
{
    /// <summary>C:\ProgramData\AppStart (+ Reports): owner Administrators, protected DACL, SYSTEM and Administrators full only.</summary>
    void ProtectConfigDirectory();

    /// <summary>Writes a file in C:\ProgramData\AppStart atomically (temp + replace) with the protected ACL.</summary>
    void WriteProtectedFile(string path, string content);

    /// <summary>
    /// Restricts a folder (created if missing) to appstart-app, SYSTEM and Administrators (full control,
    /// inherited to children, inheritance from parent removed). Returns the original descriptor for undo.
    /// </summary>
    AclBackup ProtectDataDirectory(string path, SecurityIdentifier appAccount, string appId);

    /// <summary>Same for a registry key under HKLM (created if missing), concept "Lock the registry key with passwords".</summary>
    AclBackup ProtectRegistryKey(string key, SecurityIdentifier appAccount, string appId);

    /// <summary>Puts the original descriptor back (uninstall, or app removed).</summary>
    void Restore(AclBackup backup);

    /// <summary>Principals other than appstart-app, SYSTEM, Administrators, TrustedInstaller that have ANY allow ACE (verify).</summary>
    IReadOnlyList<string> FindForeignAccess(AclTarget target, string path, SecurityIdentifier appAccount);
}

// ------------------------------------------------------------------ Shortcuts (task 6.4)

/// <summary>Launcher shortcuts per app and the handling of existing direct shortcuts.</summary>
public interface IShortcutService
{
    /// <summary>
    /// Creates "&lt;app.Name&gt;.lnk" in the public Start menu (Programs) and on the public desktop:
    /// target = launcher, arguments "--app &lt;id&gt;", icon = app EXE index 0, working dir = app.WorkingDir.
    /// Overwrites own shortcuts of the same name. Returns the created paths.
    /// </summary>
    IReadOnlyList<string> CreateAppShortcuts(AppEntry app, string launcherPath);

    /// <summary>
    /// Shortcuts that point directly to <paramref name="exePath"/> (case-insensitive) in the public Start menu,
    /// public desktop and the Desktop/Start menu of every user profile under C:\Users.
    /// </summary>
    IReadOnlyList<string> FindDirectShortcuts(string exePath);

    /// <summary>Moves a direct shortcut to C:\ProgramData\AppStart\ShortcutBackup (unique name) and returns the record.</summary>
    ShortcutBackup BackupAndRemove(string shortcutPath, string appId);

    /// <summary>Puts a backed-up shortcut back to its original path (uninstall).</summary>
    void Restore(ShortcutBackup backup);

    /// <summary>Deletes shortcuts created by setup (missing files are ignored).</summary>
    void Delete(IEnumerable<string> paths);

    /// <summary>Target, arguments and icon of a shortcut (verify, tests). Null when the file is no readable shortcut.</summary>
    ShortcutInfo? Read(string path);
}

public sealed record ShortcutInfo(string Path, string TargetPath, string Arguments, string IconLocation, string WorkingDirectory);

// ------------------------------------------------------------------ Verification (tasks 6.6 verify, 9.x)

public enum CheckOutcome
{
    Passed,
    Deviation,
}

/// <summary>One verify check point (spec verification, table "Verify command").</summary>
public sealed record CheckResult(string Id, string Title, string Expected, CheckOutcome Outcome, IReadOnlyList<string> Findings);

/// <summary>Complete verify result. Exit code 0 only when every check passed.</summary>
public sealed record VerifyReport(
    DateTimeOffset CreatedAt,
    string Computer,
    string Technician,
    string Version,
    IReadOnlyList<CheckResult> Checks)
{
    public int Deviations => Checks.Count(c => c.Outcome == CheckOutcome.Deviation);

    public int ExitCode => Deviations == 0 ? 0 : 1;
}

/// <summary>Runs all checks and writes the report files.</summary>
public interface IVerifier
{
    VerifyReport Run();

    /// <summary>Writes verify-yyyy-MM-dd-HHmmss.html and .json into <paramref name="directory"/>; returns the HTML path.</summary>
    string WriteReport(VerifyReport report, string directory);
}

// ------------------------------------------------------------------ Orchestration (SetupEngine, used by wizard and CLI)

/// <summary>An application EXE as proposed in wizard step 1 / "add" (before it is released).</summary>
public sealed record AppCandidate(
    string ExePath,
    string Name,
    string Id,
    IntegrityCheck Check,
    string? SignerThumbprint,
    string? Sha256,
    string? DataDir,
    string? RegistryKey,
    ChildProcessMode ChildProcesses = ChildProcessMode.Blocked,
    IReadOnlyList<AllowedChild>? AllowedChildren = null);

/// <summary>What is configured today (wizard opens with it on update / re-run, task 6.7).</summary>
public sealed record SetupSnapshot(
    IReadOnlyList<AppEntry> Apps,
    IReadOnlyList<string> Operators,
    bool AppAccountExists,
    bool OperatorAccountExists,
    bool ServiceRunning,
    IReadOnlyList<string>? AdminsWithoutPassword = null)
{
    /// <summary>Administrators with an empty or uncheckable password (warning in wizard step 2; verify reports them).</summary>
    public IReadOnlyList<string> AdminsWithoutPasswordOrEmpty => AdminsWithoutPassword ?? [];

    public bool IsConfigured => AppAccountExists && Apps.Count > 0;
}

/// <summary>Everything the wizard collected in steps 1 and 2.</summary>
public sealed record SetupRequest(
    IReadOnlyList<AppCandidate> Apps,
    bool CreateOperatorAccount,
    SecureString? OperatorPassword,
    IReadOnlyList<string> AdditionalOperators,
    IReadOnlyList<string>? RemovedOperators = null)
{
    /// <summary>Members the wizard removed from the list (any member of AppStart Users except appstart-user).</summary>
    public IReadOnlyList<string> RemovedOperatorsOrEmpty => RemovedOperators ?? [];
}

/// <summary>One line of the summary in wizard step 3.</summary>
public sealed record SetupStepResult(string Text, bool Success, string? Detail = null);

/// <summary>Orchestrates accounts, configuration, protection, shortcuts and service (task 6.x).</summary>
public interface ISetupEngine
{
    SetupSnapshot LoadCurrent();

    /// <summary>Reads an EXE: display name (FileDescription/ProductName/file name), unique AppId, signature or SHA-256.</summary>
    AppCandidate Inspect(string exePath);

    /// <summary>Applies the request (adds/updates apps, keeps released apps that are still listed, removes the others).</summary>
    IReadOnlyList<SetupStepResult> Apply(SetupRequest request, IProgress<string>? progress = null);

    VerifyReport Verify();

    string WriteReport(VerifyReport report);

    // ------------------------------------------------------------------ helper programs (add-allowed-child-processes)

    /// <summary>Reads a helper EXE: signature when validly signed, else SHA-256. Throws <see cref="SetupInputException"/>.</summary>
    AllowedChild InspectChild(string exePath, bool allowShell);

    /// <summary>Why operators could change the helper or its folder; empty when protected.</summary>
    IReadOnlyList<string> WriteViolations(string exePath);

    /// <summary>Checks the helper file against its entry (missing file = failure).</summary>
    Core.Integrity.IntegrityResult VerifyChild(AllowedChild entry);

    /// <summary>The entry with the SHA-256 of the current file (signature entries unchanged).</summary>
    AllowedChild RehashChild(AllowedChild entry);

    /// <summary>The running learn session of a released app, else null.</summary>
    Core.Learning.LearnSession? GetLearnSession(string appId);

    IReadOnlyList<SetupStepResult> StartLearning(string appId, int minutes);

    IReadOnlyList<SetupStepResult> StopLearning(string appId);

    /// <summary>What the service recorded; null when nothing.</summary>
    Core.Learning.LearnRecording? GetRecording(string appId);

    void DiscardRecording(string appId);
}
