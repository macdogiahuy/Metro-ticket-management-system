using System.Collections.Concurrent;

namespace Metro.API.Middleware;

/// <summary>BE-17: thread-safe fixed-window counter, injectable clock for deterministic tests.</summary>
public sealed class FixedWindowRateLimiter(int permitLimit, TimeSpan window, Func<DateTimeOffset>? clock = null)
{
    private readonly ConcurrentDictionary<string, (long Window, int Count)> _counters = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public bool TryAcquire(string key)
    {
        if (permitLimit <= 0) return true;
        var tick = _clock().UtcTicks / window.Ticks;
        while (true)
        {
            var current = _counters.GetOrAdd(key, (0, 0));
            if (current.Window != tick)
            {
                if (_counters.TryUpdate(key, (tick, 1), current)) return true;
                continue;
            }
            if (current.Count >= permitLimit) return false;
            if (_counters.TryUpdate(key, (tick, current.Count + 1), current)) return true;
        }
    }
}
