using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.NetManagement;
using Windows.Win32.Security;
using Windows.Win32.Security.Authorization;

namespace AppStart.Setup.Engine.Accounts;

/// <summary>A resolved account: SID, "DOMAIN\name" and its kind.</summary>
internal sealed record ResolvedAccount(SecurityIdentifier Sid, string Qualified, SID_NAME_USE Use);

/// <summary>A member of a local group.</summary>
internal sealed record GroupMember(SecurityIdentifier Sid, SID_NAME_USE Use, string? Qualified);

/// <summary>
/// Native operations where System.DirectoryServices.AccountManagement is not enough: creating a user with a password
/// that never becomes a managed string, group membership by SID (no member enumeration, which breaks on orphaned
/// SIDs), name resolution with the SID type, profile deletion and the effective group list of an account (Authz).
/// </summary>
internal static unsafe class LocalAccountsNative
{
    // lmaccess.h; not part of the USER_ACCOUNT_FLAGS enum in the metadata.
    private const USER_ACCOUNT_FLAGS UfNormalAccount = (USER_ACCOUNT_FLAGS)0x0200;

    /// <summary>Resolves "DOMAIN\name"; null when the name is unknown.</summary>
    public static ResolvedAccount? Lookup(string qualifiedName)
    {
        uint sidSize = 0;
        uint domainSize = 0;
        SID_NAME_USE use;
        fixed (char* name = qualifiedName)
        {
            if (!PInvoke.LookupAccountName(null, name, default, &sidSize, default, &domainSize, &use))
            {
                var error = (uint)Marshal.GetLastPInvokeError();
                if (error == (uint)WIN32_ERROR.ERROR_NONE_MAPPED)
                {
                    return null;
                }

                if (error != (uint)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                {
                    throw AccountErrors.FromWin32(error, $"Resolve account \"{qualifiedName}\"");
                }
            }

            var sid = new byte[sidSize];
            var domain = new char[domainSize];
            fixed (byte* s = sid)
            fixed (char* d = domain)
            {
                if (!PInvoke.LookupAccountName(null, name, (PSID)s, &sidSize, d, &domainSize, &use))
                {
                    var error = (uint)Marshal.GetLastPInvokeError();
                    if (error == (uint)WIN32_ERROR.ERROR_NONE_MAPPED)
                    {
                        return null;
                    }

                    throw AccountErrors.FromWin32(error, $"Resolve account \"{qualifiedName}\"");
                }

                var securityIdentifier = new SecurityIdentifier(sid, 0);
                var domainName = new string(d, 0, (int)domainSize);
                var userPart = qualifiedName[(qualifiedName.LastIndexOf('\\') + 1)..];
                return new ResolvedAccount(securityIdentifier, domainName.Length == 0 ? userPart : $"{domainName}\\{userPart}", use);
            }
        }
    }

    /// <summary>
    /// Creates a local user (NetUserAdd level 1). <paramref name="password"/> is a NUL-terminated string in memory the
    /// caller owns and zeroes; it is never copied into a managed string.
    /// </summary>
    public static void CreateUser(string name, char* password, string comment, USER_ACCOUNT_FLAGS flags)
    {
        fixed (char* n = name)
        fixed (char* c = comment)
        {
            var info = new USER_INFO_1
            {
                usri1_name = n,
                usri1_password = password,
                usri1_priv = USER_PRIV.USER_PRIV_USER,
                usri1_comment = c,
                usri1_flags = flags | USER_ACCOUNT_FLAGS.UF_SCRIPT | UfNormalAccount,
            };
            uint parameterError;
            var result = PInvoke.NetUserAdd(null, 1, (byte*)&info, &parameterError);
            if (result != 0)
            {
                throw AccountErrors.FromWin32(result, $"Create account \"{name}\"");
            }
        }
    }

    /// <summary>Adds the SID to the local group. False when it already was a member.</summary>
    public static bool AddMember(string groupName, SecurityIdentifier member)
    {
        var result = ChangeMember(groupName, member, add: true);
        return result switch
        {
            0 => true,
            (uint)WIN32_ERROR.ERROR_MEMBER_IN_ALIAS => false,
            _ => throw AccountErrors.FromWin32(result, $"Add {member} to group \"{groupName}\""),
        };
    }

    /// <summary>Removes the SID from the local group. False when it was no member (or the group does not exist).</summary>
    public static bool RemoveMember(string groupName, SecurityIdentifier member)
    {
        var result = ChangeMember(groupName, member, add: false);
        return result switch
        {
            0 => true,
            (uint)WIN32_ERROR.ERROR_MEMBER_NOT_IN_ALIAS or (uint)WIN32_ERROR.ERROR_NO_SUCH_MEMBER
                or (uint)WIN32_ERROR.ERROR_NO_SUCH_ALIAS or PInvoke.NERR_GroupNotFound => false,
            _ => throw AccountErrors.FromWin32(result, $"Remove {member} from group \"{groupName}\""),
        };
    }

    private static uint ChangeMember(string groupName, SecurityIdentifier member, bool add)
    {
        fixed (char* g = groupName)
        fixed (byte* s = LsaPolicy.SidBytes(member))
        {
            var info = new LOCALGROUP_MEMBERS_INFO_0 { lgrmi0_sid = (PSID)s };
            return add
                ? PInvoke.NetLocalGroupAddMembers(null, g, 0, (byte*)&info, 1)
                : PInvoke.NetLocalGroupDelMembers(null, g, 0, (byte*)&info, 1);
        }
    }

    /// <summary>Members of a local group, orphaned SIDs included (Qualified null). Null when the group does not exist.</summary>
    public static IReadOnlyList<GroupMember>? GetMembers(string groupName)
    {
        fixed (char* g = groupName)
        {
            byte* buffer = null;
            uint read;
            uint total;
            var result = PInvoke.NetLocalGroupGetMembers(null, g, 2, &buffer, PInvoke.MAX_PREFERRED_LENGTH, &read, &total, null);
            try
            {
                if (result is (uint)WIN32_ERROR.ERROR_NO_SUCH_ALIAS or PInvoke.NERR_GroupNotFound)
                {
                    return null;
                }

                if (result != 0)
                {
                    throw AccountErrors.FromWin32(result, $"Read members of group \"{groupName}\"");
                }

                var members = new List<GroupMember>((int)read);
                var entries = (LOCALGROUP_MEMBERS_INFO_2*)buffer;
                for (var i = 0; i < read; i++)
                {
                    var sid = new SecurityIdentifier((nint)entries[i].lgrmi2_sid.Value);
                    var use = entries[i].lgrmi2_sidusage;
                    var name = use is SID_NAME_USE.SidTypeDeletedAccount or SID_NAME_USE.SidTypeUnknown or SID_NAME_USE.SidTypeInvalid
                        || entries[i].lgrmi2_domainandname.Value is null
                        ? null
                        : new string(entries[i].lgrmi2_domainandname.Value);
                    members.Add(new GroupMember(sid, use, name));
                }

                return members;
            }
            finally
            {
                if (buffer is not null)
                {
                    PInvoke.NetApiBufferFree(buffer);
                }
            }
        }
    }

    /// <summary>Deletes the user profile (folder and ProfileList entry) of the SID. False when there is none.</summary>
    public static bool DeleteProfile(SecurityIdentifier sid)
    {
        fixed (char* s = sid.Value)
        {
            if (PInvoke.DeleteProfile(s, null, null))
            {
                return true;
            }

            var error = (uint)Marshal.GetLastPInvokeError();
            if (error is (uint)WIN32_ERROR.ERROR_FILE_NOT_FOUND or (uint)WIN32_ERROR.ERROR_PATH_NOT_FOUND)
            {
                return false;
            }

            throw AccountErrors.FromWin32(error, $"Delete profile of {sid}");
        }
    }

    /// <summary>
    /// All group SIDs of the account as Windows would put them into its token (nested domain groups, local aliases,
    /// well-known groups such as Everyone), computed by Authz without a logon.
    /// </summary>
    public static IReadOnlyList<SecurityIdentifier> GetEffectiveGroups(SecurityIdentifier account)
    {
        const uint AuthzRmFlagNoAudit = 0x1;
        AUTHZ_RESOURCE_MANAGER_HANDLE manager;
        if (!PInvoke.AuthzInitializeResourceManager(AuthzRmFlagNoAudit, null, null, null, default, &manager))
        {
            throw AccountErrors.FromWin32((uint)Marshal.GetLastPInvokeError(), "Initialize Authz resource manager");
        }

        try
        {
            AUTHZ_CLIENT_CONTEXT_HANDLE context;
            fixed (byte* s = LsaPolicy.SidBytes(account))
            {
                if (!PInvoke.AuthzInitializeContextFromSid(0, (PSID)s, manager, null, default, null, &context))
                {
                    throw AccountErrors.FromWin32((uint)Marshal.GetLastPInvokeError(), $"Determine groups of {account}");
                }
            }

            try
            {
                uint size = 0;
                PInvoke.AuthzGetInformationFromContext(context, AUTHZ_CONTEXT_INFORMATION_CLASS.AuthzContextInfoGroupsSids, 0, &size, null);
                var buffer = new byte[size];
                fixed (byte* b = buffer)
                {
                    if (!PInvoke.AuthzGetInformationFromContext(context, AUTHZ_CONTEXT_INFORMATION_CLASS.AuthzContextInfoGroupsSids, size, &size, b))
                    {
                        throw AccountErrors.FromWin32((uint)Marshal.GetLastPInvokeError(), $"Read groups of {account}");
                    }

                    var groups = (TOKEN_GROUPS*)b;
                    var result = new List<SecurityIdentifier>((int)groups->GroupCount);
                    var entries = groups->Groups.AsSpan((int)groups->GroupCount);
                    foreach (var entry in entries)
                    {
                        result.Add(new SecurityIdentifier((nint)entry.Sid.Value));
                    }

                    return result;
                }
            }
            finally
            {
                PInvoke.AuthzFreeContext(context);
            }
        }
        finally
        {
            PInvoke.AuthzFreeResourceManager(manager);
        }
    }

    /// <summary>
    /// Network logon of a local account with an empty password; returns 0 on success (token closed right away)
    /// or the Win32 error. Never creates a session or profile.
    /// </summary>
    public static uint LogonWithEmptyPassword(string samName)
    {
        fixed (char* user = samName)
        fixed (char* domain = ".")
        fixed (char* password = string.Empty)
        {
            HANDLE token;
            if (!PInvoke.LogonUser(user, domain, password, LOGON32_LOGON.LOGON32_LOGON_NETWORK, LOGON32_PROVIDER.LOGON32_PROVIDER_DEFAULT, &token))
            {
                return (uint)Marshal.GetLastPInvokeError();
            }

            PInvoke.CloseHandle(token);
            return 0;
        }
    }
}
