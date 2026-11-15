using System.Runtime.InteropServices;

namespace AppStart.Setup.Engine.Protection;

/// <summary>
/// Enables SeTakeOwnershipPrivilege and SeRestorePrivilege in the process token (held, but disabled, by an
/// elevated administrator). Needed to set the owner of an object to Administrators when the current DACL
/// does not grant WRITE_OWNER, and to put back an arbitrary original owner on restore.
/// Best effort: without the privileges (non-elevated tests) nothing changes and the ACL calls decide.
/// Hand-written imports instead of CsWin32 so this area does not depend on the shared NativeMethods.txt.
/// </summary>
internal static partial class OwnershipPrivileges
{
    private const uint TokenAdjustPrivileges = 0x20;
    private const uint TokenQuery = 0x8;
    private const uint SePrivilegeEnabled = 0x2;

    private static readonly Lazy<bool> Enabled = new(EnableCore);

    /// <summary>True when both privileges could be enabled (informative only).</summary>
    public static bool EnsureEnabled() => Enabled.Value;

    private static bool EnableCore()
    {
        if (!OpenProcessToken(new IntPtr(-1), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            var all = true;
            foreach (var name in new[] { "SeTakeOwnershipPrivilege", "SeRestorePrivilege" })
            {
                if (!LookupPrivilegeValueW(null, name, out var luid))
                {
                    all = false;
                    continue;
                }

                var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SePrivilegeEnabled };

                // Succeeds with ERROR_NOT_ALL_ASSIGNED (1300) when the token does not hold the privilege.
                if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                    || Marshal.GetLastPInvokeError() != 0)
                {
                    all = false;
                }
            }

            return all;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
