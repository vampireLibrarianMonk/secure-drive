using EmergencyArchive.Core;
using Xunit;

namespace EmergencyArchive.Core.Tests;

public class AttemptRateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstAttempt_IsNeverBlocked()
    {
        var limiter = new AttemptRateLimiter();

        // Before any attempt there is no delay at all.
        Assert.Equal(TimeSpan.Zero, limiter.RemainingDelay(T0));

        // The first attempt schedules the next attempt 5 seconds out.
        DateTimeOffset earliestNext = limiter.RegisterAttempt(T0);
        Assert.Equal(T0 + AttemptRateLimiter.MinimumInterval, earliestNext);
    }

    [Fact]
    public void SecondAttempt_WithinInterval_MustWait()
    {
        var limiter = new AttemptRateLimiter();
        limiter.RegisterAttempt(T0);

        TimeSpan remaining = limiter.RemainingDelay(T0 + TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(3), remaining);
    }

    [Fact]
    public void SecondAttempt_AfterInterval_MayProceed()
    {
        var limiter = new AttemptRateLimiter();
        limiter.RegisterAttempt(T0);

        TimeSpan remaining = limiter.RemainingDelay(T0 + AttemptRateLimiter.MinimumInterval);

        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void Interval_IsFiveSeconds()
    {
        Assert.Equal(5, AttemptRateLimiter.MinimumInterval.TotalSeconds);
    }

    [Fact]
    public void RegisterAttempt_MovesTheWindow()
    {
        var limiter = new AttemptRateLimiter();
        limiter.RegisterAttempt(T0);
        limiter.RegisterAttempt(T0 + TimeSpan.FromSeconds(4)); // the new attempt restarts the window

        Assert.Equal(TimeSpan.FromSeconds(5), limiter.RemainingDelay(T0 + TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.Zero, limiter.RemainingDelay(T0 + TimeSpan.FromSeconds(9)));
    }
}
