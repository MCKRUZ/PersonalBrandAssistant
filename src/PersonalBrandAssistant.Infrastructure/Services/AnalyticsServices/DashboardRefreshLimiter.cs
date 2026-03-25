namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// Thread-safe, singleton rate limiter for dashboard cache invalidation.
/// Uses Interlocked.CompareExchange to prevent concurrent refresh storms.
/// </summary>
internal sealed class DashboardRefreshLimiter
{
    private long _lastRefreshTicks;

    public bool TryAcquire(TimeProvider timeProvider, TimeSpan cooldown)
    {
        var nowTicks = timeProvider.GetUtcNow().UtcTicks;
        var previous = Interlocked.Read(ref _lastRefreshTicks);
        if (nowTicks - previous < cooldown.Ticks)
            return false;
        return Interlocked.CompareExchange(ref _lastRefreshTicks, nowTicks, previous) == previous;
    }
}
