using System.Text;
using AppStart.Core.Configuration;
using AppStart.Core.Protocol;
using AppStart.Service.Launch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace AppStart.Service.Tests;

public class LaunchRequestHandlerTests
{
    private static readonly AppEntry App = new()
    {
        Id = "app-a",
        Name = "Applikation A",
        Exe = @"C:\Program Files\Hersteller\AppA\AppA.exe",
        WorkingDir = @"C:\Program Files\Hersteller\AppA",
        Check = IntegrityCheck.Sha256,
        Sha256 = new string('A', 64),
    };

    private readonly FakeTimeProvider _time = new();
    private readonly FakeConfig _config = new();
    private readonly FakeStarter _starter = new();
    private readonly ListLogger _log = new();
    private readonly LaunchRequestHandler _handler;
    private readonly RequestContext _ctx = new(SessionId: 3, User: @"PC\bediener");

    public LaunchRequestHandlerTests() =>
        _handler = new LaunchRequestHandler(_config, _starter, new SessionGate(_time, TimeSpan.FromSeconds(2)), _log, () => 4711);

    private string Send(string message) =>
        _handler.Handle(Encoding.ASCII.GetBytes(message), _ctx) is { } r ? Encoding.ASCII.GetString(r) : "<none>";

    [Fact]
    public void Released_app_is_started_in_the_requesting_session()
    {
        Assert.Equal("OK", Send("START app-a"));
        Assert.Equal((App, 3u), Assert.Single(_starter.Calls));
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(EventIds.LaunchSucceeded, entry.Id);
        Assert.Contains(@"PC\bediener", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Session: 3", entry.Message, StringComparison.Ordinal);
        Assert.Contains("app-a", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_app_is_denied_and_logged()
    {
        Assert.Equal("DENIED unknown-app 4711", Send("START app-b"));
        Assert.Empty(_starter.Calls);
        Assert.Equal(EventIds.DeniedUnknownApp, Assert.Single(_log.Entries).Id);
    }

    [Fact]
    public void Changed_exe_is_denied_without_details_to_the_client()
    {
        _starter.Next = new StartResult(StartOutcome.IntegrityFailed, "SHA-256 weicht ab: erwartet AAAA, gefunden BBBB.");

        var answer = Send("START app-a");

        Assert.Equal("DENIED integrity 4711", answer);
        Assert.DoesNotContain("AAAA", answer, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", answer, StringComparison.Ordinal);
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(EventIds.DeniedIntegrity, entry.Id);
        Assert.Contains("BBBB", entry.Message, StringComparison.Ordinal);   // details go to the event log
        Assert.Contains("4711", entry.Message, StringComparison.Ordinal);   // with the incident id
    }

    [Fact]
    public void Unprotected_or_invalid_config_blocks_every_start()
    {
        _config.Result = ConfigLoadResult.Fail(@"C:\ProgramData\AppStart ist nicht geschützt: Benutzer hat Schreibrechte.");

        Assert.Equal("DENIED config 4711", Send("START app-a"));
        Assert.Empty(_starter.Calls);
        Assert.Equal(EventIds.DeniedConfig, Assert.Single(_log.Entries).Id);
    }

    [Fact]
    public void Config_is_read_on_every_request()
    {
        Send("START app-a");
        _time.Advance(TimeSpan.FromSeconds(3));
        Send("START app-a");
        Assert.Equal(2, _config.Loads);
    }

    [Fact]
    public void Second_request_within_cooldown_is_busy()
    {
        Assert.Equal("OK", Send("START app-a"));
        Assert.Equal("DENIED busy 4711", Send("START app-a"));
        Assert.Single(_starter.Calls);
    }

    [Fact]
    public void Launch_error_is_denied_as_error()
    {
        _starter.Next = new StartResult(StartOutcome.Failed, "CreateProcessAsUser: Zugriff verweigert");
        Assert.Equal("DENIED error 4711", Send("START app-a"));
        Assert.Equal(EventIds.LaunchFailed, Assert.Single(_log.Entries).Id);
    }

    [Fact]
    public void Exception_in_starter_ends_in_logged_generic_refusal()
    {
        _starter.Throw = true;
        Assert.Equal("DENIED error 4711", Send("START app-a"));
        Assert.Equal(EventIds.LaunchFailed, Assert.Single(_log.Entries).Id);
    }

    [Theory]
    [InlineData("START app-a C:\\Windows\\System32\\cmd.exe")]
    [InlineData("start app-a")]
    [InlineData("START APP-A")]
    [InlineData("")]
    public void Malformed_request_is_dropped_and_logged(string message)
    {
        Assert.Equal("<none>", Send(message));
        Assert.Empty(_starter.Calls);
        Assert.Equal(0, _config.Loads);
        Assert.Equal(EventIds.InvalidRequest, Assert.Single(_log.Entries).Id);
    }

    [Fact]
    public void Default_incident_ids_are_ten_digits()
    {
        var handler = new LaunchRequestHandler(_config, _starter, new SessionGate(_time, TimeSpan.Zero), _log);
        var bytes = handler.Handle("START app-b"u8, _ctx)!;
        Assert.True(PipeProtocol.TryParseResponse(bytes, out var r));
        Assert.InRange(r.IncidentId, 1_000_000_000, PipeProtocol.MaxIncidentId);
    }

    private sealed class FakeConfig : IConfigSource
    {
        public ConfigLoadResult Result { get; set; } = ConfigLoadResult.Ok(new AppsConfig { Version = 1, Apps = [App] });

        public int Loads { get; private set; }

        public ConfigLoadResult Load()
        {
            Loads++;
            return Result;
        }
    }

    private sealed class FakeStarter : IAppStarter
    {
        public List<(AppEntry, uint)> Calls { get; } = [];

        public StartResult Next { get; set; } = new(StartOutcome.Started, "SHA-256 stimmt.", 1234);

        public bool Throw { get; set; }

        public StartResult Start(AppEntry app, uint sessionId)
        {
            Calls.Add((app, sessionId));
            return Throw ? throw new InvalidOperationException("boom") : Next;
        }
    }

    private sealed class ListLogger : ILogger<LaunchRequestHandler>
    {
        public List<(EventId Id, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, formatter(state, exception)));
    }
}
