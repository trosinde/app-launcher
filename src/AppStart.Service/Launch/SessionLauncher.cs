using System.ComponentModel;
using System.Runtime.InteropServices;
using AppStart.Core;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;

namespace AppStart.Service.Launch;

/// <summary>
/// Verifies the locked EXE and starts it as appstart-app in the requesting session (tasks 4.4, 4.5;
/// design D1-D3, D7). The EXE stays locked from verification until the process exists.
/// </summary>
public sealed class VerifyingAppStarter : IAppStarter
{
    public StartResult Start(AppEntry app, uint sessionId)
    {
        LockedExecutable exe;
        try
        {
            exe = LockedExecutable.Open(app.Exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartResult(StartOutcome.Failed, $"EXE kann nicht gesperrt geöffnet werden: {ex.Message}");
        }

        using (exe)
        {
            var integrity = ExecutableVerifier.Verify(app, exe);
            if (!integrity.Success)
            {
                return new StartResult(StartOutcome.IntegrityFailed, integrity.Detail);
            }

            try
            {
                var pid = SessionLauncher.Launch(app, sessionId);
                return new StartResult(StartOutcome.Started, integrity.Detail, pid);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return new StartResult(StartOutcome.Failed, ex.Message);
            }
        }
    }
}

internal static unsafe class SessionLauncher
{
    private static int _privilegesEnabled;

    /// <summary>Starts <paramref name="app"/> in <paramref name="sessionId"/> and returns the process id.</summary>
    public static int Launch(AppEntry app, uint sessionId)
    {
        if (Interlocked.Exchange(ref _privilegesEnabled, 1) == 0)
        {
            Native.Privileges.EnableForLaunch();
        }

        // D1: full token of appstart-app via S4U, no password.
        var logonToken = S4ULogon.LogonBatch(AppStartConstants.AppAccountName, Environment.MachineName);
        HANDLE token;
        try
        {
            if (!PInvoke.DuplicateTokenEx(logonToken, TOKEN_ACCESS_MASK.TOKEN_ALL_ACCESS, null,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, &token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
            }
        }
        finally
        {
            PInvoke.CloseHandle(logonToken);
        }

        var cleanup = new LaunchResources(token);
        try
        {
            // D2: exactly the requesting session, with desktop access and the account's own profile.
            var session = sessionId;
            if (!PInvoke.SetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenSessionId, &session, sizeof(uint)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenSessionId)");
            }

            DesktopAccess.GrantInSession(sessionId);

            fixed (char* user = AppStartConstants.AppAccountName)
            {
                var profile = new PROFILEINFOW { dwSize = (uint)sizeof(PROFILEINFOW), dwFlags = 1 /* PI_NOUI */, lpUserName = user };
                if (!PInvoke.LoadUserProfile(token, &profile))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadUserProfile");
                }

                cleanup.Profile = profile.hProfile;
            }

            void* environment;
            if (!PInvoke.CreateEnvironmentBlock(&environment, token, false))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEnvironmentBlock");
            }

            PROCESS_INFORMATION info;
            try
            {
                info = ProcessStarter.CreateSuspended(token, app.Exe, null, app.WorkingDir, environment);
            }
            finally
            {
                PInvoke.DestroyEnvironmentBlock(environment);
            }

            cleanup.Process = info.hProcess;
            try
            {
                // D3: no child processes unless allowed. Assigned while suspended, so no code runs outside the job.
                if (!app.AllowChildProcesses)
                {
                    cleanup.Job = CreateSingleProcessJob();
                    if (!PInvoke.AssignProcessToJobObject(cleanup.Job, info.hProcess))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
                    }
                }

                if (PInvoke.ResumeThread(info.hThread) == uint.MaxValue)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread");
                }
            }
            catch
            {
                PInvoke.TerminateProcess(info.hProcess, 1);
                throw;
            }
            finally
            {
                PInvoke.CloseHandle(info.hThread);
            }

            // Profile, token and job stay until the application exits; then everything is released.
            cleanup.ReleaseWhenProcessExits();
            return (int)info.dwProcessId;
        }
        catch
        {
            cleanup.Dispose();
            throw;
        }
    }

    private static HANDLE CreateSingleProcessJob()
    {
        var job = PInvoke.CreateJobObject(null, (char*)null);
        if (job.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
        }

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
        limits.BasicLimitInformation.ActiveProcessLimit = 1;
        if (!PInvoke.SetInformationJobObject(job, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits,
                (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
        {
            var error = Marshal.GetLastWin32Error();
            PInvoke.CloseHandle(job);
            throw new Win32Exception(error, "SetInformationJobObject");
        }

        return job;
    }

    /// <summary>Handles that must live as long as the application runs.</summary>
    private sealed class LaunchResources(HANDLE token) : IDisposable
    {
        private int _disposed;

        public HANDLE Process { get; set; }

        public HANDLE Job { get; set; }

        public HANDLE Profile { get; set; }

        public void ReleaseWhenProcessExits()
        {
            var wait = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle((IntPtr)Process.Value, ownsHandle: false) };
            RegisteredWaitHandle? registration = null;
            registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) =>
            {
                registration?.Unregister(null);
                wait.Dispose();
                Dispose();
            }, null, Timeout.Infinite, executeOnlyOnce: true);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (!Profile.IsNull)
            {
                PInvoke.UnloadUserProfile(token, Profile);
            }

            if (!Job.IsNull)
            {
                PInvoke.CloseHandle(Job); // no KILL_ON_JOB_CLOSE: limits stay with the process
            }

            if (!Process.IsNull)
            {
                PInvoke.CloseHandle(Process);
            }

            PInvoke.CloseHandle(token);
        }
    }
}
