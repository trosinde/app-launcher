using System.ComponentModel;
using System.Runtime.InteropServices;
using AppStart.Core;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using Microsoft.Extensions.Logging;
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
public sealed class VerifyingAppStarter(
    IChildProcessInspector inspector,
    ILearnRecorder recorder,
    ILoggerFactory loggers,
    TimeProvider time) : IAppStarter
{
    public StartResult Start(AppEntry app, LaunchContext context)
    {
        LockedExecutable exe;
        try
        {
            exe = LockedExecutable.Open(app.Exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartResult(StartOutcome.Failed, $"EXE cannot be opened locked: {ex.Message}");
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
                var log = loggers.CreateLogger("AppStart.Service.Launch.JobMonitor");
                var pid = SessionLauncher.Launch(app, context,
                    rootPid => new JobMonitor(app, context, rootPid, inspector, recorder, log, time), log);
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

    /// <summary>Starts <paramref name="app"/> in the requesting session and returns the process id.</summary>
    public static int Launch(AppEntry app, LaunchContext context, Func<int, JobMonitor> monitorFor, ILogger log)
    {
        var sessionId = context.SessionId;
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
                // D3 and add-allowed-child-processes D2: the job per mode, assigned while suspended so no
                // code runs outside it. allowlist or learn mode: monitored job (KILL_ON_JOB_CLOSE, no
                // breakaway); blocked: ActiveProcessLimit 1; all: no job.
                if (JobMonitor.IsNeeded(app, context.Learn))
                {
                    cleanup.Monitored = MonitoredJob.Create();
                    if (!PInvoke.AssignProcessToJobObject((HANDLE)cleanup.Monitored.Job, info.hProcess))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
                    }
                }
                else if (app.ChildProcessMode != ChildProcessMode.All)
                {
                    cleanup.Job = CreateSingleProcessJob();
                    if (!PInvoke.AssignProcessToJobObject(cleanup.Job, info.hProcess))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
                    }
                }

                // Watch before the first instruction runs: every NEW_PROCESS is checked.
                if (cleanup.Monitored is { } job)
                {
                    var monitor = monitorFor((int)info.dwProcessId);
                    job.Watch(monitor, cleanup.Dispose, ex => log.LogError(EventIds.JobMonitorFailed,
                        "Monitoring of application {AppId} failed, all its processes are ended: {Error}", app.Id, ex.Message));
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
            // A monitored job lives until it is empty (allowed helpers may outlive the application).
            if (cleanup.Monitored is null)
            {
                cleanup.ReleaseWhenProcessExits();
            }
            else
            {
                cleanup.CloseProcessHandleWhenExits();
            }

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

        public MonitoredJob? Monitored { get; set; }

        public HANDLE Profile { get; set; }

        private int _processClosed;

        /// <summary>Monitored job: the application handle goes when it exits, the rest when the job is empty.</summary>
        public void CloseProcessHandleWhenExits()
        {
            var wait = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle((IntPtr)Process.Value, ownsHandle: false) };
            RegisteredWaitHandle? registration = null;
            registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) =>
            {
                registration?.Unregister(null);
                wait.Dispose();
                CloseProcess();
            }, null, Timeout.Infinite, executeOnlyOnce: true);
        }

        private void CloseProcess()
        {
            if (Interlocked.Exchange(ref _processClosed, 1) == 0 && !Process.IsNull)
            {
                PInvoke.CloseHandle(Process);
            }
        }

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

            Monitored?.Dispose(); // KILL_ON_JOB_CLOSE: nothing in the job survives without monitoring

            CloseProcess();

            PInvoke.CloseHandle(token);
        }
    }
}
