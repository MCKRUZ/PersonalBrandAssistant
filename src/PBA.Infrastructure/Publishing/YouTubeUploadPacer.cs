using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Publishing;

/// <summary>
/// Spreads YouTube uploads across days so a batch cannot exhaust the day's API quota.
///
/// An upload costs 1,600 units of a 10,000-unit daily allowance shared with everything else on the
/// same Google project — including PBA's own analytics polling. Overrun does not fail politely: the
/// API returns quotaExceeded for the REST OF THE DAY, so on a launch day the long-form video that
/// matters most is the one that cannot be uploaded. Hence a deliberate budget with headroom rather
/// than racing to empty the queue.
///
/// Capacity is counted from the clips already booked onto each day rather than a spend ledger. A
/// ledger would be a second source of truth that drifts the first time an upload fails, is retried,
/// or is cancelled; the bookings ARE the plan.
/// </summary>
public sealed class YouTubeUploadPacer(
    IAppDbContext db,
    IOptionsMonitor<YouTubePublishingOptions> options,
    TimeProvider clock) : IUploadPacer
{
    // YouTube's quota day starts at midnight Pacific. Read as UTC-8 (PST) even during daylight
    // saving: that rolls the day over an hour LATE, so a handover near the boundary is charged to
    // the day that is definitely still open rather than being handed an allowance that has not
    // actually reset yet.
    private static readonly TimeSpan PacificOffset = TimeSpan.FromHours(-8);

    public Platform Platform => Platform.YouTube;

    public async Task<DateTimeOffset?> NextHandoverSlotAsync(DateTimeOffset goLiveAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (goLiveAt <= now)
            return null;

        var budget = Math.Max(1, options.CurrentValue.DailyUploadBudget);

        // Everything already booked from today onwards. Small by construction — a campaign is tens
        // of clips — so grouping in memory beats expressing a Pacific-day bucket in SQL.
        var booked = await db.Contents
            .Where(c => c.PrimaryPlatform == Platform.YouTube
                        && c.HandoverAt != null
                        && c.HandoverAt >= now.AddDays(-1)
                        && !c.IsDeleted)
            .Select(c => c.HandoverAt!.Value)
            .ToListAsync(ct);

        var perDay = booked
            .GroupBy(PacificDay)
            .ToDictionary(g => g.Key, g => g.Count());

        // Walk forward a day at a time from today until the go-live moment, taking the first day
        // with room. Earliest-possible is the right bias: the video is private until its publish
        // time either way, and every day of slack is a day for a failed upload to be noticed.
        for (var candidate = now; candidate < goLiveAt; candidate = NextPacificMidnightUtc(candidate))
        {
            var day = PacificDay(candidate);
            if (perDay.GetValueOrDefault(day) >= budget)
                continue;

            var slot = candidate > now ? candidate : now;
            if (slot >= goLiveAt)
                break;

            return slot;
        }

        return null;
    }

    private static DateOnly PacificDay(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToUniversalTime().Add(PacificOffset).DateTime);

    /// <summary>The instant the next quota day opens, expressed in UTC.</summary>
    private static DateTimeOffset NextPacificMidnightUtc(DateTimeOffset from)
    {
        var pacific = from.ToUniversalTime().Add(PacificOffset);
        var nextMidnight = pacific.Date.AddDays(1);
        return new DateTimeOffset(nextMidnight, TimeSpan.Zero).Subtract(PacificOffset);
    }
}
