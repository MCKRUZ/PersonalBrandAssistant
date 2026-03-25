using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Models;

public class AnalyticsDashboardModelTests
{
    [Fact]
    public void DashboardSummary_CanBeInstantiated_WithAllProperties()
    {
        var summary = new DashboardSummary(
            TotalEngagement: 1500,
            PreviousEngagement: 1200,
            TotalImpressions: 50000,
            PreviousImpressions: 45000,
            EngagementRate: 3.0m,
            PreviousEngagementRate: 2.67m,
            ContentPublished: 10,
            PreviousContentPublished: 8,
            CostPerEngagement: 0.05m,
            PreviousCostPerEngagement: 0.06m,
            WebsiteUsers: 2500,
            PreviousWebsiteUsers: 2100,
            GeneratedAt: DateTimeOffset.UtcNow);

        Assert.Equal(1500, summary.TotalEngagement);
        Assert.Equal(1200, summary.PreviousEngagement);
        Assert.Equal(50000, summary.TotalImpressions);
        Assert.Equal(3.0m, summary.EngagementRate);
        Assert.Equal(10, summary.ContentPublished);
        Assert.Equal(0.05m, summary.CostPerEngagement);
        Assert.Equal(2500, summary.WebsiteUsers);
    }

    [Fact]
    public void DailyEngagement_HoldsPerPlatformBreakdown()
    {
        var platforms = new List<PlatformDailyMetrics>
        {
            new(PlatformType.LinkedIn, 50, 10, 5, 65),
            new(PlatformType.TwitterX, 30, 20, 15, 65)
        };

        var daily = new DailyEngagement(
            Date: new DateOnly(2026, 3, 24),
            Platforms: platforms,
            Total: 130);

        Assert.Equal(new DateOnly(2026, 3, 24), daily.Date);
        Assert.Equal(2, daily.Platforms.Count);
        Assert.Equal(130, daily.Total);
        Assert.Equal(PlatformType.LinkedIn, daily.Platforms[0].Platform);
    }

    [Fact]
    public void PlatformSummary_SupportsUnavailablePlatform()
    {
        var summary = new PlatformSummary(
            Platform: PlatformType.Reddit,
            FollowerCount: null,
            PostCount: 5,
            AvgEngagement: 12.5,
            TopPostTitle: null,
            TopPostUrl: null,
            IsAvailable: false);

        Assert.False(summary.IsAvailable);
        Assert.Null(summary.FollowerCount);
        Assert.Equal(5, summary.PostCount);
    }

    [Fact]
    public void WebsiteOverview_HasAllGa4MetricProperties()
    {
        var overview = new WebsiteOverview(
            ActiveUsers: 500,
            Sessions: 800,
            PageViews: 2000,
            AvgSessionDuration: 120.5,
            BounceRate: 45.2,
            NewUsers: 300);

        Assert.Equal(500, overview.ActiveUsers);
        Assert.Equal(800, overview.Sessions);
        Assert.Equal(2000, overview.PageViews);
        Assert.Equal(120.5, overview.AvgSessionDuration);
        Assert.Equal(45.2, overview.BounceRate);
        Assert.Equal(300, overview.NewUsers);
    }

    [Fact]
    public void SearchQueryEntry_HoldsCtrAndPositionAsDoubles()
    {
        var entry = new SearchQueryEntry(
            Query: "AI personal branding",
            Clicks: 42,
            Impressions: 1200,
            Ctr: 3.5,
            Position: 4.2);

        Assert.Equal(3.5, entry.Ctr);
        Assert.Equal(4.2, entry.Position);
    }

    [Fact]
    public void SubstackPost_HasNullableSummary()
    {
        var post = new SubstackPost(
            Title: "Test Post",
            Url: "https://example.substack.com/p/test",
            PublishedAt: DateTimeOffset.UtcNow,
            Summary: null);

        Assert.Null(post.Summary);
        Assert.Equal("Test Post", post.Title);
    }

    [Fact]
    public void GoogleAnalyticsOptions_HasCorrectDefaults()
    {
        var options = new GoogleAnalyticsOptions();

        Assert.Equal("GoogleAnalytics", GoogleAnalyticsOptions.SectionName);
        Assert.Equal("secrets/google-analytics-sa.json", options.CredentialsPath);
        Assert.Equal("261358185", options.PropertyId);
        Assert.Equal("https://matthewkruczek.ai/", options.SiteUrl);
    }

    [Fact]
    public void SubstackOptions_HasCorrectDefaults()
    {
        var options = new SubstackOptions();

        Assert.Equal("Substack", SubstackOptions.SectionName);
        Assert.Contains("substack.com", options.FeedUrl);
    }

    [Fact]
    public void TopPerformingContent_IncludesImpressionsAndEngagementRate()
    {
        var content = new TopPerformingContent(
            ContentId: Guid.NewGuid(),
            Title: "Test Content",
            TotalEngagement: 100,
            EngagementByPlatform: new Dictionary<PlatformType, int>
            {
                { PlatformType.LinkedIn, 60 },
                { PlatformType.TwitterX, 40 }
            },
            Impressions: 5000,
            EngagementRate: 2.0m);

        Assert.Equal(5000, content.Impressions);
        Assert.Equal(2.0m, content.EngagementRate);
    }

    [Fact]
    public void TopPerformingContent_DefaultsNullForNewFields()
    {
        var content = new TopPerformingContent(
            ContentId: Guid.NewGuid(),
            Title: "Legacy Content",
            TotalEngagement: 50,
            EngagementByPlatform: new Dictionary<PlatformType, int>());

        Assert.Null(content.Impressions);
        Assert.Null(content.EngagementRate);
    }
}
