using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Core.Learning;
using AppStart.Service.Launch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace AppStart.Service.Tests;

// add-allowed-child-processes, tasks 3.2-3.4: spec app-launch «Positivliste für Kindprozesse» and «Lernmodus».
public class JobMonitorTests
{
    private const string Dir = @"C:\Program Files\Hersteller\AppA";
    private const string Export = Dir + @"\Export.exe";
    private const string Updater = Dir + @"\Updater.exe";
    private const string Cmd = @"C:\Windows\System32\cmd.exe";
    private const string Conhost = @"C:\Windows\System32\conhost.exe";
    private const int Root = 100;

    private static readonly AppEntry AllowlistApp = new()
    {
        Id = "appa", Name = "A", Exe = Dir + @"\AppA.exe", WorkingDir = Dir,
        Check = IntegrityCheck.Signature, SignerThumbprint = new string('A', 40),
        ChildProcesses = ChildProcessMode.Allowlist,
        AllowedChildren =
        [
            new AllowedChild { Exe = Export, Check = IntegrityCheck.Signature, SignerThumbprint = new string('B', 40) },
            new AllowedChild { Exe = Updater, Check = IntegrityCheck.Sha256, Sha256 = new string('C', 64) },
        ],
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeInspector _inspector = new();
    private readonly FakeRecorder _recorder = new();
    private readonly ListLogger _log = new();

    private JobMonitor Monitor(AppEntry? app = null, LearnSession? learn = null) =>
        new(app ?? AllowlistApp, new LaunchContext(2, @"PC\bediener", learn), Root, _inspector, _recorder, _log, _time);

    private LearnSession Learning(int minutes = 30) =>
        new() { AppId = "appa", StartedUtc = _time.GetUtcNow(), ExpiresUtc = _time.GetUtcNow().AddMinutes(minutes) };

    [Fact]
    public void Allowed_helper_keeps_running_with_info_event()
    {
        _inspector.Add(7, Export);
        Monitor().OnNewProcess(7);

        Assert.False(_inspector.Processes[7].Terminated);
        var e = Assert.Single(_log.Entries);
        Assert.Equal(EventIds.ChildAllowed, e.Id);
        Assert.Contains(Export, e.Message, StringComparison.Ordinal);
        Assert.Contains(@"PC\bediener", e.Message, StringComparison.Ordinal);
        Assert.Contains("appa", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_not_on_the_list_is_terminated_with_warning()
    {
        _inspector.Add(8, Cmd);
        Monitor().OnNewProcess(8);

        Assert.True(_inspector.Processes[8].Terminated);
        var e = Assert.Single(_log.Entries);
        Assert.Equal(EventIds.ChildTerminated, e.Id);
        Assert.Contains(Cmd, e.Message, StringComparison.Ordinal);
        Assert.Contains("Nicht auf der Liste", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Changed_file_is_terminated_with_reason_checksum()
    {
        _inspector.Add(9, Updater);
        _inspector.FailVerify.Add(Updater);
        Monitor().OnNewProcess(9);

        Assert.True(_inspector.Processes[9].Terminated);
        Assert.Contains("Prüfsumme stimmt nicht", Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Grandchild_is_checked_like_every_process_in_the_job()
    {
        _inspector.Add(7, Export);
        _inspector.Add(11, @"C:\Users\Public\evil.exe", parent: Export);
        var monitor = Monitor();
        monitor.OnNewProcess(7);
        monitor.OnNewProcess(11);

        Assert.False(_inspector.Processes[7].Terminated);
        Assert.True(_inspector.Processes[11].Terminated);
    }

    [Fact]
    public void The_application_itself_is_not_checked()
    {
        Monitor().OnNewProcess(Root);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void Path_compare_ignores_case()
    {
        _inspector.Add(7, Export.ToUpperInvariant());
        Monitor().OnNewProcess(7);
        Assert.False(_inspector.Processes[7].Terminated);
    }

    [Fact]
    public void System_console_host_is_always_allowed()
    {
        _inspector.Add(12, Conhost);
        Monitor(AllowlistApp with { AllowedChildren = [AllowlistApp.AllowedChildren![0]] }).OnNewProcess(12);

        Assert.False(_inspector.Processes[12].Terminated);
        Assert.Equal(EventIds.ChildAllowed, Assert.Single(_log.Entries).Id);
    }

    [Fact]
    public void Conhost_elsewhere_is_terminated()
    {
        _inspector.Add(13, @"C:\Users\Public\conhost.exe");
        Monitor().OnNewProcess(13);
        Assert.True(_inspector.Processes[13].Terminated);
    }

    // Fail-closed: every error in the check terminates the process.
    [Fact]
    public void Unreadable_path_is_terminated()
    {
        _inspector.Add(14, null);
        Monitor().OnNewProcess(14);
        Assert.True(_inspector.Processes[14].Terminated);
    }

    [Fact]
    public void Exception_in_the_check_terminates()
    {
        _inspector.Add(15, Export);
        _inspector.ThrowOnVerify = true;
        Monitor().OnNewProcess(15);

        Assert.True(_inspector.Processes[15].Terminated);
        Assert.Contains("Prüfung fehlgeschlagen", Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_that_cannot_be_opened_is_reported_as_error()
    {
        Monitor().OnNewProcess(99); // gone already or no access
        Assert.Equal(EventIds.JobMonitorFailed, Assert.Single(_log.Entries).Id);
    }

    [Fact]
    public void Failed_termination_is_visible_in_the_event()
    {
        _inspector.Add(16, Cmd);
        _inspector.Processes[16].CanTerminate = false;
        Monitor().OnNewProcess(16);
        Assert.Contains("beendet: NEIN", Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------ learn mode

    [Fact]
    public void Learn_mode_records_and_terminates_nothing()
    {
        _inspector.Add(20, Cmd, parent: Export);
        _inspector.Add(21, Export);
        var monitor = Monitor(learn: Learning());
        monitor.OnNewProcess(20);
        monitor.OnNewProcess(21);

        Assert.False(_inspector.Processes[20].Terminated);
        Assert.False(_inspector.Processes[21].Terminated);
        Assert.Equal([Cmd, Export], _recorder.Recorded.Select(r => r.Exe));
        Assert.Equal(Export, _recorder.Recorded[0].Parent);
        Assert.All(_log.Entries, e => Assert.Equal(EventIds.ChildRecorded, e.Id));
    }

    // Spec scenario «Zeitablauf»: also in an instance that is still running.
    [Fact]
    public void After_expiry_the_allowlist_applies_again_in_the_running_instance()
    {
        var monitor = Monitor(learn: Learning(5));
        _inspector.Add(22, Cmd);
        _inspector.Add(23, Cmd);
        monitor.OnNewProcess(22);
        _time.Advance(TimeSpan.FromMinutes(6));
        monitor.OnNewProcess(23);

        Assert.False(_inspector.Processes[22].Terminated);
        Assert.True(_inspector.Processes[23].Terminated);
        Assert.Single(_log.Entries, e => e.Id == EventIds.LearnModeEnded);
    }

    [Theory]
    [InlineData(ChildProcessMode.Blocked, true)]
    [InlineData(ChildProcessMode.All, false)]
    public void After_expiry_the_configured_mode_applies(ChildProcessMode mode, bool terminated)
    {
        var app = AllowlistApp with { ChildProcesses = mode, AllowedChildren = null };
        var monitor = Monitor(app, Learning(5));
        _time.Advance(TimeSpan.FromMinutes(6));
        _inspector.Add(24, Export);
        monitor.OnNewProcess(24);

        Assert.Equal(terminated, _inspector.Processes[24].Terminated);
    }

    [Fact]
    public void Recording_failure_in_learn_mode_terminates_nothing()
    {
        _recorder.Throw = true;
        _inspector.Add(25, Export);
        Monitor(learn: Learning()).OnNewProcess(25);

        Assert.False(_inspector.Processes[25].Terminated);
        Assert.Equal(EventIds.JobMonitorFailed, Assert.Single(_log.Entries).Id);
    }

    [Theory]
    [InlineData(ChildProcessMode.Blocked, false, false)]
    [InlineData(ChildProcessMode.All, false, false)]
    [InlineData(ChildProcessMode.Allowlist, false, true)]
    [InlineData(ChildProcessMode.Blocked, true, true)]
    [InlineData(ChildProcessMode.All, true, true)]
    public void Monitored_job_only_for_allowlist_or_learning(ChildProcessMode mode, bool learning, bool expected) =>
        Assert.Equal(expected, JobMonitor.IsNeeded(AllowlistApp with { ChildProcesses = mode }, learning ? Learning() : null));

    // ------------------------------------------------ fakes

    private sealed class FakeProcess(int id, string? path, string? parent) : IChildProcess
    {
        public int Id { get; } = id;

        public string? Path { get; } = path;

        public string? ParentPath { get; } = parent;

        public bool Terminated { get; private set; }

        public bool CanTerminate { get; set; } = true;

        public bool Terminate()
        {
            Terminated = CanTerminate;
            return CanTerminate;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeInspector : IChildProcessInspector
    {
        public Dictionary<int, FakeProcess> Processes { get; } = [];

        public HashSet<string> FailVerify { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowOnVerify { get; set; }

        public void Add(int id, string? path, string? parent = null) => Processes[id] = new FakeProcess(id, path, parent);

        public IChildProcess? Open(int processId) => Processes.GetValueOrDefault(processId);

        public IntegrityResult Verify(AllowedChild entry, string path) =>
            ThrowOnVerify ? throw new IOException("gesperrt")
            : FailVerify.Contains(path) ? IntegrityResult.Fail("SHA-256 weicht ab.") : IntegrityResult.Ok("ok");

        public (string? Thumbprint, string Sha256) Fingerprint(string path) => (null, new string('D', 64));

        public bool IsSystemConsoleHost(string path) => string.Equals(path, Conhost, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeRecorder : ILearnRecorder
    {
        public List<(string Exe, string? Parent)> Recorded { get; } = [];

        public bool Throw { get; set; }

        public void Record(string appId, string exe, string? thumbprint, string sha256, string? parentExe, DateTimeOffset nowUtc)
        {
            if (Throw)
            {
                throw new InvalidOperationException("nicht geschützt");
            }

            Recorded.Add((exe, parentExe));
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<(EventId Id, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, formatter(state, exception)));
    }
}
