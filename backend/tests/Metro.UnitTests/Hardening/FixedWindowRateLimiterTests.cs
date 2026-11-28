using Metro.API.Middleware;

namespace Metro.UnitTests.Hardening;

public sealed class FixedWindowRateLimiterTests
{
    [Fact]
    public void Allows_up_to_limit_then_rejects_within_window()
    {
        var now = DateTimeOffset.Parse("2026-11-28T10:00:00+00:00");
        var clock = new Func<DateTimeOffset>(() => now);
        var limiter = new FixedWindowRateLimiter(2, TimeSpan.FromSeconds(60), clock);

        Assert.True(limiter.TryAcquire("ip:1"));
        Assert.True(limiter.TryAcquire("ip:1"));
        Assert.False(limiter.TryAcquire("ip:1"));
        Assert.True(limiter.TryAcquire("ip:2"));
    }

    [Fact]
    public void Resets_after_window_and_other_keys_are_independent()
    {
        var now = DateTimeOffset.Parse("2026-11-28T10:00:00+00:00");
        var clock = new Func<DateTimeOffset>(() => now);
        var limiter = new FixedWindowRateLimiter(1, TimeSpan.FromSeconds(10), clock);

        Assert.True(limiter.TryAcquire("k"));
        Assert.False(limiter.TryAcquire("k"));
        now = now.AddSeconds(11);
        Assert.True(limiter.TryAcquire("k"));
    }
}
