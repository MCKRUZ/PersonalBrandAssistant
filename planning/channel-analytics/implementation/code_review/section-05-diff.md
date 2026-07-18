diff --git a/src/PBA.Api/appsettings.json b/src/PBA.Api/appsettings.json
index 8a38f90..7ac7c59 100644
--- a/src/PBA.Api/appsettings.json
+++ b/src/PBA.Api/appsettings.json
@@ -48,6 +48,13 @@
       "Enabled": false
     }
   },
+  "ChannelAnalytics": {
+    "RunAtLocalTime": "05:00",
+    "RecentVideoCount": 50,
+    "YouTubeEnabled": false,
+    "InstagramEnabled": false,
+    "TikTokEnabled": false
+  },
   "GoogleAnalytics": {
     "PropertyId": "261358185",
     "SiteUrl": "https://matthewkruczek.ai/",
diff --git a/src/PBA.Infrastructure/Configuration/ChannelAnalyticsOptions.cs b/src/PBA.Infrastructure/Configuration/ChannelAnalyticsOptions.cs
new file mode 100644
index 0000000..9d65fda
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/ChannelAnalyticsOptions.cs
@@ -0,0 +1,12 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class ChannelAnalyticsOptions
+{
+    public const string SectionName = "ChannelAnalytics";
+
+    public string RunAtLocalTime { get; init; } = "05:00";
+    public int RecentVideoCount { get; init; } = 50;
+    public bool YouTubeEnabled { get; init; }
+    public bool InstagramEnabled { get; init; }
+    public bool TikTokEnabled { get; init; }
+}
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 7e2d9a3..9b62e93 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -93,9 +93,12 @@ public static class DependencyInjection
         services.AddScoped<PBA.Infrastructure.Services.Radar.IdeaEmbeddingService>();
         services.AddScoped<IDigestWriter, PBA.Infrastructure.Services.Radar.DigestWriter>();
 
+        services.Configure<ChannelAnalyticsOptions>(configuration.GetSection(ChannelAnalyticsOptions.SectionName));
+
         services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaScoringService>();
         services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaDedupService>();
         services.AddHostedService<PBA.Infrastructure.Services.Radar.DigestService>();
+        services.AddHostedService<PBA.Infrastructure.Services.Analytics.ChannelMetricPollingService>();
 
         // AI News Radar Phase 2: external delivery (email + Discord) + instant high-score alerts.
         services.Configure<DigestDeliveryOptions>(configuration.GetSection(DigestDeliveryOptions.SectionName));
diff --git a/src/PBA.Infrastructure/Services/Analytics/ChannelMetricPollingService.cs b/src/PBA.Infrastructure/Services/Analytics/ChannelMetricPollingService.cs
new file mode 100644
index 0000000..7efdf3a
--- /dev/null
+++ b/src/PBA.Infrastructure/Services/Analytics/ChannelMetricPollingService.cs
@@ -0,0 +1,184 @@
+using System.Globalization;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Security;
+
+namespace PBA.Infrastructure.Services.Analytics;
+
+// Daily write-side heartbeat: walks every active Analytics-purpose credential for enabled platforms, ensures
+// a fresh token, polls the platform's IChannelAnalyticsService, and persists cumulative ChannelMetricSnapshot
+// rows idempotently. Trends are derived at read time (section 06); this only produces the raw cumulative rows.
+public sealed class ChannelMetricPollingService(
+    IServiceScopeFactory scopeFactory,
+    IOptionsMonitor<ChannelAnalyticsOptions> optionsMonitor,
+    ILogger<ChannelMetricPollingService> logger) : BackgroundService
+{
+    private static readonly Platform[] AnalyticsPlatforms = [Platform.YouTube, Platform.Instagram, Platform.TikTok];
+
+    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
+    {
+        logger.LogInformation("ChannelMetricPollingService scheduled daily at {Time} (host TZ {Tz})",
+            optionsMonitor.CurrentValue.RunAtLocalTime, TimeZoneInfo.Local.Id);
+
+        while (!stoppingToken.IsCancellationRequested)
+        {
+            try
+            {
+                var now = DateTimeOffset.Now;
+                var runAt = TimeOnly.ParseExact(
+                    optionsMonitor.CurrentValue.RunAtLocalTime, "HH:mm", CultureInfo.InvariantCulture);
+                if (TimeOnly.FromDateTime(now.DateTime) >= runAt)
+                    await PollAllAsync(now, stoppingToken);
+            }
+            catch (Exception ex) when (ex is not OperationCanceledException)
+            {
+                logger.LogError(ex, "Channel metric polling failed");
+            }
+
+            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
+        }
+    }
+
+    internal async Task PollAllAsync(DateTimeOffset now, CancellationToken ct)
+    {
+        var options = optionsMonitor.CurrentValue;
+        // Host-LOCAL date — lines up with RunAtLocalTime and the read-side trend math.
+        var today = DateOnly.FromDateTime(now.LocalDateTime);
+
+        using var scope = scopeFactory.CreateScope();
+        var sp = scope.ServiceProvider;
+        var db = sp.GetRequiredService<ApplicationDbContext>();
+        var encryptor = sp.GetRequiredService<ITokenEncryptor>();
+
+        var enabled = AnalyticsPlatforms.Where(p => IsEnabled(p, options)).ToHashSet();
+
+        var candidates = await db.PlatformCredentials
+            .Where(c => c.IsActive && c.Purpose == CredentialPurpose.Analytics)
+            .ToListAsync(ct);
+
+        foreach (var credential in candidates.Where(c => enabled.Contains(c.Platform)))
+        {
+            // Never throw out of the loop — one platform's failure must not stop the others.
+            try
+            {
+                await PollCredentialAsync(sp, db, encryptor, credential, today, options.RecentVideoCount, now, ct);
+            }
+            catch (Exception ex)
+            {
+                logger.LogError(ex, "Channel metric poll failed for {Platform}; continuing", credential.Platform);
+            }
+        }
+    }
+
+    private async Task PollCredentialAsync(
+        IServiceProvider sp, ApplicationDbContext db, ITokenEncryptor encryptor,
+        PlatformCredential credential, DateOnly today, int recentVideoCount, DateTimeOffset now, CancellationToken ct)
+    {
+        var platform = credential.Platform;
+
+        // a. Idempotency guard: the Account row is written LAST, so its presence means today is complete.
+        if (await db.ChannelMetricSnapshots.AnyAsync(
+                s => s.Platform == platform && s.SnapshotDate == today && s.Scope == SnapshotScope.Account, ct))
+            return;
+
+        var provider = sp.GetRequiredKeyedService<IOAuthProvider>(platform);
+
+        // b. Ensure a valid token. Call the provider DIRECTLY (not IOAuthService.RefreshTokenAsync): the
+        //    coordinator drops the RefreshFailureReason and would wrongly deactivate a no-refresh-token
+        //    provider (Instagram). The poller owns revoked-only deactivation and persists the tokens itself.
+        if (provider.NeedsRefresh(credential, now))
+        {
+            var refresh = await provider.RefreshAsync(credential, ct);
+            if (!refresh.IsSuccess)
+            {
+                if (refresh.FailureReason == RefreshFailureReason.Revoked)
+                {
+                    credential.IsActive = false;
+                    credential.UpdatedAt = now;
+                    await db.SaveChangesAsync(ct);
+                    logger.LogWarning("Analytics credential for {Platform} revoked; reconnect required", platform);
+                }
+                else
+                {
+                    // Transient — leave IsActive true and retry tomorrow. Never deactivate on Transient.
+                    logger.LogWarning("Transient refresh failure for {Platform}; skipping this run", platform);
+                }
+                return;
+            }
+
+            var tokens = refresh.Tokens!;
+            credential.EncryptedAccessToken = encryptor.Encrypt(tokens.AccessToken);
+            credential.AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn);
+            if (tokens.RefreshToken is not null)   // TikTok rotates the refresh token
+                credential.EncryptedRefreshToken = encryptor.Encrypt(tokens.RefreshToken);
+            credential.UpdatedAt = now;
+            await db.SaveChangesAsync(ct);
+        }
+
+        // c. Poll.
+        var service = sp.GetRequiredKeyedService<IChannelAnalyticsService>(platform);
+        var result = await service.PollAsync(credential, recentVideoCount, ct);
+        if (!result.IsSuccess)
+        {
+            logger.LogWarning("Poll failed for {Platform}: {Errors}", platform, string.Join("; ", result.Errors));
+            return;   // do not write partial rows
+        }
+
+        var poll = result.Value!;
+        var videos = poll.RecentVideos.Take(recentVideoCount).ToList();   // d. defensive cap
+
+        // e. Write video rows FIRST, then the Account row LAST (completion sentinel). No explicit transaction:
+        //    the sentinel + idempotent re-poll self-heals a crash between the two writes (portable to InMemory).
+        //    Clear any partial-write leftover video rows first so a re-poll doesn't duplicate them.
+        var staleVideos = await db.ChannelMetricSnapshots
+            .Where(s => s.Platform == platform && s.SnapshotDate == today && s.Scope == SnapshotScope.Video)
+            .ToListAsync(ct);
+        if (staleVideos.Count > 0)
+        {
+            db.ChannelMetricSnapshots.RemoveRange(staleVideos);
+            await db.SaveChangesAsync(ct);
+        }
+
+        db.ChannelMetricSnapshots.AddRange(videos.Select(v => new ChannelMetricSnapshot
+        {
+            Platform = platform,
+            SnapshotDate = today,
+            Scope = SnapshotScope.Video,
+            VideoId = v.VideoId,
+            VideoTitle = v.Title,
+            Metrics = v.Metrics,
+            CapturedAt = now
+        }));
+        await db.SaveChangesAsync(ct);
+
+        db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+        {
+            Platform = platform,
+            SnapshotDate = today,
+            Scope = SnapshotScope.Account,
+            VideoId = string.Empty,
+            VideoTitle = null,
+            Metrics = poll.Account.Metrics,
+            CapturedAt = now
+        });
+        await db.SaveChangesAsync(ct);
+
+        logger.LogInformation("Captured {Platform} snapshot for {Date}: {VideoCount} video rows", platform, today, videos.Count);
+    }
+
+    private static bool IsEnabled(Platform platform, ChannelAnalyticsOptions options) => platform switch
+    {
+        Platform.YouTube => options.YouTubeEnabled,
+        Platform.Instagram => options.InstagramEnabled,
+        Platform.TikTok => options.TikTokEnabled,
+        _ => false
+    };
+}
diff --git a/tests/PBA.Infrastructure.Tests/Services/Analytics/ChannelMetricPollingServiceTests.cs b/tests/PBA.Infrastructure.Tests/Services/Analytics/ChannelMetricPollingServiceTests.cs
new file mode 100644
index 0000000..8379449
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Services/Analytics/ChannelMetricPollingServiceTests.cs
@@ -0,0 +1,304 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Services.Analytics;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Services.Analytics;
+
+public class ChannelMetricPollingServiceTests
+{
+    private static readonly Platform[] Platforms = [Platform.YouTube, Platform.Instagram, Platform.TikTok];
+
+    private sealed class Harness
+    {
+        public required ChannelMetricPollingService Svc { get; init; }
+        public required ApplicationDbContext Db { get; init; }
+        public required Dictionary<Platform, Mock<IChannelAnalyticsService>> Services { get; init; }
+        public required Dictionary<Platform, Mock<IOAuthProvider>> Providers { get; init; }
+        public required Mock<ITokenEncryptor> Encryptor { get; init; }
+    }
+
+    private static Harness Build(ChannelAnalyticsOptions options, ApplicationDbContext? dbOverride = null)
+    {
+        var db = dbOverride ?? new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+        var services = new Dictionary<Platform, Mock<IChannelAnalyticsService>>();
+        var providers = new Dictionary<Platform, Mock<IOAuthProvider>>();
+        var encryptor = new Mock<ITokenEncryptor>();
+        encryptor.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => $"enc:{s}");
+
+        var collection = new ServiceCollection();
+        collection.AddSingleton(db);
+        collection.AddSingleton(encryptor.Object);
+        foreach (var p in Platforms)
+        {
+            var svc = new Mock<IChannelAnalyticsService>();
+            var prov = new Mock<IOAuthProvider>();
+            prov.Setup(x => x.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(false);
+            services[p] = svc;
+            providers[p] = prov;
+            collection.AddKeyedSingleton<IChannelAnalyticsService>(p, svc.Object);
+            collection.AddKeyedSingleton<IOAuthProvider>(p, prov.Object);
+        }
+        var sp = collection.BuildServiceProvider();
+
+        var scope = new Mock<IServiceScope>();
+        scope.Setup(s => s.ServiceProvider).Returns(sp);
+        var scopeFactory = new Mock<IServiceScopeFactory>();
+        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
+
+        var monitor = new Mock<IOptionsMonitor<ChannelAnalyticsOptions>>();
+        monitor.Setup(m => m.CurrentValue).Returns(options);
+
+        return new Harness
+        {
+            Svc = new ChannelMetricPollingService(scopeFactory.Object, monitor.Object, NullLogger<ChannelMetricPollingService>.Instance),
+            Db = db,
+            Services = services,
+            Providers = providers,
+            Encryptor = encryptor
+        };
+    }
+
+    private static ChannelAnalyticsOptions AllEnabled(int recentVideoCount = 50) => new()
+    {
+        RecentVideoCount = recentVideoCount,
+        YouTubeEnabled = true,
+        InstagramEnabled = true,
+        TikTokEnabled = true
+    };
+
+    private static PlatformCredential AnalyticsCredential(Platform platform) => new()
+    {
+        Platform = platform,
+        Purpose = CredentialPurpose.Analytics,
+        IsActive = true,
+        EncryptedAccessToken = "enc:token",
+        EncryptedRefreshToken = "enc:refresh"
+    };
+
+    private static void SetupSuccessfulPoll(
+        Mock<IChannelAnalyticsService> service, IReadOnlyList<VideoMetrics> videos, IReadOnlyDictionary<string, long>? account = null) =>
+        service.Setup(s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<ChannelPollResult>.Success(new ChannelPollResult(
+                new AccountMetrics(account ?? new Dictionary<string, long> { ["subscribers"] = 100 }, false),
+                videos)));
+
+    private static VideoMetrics Video(string id) =>
+        new(id, $"title-{id}", new Dictionary<string, long> { ["views"] = 10 }, false);
+
+    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);
+    private static DateOnly Today => DateOnly.FromDateTime(Now.LocalDateTime);
+
+    [Fact]
+    public async Task Poller_SkipsPlatform_WhenAccountSnapshotExistsForToday()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        h.Db.ChannelMetricSnapshots.Add(new ChannelMetricSnapshot
+        {
+            Platform = Platform.YouTube, SnapshotDate = Today, Scope = SnapshotScope.Account, VideoId = ""
+        });
+        await h.Db.SaveChangesAsync();
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        h.Services[Platform.YouTube].Verify(
+            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task Poller_TriggersRefresh_WhenProviderNeedsRefresh()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+
+        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
+        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(OAuthRefreshResult.Success(new OAuthTokenResult("new-access", null, 3600, null, "scope")));
+        SetupSuccessfulPoll(h.Services[Platform.YouTube], []);
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        h.Providers[Platform.YouTube].Verify(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()), Times.Once);
+        Assert.True(await h.Db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.YouTube && s.Scope == SnapshotScope.Account));
+    }
+
+    [Fact]
+    public async Task Poller_DeactivatesCredential_OnlyOnRevoked()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+
+        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
+        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, "revoked"));
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        var cred = await h.Db.PlatformCredentials.SingleAsync(c => c.Platform == Platform.YouTube);
+        Assert.False(cred.IsActive);
+        h.Services[Platform.YouTube].Verify(
+            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task Poller_TransientRefreshFailure_SkipsRunWithoutDeactivating()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+
+        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
+        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(OAuthRefreshResult.Fail(RefreshFailureReason.Transient, "network"));
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        var cred = await h.Db.PlatformCredentials.SingleAsync(c => c.Platform == Platform.YouTube);
+        Assert.True(cred.IsActive);   // Transient must NOT deactivate
+        Assert.False(await h.Db.ChannelMetricSnapshots.AnyAsync());
+        h.Services[Platform.YouTube].Verify(
+            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task Poller_PersistsRotatedRefreshToken_AfterRefresh()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+
+        h.Providers[Platform.YouTube].Setup(p => p.NeedsRefresh(It.IsAny<PlatformCredential>(), It.IsAny<DateTimeOffset>())).Returns(true);
+        h.Providers[Platform.YouTube].Setup(p => p.RefreshAsync(It.IsAny<PlatformCredential>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(OAuthRefreshResult.Success(new OAuthTokenResult("new-access", "rotated-refresh", 3600, null, "scope")));
+        SetupSuccessfulPoll(h.Services[Platform.YouTube], []);
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        var cred = await h.Db.PlatformCredentials.SingleAsync(c => c.Platform == Platform.YouTube);
+        Assert.Equal("enc:rotated-refresh", cred.EncryptedRefreshToken);
+        h.Encryptor.Verify(e => e.Encrypt("rotated-refresh"), Times.Once);
+    }
+
+    [Fact]
+    public async Task Poller_WritesVideoRowsFirst_ThenAccountRowLast()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("a"), Video("b")]);
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        var rows = await h.Db.ChannelMetricSnapshots.Where(s => s.Platform == Platform.YouTube && s.SnapshotDate == Today).ToListAsync();
+        Assert.Equal(2, rows.Count(r => r.Scope == SnapshotScope.Video));
+        Assert.Equal(1, rows.Count(r => r.Scope == SnapshotScope.Account));
+        Assert.Equal(string.Empty, rows.Single(r => r.Scope == SnapshotScope.Account).VideoId);
+    }
+
+    // Throws precisely when the Account row is being persisted, letting the video write commit first.
+    private sealed class FailOnAccountWriteDbContext(DbContextOptions<ApplicationDbContext> options)
+        : ApplicationDbContext(options)
+    {
+        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
+        {
+            if (ChangeTracker.Entries<ChannelMetricSnapshot>()
+                .Any(e => e.State == EntityState.Added && e.Entity.Scope == SnapshotScope.Account))
+                throw new InvalidOperationException("simulated account-write failure");
+            return base.SaveChangesAsync(cancellationToken);
+        }
+    }
+
+    [Fact]
+    public async Task Poller_MidWriteFailure_LeavesNoAccountRow()
+    {
+        var db = new FailOnAccountWriteDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+        var h = Build(AllEnabled(), db);
+        db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await db.SaveChangesAsync();
+        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("a"), Video("b")]);
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);   // account write throws, caught, continues
+
+        var rows = await db.ChannelMetricSnapshots.Where(s => s.Platform == Platform.YouTube).ToListAsync();
+        Assert.Equal(2, rows.Count(r => r.Scope == SnapshotScope.Video));   // video rows committed first
+        Assert.DoesNotContain(rows, r => r.Scope == SnapshotScope.Account);  // no completion sentinel -> re-poll next run
+    }
+
+    [Fact]
+    public async Task Poller_CapsVideoSnapshots_AtRecentVideoCount()
+    {
+        var h = Build(AllEnabled(recentVideoCount: 3));
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+        SetupSuccessfulPoll(h.Services[Platform.YouTube], [Video("a"), Video("b"), Video("c"), Video("d"), Video("e")]);
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        Assert.Equal(3, await h.Db.ChannelMetricSnapshots.CountAsync(s => s.Scope == SnapshotScope.Video));
+    }
+
+    [Fact]
+    public async Task Poller_UsesHostLocalDate_ForSnapshotDateAndGuard()
+    {
+        // 02:00 UTC — on a negative-offset host this lands on the previous LOCAL day.
+        var now = new DateTimeOffset(2026, 3, 15, 2, 0, 0, TimeSpan.Zero);
+        var localDate = DateOnly.FromDateTime(now.LocalDateTime);
+
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+        SetupSuccessfulPoll(h.Services[Platform.YouTube], []);
+
+        await h.Svc.PollAllAsync(now, CancellationToken.None);
+
+        var account = await h.Db.ChannelMetricSnapshots.SingleAsync(s => s.Scope == SnapshotScope.Account);
+        Assert.Equal(localDate, account.SnapshotDate);   // host-local, not UtcDateTime
+    }
+
+    [Fact]
+    public async Task Poller_ServiceFailure_LogsAndContinues_ToNextPlatform()
+    {
+        var h = Build(AllEnabled());
+        h.Db.PlatformCredentials.AddRange(AnalyticsCredential(Platform.YouTube), AnalyticsCredential(Platform.Instagram));
+        await h.Db.SaveChangesAsync();
+
+        // YouTube fails; Instagram succeeds. The failure must not stop Instagram.
+        h.Services[Platform.YouTube].Setup(s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<ChannelPollResult>.Fail("boom"));
+        SetupSuccessfulPoll(h.Services[Platform.Instagram], [Video("ig1")]);
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        Assert.False(await h.Db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.YouTube));
+        Assert.True(await h.Db.ChannelMetricSnapshots.AnyAsync(s => s.Platform == Platform.Instagram && s.Scope == SnapshotScope.Account));
+    }
+
+    [Fact]
+    public async Task Poller_SkipsDisabledPlatforms()
+    {
+        var h = Build(new ChannelAnalyticsOptions { YouTubeEnabled = false, InstagramEnabled = true, TikTokEnabled = true, RecentVideoCount = 50 });
+        h.Db.PlatformCredentials.Add(AnalyticsCredential(Platform.YouTube));
+        await h.Db.SaveChangesAsync();
+
+        await h.Svc.PollAllAsync(Now, CancellationToken.None);
+
+        h.Services[Platform.YouTube].Verify(
+            s => s.PollAsync(It.IsAny<PlatformCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+}
