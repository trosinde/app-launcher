using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace AppStart.Service.Native;

/// <summary>Enables privileges of the service token (LocalSystem holds them, some are disabled by default).</summary>
internal static unsafe class Privileges
{
    /// <summary>SeTcb (S4U logon, TokenSessionId), SeAssignPrimaryToken and SeIncreaseQuota (CreateProcessAsUser).</summary>
    public static void EnableForLaunch()
    {
        Enable(PInvoke.SE_TCB_NAME);
        Enable(PInvoke.SE_ASSIGNPRIMARYTOKEN_NAME);
        Enable(PInvoke.SE_INCREASE_QUOTA_NAME);
    }

    private static void Enable(string name)
    {
        HANDLE token;
        if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
        }

        try
        {
            LUID luid;
            fixed (char* n = name)
            {
                if (!PInvoke.LookupPrivilegeValue(null, n, &luid))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"LookupPrivilegeValue({name})");
                }
            }

            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            tp.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };
            if (!PInvoke.AdjustTokenPrivileges(token, false, &tp, 0, null, null) || Marshal.GetLastWin32Error() != 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"AdjustTokenPrivileges({name})");
            }
        }
        finally
        {
            PInvoke.CloseHandle(token);
        }
    }
}
