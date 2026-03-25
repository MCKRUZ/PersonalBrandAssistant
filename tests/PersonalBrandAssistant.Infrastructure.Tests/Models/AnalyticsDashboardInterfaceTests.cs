using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Tests.Models;

public class AnalyticsDashboardInterfaceTests
{
    [Fact]
    public void IGoogleAnalyticsService_HasExpectedMethodSignatures()
    {
        var mock = new Mock<IGoogleAnalyticsService>();
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;

        mock.Setup(s => s.GetOverviewAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WebsiteOverview>.Success(
                new WebsiteOverview(100, 200, 500, 60.0, 40.0, 80)));

        mock.Setup(s => s.GetTopPagesAsync(from, to, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PageViewEntry>>.Success(
                new List<PageViewEntry>()));

        mock.Setup(s => s.GetTrafficSourcesAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TrafficSourceEntry>>.Success(
                new List<TrafficSourceEntry>()));

        mock.Setup(s => s.GetTopQueriesAsync(from, to, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success(
                new List<SearchQueryEntry>()));

        Assert.NotNull(mock.Object);
    }

    [Fact]
    public void ISubstackService_HasExpectedMethodSignature()
    {
        var mock = new Mock<ISubstackService>();

        mock.Setup(s => s.GetRecentPostsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubstackPost>>.Success(
                new List<SubstackPost>()));

        Assert.NotNull(mock.Object);
    }

    [Fact]
    public void IDashboardAggregator_HasExpectedMethodSignatures()
    {
        var mock = new Mock<IDashboardAggregator>();
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;

        mock.Setup(s => s.GetSummaryAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DashboardSummary>.Success(
                new DashboardSummary(0, 0, 0, 0, 0m, 0m, 0, 0, 0m, 0m, 0, 0, DateTimeOffset.UtcNow)));

        mock.Setup(s => s.GetTimelineAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DailyEngagement>>.Success(
                new List<DailyEngagement>()));

        mock.Setup(s => s.GetPlatformSummariesAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PlatformSummary>>.Success(
                new List<PlatformSummary>()));

        Assert.NotNull(mock.Object);
    }
}
