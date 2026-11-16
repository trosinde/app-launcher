using System.Diagnostics;
using System.Text;
using static AppStart.Spike.ChildJob.Native;

namespace AppStart.Spike.ChildJob;

// Spike 1.1 (add-allowed-child-processes). Throwaway code.
//
//   monitor --mode learn|enforce [--allow a.exe,b.exe] --log <file> -- <probe args>
//       Creates a job (KILL_ON_JOB_CLOSE, no breakaway, no process limit) with a completion port,
//       starts "<self> probe <probe args>" suspended inside it and logs every job message.
//       enforce: every new process whose file name is not in --allow (the probe itself is always
//       allowed) is terminated at once; the time from process creation to exit is measured.
//   probe <scenario>... --log <file> [--pdf <file>]
//       GUI-subsystem process without a window (like the TestApp) that runs the scenarios in order.
//   crash        throws an unhandled exception (WerFault test)
internal static unsafe class Program
{
    private static Log log = null!;

    private static int Main(string[] args)
    {
        var logPath = Option(args, "--log") ?? Path.Combine(AppContext.BaseDirectory, "spike.log");
        log = new Log(logPath, args.Length > 0 ? args[0] : "?");
        try
        {
            return args.FirstOrDefault() switch
            {
                "monitor" => Monitor(args),
                "probe" => Probe(args),
                "crash" => throw new InvalidOperationException("Spike 1.1: deliberate unhandled exception"),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (args.FirstOrDefault() != "crash")
        {
            log.Write($"FATAL {ex}");
            return 99;
        }
    }

    private static int Usage()
    {
        log.Write("usage: monitor --mode learn|enforce [--allow a.exe,b.exe] --log <file> -- <probe args>");
        return 2;
    }

    // ---------------------------------------------------------------- monitor

    private static int Monitor(string[] args)
    {
        var enforce = Option(args, "--mode") == "enforce";
        var self = Environment.ProcessPath!;
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileName(self) };
        foreach (var name in (Option(args, "--allow") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            allow.Add(name.Trim());
        }

        var split = Array.IndexOf(args, "--");
        var probeArgs = split >= 0 ? args[(split + 1)..] : [];
        log.Write($"monitor start mode={(enforce ? "enforce" : "learn")} allow=[{string.Join(", ", allow)}] monitorPid={Environment.ProcessId}");

        var job = CreateJobObjectW(0, null);
        Check(job != 0, "CreateJobObject");
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; // no BREAKAWAY_OK, no process limit
        Check(SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)), "SetInformationJobObject(limits)");

        var port = CreateIoCompletionPort(-1, 0, 0, 1);
        Check(port != 0, "CreateIoCompletionPort");
        var assoc = new JOBOBJECT_ASSOCIATE_COMPLETION_PORT { CompletionKey = 1, CompletionPort = port };
        Check(SetInformationJobObject(job, JobObjectAssociateCompletionPortInformation, &assoc, (uint)sizeof(JOBOBJECT_ASSOCIATE_COMPLETION_PORT)), "SetInformationJobObject(port)");

        var cmd = new StringBuilder($"\"{self}\" probe");
        foreach (var a in probeArgs)
        {
            cmd.Append(' ').Append(a.Contains(' ') ? $"\"{a}\"" : a);
        }
        cmd.Append($" --log \"{log.Path}\"");
        var si = new STARTUPINFO { cb = sizeof(STARTUPINFO) };
        var line = (cmd + "\0").ToCharArray();
        PROCESS_INFORMATION pi;
        fixed (char* p = line)
        {
            Check(CreateProcessW(null, p, 0, 0, false, CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT, 0, null, ref si, out pi), "CreateProcess(probe)");
        }
        Check(AssignProcessToJobObject(job, pi.hProcess), "AssignProcessToJobObject");
        ResumeThread(pi.hThread);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        log.Write($"probe started pid={pi.dwProcessId}");

        var names = new Dictionary<int, string>();
        while (true)
        {
            if (!GetQueuedCompletionStatus(port, out var msg, out _, out var overlapped, 300_000))
            {
                log.Write("timeout waiting for job messages (300 s), giving up");
                break;
            }
            var pid = (int)overlapped;
            switch (msg)
            {
                case JOB_OBJECT_MSG_NEW_PROCESS:
                    OnNewProcess(pid, pi.dwProcessId, enforce, allow, names);
                    break;
                case JOB_OBJECT_MSG_EXIT_PROCESS:
                case JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS:
                    log.Write($"{(msg == JOB_OBJECT_MSG_EXIT_PROCESS ? "EXIT" : "ABNORMAL_EXIT")} pid={pid} {names.GetValueOrDefault(pid, "?")}");
                    break;
                case JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO:
                    log.Write("ACTIVE_PROCESS_ZERO - job empty, monitor ends");
                    CloseHandle(job);
                    return 0;
                default:
                    log.Write($"MSG {msg} pid/data={pid}");
                    break;
            }
        }
        CloseHandle(job);
        return 1;
    }

    private static void OnNewProcess(int pid, int probePid, bool enforce, HashSet<string> allow, Dictionary<int, string> names)
    {
        GetSystemTimePreciseAsFileTime(out var received);
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE | SYNCHRONIZE, false, pid);
        if (h == 0)
        {
            log.Write($"NEW pid={pid} OpenProcess failed err={System.Runtime.InteropServices.Marshal.GetLastPInvokeError()} (fail-closed would kill - not possible without handle)");
            return;
        }
        try
        {
            var path = ImagePath(h) ?? "<path unreadable>";
            names[pid] = Path.GetFileName(path);
            GetProcessTimes(h, out var created, out _, out _, out _);
            var latencyMs = (received - created) / 10_000.0;
            var isProbe = pid == probePid;
            var allowed = isProbe || allow.Contains(Path.GetFileName(path));
            if (!enforce || allowed)
            {
                log.Write($"NEW pid={pid} {path} msgLatency={latencyMs:F2}ms -> {(isProbe ? "root" : enforce ? "ALLOWED" : "LEARNED")}");
                return;
            }

            var killed = TerminateProcess(h, 0xC0DE);
            WaitForSingleObject(h, 5000);
            GetProcessTimes(h, out _, out var exited, out var kernel, out var user);
            log.Write($"NEW pid={pid} {path} msgLatency={latencyMs:F2}ms -> KILLED={killed} lifetime={(exited - created) / 10_000.0:F2}ms cpu={(kernel + user) / 10_000.0:F2}ms");
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // ---------------------------------------------------------------- probe

    private static int Probe(string[] args)
    {
        log.Write($"probe pid={Environment.ProcessId} args={string.Join(' ', args)}");
        var pdf = Option(args, "--pdf");
        var skip = false;
        foreach (var scenario in args.Skip(1))
        {
            if (skip) { skip = false; continue; }
            if (scenario is "--log" or "--pdf") { skip = true; continue; }
            log.Write($"--- scenario {scenario}");
            try
            {
                RunScenario(scenario, pdf);
            }
            catch (Exception ex)
            {
                log.Write($"scenario {scenario} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        log.Write("probe ends");
        return 0;
    }

    private static void RunScenario(string scenario, string? pdf)
    {
        var self = Environment.ProcessPath!;
        switch (scenario)
        {
            case "console": // console child from a GUI parent, visible window (new console -> conhost / terminal)
                StartAndWait("cmd.exe", "/c ping -n 2 127.0.0.1 >nul", hidden: false);
                break;
            case "console-hidden": // typical helper tool started without window
                StartAndWait("cmd.exe", "/c ping -n 2 127.0.0.1 >nul", hidden: true);
                break;
            case "ping": // console program that is not a shell
                StartAndWait("ping.exe", "-n 2 127.0.0.1", hidden: true);
                break;
            case "crash": // unhandled exception in a child -> WerFault?
                StartAndWait(self, $"crash --log \"{log.Path}\"", hidden: true, timeoutMs: 60_000);
                break;
            case "pdf":
                if (pdf is null) { log.Write("no --pdf given"); break; }
                var p = Process.Start(new ProcessStartInfo(pdf) { UseShellExecute = true });
                log.Write($"ShellExecute pdf -> pid={p?.Id.ToString() ?? "none (handed off)"}");
                Thread.Sleep(15_000);
                break;
            case "grandchild": // allowed child (the probe itself) that starts a console program
                StartAndWait(self, $"probe ping --log \"{log.Path}\"", hidden: true);
                break;
            case "longrun": // for the kill-on-job-close test: allowed child keeps running
                Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true });
                log.Write("longrun: ping -n 120 started, probe sleeps 120 s");
                Thread.Sleep(120_000);
                break;
            case "exit-early": // the application ends while an allowed helper still runs
                var helper = Process.Start(new ProcessStartInfo("ping.exe", "-n 6 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true });
                log.Write($"exit-early: helper ping pid={helper?.Id}, probe returns now");
                break;
            default:
                log.Write($"unknown scenario {scenario}");
                break;
        }
    }

    private static void StartAndWait(string file, string arguments, bool hidden, int timeoutMs = 20_000)
    {
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = hidden })!;
        var done = p.WaitForExit(timeoutMs);
        log.Write($"{Path.GetFileName(file)} {arguments} hidden={hidden} pid={p.Id} -> {(done ? $"exit=0x{p.ExitCode:X}" : "still running")} after {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------------- helpers

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), what);
        }
    }

    private sealed class Log(string path, string role)
    {
        private static readonly object Gate = new();
        public string Path { get; } = path;

        public void Write(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{role,-7} {Environment.ProcessId,6}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                for (var i = 0; i < 20; i++)
                {
                    try
                    {
                        using var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        var bytes = Encoding.UTF8.GetBytes(line);
                        fs.Write(bytes);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(10);
                    }
                }
            }
        }
    }
}
