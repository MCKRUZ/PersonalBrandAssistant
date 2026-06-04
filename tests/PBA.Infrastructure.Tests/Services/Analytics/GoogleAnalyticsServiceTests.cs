using Google.Analytics.Data.V1Beta;
using Google.Apis.SearchConsole.v1.Data;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class GoogleAnalyticsServiceTests
{
    private readonly Mock<IGa4Client> _ga4 = new();
    private readonly Mock<ISearchConsoleClient> _gsc = new();
    private readonly GoogleAnalyticsService _sut;

    private readonly DateTimeOffset _from = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly DateTimeOffset _to = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);

    public GoogleAnalyticsServiceTests()
    {
        var options = Options.Create(new GoogleAnalyticsOptions
        {
            PropertyId = "test-property-123",
            SiteUrl = "https://example.com/",
            CredentialsPath = "secrets/google-analytics-sa.json"
        });
        _sut = new GoogleAnalyticsService(
            _ga4.Object, _gsc.Object, options, Mock.Of<ILogger<GoogleAnalyticsService>>());
    }

    [Fact]
    public async Task GetOverviewAsync_MapsSixMetricValues_InOrder()
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
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
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
    public async Task GetOverviewAsync_EmptyResponse_ReturnsZeroFilled()
    {
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunReportResponse());

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.ActiveUsers);
        Assert.Equal(0, result.Value.Sessions);
        Assert.Equal(0, result.Value.PageViews);
    }

    [Fact]
    public async Task GetOverviewAsync_RpcException_ReturnsFailureMentioningGa4()
    {
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("GA4"));
    }

    [Fact]
    public async Task GetTopPagesAsync_ReturnsRows_RespectingLimit()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                PageRow("/blog/post-1", "300", "100"),
                PageRow("/blog/post-2", "200", "80"),
                PageRow("/about", "150", "60")
            }
        };
        RunReportRequest? captured = null;
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RunReportRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(response);

        var result = await _sut.GetTopPagesAsync(_from, _to, 3, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("/blog/post-1", result.Value[0].PagePath);
        Assert.Equal(300, result.Value[0].Views);
        Assert.Equal(100, result.Value[0].UniqueUsers);
        Assert.Equal(3L, captured!.Limit);
    }

    [Fact]
    public async Task GetTrafficSourcesAsync_MapsChannelSessionsUsers()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                ChannelRow("Organic Search", "120", "90"),
                ChannelRow("Direct", "80", "70")
            }
        };
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTrafficSourcesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Organic Search", result.Value![0].Channel);
        Assert.Equal(120, result.Value[0].Sessions);
        Assert.Equal(90, result.Value[0].Users);
    }

    [Fact]
    public async Task GetTopQueriesAsync_MapsSearchConsoleRows()
    {
        var response = new SearchAnalyticsQueryResponse
        {
            Rows =
            [
                new ApiDataRow { Keys = ["ai tools"], Clicks = 50, Impressions = 1000, Ctr = 0.05, Position = 3.2 }
            ]
        };
        _gsc.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("ai tools", result.Value![0].Query);
        Assert.Equal(50, result.Value[0].Clicks);
        Assert.Equal(1000, result.Value[0].Impressions);
        Assert.Equal(0.05, result.Value[0].Ctr);
        Assert.Equal(3.2, result.Value[0].Position);
    }

    [Fact]
    public async Task GetTopQueriesAsync_GoogleApiException_ReturnsFailure()
    {
        _gsc.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Google.GoogleApiException("SearchConsole", "Forbidden"));

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("Search Console"));
    }

    [Fact]
    public async Task GetTopQueriesAsync_NullRows_ReturnsEmptyList()
    {
        _gsc.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchAnalyticsQueryResponse());

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    private static Row PageRow(string path, string views, string users) => new()
    {
        DimensionValues = { new DimensionValue { Value = path } },
        MetricValues = { new MetricValue { Value = views }, new MetricValue { Value = users } }
    };

    private static Row ChannelRow(string channel, string sessions, string users) => new()
    {
        DimensionValues = { new DimensionValue { Value = channel } },
        MetricValues = { new MetricValue { Value = sessions }, new MetricValue { Value = users } }
    };
}
