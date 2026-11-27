using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Core.Learning;
using Microsoft.Extensions.Logging;

namespace AppStart.Service.Launch;

/// <summary>A process of the job, opened for inspection and termination.</summary>
public interface IChildProcess : IDisposable
{
    int Id { get; }

    /// <summary>Full image path, null when it cannot be read.</summary>
    string? Path { get; }

    /// <summary>Image path of the parent process, null when unknown or already gone (learn mode only).</summary>
    string? ParentPath { get; }

    bool Terminate();
}

/// <summary>Win32 access needed by <see cref="JobMonitor"/>; faked in the unit tests.</summary>
public interface IChildProcessInspector
{
    /// <summary>Opens the process (query and terminate). Null when it cannot be opened.</summary>
    IChildProcess? Open(int processId);

    /// <summary>Opens the file locked (like LockedExecutable) and checks it against the entry.</summary>
    IntegrityResult Verify(AllowedChild entry, string path);

    /// <summary>Signer thumbprint (null when not validly signed) and SHA-256 of the file, for learning.</summary>
    (string? Thumbprint, string Sha256) Fingerprint(string path);

    /// <summary>True only for %SystemRoot%\System32\conhost.exe in a protected folder (design D2).</summary>
    bool IsSystemConsoleHost(string path);
}

/// <summary>Writes Learn\&lt;AppId&gt;.json.</summary>
public interface ILearnRecorder
{
    void Record(string appId, string exe, string? thumbprint, string sha256, string? parentExe, DateTimeOffset nowUtc);
}

/// <summary>Everything the service knows about one start request.</summary>
public sealed record LaunchContext(uint SessionId, string User, LearnSession? Learn = null);

/// <summary>
/// Decides for every new process in the job of one application (add-allowed-child-processes, design D2/D3):
/// allowed helper, system console host, learn-mode recording or immediate termination. Fail-closed: any
/// error in the check terminates the process. Every decision is written to the event log.
/// </summary>
public sealed class JobMonitor(
    AppEntry app,
    LaunchContext context,
    int rootProcessId,
    IChildProcessInspector inspector,
    ILearnRecorder recorder,
    ILogger log,
    TimeProvider time)
{
    private int _learnEndLogged;

    /// <summary>A job is needed only for allowlist or while learning; blocked and all keep their behaviour.</summary>
    public static bool IsNeeded(AppEntry app, LearnSession? learn) =>
        app.ChildProcessMode == ChildProcessMode.Allowlist || learn is not null;

    public void OnNewProcess(int processId)
    {
        if (processId == rootProcessId)
        {
            return; // the application itself
        }

        IChildProcess? process;
        try
        {
            process = inspector.Open(processId);
        }
        catch (Exception ex)
        {
            Unchecked(processId, $"Process cannot be opened: {ex.Message}");
            return;
        }

        if (process is null)
        {
            Unchecked(processId, "Process cannot be opened (ended or no access).");
            return;
        }

        using (process)
        {
            string? path = null;
            try
            {
                path = process.Path;
                if (path is null)
                {
                    Kill(process, "<unknown>", "Path of the process not readable.");
                    return;
                }

                Decide(process, path);
            }
            catch (Exception ex)
            {
                Kill(process, path ?? "<unknown>", $"Verification failed: {ex.Message}");
            }
        }
    }

    private void Decide(IChildProcess process, string path)
    {
        if (inspector.IsSystemConsoleHost(path))
        {
            Allowed(process, path, "Windows console window (conhost.exe), always allowed.");
            return;
        }

        var now = time.GetUtcNow();
        if (context.Learn is { } learn)
        {
            if (learn.IsActive(now))
            {
                // Learn mode terminates nothing (design D3), also when recording fails.
                try
                {
                    var (thumbprint, sha) = inspector.Fingerprint(path);
                    recorder.Record(app.Id, path, thumbprint, sha, process.ParentPath, now);
                    log.LogInformation(EventIds.ChildRecorded,
                        "Learning mode: program recorded. User: {User}, AppId: {AppId}, Path: {Path}, Process: {ProcessId}, Parent process: {Parent}, Signature: {Signature}.",
                        context.User, app.Id, path, process.Id, process.ParentPath ?? "-", thumbprint ?? "none valid");
                }
                catch (Exception ex)
                {
                    log.LogError(EventIds.JobMonitorFailed,
                        "Learning mode: program not recorded. User: {User}, AppId: {AppId}, Path: {Path}, Process: {ProcessId}. {Reason}",
                        context.User, app.Id, path, process.Id, ex.Message);
                }

                return;
            }

            if (Interlocked.Exchange(ref _learnEndLogged, 1) == 0)
            {
                log.LogInformation(EventIds.LearnModeEnded,
                    "Learning mode expired ({Expires:u}), the configured mode applies again. AppId: {AppId}, User: {User}.",
                    learn.ExpiresUtc, app.Id, context.User);
            }
        }

        switch (app.ChildProcessMode)
        {
            case ChildProcessMode.All:
                Allowed(process, path, "Mode \"all\": all other programs allowed.");
                return;
            case ChildProcessMode.Blocked:
                Kill(process, path, "Mode \"blocked\": no other programs allowed.");
                return;
        }

        var entry = app.AllowedChildren?.FirstOrDefault(c => SamePath(c.Exe, path));
        if (entry is null)
        {
            Kill(process, path, "Not on the list of allowed helper programs.");
            return;
        }

        var check = inspector.Verify(entry, path);
        if (!check.Success)
        {
            var reason = entry.Check == IntegrityCheck.Sha256 ? "Checksum does not match" : "Signature does not match";
            Kill(process, path, $"{reason}: {check.Detail}");
            return;
        }

        Allowed(process, path, check.Detail);
    }

    private void Allowed(IChildProcess process, string path, string detail) =>
        log.LogInformation(EventIds.ChildAllowed,
            "Helper program allowed. User: {User}, Session: {Session}, AppId: {AppId}, Path: {Path}, Process: {ProcessId}. {Detail}",
            context.User, context.SessionId, app.Id, path, process.Id, detail);

    private void Kill(IChildProcess process, string path, string reason)
    {
        var done = false;
        try
        {
            done = process.Terminate();
        }
        catch (Exception ex)
        {
            reason += $" Ending failed: {ex.Message}";
        }

        log.LogWarning(EventIds.ChildTerminated,
            "Program ended. User: {User}, Session: {Session}, AppId: {AppId}, Path: {Path}, Process: {ProcessId}, Ended: {Terminated}. Reason: {Reason}",
            context.User, context.SessionId, app.Id, path, process.Id, done ? "yes" : "NO", reason);
    }

    // The process could not even be opened: nothing can be terminated, but it must be visible.
    private void Unchecked(int processId, string reason) =>
        log.LogError(EventIds.JobMonitorFailed,
            "New process in the job cannot be verified. User: {User}, AppId: {AppId}, Process: {ProcessId}. {Reason}",
            context.User, app.Id, processId, reason);

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
