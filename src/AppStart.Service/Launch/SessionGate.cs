namespace AppStart.Service.Launch;

/// <summary>
/// One request per session at a time and a short cooldown after each request (spec pipe-security,
/// concept "One request per session at a time, short lockout after each start").
/// </summary>
public sealed class SessionGate(TimeProvider time, TimeSpan cooldown)
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(2);

    private readonly Dictionary<uint, DateTimeOffset> _blockedUntil = [];
    private readonly HashSet<uint> _active = [];
    private readonly Lock _lock = new();

    /// <summary>Returns a lease when the session may proceed, or null when it is busy or cooling down.</summary>
    public IDisposable? TryEnter(uint sessionId)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_active.Contains(sessionId)
                || (_blockedUntil.TryGetValue(sessionId, out var until) && now < until))
            {
                return null;
            }

            _active.Add(sessionId);
            return new Lease(this, sessionId);
        }
    }

    private void Release(uint sessionId)
    {
        lock (_lock)
        {
            _active.Remove(sessionId);
            _blockedUntil[sessionId] = time.GetUtcNow() + cooldown;
        }
    }

    private sealed class Lease(SessionGate gate, uint sessionId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release(sessionId);
            }
        }
    }
}
