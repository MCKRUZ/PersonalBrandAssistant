using Microsoft.EntityFrameworkCore;
using PBA.Application.Features.ChannelAnalytics.Dtos;
using PBA.Application.Features.ChannelAnalytics.Queries;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Application.Tests.Features.ChannelAnalytics;

public class GetAnalyticsOverviewHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly DateOnly Day1 = new(2026, 6, 1);

    private static ChannelMetricSnapshot Account(Platform p, DateOnly date, Dictionary<string, long> metrics) =>
        new() { Platform = p, SnapshotDate = date, Scope = SnapshotScope.Account, VideoId = "", Metrics = metrics };

    private static PlatformCredential AnalyticsCred(Platform p, bool active = true) =>
        new() { Platform = p, Purpose = CredentialPurpose.Analytics, IsActive = active, EncryptedAccessToken = "enc" };

    private static async Task<OverviewDto> RunAsync(ApplicationDbContext db)
    {
        var result = await new GetAnalyticsOverview.Handler(db).Handle(new GetAnalyticsOverview.Query(Day1, Day1.AddDays(2)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task GetAnalyticsOverview_AggregatesTotalAudience_AcrossConnectedChannels()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.AddRange(AnalyticsCred(Platform.YouTube), AnalyticsCred(Platform.Instagram), AnalyticsCred(Platform.TikTok, active: false));
        db.ChannelMetricSnapshots.AddRange(
            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 1000 }),
            Account(Platform.Instagram, Day1, new() { ["followers"] = 500 }),
            Account(Platform.TikTok, Day1, new() { ["followers"] = 9999 }));   // inactive -> excluded
        await db.SaveChangesAsync();

        var dto = await RunAsync(db);

        Assert.Equal(1500, dto.TotalAudience);   // YouTube 1000 + Instagram 500; TikTok (ReconnectRequired) excluded
        Assert.Equal(1500, dto.CombinedKpis.Single(k => k.Key == "total_audience").Value);
        Assert.Equal(ConnectionStatus.ReconnectRequired, dto.Channels.Single(c => c.Platform == Platform.TikTok).Status);
    }

    [Fact]
    public async Task GetAnalyticsOverview_BuildsPerPlatformFollowerSparklines()
    {
        await using var db = CreateContext();
        db.PlatformCredentials.Add(AnalyticsCred(Platform.Instagram));
        db.ChannelMetricSnapshots.AddRange(
            Account(Platform.Instagram, Day1, new() { ["followers"] = 500 }),
            Account(Platform.Instagram, Day1.AddDays(1), new() { ["followers"] = 508 }),
            Account(Platform.Instagram, Day1.AddDays(2), new() { ["followers"] = 515 }));
        await db.SaveChangesAsync();

        var dto = await RunAsync(db);

        var ig = dto.Channels.Single(c => c.Platform == Platform.Instagram);
        Assert.Equal(515, ig.Followers);
        Assert.Equal([8, 7], ig.FollowerSparkline.Select(p => p.Value));   // deltas of [500,508,515]
    }
}
