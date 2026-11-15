using AppStart.Service.Launch;
using Microsoft.Extensions.Time.Testing;

namespace AppStart.Service.Tests;

public class SessionGateTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly SessionGate _gate;

    public SessionGateTests() => _gate = new SessionGate(_time, TimeSpan.FromSeconds(2));

    [Fact]
    public void Second_request_of_same_session_is_busy_while_first_runs()
    {
        using var first = _gate.TryEnter(1);
        Assert.NotNull(first);
        Assert.Null(_gate.TryEnter(1));
    }

    [Fact]
    public void Other_sessions_are_independent()
    {
        using var first = _gate.TryEnter(1);
        using var other = _gate.TryEnter(2);
        Assert.NotNull(other);
    }

    [Fact]
    public void Cooldown_after_each_request()
    {
        _gate.TryEnter(1)!.Dispose();

        Assert.Null(_gate.TryEnter(1));
        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Null(_gate.TryEnter(1));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotNull(_gate.TryEnter(1));
    }

    [Fact]
    public void Double_dispose_does_not_extend_or_break_the_gate()
    {
        var lease = _gate.TryEnter(1)!;
        lease.Dispose();
        _time.Advance(TimeSpan.FromSeconds(2));
        lease.Dispose();
        Assert.NotNull(_gate.TryEnter(1));
    }
}
