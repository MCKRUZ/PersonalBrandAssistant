using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.ChannelAnalytics.Dtos;
using PBA.Application.Features.ChannelAnalytics.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.ChannelAnalytics;

public class GetChannelAnalyticsHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly DateOnly Day1 = new(2026, 6, 1);

    private static ChannelMetricSnapshot Account(Platform p, DateOnly date, Dictionary<string, long> metrics) =>
        new() { Platform = p, SnapshotDate = date, Scope = SnapshotScope.Account, VideoId = "", Metrics = metrics };

    private static PlatformCredential AnalyticsCred(Platform p, bool active = true) =>
        new() { Platform = p, Purpose = CredentialPurpose.Analytics, IsActive = active, EncryptedAccessToken = "enc" };

    private static async Task<ChannelAnalyticsDto> RunAsync(ApplicationDbContext db, Platform platform, DateOnly from, DateOnly to)
    {
        var result = await new GetChannelAnalytics.Handler(db).Handle(new GetChannelAnalytics.Query(platform, from, to), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task GetChannelAnalytics_BuildsKpis_LatestCumulativeWithDeltaPct()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
        db.ChannelMetricSnapshots.AddRange(
            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 100 }),
            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 110 }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(1));

        var subs = dto.Kpis.Single(k => k.Key == "subscribers");
        Assert.Equal(110, subs.Value);              // latest cumulative
        Assert.Equal(10, subs.DeltaPct);            // (110-100)/100 * 100
        Assert.Equal(Day1.AddDays(1), dto.AsOf);
    }

    [Fact]
    public async Task GetChannelAnalytics_TrendSeries_AreDeltasBetweenConsecutiveSnapshots()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
        db.ChannelMetricSnapshots.AddRange(
            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 100 }),
            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 105 }),
            Account(Platform.YouTube, Day1.AddDays(2), new() { ["subscribers"] = 111 }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(2));

        var series = dto.Trends.Single(t => t.Metric == "subscribers");
        Assert.Equal([5, 6], series.Points.Select(p => p.Value));   // cumulative [100,105,111] -> deltas [+5,+6]
        Assert.Equal(Day1.AddDays(1), series.Points[0].Date);       // first day seeds the baseline, not a point
    }

    [Fact]
    public async Task GetChannelAnalytics_EngagementRate_UsesReachWhenPresent()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.Instagram));
        db.ChannelMetricSnapshots.Add(Account(Platform.Instagram, Day1, new()
        {
            ["followers"] = 9999, ["reach"] = 1000, ["total_interactions"] = 50
        }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.Instagram, Day1, Day1);

        // reach present -> interactions/reach = 50/1000, NOT interactions/followers.
        Assert.Equal(0.05, dto.Kpis.Single(k => k.Key == "engagement_rate").Rate);
    }

    [Fact]
    public async Task GetChannelAnalytics_EngagementRate_FallsBackToFollowers_WhenNoReach()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.TikTok));
        db.ChannelMetricSnapshots.Add(Account(Platform.TikTok, Day1, new()
        {
            ["followers"] = 1000, ["likes"] = 40
        }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.TikTok, Day1, Day1);

        // no reach -> interactions/followers = 40/1000.
        Assert.Equal(0.04, dto.Kpis.Single(k => k.Key == "engagement_rate").Rate);
    }

    [Fact]
    public async Task GetChannelAnalytics_TrendSeries_HandlesNegativeDeltas_SubscribersLost()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
        db.ChannelMetricSnapshots.AddRange(
            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 111 }),
            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 105 }));   // lost 6
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(1));

        Assert.Equal(-6, dto.Trends.Single(t => t.Metric == "subscribers").Points.Single().Value);
    }

    [Fact]
    public async Task GetChannelAnalytics_EngagementRate_PrefersTotalInteractions_NoDoubleCount()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.Instagram));
        db.ChannelMetricSnapshots.Add(Account(Platform.Instagram, Day1, new()
        {
            ["reach"] = 1000, ["total_interactions"] = 50, ["likes"] = 10, ["comments"] = 5   // must use 50, not 65
        }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.Instagram, Day1, Day1);

        Assert.Equal(0.05, dto.Kpis.Single(k => k.Key == "engagement_rate").Rate);
    }

    [Fact]
    public async Task GetChannelAnalytics_EngagementRate_NullWhenNoInteractions()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
        db.ChannelMetricSnapshots.Add(Account(Platform.YouTube, Day1, new() { ["subscribers"] = 1000 }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1);

        Assert.DoesNotContain(dto.Kpis, k => k.Key == "engagement_rate");   // no interaction metrics -> no card
    }

    [Fact]
    public async Task GetChannelAnalytics_EngagementRate_NullWhenDenominatorZero()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.Instagram));
        db.ChannelMetricSnapshots.Add(Account(Platform.Instagram, Day1, new()
        {
            ["reach"] = 0, ["total_interactions"] = 50
        }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.Instagram, Day1, Day1);

        Assert.DoesNotContain(dto.Kpis, k => k.Key == "engagement_rate");   // divide-by-zero -> no card
    }

    [Fact]
    public async Task GetChannelAnalytics_ResolvesStatus_PrefersActiveOverStaleInactive()
    {
        await using var db = CreateContext();
        // A reconnect left a stale inactive row beside the new active one (same platform + purpose).
        db.PlatformCredentials.AddRange(AnalyticsCred(Platform.YouTube, active: false), AnalyticsCred(Platform.YouTube, active: true));
        db.ChannelMetricSnapshots.Add(Account(Platform.YouTube, Day1, new() { ["subscribers"] = 1 }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1);

        Assert.Equal(ConnectionStatus.Connected, dto.Status);   // active wins, not the stale inactive row
    }

    [Theory]
    [InlineData(true, ConnectionStatus.Connected)]
    [InlineData(false, ConnectionStatus.ReconnectRequired)]
    public async Task GetChannelAnalytics_ResolvesConnectionStatus_FromAnalyticsCredential(bool active, ConnectionStatus expected)
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube, active));
        db.ChannelMetricSnapshots.Add(Account(Platform.YouTube, Day1, new() { ["subscribers"] = 1 }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1);

        Assert.Equal(expected, dto.Status);
    }

    [Fact]
    public async Task GetChannelAnalytics_NotConnected_ReturnsEmptyWithNotConnectedStatus()
    {
        await using var db = CreateContext();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1);

        Assert.Equal(ConnectionStatus.NotConnected, dto.Status);
        Assert.Empty(dto.Kpis);
        Assert.Empty(dto.Trends);
        Assert.Empty(dto.RecentPosts);
        Assert.Null(dto.AsOf);
    }

    [Fact]
    public async Task GetChannelAnalytics_RecentPosts_AreLatestDayVideoSnapshots()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
        db.ChannelMetricSnapshots.AddRange(
            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 1 }),
            new ChannelMetricSnapshot { Platform = Platform.YouTube, SnapshotDate = Day1, Scope = SnapshotScope.Video, VideoId = "old", Metrics = new Dictionary<string, long> { ["views"] = 1 } },
            new ChannelMetricSnapshot { Platform = Platform.YouTube, SnapshotDate = Day1.AddDays(1), Scope = SnapshotScope.Video, VideoId = "new", VideoTitle = "New", Metrics = new Dictionary<string, long> { ["views"] = 5 } });
        await db.SaveChangesAsync();

        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(1));

        var post = Assert.Single(dto.RecentPosts);
        Assert.Equal("new", post.VideoId);   // only the latest day's video rows
    }
}
