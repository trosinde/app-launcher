using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace AppStart.Service.Launch;

/// <summary>Thin wrapper around CreateProcessAsUser on WinSta0\Default.</summary>
internal static unsafe class ProcessStarter
{
    public const string Desktop = @"winsta0\default";

    /// <summary>Creates a suspended process. The caller resumes it and closes both handles.</summary>
    public static PROCESS_INFORMATION CreateSuspended(HANDLE token, string exe, string? arguments, string workingDir, void* environment,
        PROCESS_CREATION_FLAGS extraFlags = 0)
    {
        var commandLine = arguments is null ? $"\"{exe}\"" : $"\"{exe}\" {arguments}";
        var cmd = (commandLine + '\0').ToCharArray(); // CreateProcessW may modify the buffer
        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
        PROCESS_INFORMATION info;

        fixed (char* app = exe)
        fixed (char* cmdPtr = cmd)
        fixed (char* dir = workingDir)
        fixed (char* desktop = Desktop)
        {
            startup.lpDesktop = desktop;
            var flags = PROCESS_CREATION_FLAGS.CREATE_SUSPENDED
                        | (environment is null ? 0 : PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT)
                        | extraFlags;
            if (!PInvoke.CreateProcessAsUser(token, app, cmdPtr, null, null, false, flags, environment, dir, &startup, &info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessAsUser({exe})");
            }
        }

        return info;
    }

    /// <summary>Starts a hidden helper with the given token, waits up to <paramref name="timeout"/>, returns its exit code.</summary>
    public static uint RunAndWait(HANDLE token, string exe, string arguments, TimeSpan timeout)
    {
        // The service EXE is a console program: no window may flash up in the operator's session.
        var info = CreateSuspended(token, exe, arguments, Path.GetDirectoryName(exe)!, null, PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW);
        try
        {
            PInvoke.ResumeThread(info.hThread);
            if (PInvoke.WaitForSingleObject(info.hProcess, (uint)timeout.TotalMilliseconds) != WAIT_EVENT.WAIT_OBJECT_0)
            {
                PInvoke.TerminateProcess(info.hProcess, 1);
                return uint.MaxValue;
            }

            uint code;
            return PInvoke.GetExitCodeProcess(info.hProcess, &code) ? code : uint.MaxValue;
        }
        finally
        {
            PInvoke.CloseHandle(info.hThread);
            PInvoke.CloseHandle(info.hProcess);
        }
    }
}
