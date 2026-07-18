diff --git a/src/PBA.Api/Endpoints/ChannelAnalyticsEndpoints.cs b/src/PBA.Api/Endpoints/ChannelAnalyticsEndpoints.cs
new file mode 100644
index 0000000..1d39424
--- /dev/null
+++ b/src/PBA.Api/Endpoints/ChannelAnalyticsEndpoints.cs
@@ -0,0 +1,65 @@
+using MediatR;
+using PBA.Api.Extensions;
+using PBA.Application.Features.ChannelAnalytics.Queries;
+using PBA.Domain.Enums;
+
+namespace PBA.Api.Endpoints;
+
+public static class ChannelAnalyticsEndpoints
+{
+    // Analytics OAuth exists only for these platforms; a channel request for anything else is a 400.
+    private static readonly HashSet<Platform> AnalyticsPlatforms =
+        [Platform.YouTube, Platform.Instagram, Platform.TikTok];
+
+    public static void MapChannelAnalyticsEndpoints(this IEndpointRouteBuilder app)
+    {
+        // Reuses the existing /api/analytics group prefix (website + health are untouched).
+        var group = app.MapGroup("/api/analytics").WithTags("Analytics");
+
+        group.MapGet("/overview", async (string? period, ISender sender, CancellationToken ct) =>
+        {
+            if (!TryResolvePeriod(period, out var window))
+                return Results.BadRequest("Invalid period. Use 7d, 30d, or 90d.");
+
+            var result = await sender.Send(new GetAnalyticsOverview.Query(window.From, window.To), ct);
+            return result.ToApiResult();
+        });
+
+        group.MapGet("/channel/{platform}", async (string platform, string? period, ISender sender, CancellationToken ct) =>
+        {
+            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p) || !AnalyticsPlatforms.Contains(p))
+                return Results.BadRequest($"'{platform}' is not an analytics platform. Use youtube, instagram, or tiktok.");
+            if (!TryResolvePeriod(period, out var window))
+                return Results.BadRequest("Invalid period. Use 7d, 30d, or 90d.");
+
+            var result = await sender.Send(new GetChannelAnalytics.Query(p, window.From, window.To), ct);
+            return result.ToApiResult();
+        });
+
+        group.MapGet("/youtube/deep", async (string? period, ISender sender, CancellationToken ct) =>
+        {
+            if (!TryResolvePeriod(period, out var window))
+                return Results.BadRequest("Invalid period. Use 7d, 30d, or 90d.");
+
+            var result = await sender.Send(new GetYouTubeDeepAnalytics.Query(window.From, window.To), ct);
+            return result.ToApiResult();
+        });
+    }
+
+    // Same 7d|30d|90d vocabulary as the Website endpoint (default 30d), resolved to a host-local DateOnly
+    // window matching the poller's SnapshotDate clock.
+    private static bool TryResolvePeriod(string? period, out (DateOnly From, DateOnly To) window)
+    {
+        var today = DateOnly.FromDateTime(DateTime.Now);
+        window = default;
+
+        var days = string.IsNullOrWhiteSpace(period)
+            ? 30
+            : period switch { "7d" => 7, "30d" => 30, "90d" => 90, _ => -1 };
+        if (days < 0)
+            return false;
+
+        window = (today.AddDays(-(days - 1)), today);
+        return true;
+    }
+}
diff --git a/src/PBA.Api/Program.cs b/src/PBA.Api/Program.cs
index d580e18..879b942 100644
--- a/src/PBA.Api/Program.cs
+++ b/src/PBA.Api/Program.cs
@@ -70,6 +70,7 @@ app.MapOAuthEndpoints();
 app.MapPlatformEndpoints();
 app.MapFeedEndpoints();
 app.MapAnalyticsEndpoints();
+app.MapChannelAnalyticsEndpoints();
 app.MapDigestEndpoints();
 app.MapBrandRankingProfileEndpoints();
 app.MapExternalEndpoints();
diff --git a/src/PBA.Application/Features/ChannelAnalytics/ChannelAnalyticsReadHelper.cs b/src/PBA.Application/Features/ChannelAnalytics/ChannelAnalyticsReadHelper.cs
new file mode 100644
index 0000000..e68c663
--- /dev/null
+++ b/src/PBA.Application/Features/ChannelAnalytics/ChannelAnalyticsReadHelper.cs
@@ -0,0 +1,117 @@
+using System.Globalization;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.ChannelAnalytics.Dtos;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.ChannelAnalytics;
+
+// Shared snapshot-read logic for the two snapshot-backed queries. Cumulative counts in; deltas/KPIs/rates
+// derived here at read time (nothing fractional is ever persisted). Kept small and internal (YAGNI).
+internal static class ChannelAnalyticsReadHelper
+{
+    private static readonly string[] InteractionKeys = ["likes", "comments", "shares", "saves"];
+
+    public static async Task<ConnectionStatus> ResolveStatusAsync(
+        IAppDbContext db, Platform platform, CancellationToken ct)
+    {
+        var credential = await db.PlatformCredentials
+            .FirstOrDefaultAsync(c => c.Platform == platform && c.Purpose == CredentialPurpose.Analytics, ct);
+
+        return credential is null
+            ? ConnectionStatus.NotConnected
+            : credential.IsActive ? ConnectionStatus.Connected : ConnectionStatus.ReconnectRequired;
+    }
+
+    public static async Task<List<ChannelMetricSnapshot>> LoadAccountSnapshotsAsync(
+        IAppDbContext db, Platform platform, DateOnly from, DateOnly to, CancellationToken ct) =>
+        await db.ChannelMetricSnapshots
+            .Where(s => s.Platform == platform && s.Scope == SnapshotScope.Account
+                && s.SnapshotDate >= from && s.SnapshotDate <= to)
+            .OrderBy(s => s.SnapshotDate)
+            .ToListAsync(ct);
+
+    public static long? AudienceValue(IReadOnlyDictionary<string, long> bag) =>
+        bag.TryGetValue("followers", out var f) ? f
+        : bag.TryGetValue("subscribers", out var s) ? s
+        : null;
+
+    // interactions / reach (Instagram) else interactions / followers|subscribers (YouTube, TikTok).
+    // null when interactions can't be computed or the denominator is zero/absent.
+    public static double? EngagementRate(IReadOnlyDictionary<string, long> bag)
+    {
+        long? interactions = null;
+        if (bag.TryGetValue("total_interactions", out var ti))
+        {
+            interactions = ti;
+        }
+        else
+        {
+            long sum = 0;
+            var any = false;
+            foreach (var key in InteractionKeys)
+                if (bag.TryGetValue(key, out var v)) { sum += v; any = true; }
+            if (any) interactions = sum;
+        }
+
+        if (interactions is null)
+            return null;
+
+        long? denominator = bag.TryGetValue("reach", out var reach) ? reach : AudienceValue(bag);
+        if (denominator is null or 0)
+            return null;
+
+        return (double)interactions.Value / denominator.Value;
+    }
+
+    // One consecutive-delta point per snapshot after the first (the first seeds the baseline).
+    public static IReadOnlyList<MetricPoint> DeltaSeries(IReadOnlyList<ChannelMetricSnapshot> ordered, string key)
+    {
+        var points = new List<MetricPoint>();
+        for (var i = 1; i < ordered.Count; i++)
+        {
+            var prev = ordered[i - 1].Metrics.TryGetValue(key, out var p) ? p : 0;
+            var curr = ordered[i].Metrics.TryGetValue(key, out var c) ? c : 0;
+            points.Add(new MetricPoint(ordered[i].SnapshotDate, curr - prev));
+        }
+        return points;
+    }
+
+    public static IReadOnlyList<KpiCard> BuildKpis(IReadOnlyList<ChannelMetricSnapshot> ordered)
+    {
+        if (ordered.Count == 0)
+            return [];
+
+        var latest = ordered[^1].Metrics;
+        var prior = ordered.Count >= 2 ? ordered[^2].Metrics : null;
+
+        var cards = latest.Select(kv =>
+        {
+            double? deltaPct = null;
+            if (prior is not null && prior.TryGetValue(kv.Key, out var prev) && prev != 0)
+                deltaPct = (kv.Value - prev) / (double)prev * 100;
+            return new KpiCard(kv.Key, Label(kv.Key), kv.Value, deltaPct, Rate: null);
+        }).ToList();
+
+        var rate = EngagementRate(latest);
+        if (rate is not null)
+            cards.Add(new KpiCard("engagement_rate", "Engagement Rate", 0, null, rate));
+
+        return cards;
+    }
+
+    public static IReadOnlyList<TrendSeries> BuildTrends(IReadOnlyList<ChannelMetricSnapshot> ordered)
+    {
+        if (ordered.Count == 0)
+            return [];
+
+        return ordered[^1].Metrics.Keys
+            .Select(key => new TrendSeries(key, DeltaSeries(ordered, key)))
+            .ToList();
+    }
+
+    private static string Label(string key) =>
+        string.Join(' ', key.Split('_')
+            .Select(w => w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
+}
diff --git a/src/PBA.Application/Features/ChannelAnalytics/Dtos/ChannelAnalyticsDtos.cs b/src/PBA.Application/Features/ChannelAnalytics/Dtos/ChannelAnalyticsDtos.cs
new file mode 100644
index 0000000..52a2c4b
--- /dev/null
+++ b/src/PBA.Application/Features/ChannelAnalytics/Dtos/ChannelAnalyticsDtos.cs
@@ -0,0 +1,49 @@
+using PBA.Application.Features.Analytics.Dtos;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.ChannelAnalytics.Dtos;
+
+public enum ConnectionStatus
+{
+    NotConnected = 0,
+    Connected = 1,
+    ReconnectRequired = 2
+}
+
+// A single day's DELTA value (derived from cumulative snapshots), or a cumulative point for sparklines.
+public record MetricPoint(DateOnly Date, long Value);
+
+public record TrendSeries(string Metric, IReadOnlyList<MetricPoint> Points);
+
+// Value = latest cumulative; DeltaPct = % change vs the prior in-range snapshot (null when no prior);
+// Rate = engagement-rate fraction (null except on the engagement card).
+public record KpiCard(string Key, string Label, long Value, double? DeltaPct, double? Rate);
+
+public record RecentPost(string VideoId, string? Title, IReadOnlyDictionary<string, long> Metrics);
+
+public record ChannelAnalyticsDto(
+    Platform Platform,
+    ConnectionStatus Status,
+    DateOnly? AsOf,
+    IReadOnlyList<KpiCard> Kpis,
+    IReadOnlyList<TrendSeries> Trends,
+    IReadOnlyList<RecentPost> RecentPosts);
+
+public record OverviewChannel(
+    Platform Platform,
+    ConnectionStatus Status,
+    long? Followers,
+    IReadOnlyList<MetricPoint> FollowerSparkline);
+
+public record OverviewDto(
+    long TotalAudience,   // sum of followers/subscribers across connected channels (APPROXIMATE — YouTube rounds)
+    IReadOnlyList<KpiCard> CombinedKpis,
+    IReadOnlyList<OverviewChannel> Channels);
+
+// Live YouTube Analytics v2 deep path — flat labeled series (reuses section-04's YouTubeMetricSeries) so the
+// frontend charts them directly. Not snapshotted.
+public record YouTubeDeepAnalyticsDto(
+    IReadOnlyList<YouTubeMetricSeries> DaySeries,
+    IReadOnlyList<YouTubeMetricSeries> TrafficSources,
+    IReadOnlyList<YouTubeMetricSeries> Geography,
+    IReadOnlyList<YouTubeMetricSeries> Demographics);
diff --git a/src/PBA.Application/Features/ChannelAnalytics/Queries/GetAnalyticsOverview.cs b/src/PBA.Application/Features/ChannelAnalytics/Queries/GetAnalyticsOverview.cs
new file mode 100644
index 0000000..28d41e3
--- /dev/null
+++ b/src/PBA.Application/Features/ChannelAnalytics/Queries/GetAnalyticsOverview.cs
@@ -0,0 +1,52 @@
+using MediatR;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.ChannelAnalytics.Dtos;
+using PBA.Domain.Common;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.ChannelAnalytics.Queries;
+
+public static class GetAnalyticsOverview
+{
+    private static readonly Platform[] Platforms = [Platform.YouTube, Platform.Instagram, Platform.TikTok];
+
+    public record Query(DateOnly From, DateOnly To) : IRequest<Result<OverviewDto>>;
+
+    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<OverviewDto>>
+    {
+        public async Task<Result<OverviewDto>> Handle(Query request, CancellationToken ct)
+        {
+            var channels = new List<OverviewChannel>();
+            long totalAudience = 0;
+
+            foreach (var platform in Platforms)
+            {
+                var status = await ChannelAnalyticsReadHelper.ResolveStatusAsync(db, platform, ct);
+                var accounts = await ChannelAnalyticsReadHelper.LoadAccountSnapshotsAsync(db, platform, request.From, request.To, ct);
+
+                long? followers = accounts.Count > 0
+                    ? ChannelAnalyticsReadHelper.AudienceValue(accounts[^1].Metrics)
+                    : null;
+                var sparkline = accounts.Count > 0
+                    ? ChannelAnalyticsReadHelper.DeltaSeries(accounts, AudienceKey(accounts[^1].Metrics))
+                    : [];
+
+                // TotalAudience = latest followers/subscribers across CONNECTED channels only.
+                if (status == ConnectionStatus.Connected && followers is not null)
+                    totalAudience += followers.Value;
+
+                channels.Add(new OverviewChannel(platform, status, followers, sparkline));
+            }
+
+            var combinedKpis = new List<KpiCard>
+            {
+                new("total_audience", "Total Audience", totalAudience, null, null)
+            };
+
+            return Result<OverviewDto>.Success(new OverviewDto(totalAudience, combinedKpis, channels));
+        }
+
+        private static string AudienceKey(IReadOnlyDictionary<string, long> bag) =>
+            bag.ContainsKey("followers") ? "followers" : "subscribers";
+    }
+}
diff --git a/src/PBA.Application/Features/ChannelAnalytics/Queries/GetChannelAnalytics.cs b/src/PBA.Application/Features/ChannelAnalytics/Queries/GetChannelAnalytics.cs
new file mode 100644
index 0000000..7037b03
--- /dev/null
+++ b/src/PBA.Application/Features/ChannelAnalytics/Queries/GetChannelAnalytics.cs
@@ -0,0 +1,54 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.ChannelAnalytics.Dtos;
+using PBA.Domain.Common;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.ChannelAnalytics.Queries;
+
+public static class GetChannelAnalytics
+{
+    public record Query(Platform Platform, DateOnly From, DateOnly To) : IRequest<Result<ChannelAnalyticsDto>>;
+
+    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<ChannelAnalyticsDto>>
+    {
+        public async Task<Result<ChannelAnalyticsDto>> Handle(Query request, CancellationToken ct)
+        {
+            var platform = request.Platform;
+            var status = await ChannelAnalyticsReadHelper.ResolveStatusAsync(db, platform, ct);
+
+            // NotConnected -> empty DTO with that status (not an error).
+            if (status == ConnectionStatus.NotConnected)
+                return Result<ChannelAnalyticsDto>.Success(
+                    new ChannelAnalyticsDto(platform, status, AsOf: null, [], [], []));
+
+            var accounts = await ChannelAnalyticsReadHelper.LoadAccountSnapshotsAsync(db, platform, request.From, request.To, ct);
+            var kpis = ChannelAnalyticsReadHelper.BuildKpis(accounts);
+            var trends = ChannelAnalyticsReadHelper.BuildTrends(accounts);
+            var asOf = accounts.Count > 0 ? accounts[^1].SnapshotDate : (DateOnly?)null;
+            var recentPosts = await LoadRecentPostsAsync(platform, request.From, request.To, ct);
+
+            return Result<ChannelAnalyticsDto>.Success(
+                new ChannelAnalyticsDto(platform, status, asOf, kpis, trends, recentPosts));
+        }
+
+        // Latest captured day's video-scope snapshots.
+        private async Task<IReadOnlyList<RecentPost>> LoadRecentPostsAsync(
+            Platform platform, DateOnly from, DateOnly to, CancellationToken ct)
+        {
+            var videos = await db.ChannelMetricSnapshots
+                .Where(s => s.Platform == platform && s.Scope == SnapshotScope.Video
+                    && s.SnapshotDate >= from && s.SnapshotDate <= to)
+                .ToListAsync(ct);
+            if (videos.Count == 0)
+                return [];
+
+            var latestDate = videos.Max(v => v.SnapshotDate);
+            return videos
+                .Where(v => v.SnapshotDate == latestDate)
+                .Select(v => new RecentPost(v.VideoId, v.VideoTitle, v.Metrics))
+                .ToList();
+        }
+    }
+}
diff --git a/src/PBA.Application/Features/ChannelAnalytics/Queries/GetYouTubeDeepAnalytics.cs b/src/PBA.Application/Features/ChannelAnalytics/Queries/GetYouTubeDeepAnalytics.cs
new file mode 100644
index 0000000..8e25a03
--- /dev/null
+++ b/src/PBA.Application/Features/ChannelAnalytics/Queries/GetYouTubeDeepAnalytics.cs
@@ -0,0 +1,60 @@
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.Analytics;
+using PBA.Application.Features.Analytics.Dtos;
+using PBA.Application.Features.ChannelAnalytics.Dtos;
+using PBA.Domain.Common;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Features.ChannelAnalytics.Queries;
+
+// The one LIVE read path: YouTube Analytics v2 reports.query for the selected window (its own history, not
+// snapshotted). Wrapped in Result so the YouTube tab degrades gracefully if the live call fails.
+public static class GetYouTubeDeepAnalytics
+{
+    private static readonly string[] DayMetrics =
+    [
+        "views", "estimatedMinutesWatched", "averageViewDuration",
+        "subscribersGained", "subscribersLost", "likes", "comments", "shares"
+    ];
+
+    public record Query(DateOnly From, DateOnly To) : IRequest<Result<YouTubeDeepAnalyticsDto>>;
+
+    public sealed class Handler(IAppDbContext db, IYouTubeApiClient youtube, ITokenEncryptor encryptor)
+        : IRequestHandler<Query, Result<YouTubeDeepAnalyticsDto>>
+    {
+        public async Task<Result<YouTubeDeepAnalyticsDto>> Handle(Query request, CancellationToken ct)
+        {
+            var credential = await db.PlatformCredentials.FirstOrDefaultAsync(
+                c => c.Platform == Platform.YouTube && c.Purpose == CredentialPurpose.Analytics && c.IsActive, ct);
+            if (credential is null)
+                return Result<YouTubeDeepAnalyticsDto>.Fail("YouTube analytics is not connected.");
+
+            try
+            {
+                var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);
+
+                var day = await RunAsync(request, "day", DayMetrics, accessToken, ct);
+                var traffic = await RunAsync(request, "insightTrafficSourceType", ["views"], accessToken, ct);
+                var geography = await RunAsync(request, "country", ["views"], accessToken, ct);
+                var demographics = await RunAsync(request, "ageGroup", ["viewerPercentage"], accessToken, ct);
+
+                return Result<YouTubeDeepAnalyticsDto>.Success(
+                    new YouTubeDeepAnalyticsDto(day, traffic, geography, demographics));
+            }
+            catch (Exception ex)
+            {
+                return Result<YouTubeDeepAnalyticsDto>.Fail($"YouTube deep analytics failed: {ex.Message}");
+            }
+        }
+
+        private async Task<IReadOnlyList<YouTubeMetricSeries>> RunAsync(
+            Query request, string dimensions, IReadOnlyList<string> metrics, string accessToken, CancellationToken ct)
+        {
+            var report = await youtube.RunAnalyticsReportAsync(
+                new YouTubeReportRequest("channel==MINE", dimensions, metrics, request.From, request.To), accessToken, ct);
+            return YouTubeDeepAnalyticsMapper.MapToSeries(report);
+        }
+    }
+}
diff --git a/tests/PBA.Api.Tests/Endpoints/ChannelAnalyticsEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/ChannelAnalyticsEndpointsTests.cs
new file mode 100644
index 0000000..e70ad82
--- /dev/null
+++ b/tests/PBA.Api.Tests/Endpoints/ChannelAnalyticsEndpointsTests.cs
@@ -0,0 +1,61 @@
+using System.Net;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Hosting;
+using PBA.Infrastructure.Services.Analytics;
+using Xunit;
+
+namespace PBA.Api.Tests.Endpoints;
+
+public class ChannelAnalyticsEndpointsTests : IClassFixture<TestWebApplicationFactory>
+{
+    private readonly HttpClient _client;
+    private readonly TestWebApplicationFactory _factory;
+
+    public ChannelAnalyticsEndpointsTests(TestWebApplicationFactory factory)
+    {
+        _factory = factory;
+        _client = factory.CreateClient();
+    }
+
+    [Fact]
+    public async Task ChannelAnalyticsEndpoints_Overview_ReturnsOk()
+    {
+        var response = await _client.GetAsync("/api/analytics/overview");
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ChannelAnalyticsEndpoints_Channel_InvalidPlatform_ReturnsBadRequest()
+    {
+        // LinkedIn is not an analytics platform.
+        var response = await _client.GetAsync("/api/analytics/channel/LinkedIn");
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
+    [Theory]
+    [InlineData("7d", HttpStatusCode.OK)]
+    [InlineData("30d", HttpStatusCode.OK)]
+    [InlineData("90d", HttpStatusCode.OK)]
+    [InlineData("bogus", HttpStatusCode.BadRequest)]
+    public async Task ChannelAnalyticsEndpoints_Channel_ParsesPeriod(string period, HttpStatusCode expected)
+    {
+        var response = await _client.GetAsync($"/api/analytics/channel/youtube?period={period}");
+        Assert.Equal(expected, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ChannelAnalyticsEndpoints_MapsResultFailure_ViaToApiResult()
+    {
+        // No YouTube analytics credential in the test DB -> GetYouTubeDeepAnalytics returns Result.Fail
+        // (General) -> ToApiResult -> Problem (500). Proves failure mapping, not an unhandled exception.
+        var response = await _client.GetAsync("/api/analytics/youtube/deep");
+        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
+    }
+
+    [Fact]
+    public void TestFactory_DoesNotStartChannelMetricPollingService()
+    {
+        var hostedServices = _factory.Services.GetServices<IHostedService>();
+        Assert.DoesNotContain(hostedServices, h => h is ChannelMetricPollingService);
+    }
+}
diff --git a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
index a944b7a..c04a136 100644
--- a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
+++ b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
@@ -32,7 +32,9 @@ public class TestWebApplicationFactory : WebApplicationFactory<Program>
                     d.ServiceType.FullName?.Contains("Hangfire") == true ||
                     d.ImplementationType?.FullName?.Contains("Hangfire") == true ||
                     d.ImplementationFactory?.Method.DeclaringType?.FullName?.Contains("Hangfire") == true ||
-                    d.ImplementationType?.FullName?.Contains("ScheduledPublishReconciler") == true)
+                    d.ImplementationType?.FullName?.Contains("ScheduledPublishReconciler") == true ||
+                    // Poller must not start / hit external APIs during integration tests (M4 isolation).
+                    d.ImplementationType?.FullName?.Contains("ChannelMetricPollingService") == true)
                 .ToList();
 
             foreach (var d in descriptorsToRemove)
diff --git a/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetAnalyticsOverviewHandlerTests.cs b/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetAnalyticsOverviewHandlerTests.cs
new file mode 100644
index 0000000..7877a1b
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetAnalyticsOverviewHandlerTests.cs
@@ -0,0 +1,66 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Features.ChannelAnalytics.Dtos;
+using PBA.Application.Features.ChannelAnalytics.Queries;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.ChannelAnalytics;
+
+public class GetAnalyticsOverviewHandlerTests
+{
+    private static ApplicationDbContext CreateContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+    private static readonly DateOnly Day1 = new(2026, 6, 1);
+
+    private static ChannelMetricSnapshot Account(Platform p, DateOnly date, Dictionary<string, long> metrics) =>
+        new() { Platform = p, SnapshotDate = date, Scope = SnapshotScope.Account, VideoId = "", Metrics = metrics };
+
+    private static PlatformCredential AnalyticsCred(Platform p, bool active = true) =>
+        new() { Platform = p, Purpose = CredentialPurpose.Analytics, IsActive = active, EncryptedAccessToken = "enc" };
+
+    private static async Task<OverviewDto> RunAsync(ApplicationDbContext db)
+    {
+        var result = await new GetAnalyticsOverview.Handler(db).Handle(new GetAnalyticsOverview.Query(Day1, Day1.AddDays(2)), CancellationToken.None);
+        Assert.True(result.IsSuccess);
+        return result.Value!;
+    }
+
+    [Fact]
+    public async Task GetAnalyticsOverview_AggregatesTotalAudience_AcrossConnectedChannels()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.AddRange(AnalyticsCred(Platform.YouTube), AnalyticsCred(Platform.Instagram), AnalyticsCred(Platform.TikTok, active: false));
+        db.ChannelMetricSnapshots.AddRange(
+            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 1000 }),
+            Account(Platform.Instagram, Day1, new() { ["followers"] = 500 }),
+            Account(Platform.TikTok, Day1, new() { ["followers"] = 9999 }));   // inactive -> excluded
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db);
+
+        Assert.Equal(1500, dto.TotalAudience);   // YouTube 1000 + Instagram 500; TikTok (ReconnectRequired) excluded
+        Assert.Equal(1500, dto.CombinedKpis.Single(k => k.Key == "total_audience").Value);
+        Assert.Equal(ConnectionStatus.ReconnectRequired, dto.Channels.Single(c => c.Platform == Platform.TikTok).Status);
+    }
+
+    [Fact]
+    public async Task GetAnalyticsOverview_BuildsPerPlatformFollowerSparklines()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.Instagram));
+        db.ChannelMetricSnapshots.AddRange(
+            Account(Platform.Instagram, Day1, new() { ["followers"] = 500 }),
+            Account(Platform.Instagram, Day1.AddDays(1), new() { ["followers"] = 508 }),
+            Account(Platform.Instagram, Day1.AddDays(2), new() { ["followers"] = 515 }));
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db);
+
+        var ig = dto.Channels.Single(c => c.Platform == Platform.Instagram);
+        Assert.Equal(515, ig.Followers);
+        Assert.Equal([8, 7], ig.FollowerSparkline.Select(p => p.Value));   // deltas of [500,508,515]
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetChannelAnalyticsHandlerTests.cs b/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetChannelAnalyticsHandlerTests.cs
new file mode 100644
index 0000000..bf18be4
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetChannelAnalyticsHandlerTests.cs
@@ -0,0 +1,146 @@
+using Microsoft.EntityFrameworkCore;
+using PBA.Application.Features.ChannelAnalytics.Dtos;
+using PBA.Application.Features.ChannelAnalytics.Queries;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.ChannelAnalytics;
+
+public class GetChannelAnalyticsHandlerTests
+{
+    private static ApplicationDbContext CreateContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+    private static readonly DateOnly Day1 = new(2026, 6, 1);
+
+    private static ChannelMetricSnapshot Account(Platform p, DateOnly date, Dictionary<string, long> metrics) =>
+        new() { Platform = p, SnapshotDate = date, Scope = SnapshotScope.Account, VideoId = "", Metrics = metrics };
+
+    private static PlatformCredential AnalyticsCred(Platform p, bool active = true) =>
+        new() { Platform = p, Purpose = CredentialPurpose.Analytics, IsActive = active, EncryptedAccessToken = "enc" };
+
+    private static async Task<ChannelAnalyticsDto> RunAsync(ApplicationDbContext db, Platform platform, DateOnly from, DateOnly to)
+    {
+        var result = await new GetChannelAnalytics.Handler(db).Handle(new GetChannelAnalytics.Query(platform, from, to), CancellationToken.None);
+        Assert.True(result.IsSuccess);
+        return result.Value!;
+    }
+
+    [Fact]
+    public async Task GetChannelAnalytics_BuildsKpis_LatestCumulativeWithDeltaPct()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
+        db.ChannelMetricSnapshots.AddRange(
+            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 100 }),
+            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 110 }));
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(1));
+
+        var subs = dto.Kpis.Single(k => k.Key == "subscribers");
+        Assert.Equal(110, subs.Value);              // latest cumulative
+        Assert.Equal(10, subs.DeltaPct);            // (110-100)/100 * 100
+        Assert.Equal(Day1.AddDays(1), dto.AsOf);
+    }
+
+    [Fact]
+    public async Task GetChannelAnalytics_TrendSeries_AreDeltasBetweenConsecutiveSnapshots()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
+        db.ChannelMetricSnapshots.AddRange(
+            Account(Platform.YouTube, Day1, new() { ["subscribers"] = 100 }),
+            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 105 }),
+            Account(Platform.YouTube, Day1.AddDays(2), new() { ["subscribers"] = 111 }));
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(2));
+
+        var series = dto.Trends.Single(t => t.Metric == "subscribers");
+        Assert.Equal([5, 6], series.Points.Select(p => p.Value));   // cumulative [100,105,111] -> deltas [+5,+6]
+        Assert.Equal(Day1.AddDays(1), series.Points[0].Date);       // first day seeds the baseline, not a point
+    }
+
+    [Fact]
+    public async Task GetChannelAnalytics_EngagementRate_UsesReachWhenPresent()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.Instagram));
+        db.ChannelMetricSnapshots.Add(Account(Platform.Instagram, Day1, new()
+        {
+            ["followers"] = 9999, ["reach"] = 1000, ["total_interactions"] = 50
+        }));
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db, Platform.Instagram, Day1, Day1);
+
+        // reach present -> interactions/reach = 50/1000, NOT interactions/followers.
+        Assert.Equal(0.05, dto.Kpis.Single(k => k.Key == "engagement_rate").Rate);
+    }
+
+    [Fact]
+    public async Task GetChannelAnalytics_EngagementRate_FallsBackToFollowers_WhenNoReach()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.TikTok));
+        db.ChannelMetricSnapshots.Add(Account(Platform.TikTok, Day1, new()
+        {
+            ["followers"] = 1000, ["likes"] = 40
+        }));
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db, Platform.TikTok, Day1, Day1);
+
+        // no reach -> interactions/followers = 40/1000.
+        Assert.Equal(0.04, dto.Kpis.Single(k => k.Key == "engagement_rate").Rate);
+    }
+
+    [Theory]
+    [InlineData(true, ConnectionStatus.Connected)]
+    [InlineData(false, ConnectionStatus.ReconnectRequired)]
+    public async Task GetChannelAnalytics_ResolvesConnectionStatus_FromAnalyticsCredential(bool active, ConnectionStatus expected)
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube, active));
+        db.ChannelMetricSnapshots.Add(Account(Platform.YouTube, Day1, new() { ["subscribers"] = 1 }));
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1);
+
+        Assert.Equal(expected, dto.Status);
+    }
+
+    [Fact]
+    public async Task GetChannelAnalytics_NotConnected_ReturnsEmptyWithNotConnectedStatus()
+    {
+        await using var db = CreateContext();
+
+        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1);
+
+        Assert.Equal(ConnectionStatus.NotConnected, dto.Status);
+        Assert.Empty(dto.Kpis);
+        Assert.Empty(dto.Trends);
+        Assert.Empty(dto.RecentPosts);
+        Assert.Null(dto.AsOf);
+    }
+
+    [Fact]
+    public async Task GetChannelAnalytics_RecentPosts_AreLatestDayVideoSnapshots()
+    {
+        await using var db = CreateContext();
+        db.PlatformCredentials.Add(AnalyticsCred(Platform.YouTube));
+        db.ChannelMetricSnapshots.AddRange(
+            Account(Platform.YouTube, Day1.AddDays(1), new() { ["subscribers"] = 1 }),
+            new ChannelMetricSnapshot { Platform = Platform.YouTube, SnapshotDate = Day1, Scope = SnapshotScope.Video, VideoId = "old", Metrics = new Dictionary<string, long> { ["views"] = 1 } },
+            new ChannelMetricSnapshot { Platform = Platform.YouTube, SnapshotDate = Day1.AddDays(1), Scope = SnapshotScope.Video, VideoId = "new", VideoTitle = "New", Metrics = new Dictionary<string, long> { ["views"] = 5 } });
+        await db.SaveChangesAsync();
+
+        var dto = await RunAsync(db, Platform.YouTube, Day1, Day1.AddDays(1));
+
+        var post = Assert.Single(dto.RecentPosts);
+        Assert.Equal("new", post.VideoId);   // only the latest day's video rows
+    }
+}
diff --git a/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetYouTubeDeepAnalyticsHandlerTests.cs b/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetYouTubeDeepAnalyticsHandlerTests.cs
new file mode 100644
index 0000000..cf3eb09
--- /dev/null
+++ b/tests/PBA.Application.Tests/Features/ChannelAnalytics/GetYouTubeDeepAnalyticsHandlerTests.cs
@@ -0,0 +1,83 @@
+using Microsoft.EntityFrameworkCore;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Application.Features.ChannelAnalytics.Queries;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Data;
+using Xunit;
+
+namespace PBA.Application.Tests.Features.ChannelAnalytics;
+
+public class GetYouTubeDeepAnalyticsHandlerTests
+{
+    private static readonly DateOnly Day1 = new(2026, 6, 1);
+
+    private static ApplicationDbContext CreateContext() =>
+        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
+
+    private static readonly Mock<ITokenEncryptor> Encryptor = BuildEncryptor();
+    private static Mock<ITokenEncryptor> BuildEncryptor()
+    {
+        var m = new Mock<ITokenEncryptor>();
+        m.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
+        return m;
+    }
+
+    private static async Task SeedActiveYouTubeCredential(ApplicationDbContext db)
+    {
+        db.PlatformCredentials.Add(new PlatformCredential
+        {
+            Platform = Platform.YouTube, Purpose = CredentialPurpose.Analytics, IsActive = true, EncryptedAccessToken = "token"
+        });
+        await db.SaveChangesAsync();
+    }
+
+    [Fact]
+    public async Task GetYouTubeDeepAnalytics_LiveCallFails_ReturnsResultFail_TabDegrades()
+    {
+        await using var db = CreateContext();
+        await SeedActiveYouTubeCredential(db);
+
+        var youtube = new Mock<IYouTubeApiClient>();
+        youtube.Setup(c => c.RunAnalyticsReportAsync(It.IsAny<YouTubeReportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("live call blew up"));
+
+        var result = await new GetYouTubeDeepAnalytics.Handler(db, youtube.Object, Encryptor.Object)
+            .Handle(new GetYouTubeDeepAnalytics.Query(Day1, Day1.AddDays(6)), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);   // Result.Fail, not an exception
+    }
+
+    [Fact]
+    public async Task GetYouTubeDeepAnalytics_NoActiveCredential_ReturnsResultFail()
+    {
+        await using var db = CreateContext();   // no credential seeded
+
+        var result = await new GetYouTubeDeepAnalytics.Handler(db, Mock.Of<IYouTubeApiClient>(), Encryptor.Object)
+            .Handle(new GetYouTubeDeepAnalytics.Query(Day1, Day1.AddDays(6)), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task GetYouTubeDeepAnalytics_LiveCallSucceeds_MapsSeries()
+    {
+        await using var db = CreateContext();
+        await SeedActiveYouTubeCredential(db);
+
+        var youtube = new Mock<IYouTubeApiClient>();
+        youtube.Setup(c => c.RunAnalyticsReportAsync(It.IsAny<YouTubeReportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(new YouTubeReportResult(
+                [new YouTubeReportColumn("day", "DIMENSION"), new YouTubeReportColumn("views", "METRIC")],
+                [["2026-06-01", "100"], ["2026-06-02", "150"]]));
+
+        var result = await new GetYouTubeDeepAnalytics.Handler(db, youtube.Object, Encryptor.Object)
+            .Handle(new GetYouTubeDeepAnalytics.Query(Day1, Day1.AddDays(6)), CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        var views = result.Value!.DaySeries.Single(s => s.Metric == "views");
+        Assert.Equal(100, views.Points[0].Value);
+        Assert.Equal(150, views.Points[1].Value);
+    }
+}
