using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Security;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class ChannelMetricPollingServiceTests
{
    private static readonly Platform[] Platforms = [Platform.YouTube, Platform.Instagram, Platform.TikTok];

    private sealed class Harness
    {
        public required ChannelMetricPollingService Svc { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required Dictionary<Platform, Mock<IChannelAnalyticsService>> Services { get; init; }
        public required Dictionary<Platform, Mock<IOAuthProvider>> Providers { get; init; }
        public required Mock<ITokenEncryptor> Encryptor { get; init; }
    }

    private static Harness Build(ChannelAnalyticsOptions options, ApplicationDbContext? dbOverride = null)
    {
        var db = dbOverride ?? new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var services = new Dictionary<Platform, Mock<IChannelAnalyticsService>>();
        var providers = new Dictionary<Platform, Mock<IOAuthProvider>>();
        var encryptor = new Mock<ITokenEncryptor>();
        encryptor.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => $"enc:{s}");

        var collection = new ServiceCollection();
        collection.AddSingleton(db);
        collection.AddSingleton(encryptor.Object);
        foreach (var p in Platforms)
        {
            var svc = new Mock<IChannelAnalyticsService>();
            var prov = new Mock<IOAuthProvider>();
            prov.Setup(x => x.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(false);
            services[p] = svc;
            providers[p] = prov;
            collection.AddKeyedSingleton<IChannelAnalyticsService>(p, svc.Object);
            collection.AddKeyedSingleton<IOAuthProvider>(p, prov.Object);
        }
        var sp = collection.BuildServiceProvider();

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var monitor = new Mock<IOptionsMonitor<ChannelAnalyticsOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        return new Harness
        {
            Svc = new ChannelMetricPollingService(scopeFactory.Object, monitor.Object, NullLogger<ChannelMetricPollingService>.Instance),
            Db = db,
            Services = services,
            Providers = providers,
            Encryptor = encryptor
        };
    }

    private static ChannelAnalyticsOptions AllEnabled(int recentVideoCount = 50) => new()
    {
        RecentVideoCount = recentVideoCount,
        YouTubeEnabled = true,
        InstagramEnabled = true,
        TikTokEnabled = true
    };

    private static PlatformCredential AnalyticsCredential(Platform platform) => new()
    {
        Platform = platform,
        Purpose = CredentialPurpose.Analytics,
        IsActive = true,
        EncryptedAccessToken = "enc:token",
        EncryptedRefreshToken = "enc:refresh"
    };

    private static void SetupSuccessfulPoll(
        Mock<IChannelAnalyticsService> service, IReadOnlyList<VideoMetrics> videos, IReadOnlyDictionary<string, long>? account = null) =>
        service.Setup(s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChannelPollResult>.Success(new ChannelPollResult(
                new AccountMetrics(account ?? new Dictionary<string, long> { ["subscribers"] = 100 }, false),
                videos)));

    private static VideoMetrics Video(string id) =>
        new(id, $"title-{id}", new Dictionary<string, long> { ["views"] = 10 }, false);

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);
    private static DateOnly Today => DateOnly.FromDateTime(Now.LocalDateTime);

    [Fact]
    public async Task Poller_SkipsPlatform_WhenAccountSnapshotExistsForToday()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        h.Db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
        {
            Platform = Platform.YouTube, SnapshotDate = Today, Scope = SnapshotScope.Account, VideoId = ""
        });
        await h.Db.SaveChangesAsync();

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        h.Services[Platform.YouTube].Verify(
            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Poller_TriggersRefresh_WhenProviderNeedsRefresh()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();

        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OAuthRefreshResult.Success(new OAuthTokenResult("new-access", null, 3600, null, "scope")));
        SetupSuccessfulPoll(h.Services[Platform.YouTube], []);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        h.Providers[Platform.YouTube].Verify(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(await h.Db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.YouTube && s.Scope == SnapshotScope.Account));
    }

    [Fact]
    public async Task Poller_DeactivatesCredential_OnlyOnRevoked()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();

        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, "revoked"));

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        var cred = await h.Db.PlatformCredentials.SingleAsync(c => c.Platform == Platform.YouTube);
        Assert.False(cred.IsActive);
        h.Services[Platform.YouTube].Verify(
            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Poller_TransientRefreshFailure_SkipsRunWithoutDeactivating()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();

        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OAuthRefreshResult.Fail(RefreshFailureReason.Transient, "network"));

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        var cred = await h.Db.PlatformCredentials.SingleAsync(c => c.Platform == Platform.YouTube);
        Assert.True(cred.IsActive);   // Transient must NOT deactivate
        Assert.False(await h.Db.ChannelMetricSnapshots.AnyAsync());
        h.Services[Platform.YouTube].Verify(
            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Poller_PersistsRotatedRefreshToken_AfterRefresh()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();

        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OAuthRefreshResult.Success(new OAuthTokenResult("new-access", "rotated-refresh", 3600, null, "scope")));
        SetupSuccessfulPoll(h.Services[Platform.YouTube], []);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        var cred = await h.Db.PlatformCredentials.SingleAsync(c => c.Platform == Platform.YouTube);
        Assert.Equal("enc:rotated-refresh", cred.EncryptedRefreshToken);
        h.Encryptor.Verify(e => e.Encrypt("rotated-refresh"), Times.Once);
    }

    [Fact]
    public async Task Poller_WritesVideoRowsFirst_ThenAccountRowLast()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();
        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("a"), Video("b")]);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        var rows = await h.Db.ChannelMetricSnapshots.Where(s => s.Platform == Platform.YouTube && s.SnapshotDate == Today).ToListAsync();
        Assert.Equal(2, rows.Count(r => r.Scope == SnapshotScope.Video));
        Assert.Equal(1, rows.Count(r => r.Scope == SnapshotScope.Account));
        Assert.Equal(string.Empty, rows.Single(r => r.Scope == SnapshotScope.Account).VideoId);
    }

    // Throws precisely when the Account row is being persisted, letting the video write commit first.
    private sealed class FailOnAccountWriteDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<ChannelMetricSnapshot>()
                .Any(e => e.State == EntityState.Added && e.Entity.Scope == SnapshotScope.Account))
                throw new InvalidOperationException("simulated account-write failure");
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    // Models the one database rule the InMemory provider ignores and production enforces: Postgres
    // rejects a VideoTitle longer than its column. Without this the failure that actually stopped
    // three platforms for a fortnight cannot be reproduced in a test at all.
    private sealed class RejectLongTitleDbContext(DbContextOptions<ApplicationDbContext> options, int limit)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<ChannelMetricSnapshot>()
                .Any(e => e.State == EntityState.Added && e.Entity.VideoTitle is { } t && t.Length > limit))
                throw new InvalidOperationException($"22001: value too long for character varying({limit})");
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    // The promise the catch in PollAllAsync makes in so many words — "one platform's failure must not
    // stop the others" — and the reason all three platforms went quiet on the same day rather than
    // just the one with the bad data. Every platform in the run shares a database session, so rows a
    // failed platform left queued in it get re-attempted by the next platform's save, and fail again.
    // Instagram had the long caption; YouTube, whose titles cannot exceed 100 characters, died of it.
    [Fact]
    public async Task Poller_WhenOnePlatformsWriteFails_TheOthersStillRecord()
    {
        var db = new RejectLongTitleDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, limit: 500);
        var h = Build(AllEnabled(), db);

        // Instagram first, so its failure precedes YouTube's turn.
        db.PlatformCredentials.Add(AnalyticsCredential(Platform.Instagram));
        db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await db.SaveChangesAsync();

        SetupSuccessfulPoll(h.Services[Platform.Instagram],
            [new VideoMetrics("ig-1", new string('x', 600), new Dictionary<string, long> { ["views"] = 10 }, false)]);
        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("yt-1")]);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        Assert.True(
            await db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.YouTube && s.Scope == SnapshotScope.Account),
            "YouTube has nothing wrong with it and must still be captured when Instagram fails");
        Assert.True(
            await db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.YouTube && s.VideoId == "yt-1"),
            "YouTube's video rows must not be lost to another platform's bad data");
    }

    [Fact]
    public async Task Poller_MidWriteFailure_LeavesNoAccountRow()
    {
        var db = new FailOnAccountWriteDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var h = Build(AllEnabled(), db);
        db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await db.SaveChangesAsync();
        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("a"), Video("b")]);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);   // account write throws, caught, continues

        var rows = await db.ChannelMetricSnapshots.Where(s => s.Platform == Platform.YouTube).ToListAsync();
        Assert.Equal(2, rows.Count(r => r.Scope == SnapshotScope.Video));   // video rows committed first
        Assert.DoesNotContain(rows, r => r.Scope == SnapshotScope.Account);  // no completion sentinel -> re-poll next run
    }

    [Fact]
    public async Task Poller_ReRunAfterMidWriteFailure_SelfHeals_NoDuplicateVideoRows()
    {
        // Run 1 crashes on the account write (2 video rows, no sentinel). Run 2 (same DB, normal context)
        // must clear the stale video rows and write exactly 2 video + 1 account — not 4 video rows.
        var dbName = Guid.NewGuid().ToString();
        var failDb = new FailOnAccountWriteDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName).Options);
        var h1 = Build(AllEnabled(), failDb);
        failDb.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await failDb.SaveChangesAsync();
        SetupSuccessfulPoll(h1.Services[Platform.YouTube], [Video("a"), Video("b")]);
        await h1.Svc.PollAllAsync(Now, CancellationToken.None);

        var okDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName).Options);   // same in-memory store, healthy context
        var h2 = Build(AllEnabled(), okDb);
        SetupSuccessfulPoll(h2.Services[Platform.YouTube], [Video("a"), Video("b")]);
        await h2.Svc.PollAllAsync(Now, CancellationToken.None);

        var rows = await okDb.ChannelMetricSnapshots.Where(s => s.Platform == Platform.YouTube && s.SnapshotDate == Today).ToListAsync();
        Assert.Equal(2, rows.Count(r => r.Scope == SnapshotScope.Video));   // stale rows cleared, not duplicated
        Assert.Equal(1, rows.Count(r => r.Scope == SnapshotScope.Account));
    }

    [Fact]
    public async Task Poller_DeduplicatesVideoRows_ByVideoId()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();
        // Same VideoId twice (pagination overlap) must collapse to one row (Postgres unique-index safety).
        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("dup"), Video("dup"), Video("other")]);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        var videoRows = await h.Db.ChannelMetricSnapshots
            .Where(s => s.Platform == Platform.YouTube && s.Scope == SnapshotScope.Video).ToListAsync();
        Assert.Equal(2, videoRows.Count);
        Assert.Equal(1, videoRows.Count(r => r.VideoId == "dup"));
    }

    [Fact]
    public async Task Poller_CapsVideoSnapshots_AtRecentVideoCount()
    {
        var h = Build(AllEnabled(recentVideoCount: 3));
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();
        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("a"), Video("b"), Video("c"), Video("d"), Video("e")]);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        Assert.Equal(3, await h.Db.ChannelMetricSnapshots.CountAsync(s => s.Scope == SnapshotScope.Video));
    }

    [Fact]
    public async Task Poller_UsesHostLocalDate_ForSnapshotDateAndGuard()
    {
        // 02:00 UTC — on a negative-offset host this lands on the previous LOCAL day.
        var now = new DateTimeOffset(2026, 3, 15, 2, 0, 0, TimeSpan.Zero);
        var localDate = DateOnly.FromDateTime(now.LocalDateTime);

        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();
        SetupSuccessfulPoll(h.Services[Platform.YouTube], []);

        await h.Svc.PollAllAsync(now, CancellationToken.None);

        var account = await h.Db.ChannelMetricSnapshots.SingleAsync(s => s.Scope == SnapshotScope.Account);
        Assert.Equal(localDate, account.SnapshotDate);   // host-local, not UtcDateTime

        // On a non-UTC host the local and UTC dates differ here; when they do, prove it's the LOCAL one
        // (a regression to UtcDateTime would fail this). On a UTC host the two coincide and this is a no-op.
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (utcDate != localDate)
            Assert.NotEqual(utcDate, account.SnapshotDate);
    }

    [Fact]
    public async Task Poller_ServiceFailure_LogsAndContinues_ToNextPlatform()
    {
        var h = Build(AllEnabled());
        h.Db.PlatformCredentials.AddRange(AnalyticsCredential(Platform.YouTube), AnalyticsCredential(Platform.Instagram));
        await h.Db.SaveChangesAsync();

        // YouTube fails; Instagram succeeds. The failure must not stop Instagram.
        h.Services[Platform.YouTube].Setup(s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChannelPollResult>.Fail("boom"));
        SetupSuccessfulPoll(h.Services[Platform.Instagram], [Video("ig1")]);

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        Assert.False(await h.Db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.YouTube));
        Assert.True(await h.Db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.Instagram && s.Scope == SnapshotScope.Account));
    }

    [Fact]
    public async Task Poller_SkipsDisabledPlatforms()
    {
        var h = Build(new ChannelAnalyticsOptions { YouTubeEnabled = false, InstagramEnabled = true, TikTokEnabled = true, RecentVideoCount = 50 });
        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
        await h.Db.SaveChangesAsync();

        await h.Svc.PollAllAsync(Now, CancellationToken.None);

        h.Services[Platform.YouTube].Verify(
            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
