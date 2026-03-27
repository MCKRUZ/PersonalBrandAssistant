diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardAggregator.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardAggregator.cs
new file mode 100644
index 0000000..3544dfc
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardAggregator.cs
@@ -0,0 +1,16 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+/// <summary>Orchestrates all data sources into unified dashboard responses.</summary>
+public interface IDashboardAggregator
+{
+    Task<Result<DashboardSummary>> GetSummaryAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IGoogleAnalyticsService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IGoogleAnalyticsService.cs
new file mode 100644
index 0000000..bd0f8b5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/IGoogleAnalyticsService.cs
@@ -0,0 +1,19 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+/// <summary>Abstracts GA4 Data API and Search Console API access.</summary>
+public interface IGoogleAnalyticsService
+{
+    Task<Result<WebsiteOverview>> GetOverviewAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
+        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
+        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
+
+    Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
+        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackService.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackService.cs
new file mode 100644
index 0000000..116fbed
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackService.cs
@@ -0,0 +1,10 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+/// <summary>Parses Substack RSS feed into structured post data.</summary>
+public interface ISubstackService
+{
+    Task<Result<IReadOnlyList<SubstackPost>>> GetRecentPostsAsync(
+        int limit, CancellationToken ct);
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/DashboardModels.cs b/src/PersonalBrandAssistant.Application/Common/Models/DashboardModels.cs
new file mode 100644
index 0000000..fd017c5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/DashboardModels.cs
@@ -0,0 +1,21 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+/// <summary>KPI summary with current vs previous period comparison.</summary>
+public record DashboardSummary(
+    int TotalEngagement, int PreviousEngagement,
+    int TotalImpressions, int PreviousImpressions,
+    decimal EngagementRate, decimal PreviousEngagementRate,
+    int ContentPublished, int PreviousContentPublished,
+    decimal CostPerEngagement, decimal PreviousCostPerEngagement,
+    int WebsiteUsers, int PreviousWebsiteUsers,
+    DateTimeOffset GeneratedAt);
+
+/// <summary>Daily engagement totals broken down by platform.</summary>
+public record DailyEngagement(
+    DateOnly Date,
+    IReadOnlyList<PlatformDailyMetrics> Platforms,
+    int Total);
+
+/// <summary>Per-platform daily breakdown of likes/comments/shares.</summary>
+public record PlatformDailyMetrics(
+    string Platform, int Likes, int Comments, int Shares, int Total);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsModels.cs b/src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsModels.cs
new file mode 100644
index 0000000..26c2d2e
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsModels.cs
@@ -0,0 +1,16 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+/// <summary>GA4 overview metrics for a date range.</summary>
+public record WebsiteOverview(
+    int ActiveUsers, int Sessions, int PageViews,
+    double AvgSessionDuration, double BounceRate, int NewUsers);
+
+/// <summary>GA4 page-level metrics.</summary>
+public record PageViewEntry(string PagePath, int Views, int Users);
+
+/// <summary>GA4 traffic source breakdown.</summary>
+public record TrafficSourceEntry(string Channel, int Sessions, int Users);
+
+/// <summary>Search Console query performance.</summary>
+public record SearchQueryEntry(
+    string Query, int Clicks, int Impressions, double Ctr, double Position);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsOptions.cs
new file mode 100644
index 0000000..1ff9c91
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsOptions.cs
@@ -0,0 +1,15 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class GoogleAnalyticsOptions
+{
+    public const string SectionName = "GoogleAnalytics";
+
+    /// <summary>Path to the Google service account JSON key file.</summary>
+    public string CredentialsPath { get; set; } = "secrets/google-analytics-sa.json";
+
+    /// <summary>GA4 property ID (numeric).</summary>
+    public string PropertyId { get; set; } = "261358185";
+
+    /// <summary>Site URL for Search Console queries (include trailing slash).</summary>
+    public string SiteUrl { get; set; } = "https://matthewkruczek.ai/";
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/PlatformSummaryModel.cs b/src/PersonalBrandAssistant.Application/Common/Models/PlatformSummaryModel.cs
new file mode 100644
index 0000000..92a8ad6
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/PlatformSummaryModel.cs
@@ -0,0 +1,7 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+/// <summary>Per-platform health summary for the dashboard.</summary>
+public record PlatformSummary(
+    string Platform, int? FollowerCount, int PostCount,
+    double AvgEngagement, string? TopPostTitle, string? TopPostUrl,
+    bool IsAvailable);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/SubstackModels.cs b/src/PersonalBrandAssistant.Application/Common/Models/SubstackModels.cs
new file mode 100644
index 0000000..f5d139c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/SubstackModels.cs
@@ -0,0 +1,5 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+/// <summary>A Substack post parsed from RSS feed.</summary>
+public record SubstackPost(
+    string Title, string Url, DateTimeOffset PublishedAt, string? Summary);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/SubstackOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/SubstackOptions.cs
new file mode 100644
index 0000000..51db2cf
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/SubstackOptions.cs
@@ -0,0 +1,9 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class SubstackOptions
+{
+    public const string SectionName = "Substack";
+
+    /// <summary>RSS feed URL for Substack newsletter.</summary>
+    public string FeedUrl { get; set; } = "https://matthewkruczek.substack.com/feed";
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs b/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs
index 60133f0..b134344 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs
@@ -6,4 +6,6 @@ public record TopPerformingContent(
     Guid ContentId,
     string Title,
     int TotalEngagement,
-    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform);
+    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform,
+    int? Impressions = null,
+    decimal? EngagementRate = null);
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/WebsiteAnalyticsResponse.cs b/src/PersonalBrandAssistant.Application/Common/Models/WebsiteAnalyticsResponse.cs
new file mode 100644
index 0000000..0a6b249
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/WebsiteAnalyticsResponse.cs
@@ -0,0 +1,8 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+/// <summary>Combined GA4 + Search Console response.</summary>
+public record WebsiteAnalyticsResponse(
+    WebsiteOverview Overview,
+    IReadOnlyList<PageViewEntry> TopPages,
+    IReadOnlyList<TrafficSourceEntry> TrafficSources,
+    IReadOnlyList<SearchQueryEntry> SearchQueries);
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardInterfaceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardInterfaceTests.cs
new file mode 100644
index 0000000..f65fc20
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardInterfaceTests.cs
@@ -0,0 +1,68 @@
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Models;
+
+public class AnalyticsDashboardInterfaceTests
+{
+    [Fact]
+    public void IGoogleAnalyticsService_HasExpectedMethodSignatures()
+    {
+        var mock = new Mock<IGoogleAnalyticsService>();
+        var from = DateTimeOffset.UtcNow.AddDays(-30);
+        var to = DateTimeOffset.UtcNow;
+
+        mock.Setup(s => s.GetOverviewAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<WebsiteOverview>.Success(
+                new WebsiteOverview(100, 200, 500, 60.0, 40.0, 80)));
+
+        mock.Setup(s => s.GetTopPagesAsync(from, to, 10, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<IReadOnlyList<PageViewEntry>>.Success(
+                new List<PageViewEntry>()));
+
+        mock.Setup(s => s.GetTrafficSourcesAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<IReadOnlyList<TrafficSourceEntry>>.Success(
+                new List<TrafficSourceEntry>()));
+
+        mock.Setup(s => s.GetTopQueriesAsync(from, to, 10, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success(
+                new List<SearchQueryEntry>()));
+
+        Assert.NotNull(mock.Object);
+    }
+
+    [Fact]
+    public void ISubstackService_HasExpectedMethodSignature()
+    {
+        var mock = new Mock<ISubstackService>();
+
+        mock.Setup(s => s.GetRecentPostsAsync(10, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<IReadOnlyList<SubstackPost>>.Success(
+                new List<SubstackPost>()));
+
+        Assert.NotNull(mock.Object);
+    }
+
+    [Fact]
+    public void IDashboardAggregator_HasExpectedMethodSignatures()
+    {
+        var mock = new Mock<IDashboardAggregator>();
+        var from = DateTimeOffset.UtcNow.AddDays(-30);
+        var to = DateTimeOffset.UtcNow;
+
+        mock.Setup(s => s.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<DashboardSummary>.Success(
+                new DashboardSummary(0, 0, 0, 0, 0m, 0m, 0, 0, 0m, 0m, 0, 0, DateTimeOffset.UtcNow)));
+
+        mock.Setup(s => s.GetTimelineAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<IReadOnlyList<DailyEngagement>>.Success(
+                new List<DailyEngagement>()));
+
+        mock.Setup(s => s.GetPlatformSummariesAsync(from, to, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<IReadOnlyList<PlatformSummary>>.Success(
+                new List<PlatformSummary>()));
+
+        Assert.NotNull(mock.Object);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardModelTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardModelTests.cs
new file mode 100644
index 0000000..a2aa2eb
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardModelTests.cs
@@ -0,0 +1,169 @@
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Models;
+
+public class AnalyticsDashboardModelTests
+{
+    [Fact]
+    public void DashboardSummary_CanBeInstantiated_WithAllProperties()
+    {
+        var summary = new DashboardSummary(
+            TotalEngagement: 1500,
+            PreviousEngagement: 1200,
+            TotalImpressions: 50000,
+            PreviousImpressions: 45000,
+            EngagementRate: 3.0m,
+            PreviousEngagementRate: 2.67m,
+            ContentPublished: 10,
+            PreviousContentPublished: 8,
+            CostPerEngagement: 0.05m,
+            PreviousCostPerEngagement: 0.06m,
+            WebsiteUsers: 2500,
+            PreviousWebsiteUsers: 2100,
+            GeneratedAt: DateTimeOffset.UtcNow);
+
+        Assert.Equal(1500, summary.TotalEngagement);
+        Assert.Equal(1200, summary.PreviousEngagement);
+        Assert.Equal(50000, summary.TotalImpressions);
+        Assert.Equal(3.0m, summary.EngagementRate);
+        Assert.Equal(10, summary.ContentPublished);
+        Assert.Equal(0.05m, summary.CostPerEngagement);
+        Assert.Equal(2500, summary.WebsiteUsers);
+    }
+
+    [Fact]
+    public void DailyEngagement_HoldsPerPlatformBreakdown()
+    {
+        var platforms = new List<PlatformDailyMetrics>
+        {
+            new("LinkedIn", 50, 10, 5, 65),
+            new("TwitterX", 30, 20, 15, 65)
+        };
+
+        var daily = new DailyEngagement(
+            Date: new DateOnly(2026, 3, 24),
+            Platforms: platforms,
+            Total: 130);
+
+        Assert.Equal(new DateOnly(2026, 3, 24), daily.Date);
+        Assert.Equal(2, daily.Platforms.Count);
+        Assert.Equal(130, daily.Total);
+        Assert.Equal("LinkedIn", daily.Platforms[0].Platform);
+    }
+
+    [Fact]
+    public void PlatformSummary_SupportsUnavailablePlatform()
+    {
+        var summary = new PlatformSummary(
+            Platform: "Reddit",
+            FollowerCount: null,
+            PostCount: 5,
+            AvgEngagement: 12.5,
+            TopPostTitle: null,
+            TopPostUrl: null,
+            IsAvailable: false);
+
+        Assert.False(summary.IsAvailable);
+        Assert.Null(summary.FollowerCount);
+        Assert.Equal(5, summary.PostCount);
+    }
+
+    [Fact]
+    public void WebsiteOverview_HasAllGa4MetricProperties()
+    {
+        var overview = new WebsiteOverview(
+            ActiveUsers: 500,
+            Sessions: 800,
+            PageViews: 2000,
+            AvgSessionDuration: 120.5,
+            BounceRate: 45.2,
+            NewUsers: 300);
+
+        Assert.Equal(500, overview.ActiveUsers);
+        Assert.Equal(800, overview.Sessions);
+        Assert.Equal(2000, overview.PageViews);
+        Assert.Equal(120.5, overview.AvgSessionDuration);
+        Assert.Equal(45.2, overview.BounceRate);
+        Assert.Equal(300, overview.NewUsers);
+    }
+
+    [Fact]
+    public void SearchQueryEntry_HoldsCtrAndPositionAsDoubles()
+    {
+        var entry = new SearchQueryEntry(
+            Query: "AI personal branding",
+            Clicks: 42,
+            Impressions: 1200,
+            Ctr: 3.5,
+            Position: 4.2);
+
+        Assert.Equal(3.5, entry.Ctr);
+        Assert.Equal(4.2, entry.Position);
+    }
+
+    [Fact]
+    public void SubstackPost_HasNullableSummary()
+    {
+        var post = new SubstackPost(
+            Title: "Test Post",
+            Url: "https://example.substack.com/p/test",
+            PublishedAt: DateTimeOffset.UtcNow,
+            Summary: null);
+
+        Assert.Null(post.Summary);
+        Assert.Equal("Test Post", post.Title);
+    }
+
+    [Fact]
+    public void GoogleAnalyticsOptions_HasCorrectDefaults()
+    {
+        var options = new GoogleAnalyticsOptions();
+
+        Assert.Equal("GoogleAnalytics", GoogleAnalyticsOptions.SectionName);
+        Assert.Equal("secrets/google-analytics-sa.json", options.CredentialsPath);
+        Assert.Equal("261358185", options.PropertyId);
+        Assert.Equal("https://matthewkruczek.ai/", options.SiteUrl);
+    }
+
+    [Fact]
+    public void SubstackOptions_HasCorrectDefaults()
+    {
+        var options = new SubstackOptions();
+
+        Assert.Equal("Substack", SubstackOptions.SectionName);
+        Assert.Contains("substack.com", options.FeedUrl);
+    }
+
+    [Fact]
+    public void TopPerformingContent_IncludesImpressionsAndEngagementRate()
+    {
+        var content = new TopPerformingContent(
+            ContentId: Guid.NewGuid(),
+            Title: "Test Content",
+            TotalEngagement: 100,
+            EngagementByPlatform: new Dictionary<PlatformType, int>
+            {
+                { PlatformType.LinkedIn, 60 },
+                { PlatformType.TwitterX, 40 }
+            },
+            Impressions: 5000,
+            EngagementRate: 2.0m);
+
+        Assert.Equal(5000, content.Impressions);
+        Assert.Equal(2.0m, content.EngagementRate);
+    }
+
+    [Fact]
+    public void TopPerformingContent_DefaultsNullForNewFields()
+    {
+        var content = new TopPerformingContent(
+            ContentId: Guid.NewGuid(),
+            Title: "Legacy Content",
+            TotalEngagement: 50,
+            EngagementByPlatform: new Dictionary<PlatformType, int>());
+
+        Assert.Null(content.Impressions);
+        Assert.Null(content.EngagementRate);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs
index 75c6fb4..0ef41f9 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs
@@ -19,11 +19,12 @@ public class ContentPipelineTests
     private readonly Mock<ISidecarClient> _sidecarClient = new();
     private readonly Mock<IBrandVoiceService> _brandVoiceService = new();
     private readonly Mock<IWorkflowEngine> _workflowEngine = new();
+    private readonly Mock<IPipelineEventBroadcaster> _broadcaster = new();
     private readonly Mock<ILogger<ContentPipeline>> _logger = new();
 
     private ContentPipeline CreatePipeline() =>
         new(_dbContext.Object, _sidecarClient.Object, _brandVoiceService.Object,
-            _workflowEngine.Object, _logger.Object);
+            _workflowEngine.Object, _broadcaster.Object, _logger.Object);
 
     private void SetupContentsDbSet(List<Content> contents)
     {
