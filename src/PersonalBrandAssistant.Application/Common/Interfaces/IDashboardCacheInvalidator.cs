namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Allows cache invalidation for dashboard data with rate limiting.</summary>
public interface IDashboardCacheInvalidator
{
    Task<bool> TryInvalidateAsync(CancellationToken ct);
}
