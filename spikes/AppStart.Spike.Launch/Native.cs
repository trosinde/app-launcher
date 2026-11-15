using System.Runtime.InteropServices;

namespace AppStart.Spike.Launch;

// Hand-written P/Invoke for the spike only. Product code uses CsWin32 (see openspec/project.md).
internal static unsafe class Native
{
    public const uint MSV1_0_S4U_LOGON = 12;
    public const int STATUS_SUCCESS = 0;

    public const uint TOKEN_ALL_ACCESS = 0xF01FF;
    public const uint MAXIMUM_ALLOWED = 0x02000000;
    public const uint CREATE_SUSPENDED = 0x00000004;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_NEW_CONSOLE = 0x00000010;
    public const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    public const uint INFINITE = 0xFFFFFFFF;

    public const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    public const uint JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400;
    public const int JobObjectExtendedLimitInformation = 9;

    public const uint READ_CONTROL = 0x00020000;
    public const uint WRITE_DAC = 0x00040000;
    public const uint WINSTA_ALL_ACCESS = 0x37F;
    public const uint DESKTOP_ALL_ACCESS = 0x01FF;

    public enum SECURITY_LOGON_TYPE : uint
    {
        Interactive = 2,
        Network = 3,
        Batch = 4,
        Service = 5,
    }

    public enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1,
        TokenSessionId = 12,
        TokenElevationType = 18,
        TokenLinkedToken = 19,
        TokenElevation = 20,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSV1_0_S4U_LOGON_HEADER
    {
        public uint MessageType;
        public uint Flags;
        public UNICODE_STRING UserPrincipalName;
        public UNICODE_STRING DomainName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_SOURCE
    {
        public fixed byte SourceName[8];
        public LUID SourceIdentifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QUOTA_LIMITS
    {
        public nuint PagedPoolLimit;
        public nuint NonPagedPoolLimit;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public nuint PagefileLimit;
        public long TimeLimit;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROFILEINFO
    {
        public int dwSize;
        public int dwFlags;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpUserName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpProfilePath;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDefaultPath;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpServerName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpPolicyPath;
        public IntPtr hProfile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    // --- secur32 / LSA ---
    [DllImport("secur32.dll")] public static extern int LsaRegisterLogonProcess(ref LSA_STRING name, out IntPtr lsaHandle, out uint mode);
    [DllImport("secur32.dll")] public static extern int LsaConnectUntrusted(out IntPtr lsaHandle);
    [DllImport("secur32.dll")] public static extern int LsaLookupAuthenticationPackage(IntPtr lsaHandle, ref LSA_STRING packageName, out uint authenticationPackage);
    [DllImport("secur32.dll")]
    public static extern int LsaLogonUser(IntPtr lsaHandle, ref LSA_STRING originName, SECURITY_LOGON_TYPE logonType,
        uint authenticationPackage, IntPtr authenticationInformation, uint authenticationInformationLength,
        IntPtr localGroups, ref TOKEN_SOURCE sourceContext, out IntPtr profileBuffer, out uint profileBufferLength,
        out LUID logonId, out IntPtr token, out QUOTA_LIMITS quotas, out int subStatus);
    [DllImport("secur32.dll")] public static extern int LsaFreeReturnBuffer(IntPtr buffer);
    [DllImport("secur32.dll")] public static extern int LsaDeregisterLogonProcess(IntPtr lsaHandle);
    [DllImport("advapi32.dll")] public static extern uint LsaNtStatusToWinError(int status);

    // --- advapi32 ---
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LogonUserW(string user, string domain, string password, int logonType, int logonProvider, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetTokenInformation(IntPtr token, TOKEN_INFORMATION_CLASS cls, ref uint info, int length);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr token, TOKEN_INFORMATION_CLASS cls, IntPtr info, int length, out int returnLength);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValueW(string? system, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, IntPtr newState, int length, IntPtr prev, IntPtr retLen);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUserW(IntPtr token, string? app, System.Text.StringBuilder? cmd, IntPtr pa, IntPtr ta,
        bool inherit, uint flags, IntPtr env, string? cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    // --- userenv ---
    [DllImport("userenv.dll", SetLastError = true)] public static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] public static extern bool DestroyEnvironmentBlock(IntPtr env);
    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool LoadUserProfileW(IntPtr token, ref PROFILEINFO profile);
    [DllImport("userenv.dll", SetLastError = true)] public static extern bool UnloadUserProfile(IntPtr token, IntPtr profile);

    // --- kernel32 ---
    [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr CreateJobObjectW(IntPtr attrs, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    // --- user32 ---
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr OpenWindowStationW(string name, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr OpenDesktopW(string name, uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessWindowStation(IntPtr winsta);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseWindowStation(IntPtr winsta);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseDesktop(IntPtr desktop);
    // Undocumented but stable since Windows XP; spike only.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int MessageBoxTimeoutW(IntPtr hwnd, string text, string caption, uint type, ushort lang, uint ms);
}
