using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ChannelAnalytics.Dtos;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.ChannelAnalytics;

// Shared snapshot-read logic for the two snapshot-backed queries. Cumulative counts in; deltas/KPIs/rates
// derived here at read time (nothing fractional is ever persisted). Kept small and internal (YAGNI).
internal static class ChannelAnalyticsReadHelper
{
    private static readonly string[] InteractionKeys = ["likes", "comments", "shares", "saves"];

    public static async Task<ConnectionStatus> ResolveStatusAsync(
        IAppDbContext db, Platform platform, CancellationToken ct)
    {
        // Prefer the active credential: a reconnect can leave a stale inactive row beside the new active one
        // (the unique index only constrains active rows), and an unordered FirstOrDefault could return the
        // stale one and wrongly report ReconnectRequired.
        var credential = await db.PlatformCredentials
            .Where(c => c.Platform == platform && c.Purpose == CredentialPurpose.Analytics)
            .OrderByDescending(c => c.IsActive)
            .FirstOrDefaultAsync(ct);

        return credential is null
            ? ConnectionStatus.NotConnected
            : credential.IsActive ? ConnectionStatus.Connected : ConnectionStatus.ReconnectRequired;
    }

    public static async Task<List<ChannelMetricSnapshot>> LoadAccountSnapshotsAsync(
        IAppDbContext db, Platform platform, DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.ChannelMetricSnapshots
            .Where(s => s.Platform == platform && s.Scope == SnapshotScope.Account
                && s.SnapshotDate >= from && s.SnapshotDate <= to)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync(ct);

    public static long? AudienceValue(IReadOnlyDictionary<string, long> bag) =>
        bag.TryGetValue("followers", out var f) ? f
        : bag.TryGetValue("subscribers", out var s) ? s
        : null;

    // interactions / reach (Instagram) else interactions / followers|subscribers (YouTube, TikTok).
    // null when interactions can't be computed or the denominator is zero/absent.
    public static double? EngagementRate(IReadOnlyDictionary<string, long> bag)
    {
        long? interactions = null;
        if (bag.TryGetValue("total_interactions", out var ti))
        {
            interactions = ti;
        }
        else
        {
            long sum = 0;
            var any = false;
            foreach (var key in InteractionKeys)
                if (bag.TryGetValue(key, out var v)) { sum += v; any = true; }
            if (any) interactions = sum;
        }

        if (interactions is null)
            return null;

        long? denominator = bag.TryGetValue("reach", out var reach) ? reach : AudienceValue(bag);
        if (denominator is null or 0)
            return null;

        return (double)interactions.Value / denominator.Value;
    }

    // One consecutive-delta point per snapshot after the first (the first seeds the baseline).
    public static IReadOnlyList<MetricPoint> DeltaSeries(IReadOnlyList<ChannelMetricSnapshot> ordered, string key)
    {
        var points = new List<MetricPoint>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1].Metrics.TryGetValue(key, out var p) ? p : 0;
            var curr = ordered[i].Metrics.TryGetValue(key, out var c) ? c : 0;
            points.Add(new MetricPoint(ordered[i].SnapshotDate, curr - prev));
        }
        return points;
    }

    public static IReadOnlyList<KpiCard> BuildKpis(IReadOnlyList<ChannelMetricSnapshot> ordered)
    {
        if (ordered.Count == 0)
            return [];

        var latest = ordered[^1].Metrics;
        var prior = ordered.Count >= 2 ? ordered[^2].Metrics : null;

        var cards = latest.Select(kv =>
        {
            double? deltaPct = null;
            if (prior is not null && prior.TryGetValue(kv.Key, out var prev) && prev != 0)
                deltaPct = (kv.Value - prev) / (double)prev * 100;
            return new KpiCard(kv.Key, Label(kv.Key), kv.Value, deltaPct, Rate: null);
        }).ToList();

        var rate = EngagementRate(latest);
        if (rate is not null)
            cards.Add(new KpiCard("engagement_rate", "Engagement Rate", 0, null, rate));

        return cards;
    }

    public static IReadOnlyList<TrendSeries> BuildTrends(IReadOnlyList<ChannelMetricSnapshot> ordered)
    {
        if (ordered.Count == 0)
            return [];

        return ordered[^1].Metrics.Keys
            .Select(key => new TrendSeries(key, DeltaSeries(ordered, key)))
            .ToList();
    }

    private static string Label(string key) =>
        string.Join(' ', key.Split('_')
            .Select(w => w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
}
