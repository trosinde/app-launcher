using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using AppStart.Core;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.System.Threading;

namespace AppStart.Service.Launch;

/// <summary>
/// Gives appstart-app access to WinSta0\Default of the operator's session (design D2). The window station
/// of another session cannot be opened from session 0, so the service starts itself as SYSTEM inside the
/// target session with "--grant-desktop". The helper takes no SID from the command line: it always grants
/// the local appstart-app account, so it cannot be misused to open the desktop to anyone else.
/// </summary>
internal static unsafe class DesktopAccess
{
    public const string HelperArgument = "--grant-desktop";

    private const uint WinstaAllAccess = 0x37F;
    private const uint DesktopAllAccess = 0x1FF;
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;

    /// <summary>Service side: runs the helper in <paramref name="sessionId"/> and waits for it (max. 10 s).</summary>
    public static void GrantInSession(uint sessionId)
    {
        HANDLE self, copy;
        if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS, &self))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
        }

        try
        {
            if (!PInvoke.DuplicateTokenEx(self, TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS, null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, &copy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx(SYSTEM)");
            }
        }
        finally
        {
            PInvoke.CloseHandle(self);
        }

        try
        {
            var session = sessionId;
            if (!PInvoke.SetTokenInformation(copy, TOKEN_INFORMATION_CLASS.TokenSessionId, &session, sizeof(uint)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenSessionId, helper)");
            }

            var exitCode = ProcessStarter.RunAndWait(copy, Environment.ProcessPath!, HelperArgument, TimeSpan.FromSeconds(10));
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Desktop-Freigabe in Session {sessionId} fehlgeschlagen (Exit-Code {exitCode}).");
            }
        }
        finally
        {
            PInvoke.CloseHandle(copy);
        }
    }

    /// <summary>Helper side (runs as SYSTEM in the operator session). Returns the process exit code.</summary>
    public static int GrantForAppAccountInCurrentSession()
    {
        try
        {
            var sid = (SecurityIdentifier)new NTAccount(AppStartConstants.AppAccountName).Translate(typeof(SecurityIdentifier));

            HWINSTA winsta;
            fixed (char* name = "WinSta0")
            {
                winsta = PInvoke.OpenWindowStation(name, false, ReadControl | WriteDac | WinstaAllAccess);
            }

            if (winsta.IsNull)
            {
                return 2;
            }

            try
            {
                if (!PInvoke.SetProcessWindowStation(winsta))
                {
                    return 3;
                }

                HDESK desktop;
                fixed (char* name = "Default")
                {
                    desktop = PInvoke.OpenDesktop(name, 0, false, ReadControl | WriteDac | DesktopAllAccess);
                }

                if (desktop.IsNull)
                {
                    return 4;
                }

                try
                {
                    AddAllow((IntPtr)winsta.Value, sid, (int)WinstaAllAccess);
                    AddAllow((IntPtr)desktop.Value, sid, (int)DesktopAllAccess);
                }
                finally
                {
                    PInvoke.CloseDesktop(desktop);
                }
            }
            finally
            {
                PInvoke.CloseWindowStation(winsta);
            }

            return 0;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or UnauthorizedAccessException or SystemException)
        {
            return 5;
        }
    }

    private static void AddAllow(IntPtr handle, SecurityIdentifier sid, int mask)
    {
        var security = new UserObjectSecurity(handle);
        security.AddRule(new UserObjectAccessRule(sid, mask, AccessControlType.Allow));
        security.Save(handle);
    }

    private sealed class UserObjectSecurity(IntPtr handle)
        : NativeObjectSecurity(false, ResourceType.WindowObject, new SafeWaitHandle(handle, ownsHandle: false), AccessControlSections.Access)
    {
        public override Type AccessRightType => typeof(int);

        public override Type AccessRuleType => typeof(UserObjectAccessRule);

        public override Type AuditRuleType => typeof(AuditRule);

        public void AddRule(AccessRule rule) => AddAccessRule(rule);

        public void Save(IntPtr h) => Persist(new SafeWaitHandle(h, ownsHandle: false), AccessControlSections.Access);

        public override AccessRule AccessRuleFactory(IdentityReference identityReference, int accessMask, bool isInherited,
            InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags, AccessControlType type) =>
            new UserObjectAccessRule(identityReference, accessMask, type);

        public override AuditRule AuditRuleFactory(IdentityReference identityReference, int accessMask, bool isInherited,
            InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags, AuditFlags flags) =>
            throw new NotSupportedException();
    }

    private sealed class UserObjectAccessRule(IdentityReference identity, int mask, AccessControlType type)
        : AccessRule(identity, mask, false, InheritanceFlags.None, PropagationFlags.None, type);
}
