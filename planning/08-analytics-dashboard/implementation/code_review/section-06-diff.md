diff --git a/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs
index de4913a..d9905e3 100644
--- a/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs
+++ b/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs
@@ -1,17 +1,30 @@
 using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Errors;
 using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
 
 namespace PersonalBrandAssistant.Api.Endpoints;
 
 public static class AnalyticsEndpoints
 {
+    private static readonly HashSet<string> ValidPeriods = ["1d", "7d", "14d", "30d", "90d"];
+
     public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
     {
         var group = app.MapGroup("/api/analytics").WithTags("Analytics");
 
+        // Existing engagement routes
         group.MapGet("/content/{id:guid}", GetPerformance);
         group.MapGet("/top", GetTopContent);
         group.MapPost("/content/{id:guid}/refresh", RefreshEngagement);
+
+        // Dashboard routes
+        group.MapGet("/dashboard", GetDashboard);
+        group.MapGet("/engagement-timeline", GetTimeline);
+        group.MapGet("/platform-summary", GetPlatformSummaries);
+        group.MapGet("/website", GetWebsiteAnalytics);
+        group.MapGet("/substack", GetSubstackPosts);
+        group.MapGet("/health", GetAnalyticsHealth);
     }
 
     private static async Task<IResult> GetPerformance(
@@ -45,4 +58,179 @@ public static class AnalyticsEndpoints
             return Results.Accepted(value: result.Value);
         return result.ToHttpResult();
     }
+
+    private static async Task<IResult> GetDashboard(
+        IDashboardAggregator aggregator,
+        IDashboardCacheInvalidator cacheInvalidator,
+        string? period = null,
+        DateTimeOffset? from = null,
+        DateTimeOffset? to = null,
+        bool refresh = false,
+        CancellationToken ct = default)
+    {
+        var rangeResult = ParseDateRange(period, from, to);
+        if (!rangeResult.IsSuccess)
+            return rangeResult.ToHttpResult();
+
+        if (refresh)
+            await cacheInvalidator.TryInvalidateAsync(ct);
+
+        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
+        var result = await aggregator.GetSummaryAsync(resolvedFrom, resolvedTo, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetTimeline(
+        IDashboardAggregator aggregator,
+        IDashboardCacheInvalidator cacheInvalidator,
+        string? period = null,
+        DateTimeOffset? from = null,
+        DateTimeOffset? to = null,
+        bool refresh = false,
+        CancellationToken ct = default)
+    {
+        var rangeResult = ParseDateRange(period, from, to);
+        if (!rangeResult.IsSuccess)
+            return rangeResult.ToHttpResult();
+
+        if (refresh)
+            await cacheInvalidator.TryInvalidateAsync(ct);
+
+        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
+        var result = await aggregator.GetTimelineAsync(resolvedFrom, resolvedTo, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetPlatformSummaries(
+        IDashboardAggregator aggregator,
+        IDashboardCacheInvalidator cacheInvalidator,
+        string? period = null,
+        DateTimeOffset? from = null,
+        DateTimeOffset? to = null,
+        bool refresh = false,
+        CancellationToken ct = default)
+    {
+        var rangeResult = ParseDateRange(period, from, to);
+        if (!rangeResult.IsSuccess)
+            return rangeResult.ToHttpResult();
+
+        if (refresh)
+            await cacheInvalidator.TryInvalidateAsync(ct);
+
+        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
+        var result = await aggregator.GetPlatformSummariesAsync(resolvedFrom, resolvedTo, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetWebsiteAnalytics(
+        IGoogleAnalyticsService gaService,
+        string? period = null,
+        DateTimeOffset? from = null,
+        DateTimeOffset? to = null,
+        CancellationToken ct = default)
+    {
+        var rangeResult = ParseDateRange(period, from, to);
+        if (!rangeResult.IsSuccess)
+            return rangeResult.ToHttpResult();
+
+        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
+
+        // Run all four calls in parallel; capture failures individually
+        var overviewTask = gaService.GetOverviewAsync(resolvedFrom, resolvedTo, ct);
+        var topPagesTask = gaService.GetTopPagesAsync(resolvedFrom, resolvedTo, 20, ct);
+        var trafficTask = gaService.GetTrafficSourcesAsync(resolvedFrom, resolvedTo, ct);
+        var queriesTask = gaService.GetTopQueriesAsync(resolvedFrom, resolvedTo, 20, ct);
+
+        await Task.WhenAll(overviewTask, topPagesTask, trafficTask, queriesTask);
+
+        var overview = overviewTask.Result;
+        var topPages = topPagesTask.Result;
+        var traffic = trafficTask.Result;
+        var queries = queriesTask.Result;
+
+        var response = new WebsiteAnalyticsResponse(
+            Overview: overview.IsSuccess ? overview.Value! : null!,
+            TopPages: topPages.IsSuccess ? topPages.Value! : [],
+            TrafficSources: traffic.IsSuccess ? traffic.Value! : [],
+            SearchQueries: queries.IsSuccess ? queries.Value! : []);
+
+        return Results.Ok(response);
+    }
+
+    private static async Task<IResult> GetSubstackPosts(
+        ISubstackService substackService,
+        int limit = 10,
+        CancellationToken ct = default)
+    {
+        var clampedLimit = Math.Clamp(limit, 1, 50);
+        var result = await substackService.GetRecentPostsAsync(clampedLimit, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetAnalyticsHealth(
+        IGoogleAnalyticsService gaService,
+        ISubstackService substackService,
+        CancellationToken ct = default)
+    {
+        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
+        var today = DateTimeOffset.UtcNow;
+
+        // Probe each service with lightweight calls
+        bool ga4 = false, searchConsole = false, substack = false;
+
+        try
+        {
+            var gaResult = await gaService.GetOverviewAsync(yesterday, today, ct);
+            ga4 = gaResult.IsSuccess;
+            // Search Console uses the same service; if GA4 works, SC likely works too
+            searchConsole = ga4;
+        }
+        catch
+        {
+            // connectivity failed
+        }
+
+        try
+        {
+            var substackResult = await substackService.GetRecentPostsAsync(1, ct);
+            substack = substackResult.IsSuccess;
+        }
+        catch
+        {
+            // connectivity failed
+        }
+
+        return Results.Ok(new { ga4, searchConsole, substack });
+    }
+
+    private static Result<(DateTimeOffset From, DateTimeOffset To)> ParseDateRange(
+        string? period, DateTimeOffset? from, DateTimeOffset? to)
+    {
+        if (period is not null)
+        {
+            if (!ValidPeriods.Contains(period))
+                return Result<(DateTimeOffset, DateTimeOffset)>.Failure(
+                    ErrorCode.ValidationFailed,
+                    $"Invalid period '{period}'. Valid values: {string.Join(", ", ValidPeriods)}");
+
+            var days = int.Parse(period.TrimEnd('d'));
+            var resolvedTo = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
+                .AddDays(1).AddTicks(-1); // end of today
+            var resolvedFrom = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-(days - 1)), TimeSpan.Zero);
+            return Result.Success((resolvedFrom, resolvedTo));
+        }
+
+        if (from.HasValue && to.HasValue)
+        {
+            return Result.Success((from.Value, to.Value));
+        }
+
+        // Default to 30d
+        {
+            var resolvedTo = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
+                .AddDays(1).AddTicks(-1);
+            var resolvedFrom = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-29), TimeSpan.Zero);
+            return Result.Success((resolvedFrom, resolvedTo));
+        }
+    }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs
new file mode 100644
index 0000000..220368c
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs
@@ -0,0 +1,264 @@
+using System.Net;
+using System.Net.Http.Json;
+using System.Text.Json;
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class AnalyticsEndpointTests : IClassFixture<AnalyticsEndpointTests.TestFactory>
+{
+    private const string TestApiKey = "test-api-key-12345";
+    private readonly TestFactory _factory;
+    private readonly Mock<IDashboardAggregator> _aggregator = new();
+    private readonly Mock<IGoogleAnalyticsService> _gaService = new();
+    private readonly Mock<ISubstackService> _substackService = new();
+    private readonly Mock<IDashboardCacheInvalidator> _cacheInvalidator = new();
+
+    private static readonly JsonSerializerOptions JsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
+    };
+
+    public AnalyticsEndpointTests(TestFactory factory)
+    {
+        _factory = factory;
+    }
+
+    private HttpClient CreateAuthClient()
+    {
+        var client = _factory.WithWebHostBuilder(builder =>
+        {
+            builder.ConfigureTestServices(services =>
+            {
+                services.AddScoped<IDashboardAggregator>(_ => _aggregator.Object);
+                services.AddScoped<IGoogleAnalyticsService>(_ => _gaService.Object);
+                services.AddScoped<ISubstackService>(_ => _substackService.Object);
+                services.AddScoped<IDashboardCacheInvalidator>(_ => _cacheInvalidator.Object);
+            });
+        }).CreateClient();
+        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
+        return client;
+    }
+
+    [Fact]
+    public async Task GetDashboard_Returns200_WithDashboardSummary()
+    {
+        var summary = new DashboardSummary(
+            TotalEngagement: 1500, PreviousEngagement: 1200,
+            TotalImpressions: 5000, PreviousImpressions: 4000,
+            EngagementRate: 30.0m, PreviousEngagementRate: 30.0m,
+            ContentPublished: 10, PreviousContentPublished: 8,
+            CostPerEngagement: 0, PreviousCostPerEngagement: 0,
+            WebsiteUsers: 200, PreviousWebsiteUsers: 150,
+            GeneratedAt: DateTimeOffset.UtcNow);
+
+        _aggregator.Setup(a => a.GetSummaryAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(summary));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/dashboard");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
+        Assert.Equal(1500, json.GetProperty("totalEngagement").GetInt32());
+        Assert.Equal(1200, json.GetProperty("previousEngagement").GetInt32());
+    }
+
+    [Fact]
+    public async Task GetDashboard_WithPeriod7d_UsesCorrectDateRange()
+    {
+        DateTimeOffset capturedFrom = default, capturedTo = default;
+        _aggregator.Setup(a => a.GetSummaryAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((f, t, _) =>
+            {
+                capturedFrom = f;
+                capturedTo = t;
+            })
+            .ReturnsAsync(Result.Success(new DashboardSummary(
+                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow)));
+
+        using var client = CreateAuthClient();
+        await client.GetAsync("/api/analytics/dashboard?period=7d");
+
+        var span = capturedTo.Date - capturedFrom.Date;
+        Assert.Equal(6, span.Days); // 7 days inclusive = 6 day span
+    }
+
+    [Fact]
+    public async Task GetEngagementTimeline_Returns200_WithDailyEngagementArray()
+    {
+        var timeline = new List<DailyEngagement>
+        {
+            new(DateOnly.FromDateTime(DateTime.Today), new List<PlatformDailyMetrics>(), 100),
+            new(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), new List<PlatformDailyMetrics>(), 80),
+            new(DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), new List<PlatformDailyMetrics>(), 60),
+        };
+
+        _aggregator.Setup(a => a.GetTimelineAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<DailyEngagement>>(timeline));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/engagement-timeline");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
+        Assert.Equal(3, json.GetArrayLength());
+    }
+
+    [Fact]
+    public async Task GetPlatformSummary_Returns200_WithPlatformSummaryArray()
+    {
+        var summaries = new List<PlatformSummary>
+        {
+            new(PlatformType.TwitterX, 1000, 5, 120.0, "Top tweet", "https://x.com/1", true),
+            new(PlatformType.LinkedIn, 2000, 3, 200.0, "Top post", "https://linkedin.com/1", true),
+            new(PlatformType.Instagram, 500, 2, 80.0, null, null, true),
+            new(PlatformType.YouTube, null, 1, 50.0, null, null, false),
+        };
+
+        _aggregator.Setup(a => a.GetPlatformSummariesAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<PlatformSummary>>(summaries));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/platform-summary");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
+        Assert.Equal(4, json.GetArrayLength());
+    }
+
+    [Fact]
+    public async Task GetWebsiteAnalytics_Returns200_WithCompositeResponse()
+    {
+        _gaService.Setup(g => g.GetOverviewAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new WebsiteOverview(100, 150, 300, 120.5, 45.0, 80)));
+
+        _gaService.Setup(g => g.GetTopPagesAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<PageViewEntry>>(
+                new List<PageViewEntry> { new("/blog", 50, 30) }));
+
+        _gaService.Setup(g => g.GetTrafficSourcesAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<TrafficSourceEntry>>(
+                new List<TrafficSourceEntry> { new("google", 100, 80) }));
+
+        _gaService.Setup(g => g.GetTopQueriesAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<SearchQueryEntry>>(
+                new List<SearchQueryEntry> { new("test query", 50, 10, 5.0, 2.0) }));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/website");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
+        Assert.True(json.TryGetProperty("overview", out _));
+        Assert.True(json.TryGetProperty("topPages", out _));
+        Assert.True(json.TryGetProperty("trafficSources", out _));
+        Assert.True(json.TryGetProperty("searchQueries", out _));
+    }
+
+    [Fact]
+    public async Task GetSubstackPosts_Returns200_WithSubstackPostArray()
+    {
+        var posts = Enumerable.Range(1, 5)
+            .Select(i => new SubstackPost($"Post {i}", $"https://substack.com/{i}", DateTimeOffset.UtcNow, $"Summary {i}"))
+            .ToList();
+
+        _substackService.Setup(s => s.GetRecentPostsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<SubstackPost>>(posts));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/substack");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
+        Assert.Equal(5, json.GetArrayLength());
+    }
+
+    [Fact]
+    public async Task GetAnalyticsHealth_Returns200_WithConnectivityStatus()
+    {
+        _gaService.Setup(g => g.GetOverviewAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new WebsiteOverview(0, 0, 0, 0, 0, 0)));
+
+        _substackService.Setup(s => s.GetRecentPostsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<SubstackPost>>(new List<SubstackPost>()));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/health");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
+        Assert.True(json.TryGetProperty("ga4", out _));
+        Assert.True(json.TryGetProperty("searchConsole", out _));
+        Assert.True(json.TryGetProperty("substack", out _));
+    }
+
+    [Fact]
+    public async Task GetDashboard_WithInvalidPeriod_Returns400()
+    {
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/analytics/dashboard?period=invalid");
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task GetDashboard_PeriodAndFromTo_PeriodTakesPrecedence()
+    {
+        DateTimeOffset capturedFrom = default, capturedTo = default;
+        _aggregator.Setup(a => a.GetSummaryAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((f, t, _) =>
+            {
+                capturedFrom = f;
+                capturedTo = t;
+            })
+            .ReturnsAsync(Result.Success(new DashboardSummary(
+                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow)));
+
+        using var client = CreateAuthClient();
+        // Provide both period=30d and explicit from/to that are different
+        await client.GetAsync("/api/analytics/dashboard?period=30d&from=2020-01-01&to=2020-01-31");
+
+        // Period (30d from today) should take precedence over explicit from/to
+        var expectedFrom = DateTimeOffset.UtcNow.Date.AddDays(-29);
+        Assert.Equal(expectedFrom, capturedFrom.Date);
+    }
+
+    public class TestFactory : WebApplicationFactory<Program>
+    {
+        protected override void ConfigureWebHost(IWebHostBuilder builder)
+        {
+            builder.UseEnvironment("Development");
+            builder.UseSetting("ApiKeys:ReadonlyKey", TestApiKey);
+            builder.UseSetting("ApiKeys:WriteKey", TestApiKey);
+            builder.UseSetting("ConnectionStrings:DefaultConnection",
+                "Host=localhost;Database=test_analytics;Username=test;Password=test");
+
+            builder.ConfigureTestServices(services =>
+            {
+                var hostedServices = services
+                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
+                    .ToList();
+                foreach (var svc in hostedServices)
+                    services.Remove(svc);
+            });
+        }
+    }
+}
