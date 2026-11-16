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
            Unchecked(processId, $"Prozess nicht zu öffnen: {ex.Message}");
            return;
        }

        if (process is null)
        {
            Unchecked(processId, "Prozess nicht zu öffnen (beendet oder kein Zugriff).");
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
                    Kill(process, "<unbekannt>", "Pfad des Prozesses nicht lesbar.");
                    return;
                }

                Decide(process, path);
            }
            catch (Exception ex)
            {
                Kill(process, path ?? "<unbekannt>", $"Prüfung fehlgeschlagen: {ex.Message}");
            }
        }
    }

    private void Decide(IChildProcess process, string path)
    {
        if (inspector.IsSystemConsoleHost(path))
        {
            Allowed(process, path, "Konsolenfenster von Windows (conhost.exe), immer erlaubt.");
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
                        "Lernmodus: Programm aufgezeichnet. Benutzer: {User}, AppId: {AppId}, Pfad: {Path}, Prozess: {ProcessId}, Elternprozess: {Parent}, Signatur: {Signature}.",
                        context.User, app.Id, path, process.Id, process.ParentPath ?? "-", thumbprint ?? "keine gültige");
                }
                catch (Exception ex)
                {
                    log.LogError(EventIds.JobMonitorFailed,
                        "Lernmodus: Programm nicht aufgezeichnet. Benutzer: {User}, AppId: {AppId}, Pfad: {Path}, Prozess: {ProcessId}. {Reason}",
                        context.User, app.Id, path, process.Id, ex.Message);
                }

                return;
            }

            if (Interlocked.Exchange(ref _learnEndLogged, 1) == 0)
            {
                log.LogInformation(EventIds.LearnModeEnded,
                    "Lernmodus abgelaufen ({Expires:u}), es gilt wieder der eingestellte Modus. AppId: {AppId}, Benutzer: {User}.",
                    learn.ExpiresUtc, app.Id, context.User);
            }
        }

        switch (app.ChildProcessMode)
        {
            case ChildProcessMode.All:
                Allowed(process, path, "Modus «all»: alle weiteren Programme erlaubt.");
                return;
            case ChildProcessMode.Blocked:
                Kill(process, path, "Modus «blocked»: keine weiteren Programme erlaubt.");
                return;
        }

        var entry = app.AllowedChildren?.FirstOrDefault(c => SamePath(c.Exe, path));
        if (entry is null)
        {
            Kill(process, path, "Nicht auf der Liste der erlaubten Hilfsprogramme.");
            return;
        }

        var check = inspector.Verify(entry, path);
        if (!check.Success)
        {
            var reason = entry.Check == IntegrityCheck.Sha256 ? "Prüfsumme stimmt nicht" : "Signatur stimmt nicht";
            Kill(process, path, $"{reason}: {check.Detail}");
            return;
        }

        Allowed(process, path, check.Detail);
    }

    private void Allowed(IChildProcess process, string path, string detail) =>
        log.LogInformation(EventIds.ChildAllowed,
            "Hilfsprogramm erlaubt. Benutzer: {User}, Session: {Session}, AppId: {AppId}, Pfad: {Path}, Prozess: {ProcessId}. {Detail}",
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
            reason += $" Beenden fehlgeschlagen: {ex.Message}";
        }

        log.LogWarning(EventIds.ChildTerminated,
            "Programm beendet. Benutzer: {User}, Session: {Session}, AppId: {AppId}, Pfad: {Path}, Prozess: {ProcessId}, beendet: {Terminated}. Grund: {Reason}",
            context.User, context.SessionId, app.Id, path, process.Id, done ? "ja" : "NEIN", reason);
    }

    // The process could not even be opened: nothing can be terminated, but it must be visible.
    private void Unchecked(int processId, string reason) =>
        log.LogError(EventIds.JobMonitorFailed,
            "Neuer Prozess im Job nicht prüfbar. Benutzer: {User}, AppId: {AppId}, Prozess: {ProcessId}. {Reason}",
            context.User, app.Id, processId, reason);

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
