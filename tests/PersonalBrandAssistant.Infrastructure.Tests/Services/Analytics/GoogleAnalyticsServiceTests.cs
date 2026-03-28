using Google.Analytics.Data.V1Beta;
using Google.Apis.SearchConsole.v1.Data;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

public class GoogleAnalyticsServiceTests
{
    private readonly Mock<IGa4Client> _ga4Client = new();
    private readonly Mock<ISearchConsoleClient> _searchConsoleClient = new();
    private readonly IOptions<GoogleAnalyticsOptions> _options;
    private readonly Mock<ILogger<GoogleAnalyticsService>> _logger = new();
    private readonly GoogleAnalyticsService _sut;

    private readonly DateTimeOffset _from = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly DateTimeOffset _to = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);

    public GoogleAnalyticsServiceTests()
    {
        _options = Options.Create(new GoogleAnalyticsOptions
        {
            PropertyId = "261358185",
            SiteUrl = "https://matthewkruczek.ai/",
            CredentialsPath = "secrets/google-analytics-sa.json"
        });
        _sut = new GoogleAnalyticsService(
            _ga4Client.Object, _searchConsoleClient.Object, _options, _logger.Object);
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsWebsiteOverview_WithCorrectMetricMapping()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                new Row
                {
                    MetricValues =
                    {
                        new MetricValue { Value = "150" },
                        new MetricValue { Value = "200" },
                        new MetricValue { Value = "500" },
                        new MetricValue { Value = "120.5" },
                        new MetricValue { Value = "0.45" },
                        new MetricValue { Value = "80" }
                    }
                }
            }
        };

        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150, result.Value!.ActiveUsers);
        Assert.Equal(200, result.Value.Sessions);
        Assert.Equal(500, result.Value.PageViews);
        Assert.Equal(120.5, result.Value.AvgSessionDuration);
        Assert.Equal(0.45, result.Value.BounceRate);
        Assert.Equal(80, result.Value.NewUsers);
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsFailure_WhenGa4ClientThrowsRpcException()
    {
        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
        Assert.Contains("GA4", result.Errors[0]);
    }

    [Fact]
    public async Task GetOverviewAsync_HandlesEmptyResponse_ReturnsZeroFilledOverview()
    {
        var response = new RunReportResponse();

        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.ActiveUsers);
        Assert.Equal(0, result.Value.Sessions);
        Assert.Equal(0, result.Value.PageViews);
    }

    [Fact]
    public async Task GetTopPagesAsync_ReturnsSortedByViewsDescending_RespectsLimit()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                CreatePageRow("/blog/post-1", "300", "100"),
                CreatePageRow("/blog/post-2", "200", "80"),
                CreatePageRow("/about", "150", "60")
            }
        };

        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTopPagesAsync(_from, _to, 3, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("/blog/post-1", result.Value[0].PagePath);
        Assert.Equal(300, result.Value[0].Views);
    }

    [Fact]
    public async Task GetTrafficSourcesAsync_GroupsSessionsByChannel()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                CreateChannelRow("Organic Search", "120", "90"),
                CreateChannelRow("Direct", "80", "70"),
                CreateChannelRow("Social", "50", "40")
            }
        };

        _ga4Client.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTrafficSourcesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("Organic Search", result.Value[0].Channel);
        Assert.Equal(120, result.Value[0].Sessions);
        Assert.Equal(90, result.Value[0].Users);
    }

    [Fact]
    public async Task GetTopQueriesAsync_ReturnsSearchQueryEntries_FromSearchConsole()
    {
        var response = new SearchAnalyticsQueryResponse
        {
            Rows =
            [
                new ApiDataRow
                {
                    Keys = ["ai tools"],
                    Clicks = 50,
                    Impressions = 1000,
                    Ctr = 0.05,
                    Position = 3.2
                }
            ]
        };

        _searchConsoleClient.Setup(c => c.QueryAsync(
                It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("ai tools", result.Value![0].Query);
        Assert.Equal(50, result.Value![0].Clicks);
        Assert.Equal(1000, result.Value![0].Impressions);
        Assert.Equal(0.05, result.Value![0].Ctr);
        Assert.Equal(3.2, result.Value![0].Position);
    }

    [Fact]
    public async Task GetTopQueriesAsync_ReturnsFailure_WhenSearchConsoleThrowsGoogleApiException()
    {
        _searchConsoleClient.Setup(c => c.QueryAsync(
                It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Google.GoogleApiException("SearchConsole", "Forbidden"));

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
    }

    private static Row CreatePageRow(string pagePath, string views, string users) =>
        new()
        {
            DimensionValues = { new DimensionValue { Value = pagePath } },
            MetricValues =
            {
                new MetricValue { Value = views },
                new MetricValue { Value = users }
            }
        };

    private static Row CreateChannelRow(string channel, string sessions, string users) =>
        new()
        {
            DimensionValues = { new DimensionValue { Value = channel } },
            MetricValues =
            {
                new MetricValue { Value = sessions },
                new MetricValue { Value = users }
            }
        };
}
