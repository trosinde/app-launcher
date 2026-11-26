using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using AppStart.Core;
using Windows.Win32.NetworkManagement.NetManagement;
using Windows.Win32.Security;

namespace AppStart.Setup.Engine.Accounts;

/// <summary>
/// Local accounts, group and logon rights (concept "Konten, die die Installation anlegt", task 6.2).
/// Built-in groups are addressed by SID only (German Windows: "Administratoren", "Benutzer").
/// Account names without domain ("user", ".\user") always mean an account of this computer.
/// Needs admin rights for everything except <see cref="GetFacts"/> and <see cref="GetOperators"/>.
/// </summary>
public sealed class AccountService : IAccountService
{
    /// <summary>Logon rights denied to appstart-app. SeBatchLogonRight stays untouched (Administrators hold it; S4U Batch needs it).</summary>
    public static readonly IReadOnlyList<string> AppAccountDeniedRights = ["SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight"];

    internal static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    internal static readonly SecurityIdentifier UsersSid = new(WellKnownSidType.BuiltinUsersSid, null);

    private const string AppAccountDescription = "AppStart: Konto für freigegebene Applikationen (keine interaktive Anmeldung)";
    private const string OperatorAccountDescription = "AppStart: Bedienerkonto";
    private const string OperatorGroupDescription = "AppStart: Mitglieder dürfen freigegebene Applikationen über den Launcher starten";

    private readonly string machineName;

    public AccountService()
        : this(Environment.MachineName)
    {
    }

    internal AccountService(string machineName) => this.machineName = machineName;

    public SecurityIdentifier EnsureAppAccount()
    {
        using (var context = MachineContext())
        {
            var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, AppStartConstants.AppAccountName);
            if (user is null)
            {
                CreateAppAccount();
            }
            else
            {
                using (user)
                {
                    EnforceAppAccountSettings(user);
                }
            }
        }

        var sid = ResolveLocal(AppStartConstants.AppAccountName)
            ?? throw new InvalidOperationException($"Das Konto «{AppStartConstants.AppAccountName}» wurde angelegt, ist aber nicht auffindbar.");
        LocalAccountsNative.AddMember(BuiltinGroupName(AdministratorsSid), sid);
        using (var policy = LsaPolicy.OpenForWrite())
        {
            policy.AddRights(sid, [.. AppAccountDeniedRights]);
        }

        return sid;
    }

    public SecurityIdentifier EnsureOperatorGroup()
    {
        using var context = MachineContext();
        var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, AppStartConstants.OperatorGroupName);
        if (group is null)
        {
            group = new GroupPrincipal(context, AppStartConstants.OperatorGroupName) { Description = OperatorGroupDescription };
            group.Save();
        }

        using (group)
        {
            return group.Sid;
        }
    }

    public unsafe bool EnsureOperatorAccount(SecureString password)
    {
        ArgumentNullException.ThrowIfNull(password);
        EnsureOperatorGroup();

        var created = false;
        if (ResolveLocal(AppStartConstants.OperatorAccountName) is null)
        {
            if (password.Length == 0)
            {
                throw new ArgumentException("Für das Konto «appstart-user» ist ein Passwort nötig.", nameof(password));
            }

            // The password exists only in this unmanaged buffer, zeroed and freed right after NetUserAdd.
            var buffer = Marshal.SecureStringToGlobalAllocUnicode(password);
            try
            {
                LocalAccountsNative.CreateUser(AppStartConstants.OperatorAccountName, (char*)buffer, OperatorAccountDescription, 0);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(buffer);
            }

            created = true;
        }

        var sid = ResolveLocal(AppStartConstants.OperatorAccountName)
            ?? throw new InvalidOperationException($"Das Konto «{AppStartConstants.OperatorAccountName}» wurde angelegt, ist aber nicht auffindbar.");
        LocalAccountsNative.AddMember(BuiltinGroupName(UsersSid), sid);
        LocalAccountsNative.AddMember(AppStartConstants.OperatorGroupName, sid);
        return created;
    }

    public bool AddOperator(string account)
    {
        var resolved = Resolve(account)
            ?? throw new ArgumentException($"Das Konto «{AccountNameParser.Parse(account, machineName).Qualified}» wurde nicht gefunden.", nameof(account));

        // Only user accounts: a group such as "Jeder" or "Benutzer" would open the launcher to everybody.
        if (resolved.Use != SID_NAME_USE.SidTypeUser)
        {
            throw new ArgumentException($"«{resolved.Qualified}» ist kein Benutzerkonto. Nur Benutzer können in «{AppStartConstants.OperatorGroupName}» aufgenommen werden.", nameof(account));
        }

        EnsureOperatorGroup();
        return LocalAccountsNative.AddMember(AppStartConstants.OperatorGroupName, resolved.Sid);
    }

    public bool RemoveOperator(string account)
    {
        var name = AccountNameParser.Parse(account, machineName);
        var sid = name.Kind == AccountNameKind.Sid ? name.Sid : LocalAccountsNative.Lookup(name.Qualified)?.Sid;
        return sid is not null && LocalAccountsNative.RemoveMember(AppStartConstants.OperatorGroupName, sid);
    }

    public IReadOnlyList<string> GetOperators()
    {
        var members = LocalAccountsNative.GetMembers(AppStartConstants.OperatorGroupName);
        return members is null ? [] : [.. members.Select(m => m.Qualified ?? m.Sid.Value)];
    }

    public void RemoveAppAccount() => RemoveUser(AppStartConstants.AppAccountName);

    public void RemoveOperatorAccount() => RemoveUser(AppStartConstants.OperatorAccountName);

    public void RemoveOperatorGroup()
    {
        using var context = MachineContext();
        using var group = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, AppStartConstants.OperatorGroupName);
        if (group is null)
        {
            return;
        }

        using (var policy = LsaPolicy.OpenForWrite())
        {
            policy.RemoveAllRights(group.Sid);
        }

        group.Delete();
    }

    public AccountFacts GetFacts(string accountName)
    {
        var resolved = Resolve(accountName);
        if (resolved is null)
        {
            return new AccountFacts(false, null, false, []);
        }

        IReadOnlyList<string> rights;
        using (var policy = LsaPolicy.OpenForRead())
        {
            rights = policy.EnumerateRights(resolved.Sid);
        }

        return new AccountFacts(true, resolved.Sid, IsLocalAdministrator(resolved), rights);
    }

    public IReadOnlyList<AdminPasswordFinding> FindAdministratorsWithoutPassword()
    {
        var findings = new List<AdminPasswordFinding>();
        using var context = MachineContext();
        using var searcher = new PrincipalSearcher(new UserPrincipal(context));
        foreach (var principal in searcher.FindAll())
        {
            using (principal)
            {
                if (principal is not UserPrincipal { Enabled: true, Sid: { } sid } user
                    || !LocalAccountsNative.GetEffectiveGroups(sid).Contains(AdministratorsSid))
                {
                    continue;
                }

                var error = LocalAccountsNative.LogonWithEmptyPassword(user.SamAccountName);
                var qualified = $"{machineName}\\{user.SamAccountName}";
                switch (ClassifyEmptyPasswordLogon(error))
                {
                    case EmptyPasswordLogon.Empty:
                        findings.Add(new AdminPasswordFinding(qualified, null));
                        break;
                    case EmptyPasswordLogon.Unknown:
                        findings.Add(new AdminPasswordFinding(qualified, error));
                        break;
                }
            }
        }

        return findings;
    }

    internal enum EmptyPasswordLogon
    {
        HasPassword,
        Empty,
        Unknown,
    }

    /// <summary>
    /// Result of LogonUser with an empty password. Windows returns these errors only after the password matched:
    /// account restriction (blank passwords limited to console logon), logon type not granted, logon hours,
    /// workstation, expired or must-change password. Only a logon failure proves a password is set.
    /// </summary>
    internal static EmptyPasswordLogon ClassifyEmptyPasswordLogon(uint error) => error switch
    {
        0 or 1327 or 1385 or 1328 or 1329 or 1330 or 1907 => EmptyPasswordLogon.Empty,
        1326 => EmptyPasswordLogon.HasPassword,
        _ => EmptyPasswordLogon.Unknown,
    };

    /// <summary>Parses and resolves an account name or SID string; null when the account does not exist. No admin rights needed.</summary>
    internal ResolvedAccount? Resolve(string account)
    {
        var name = AccountNameParser.Parse(account, machineName);
        return name.Kind == AccountNameKind.Sid ? ResolveSid(name.Sid!) : LocalAccountsNative.Lookup(name.Qualified);
    }

    // Users and computers: effective groups incl. nesting (Authz). Groups/aliases: direct membership in Administrators.
    internal static bool IsLocalAdministrator(ResolvedAccount account)
    {
        if (account.Sid == AdministratorsSid)
        {
            return true;
        }

        if (account.Use is SID_NAME_USE.SidTypeUser or SID_NAME_USE.SidTypeComputer)
        {
            return LocalAccountsNative.GetEffectiveGroups(account.Sid).Contains(AdministratorsSid);
        }

        var members = LocalAccountsNative.GetMembers(BuiltinGroupName(AdministratorsSid)) ?? [];
        return members.Any(m => m.Sid == account.Sid);
    }

    private void CreateAppAccount()
    {
        // Random password nobody knows, never stored: lives only in this stack buffer and is zeroed after use.
        Span<char> password = stackalloc char[PasswordGenerator.Length + 1];
        try
        {
            var length = PasswordGenerator.Generate(password);
            password[length] = '\0';
            unsafe
            {
                fixed (char* p = password)
                {
                    LocalAccountsNative.CreateUser(AppStartConstants.AppAccountName, p, AppAccountDescription,
                        USER_ACCOUNT_FLAGS.UF_DONT_EXPIRE_PASSWD | USER_ACCOUNT_FLAGS.UF_PASSWD_CANT_CHANGE);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password));
        }
    }

    private static void EnforceAppAccountSettings(UserPrincipal user)
    {
        var changed = false;
        if (!user.PasswordNeverExpires)
        {
            user.PasswordNeverExpires = true;
            changed = true;
        }

        if (!user.UserCannotChangePassword)
        {
            user.UserCannotChangePassword = true;
            changed = true;
        }

        if (user.Enabled != true)
        {
            user.Enabled = true;
            changed = true;
        }

        if (user.Description != AppAccountDescription)
        {
            user.Description = AppAccountDescription;
            changed = true;
        }

        if (changed)
        {
            user.Save();
        }
    }

    // Rights first (no orphaned SID in the policy), then the profile, then the account.
    private static void RemoveUser(string samName)
    {
        using var context = MachineContext();
        using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, samName);
        if (user is null)
        {
            return;
        }

        var sid = user.Sid;
        using (var policy = LsaPolicy.OpenForWrite())
        {
            policy.RemoveAllRights(sid);
        }

        LocalAccountsNative.DeleteProfile(sid);
        user.Delete();
    }

    private SecurityIdentifier? ResolveLocal(string samName) => LocalAccountsNative.Lookup($"{machineName}\\{samName}")?.Sid;

    private static ResolvedAccount? ResolveSid(SecurityIdentifier sid)
    {
        try
        {
            var name = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
            return LocalAccountsNative.Lookup(name);
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
    }

    // Localised name of a built-in group ("Administratoren" on German Windows), from its SID.
    private static string BuiltinGroupName(SecurityIdentifier sid)
    {
        var name = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
        return name[(name.LastIndexOf('\\') + 1)..];
    }

    private static PrincipalContext MachineContext() => new(ContextType.Machine);
}
