using System.ComponentModel;
using System.Runtime.InteropServices;
using AppStart.Core;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Core.Learning;
using AppStart.Core.Security;

namespace AppStart.Service.Launch;

/// <summary>Win32 implementation of <see cref="IChildProcessInspector"/> (design D2).</summary>
public sealed class Win32ChildProcessInspector : IChildProcessInspector
{
    private static readonly string ConsoleHost =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");

    public IChildProcess? Open(int processId)
    {
        var handle = JobNative.OpenProcess(JobNative.ProcessQueryLimitedInformation | JobNative.ProcessTerminate, false, processId);
        return handle == 0 ? null : new Win32ChildProcess(processId, handle);
    }

    public IntegrityResult Verify(AllowedChild entry, string path)
    {
        using var exe = LockedExecutable.Open(path);
        return ExecutableVerifier.Verify(entry, exe);
    }

    public (string? Thumbprint, string Sha256) Fingerprint(string path)
    {
        using var exe = LockedExecutable.Open(path);
        return (ExecutableVerifier.TryGetValidSignerThumbprint(exe), ExecutableVerifier.ComputeSha256(exe));
    }

    public bool IsSystemConsoleHost(string path) =>
        string.Equals(Path.GetFullPath(path), ConsoleHost, StringComparison.OrdinalIgnoreCase)
        && WriteAccessPolicy.FindViolations(ConsoleHost).Count == 0
        && WriteAccessPolicy.FindViolations(Path.GetDirectoryName(ConsoleHost)!).Count == 0;

    private sealed class Win32ChildProcess(int id, nint handle) : IChildProcess
    {
        public int Id { get; } = id;

        public string? Path { get; } = JobNative.ImagePath(handle);

        public string? ParentPath => JobNative.ParentImagePath(Id);

        public bool Terminate() => JobNative.TerminateProcess(handle, 0xC0DE);

        public void Dispose() => JobNative.CloseHandle(handle);
    }
}

/// <summary>Learn\&lt;AppId&gt;.json under C:\ProgramData\AppStart (protected folder, written by the service).</summary>
public sealed class FileLearnRecorder(string? directory = null) : ILearnRecorder
{
    private static readonly Lock Gate = new();

    public void Record(string appId, string exe, string? thumbprint, string sha256, string? parentExe, DateTimeOffset nowUtc)
    {
        var dir = directory ?? AppStartConstants.LearnDirectory;
        lock (Gate)
        {
            Directory.CreateDirectory(dir); // inherits the protection of C:\ProgramData\AppStart
            var (existing, error) = LearnRecordingStore.Load(appId, dir);
            if (error is not null)
            {
                throw new InvalidOperationException(error); // never continue a recording someone else could have changed
            }

            var updated = (existing ?? new LearnRecording { AppId = appId }).Record(exe, thumbprint, sha256, parentExe, nowUtc);
            var path = LearnRecordingStore.PathFor(appId, dir);
            var temp = path + ".tmp";
            File.WriteAllText(temp, LearnRecordingSerializer.Serialize(updated));
            File.Move(temp, path, overwrite: true);
        }
    }
}

/// <summary>Source of learn.json; read on every start request (design D3).</summary>
public interface ILearnSource
{
    LearnStateLoadResult Load();
}

public sealed class FileLearnSource : ILearnSource
{
    public LearnStateLoadResult Load() => LearnStateStore.Load();
}

/// <summary>Job with completion port for mode allowlist and learn mode (design D2, Spike 1.1).</summary>
internal sealed class MonitoredJob : IDisposable
{
    private readonly nint _port;
    private int _disposed;

    private MonitoredJob(nint job, nint port)
    {
        Job = job;
        _port = port;
    }

    public nint Job { get; }

    /// <summary>No process limit, KILL_ON_JOB_CLOSE (service stops = all processes end), no breakaway.</summary>
    public static unsafe MonitoredJob Create()
    {
        var job = JobNative.CreateJobObjectW(0, null);
        if (job == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateJobObject");
        }

        var port = JobNative.CreateIoCompletionPort(-1, 0, 0, 1);
        if (port == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            JobNative.CloseHandle(job);
            throw new Win32Exception(error, "CreateIoCompletionPort");
        }

        try
        {
            var limits = new JobNative.ExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = JobNative.LimitKillOnJobClose;
            if (!JobNative.SetInformationJobObject(job, JobNative.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobNative.ExtendedLimitInformation)))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetInformationJobObject(limits)");
            }

            var association = new JobNative.AssociateCompletionPort { CompletionKey = 1, CompletionPort = port };
            if (!JobNative.SetInformationJobObject(job, JobNative.JobObjectAssociateCompletionPortInformation, &association, (uint)sizeof(JobNative.AssociateCompletionPort)))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetInformationJobObject(port)");
            }
        }
        catch
        {
            JobNative.CloseHandle(port);
            JobNative.CloseHandle(job);
            throw;
        }

        return new MonitoredJob(job, port);
    }

    /// <summary>
    /// Reads the job messages on a dedicated thread until the job is empty (ACTIVE_PROCESS_ZERO), then
    /// calls <paramref name="onEmpty"/>. Allowed helpers may outlive the application (design D2).
    /// </summary>
    public void Watch(JobMonitor monitor, Action onEmpty, Action<Exception> onFailure)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (JobNative.GetQueuedCompletionStatus(_port, out var message, out _, out var data, JobNative.Infinite))
                {
                    switch (message)
                    {
                        case JobNative.MsgNewProcess:
                            monitor.OnNewProcess((int)data);
                            break;
                        case JobNative.MsgActiveProcessZero:
                            onEmpty();
                            return;
                    }
                }

                throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetQueuedCompletionStatus");
            }
            catch (Exception ex)
            {
                // Fail-closed: without monitoring nothing in the job may keep running.
                onFailure(ex);
                Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "AppStart job monitor",
        };
        thread.Start();
    }

    /// <summary>Closing the job handle ends every process still in it (KILL_ON_JOB_CLOSE).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        JobNative.CloseHandle(Job);
        JobNative.CloseHandle(_port);
    }
}

/// <summary>P/Invoke for the monitored job (kept apart from the CsWin32 surface of the existing launcher).</summary>
internal static unsafe partial class JobNative
{
    public const uint ProcessTerminate = 0x0001;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint LimitKillOnJobClose = 0x2000;
    public const int JobObjectAssociateCompletionPortInformation = 7;
    public const int JobObjectExtendedLimitInformation = 9;
    public const uint MsgActiveProcessZero = 4;
    public const uint MsgNewProcess = 6;
    public const uint Infinite = 0xFFFFFFFF;
    private const uint Th32csSnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct BasicLimitInformation
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
    public struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AssociateCompletionPort
    {
        public nint CompletionKey;
        public nint CompletionPort;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateIoCompletionPort(nint fileHandle, nint existingPort, nuint key, uint threads);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetQueuedCompletionStatus(nint port, out uint bytes, out nuint key, out nint overlapped, uint timeoutMs);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    public static string? ImagePath(nint process)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    /// <summary>Image path of the parent process (learn mode only). Null when unknown or gone.</summary>
    public static string? ParentImagePath(int processId)
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot is 0 or -1)
        {
            return null;
        }

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
            {
                if (entry.th32ProcessID != (uint)processId)
                {
                    continue;
                }

                var parent = OpenProcess(ProcessQueryLimitedInformation, false, (int)entry.th32ParentProcessID);
                if (parent == 0)
                {
                    return null;
                }

                try
                {
                    return ImagePath(parent);
                }
                finally
                {
                    CloseHandle(parent);
                }
            }

            return null;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }
}
